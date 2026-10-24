using System.Buffers;
using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Annotations;

/// <summary>
/// Builds the appearance stream of an annotation: the small form XObject a viewer draws instead of
/// synthesising the annotation itself.
/// </summary>
/// <remarks>
/// <para>
/// The stream is written in the annotation's own space: the lower-left corner of the rectangle is the
/// origin, and the box is mapped onto <c>/Rect</c> by the viewer. That keeps the operators independent
/// of where on the page the annotation sits, and it keeps the numbers small.
/// </para>
/// <para>
/// Every annotation the editor creates gets one, so a viewer that does not synthesise appearances — and
/// a printer, which never does — still shows the mark. A link has none: a link is an active area with
/// nothing to draw, and a viewer that drew one would paint over the page.
/// </para>
/// </remarks>
internal static class AppearanceStreams
{
    /// <summary>The resource name the font of an appearance is selected by.</summary>
    public const string FontName = "Helv";

    /// <summary>The resource name an opacity is selected by.</summary>
    private const string StateName = "GS0";

    /// <summary>Gets the colour a sticky note is filled with when the caller states none.</summary>
    private static PdfColor NoteColor { get; } = new(0xFF, 0xD4, 0x00);

    /// <summary>Builds the form XObject of one annotation.</summary>
    /// <param name="annotation">The annotation.</param>
    /// <param name="fontObjectNumber">The object number of the font the appearance names, or zero for one that draws no text.</param>
    /// <param name="stateObjectNumber">The object number of the graphics state that applies the opacity, or zero for one that is opaque.</param>
    /// <returns>The stream to write, or <see langword="null"/> for an annotation that has nothing to draw.</returns>
    public static PdfStream? Build(PdfAnnotation annotation, int fontObjectNumber, int stateObjectNumber)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        var style = annotation.TextStyle ?? PdfStandardTextStyle.Default;
        var typeface = PdfTypeface.Standard(style.Font);
        var content = new AppearanceContent();
        var clip = annotation.Quads.Count > 0 ? Union(annotation.Rectangle, annotation.Quads) : annotation.Rectangle;

        // An appearance the caller supplied is the whole drawing, and the engine appends nothing of its own.
        // It still wraps it — the box, the resources, the compression — because a form XObject is not
        // something a caller should have to spell out.
        if (annotation.Appearance is { } supplied)
        {
            content.Line(supplied.Content);

            return Wrap(annotation, content.ToBytes(), fontObjectNumber, stateObjectNumber, clip);
        }

        content.Line("q");

        if (stateObjectNumber != 0)
        {
            content.Line(string.Create(CultureInfo.InvariantCulture, $"/{StateName} gs"));
        }

        switch (annotation.Kind)
        {
            case PdfAnnotationKind.Link:
                return null;

            case PdfAnnotationKind.Text:
                DrawNote(content, annotation, clip);
                break;

            case PdfAnnotationKind.FreeText:
                DrawFreeText(content, annotation, style, typeface, clip);
                break;

            case PdfAnnotationKind.Stamp:
                DrawStamp(content, annotation, style, typeface, clip);
                break;

            case PdfAnnotationKind.Highlight:
                DrawQuads(content, annotation, clip);
                break;

            case PdfAnnotationKind.Underline:
                DrawRules(content, annotation, clip, through: false);
                break;

            case PdfAnnotationKind.StrikeOut:
                DrawRules(content, annotation, clip, through: true);
                break;

            case PdfAnnotationKind.Square:
                DrawRectangle(content, annotation, clip);
                break;

            case PdfAnnotationKind.Circle:
                DrawEllipse(content, annotation, clip);
                break;

            case PdfAnnotationKind.Line:
                DrawLine(content, annotation, clip);
                break;

            case PdfAnnotationKind.Ink:
                DrawInk(content, annotation, clip);
                break;

            default:
                return null;
        }

        content.Line("Q");

        return Wrap(annotation, content.ToBytes(), fontObjectNumber, stateObjectNumber, clip);
    }

    /// <summary>Wraps appearance operators in the form XObject dictionary that carries them.</summary>
    /// <param name="annotation">The annotation the appearance belongs to.</param>
    /// <param name="operators">The operators, uncompressed.</param>
    /// <param name="fontObjectNumber">The font the appearance names, or zero.</param>
    /// <param name="stateObjectNumber">The graphics state the appearance names, or zero.</param>
    /// <param name="box">The box the operators are drawn in, in appearance space.</param>
    /// <returns>The stream to write.</returns>
    private static PdfStream Wrap(
        PdfAnnotation annotation,
        byte[] operators,
        int fontObjectNumber,
        int stateObjectNumber,
        PdfRectangle box)
    {
        var bounds = annotation.Appearance?.BoundingBox ?? new PdfRectangle(0, 0, box.Width, box.Height);
        var resources = new PdfDictionary();

        if (fontObjectNumber != 0)
        {
            resources.Set("Font", new PdfDictionary().Set(FontName, new PdfReference(fontObjectNumber, 0)));
        }

        if (stateObjectNumber != 0)
        {
            resources.Set("ExtGState", new PdfDictionary().Set(StateName, new PdfReference(stateObjectNumber, 0)));
        }

        var dictionary = new PdfDictionary()
            .SetName("Type", "XObject")
            .SetName("Subtype", "Form")
            .Set("BBox", ToArray(bounds))
            .SetName("Filter", "FlateDecode");

        if (resources.Count > 0)
        {
            dictionary.Set("Resources", resources);
        }

        return new PdfStream(dictionary, EmbeddedImage.Compress(operators));
    }

    /// <summary>Gets the smallest rectangle that covers a box and every quad of an annotation.</summary>
    /// <param name="rectangle">The annotation rectangle.</param>
    /// <param name="quads">The quads of the annotation, in page space.</param>
    /// <returns>The union, in page space.</returns>
    private static PdfRectangle Union(PdfRectangle rectangle, IReadOnlyList<PdfRectangle> quads) =>
        PdfRectangle.FromCorners(
            Math.Min(rectangle.Left, quads.Min(quad => quad.Left)),
            Math.Min(rectangle.Bottom, quads.Min(quad => quad.Bottom)),
            Math.Max(rectangle.Right, quads.Max(quad => quad.Right)),
            Math.Max(rectangle.Top, quads.Max(quad => quad.Top)));

    /// <summary>Writes four numbers as a PDF array, which is how a box is spelled.</summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>The array.</returns>
    internal static PdfArray ToArray(PdfRectangle rectangle) => new(
    [
        new PdfReal(rectangle.Left),
        new PdfReal(rectangle.Bottom),
        new PdfReal(rectangle.Right),
        new PdfReal(rectangle.Top),
    ]);

    /// <summary>Draws a sticky note: a filled page with a folded corner.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawNote(AppearanceContent content, PdfAnnotation annotation, PdfRectangle box)
    {
        var width = box.Width;
        var height = box.Height;
        var fold = Math.Min(Math.Min(width, height) / 4.0, 12.0);

        Fill(content, annotation.Color ?? NoteColor);
        content
            .Number(0).Number(0).Number(width).Number(height).Line(" re f");

        Stroke(content, PdfColor.Black);
        content.Number(1).Line(" w").Number(0).Number(0).Number(width).Number(height).Line(" re S");

        // The corner is drawn as two rules rather than a filled triangle, so the note still reads as a
        // note when it is printed in grey or scaled down to an icon.
        content
            .Number(width - fold).Number(height).Number(width).Number(height - fold).Line(" m l S");
    }

    /// <summary>Draws the text of a free-text annotation, clipped to its box.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="style">The style the text is drawn with.</param>
    /// <param name="typeface">The face used to measure it.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawFreeText(
        AppearanceContent content,
        PdfAnnotation annotation,
        PdfStandardTextStyle style,
        PdfTypeface typeface,
        PdfRectangle box)
    {
        var width = box.Width;
        var height = box.Height;
        var inset = Math.Max(1.0, style.Size / 4.0);
        var lines = (annotation.Contents ?? string.Empty).Split('\n');

        // The text is set at the stated size unless a line would not fit: then the whole block shrinks
        // to the widest line, so a caller who states a size larger than the box asked for readable text
        // rather than text a reader has to guess at behind a clip.
        var widest = lines.Max(line => typeface.Measure(line, 1.0));
        var size = widest > 0.0
            ? Math.Min(style.Size, Math.Max(width - (2.0 * inset), 1.0) / widest)
            : style.Size;
        var leading = size * 1.2;

        content
            .Number(0).Number(0).Number(width).Number(height).Line(" re W n");

        Fill(content, style.Color);
        content.Number(size).Line(" Tf").Line("BT");

        var baseline = height - inset - (size * 0.8);
        var index = 0;

        foreach (var line in lines)
        {
            var y = baseline - (index * leading);

            if (y < inset)
            {
                break;
            }

            content
                .Number(1).Number(0).Number(0).Number(1)
                .Number(inset).Number(y).Line(" Tm")
                .Literal(line).Line(" Tj");

            index++;
        }

        content.Line("ET");
    }

    /// <summary>Draws a stamp: a frame and its caption.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="style">The style the caption is drawn with.</param>
    /// <param name="typeface">The face used to measure the caption.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawStamp(
        AppearanceContent content,
        PdfAnnotation annotation,
        PdfStandardTextStyle style,
        PdfTypeface typeface,
        PdfRectangle box)
    {
        var width = box.Width;
        var height = box.Height;
        var color = annotation.Color ?? PdfColor.Black;
        var caption = (annotation.IconName ?? "Draft").ToUpperInvariant();
        var size = Math.Min(style.Size, Math.Max(Math.Min(width, height) / 2.5, 4.0));
        var textWidth = typeface.Measure(caption, size);

        Stroke(content, color);
        content
            .Number(2).Line(" w")
            .Number(1).Number(1).Number(Math.Max(width - 2, 1)).Number(Math.Max(height - 2, 1))
            .Line(" re S");

        Fill(content, color);
        content
            .Number(size).Line(" Tf")
            .Line("BT")
            .Number(1).Number(0).Number(0).Number(1)
            .Number(Math.Max((width - textWidth) / 2.0, 2.0))
            .Number(Math.Max((height - size) / 2.0, 2.0))
            .Line(" Tm")
            .Literal(caption).Line(" Tj")
            .Line("ET");
    }

    /// <summary>Fills the quads of a highlight.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawQuads(AppearanceContent content, PdfAnnotation annotation, PdfRectangle box)
    {
        Fill(content, annotation.Color ?? PdfColor.Black);

        foreach (var quad in annotation.Quads)
        {
            content
                .Number(quad.Left - box.Left).Number(quad.Bottom - box.Bottom)
                .Number(quad.Width).Number(quad.Height)
                .Line(" re");
        }

        content.Line("f");
    }

    /// <summary>Draws the rule of an underline or a strike-out along each quad.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="box">The appearance box.</param>
    /// <param name="through">Whether the rule crosses the quad instead of following its bottom edge.</param>
    private static void DrawRules(
        AppearanceContent content,
        PdfAnnotation annotation,
        PdfRectangle box,
        bool through)
    {
        var width = Math.Max(annotation.Border?.Width ?? 1.0, 0.5);

        Stroke(content, annotation.Color ?? PdfColor.Black);
        content.Number(width).Line(" w");

        foreach (var quad in annotation.Quads)
        {
            var y = through
                ? quad.Bottom + (quad.Height / 2.0) - box.Bottom
                : quad.Bottom + Math.Max(width / 2.0, 0.5) - box.Bottom;

            content
                .Number(quad.Left - box.Left).Number(y).Line(" m")
                .Number(quad.Right - box.Left).Number(y).Line(" l");
        }

        content.Line("S");
    }

    /// <summary>Draws the outline of a rectangle, inset by half its own line width.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawRectangle(AppearanceContent content, PdfAnnotation annotation, PdfRectangle box)
    {
        var width = Math.Max(annotation.Border?.Width ?? 2.0, 0.5);
        var half = width / 2.0;

        Stroke(content, annotation.Color ?? PdfColor.Black);
        content
            .Number(width).Line(" w")
            .Number(half).Number(half)
            .Number(Math.Max(box.Width - width, 1.0)).Number(Math.Max(box.Height - width, 1.0))
            .Line(" re S");
    }

    /// <summary>Draws the outline of an ellipse, as four Bézier curves.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawEllipse(AppearanceContent content, PdfAnnotation annotation, PdfRectangle box)
    {
        var width = Math.Max(annotation.Border?.Width ?? 2.0, 0.5);
        var half = width / 2.0;
        var left = half;
        var bottom = half;
        var right = Math.Max(box.Width - half, left + 1.0);
        var top = Math.Max(box.Height - half, bottom + 1.0);
        var centreX = (left + right) / 2.0;
        var centreY = (bottom + top) / 2.0;
        var radiusX = (right - left) / 2.0;
        var radiusY = (top - bottom) / 2.0;

        // The constant that makes a cubic Bézier approximate a quarter circle to within a thousandth of
        // the radius, which is far below what a page can show.
        const double Kappa = 0.5522847498307936;
        var controlX = radiusX * Kappa;
        var controlY = radiusY * Kappa;

        Stroke(content, annotation.Color ?? PdfColor.Black);
        content
            .Number(width).Line(" w")
            .Number(centreX + radiusX).Number(centreY).Line(" m")
            .Number(centreX + radiusX).Number(centreY + controlY)
            .Number(centreX + controlX).Number(centreY + radiusY)
            .Number(centreX).Number(centreY + radiusY).Line(" c")
            .Number(centreX - controlX).Number(centreY + radiusY)
            .Number(centreX - radiusX).Number(centreY + controlY)
            .Number(centreX - radiusX).Number(centreY).Line(" c")
            .Number(centreX - radiusX).Number(centreY - controlY)
            .Number(centreX - controlX).Number(centreY - radiusY)
            .Number(centreX).Number(centreY - radiusY).Line(" c")
            .Number(centreX + controlX).Number(centreY - radiusY)
            .Number(centreX + radiusX).Number(centreY - controlY)
            .Number(centreX + radiusX).Number(centreY).Line(" c S");
    }

    /// <summary>Draws one straight line.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawLine(AppearanceContent content, PdfAnnotation annotation, PdfRectangle box)
    {
        var width = Math.Max(annotation.Border?.Width ?? 2.0, 0.5);
        var from = annotation.LineFrom ?? PdfPoint.Origin;
        var to = annotation.LineTo ?? PdfPoint.Origin;

        Stroke(content, annotation.Color ?? PdfColor.Black);
        content
            .Number(width).Line(" w")
            .Number(from.X - box.Left).Number(from.Y - box.Bottom).Line(" m")
            .Number(to.X - box.Left).Number(to.Y - box.Bottom).Line(" l S");
    }

    /// <summary>Draws every stroke of an ink annotation.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="annotation">The annotation.</param>
    /// <param name="box">The appearance box.</param>
    private static void DrawInk(AppearanceContent content, PdfAnnotation annotation, PdfRectangle box)
    {
        var width = Math.Max(annotation.Border?.Width ?? 2.0, 0.5);

        Stroke(content, annotation.Color ?? PdfColor.Black);
        content.Number(width).Line(" w").Number(1).Line(" J").Number(1).Line(" j");

        foreach (var stroke in annotation.Strokes)
        {
            content
                .Number(stroke[0].X - box.Left).Number(stroke[0].Y - box.Bottom).Line(" m");

            for (var index = 1; index < stroke.Count; index++)
            {
                content.Number(stroke[index].X - box.Left).Number(stroke[index].Y - box.Bottom).Line(" l");
            }

            content.Line("S");
        }
    }

    /// <summary>Selects a fill colour.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="color">The colour.</param>
    private static void Fill(AppearanceContent content, PdfColor color) => content
        .Number(color.Red / 255.0).Number(color.Green / 255.0).Number(color.Blue / 255.0).Line(" rg");

    /// <summary>Selects a stroke colour.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="color">The colour.</param>
    private static void Stroke(AppearanceContent content, PdfColor color) => content
        .Number(color.Red / 255.0).Number(color.Green / 255.0).Number(color.Blue / 255.0).Line(" RG");

}


