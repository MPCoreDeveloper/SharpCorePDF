using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The drawing surface of one page an authoring builder creates.
/// </summary>
/// <remarks>
/// <para>
/// The coordinate system has its origin in the top left corner of the page, with x growing to the right
/// and y growing downwards — the coordinates a layout is usually written in, not the bottom-left origin
/// PDF stores. <see cref="DrawText"/> places the baseline of the text at <c>y</c>;
/// <c>DrawImage</c> places the top-left corner of the image at <c>(x, y)</c>.
/// </para>
/// <para>
/// Every call is independent: the same page carries as many separate strings and images as the caller
/// draws, each at its own position, size and colour. There is no word wrap, table or flow layout: a line
/// break is a second call at a different <c>y</c>.
/// </para>
/// <para>
/// An instance is created by <see cref="PdfDocumentBuilder"/> and is only handed to the drawing callback
/// of one page. It is not thread safe.
/// </para>
/// </remarks>
public sealed class PdfPageCanvas
{
    private const double EllipseControl = 0.5522847498307936;

    private readonly List<IPageOperation> operations = [];
    private int stateDepth;

    internal PdfPageCanvas(PdfPageSize size)
    {
        if (size.WidthPoints <= 0d || size.HeightPoints <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "A page needs a finite width and height greater than zero.");
        }

        Size = size;
    }

    /// <summary>Gets the size of the page.</summary>
    public PdfPageSize Size { get; }

    /// <summary>Gets the width of the page in points.</summary>
    public double Width => Size.WidthPoints;

    /// <summary>Gets the height of the page in points.</summary>
    public double Height => Size.HeightPoints;

    /// <summary>Gets the drawing operations, in the order they were added.</summary>
    internal IReadOnlyList<IPageOperation> Operations => operations;

    /// <summary>Draws one line of text.</summary>
    /// <param name="text">The text to draw; it must not contain a line break.</param>
    /// <param name="x">The x coordinate of the start of the baseline, in points from the left edge.</param>
    /// <param name="y">The y coordinate of the baseline, in points from the top edge.</param>
    /// <param name="typeface">The face to set the text in.</param>
    /// <param name="fontSize">The size of the font in points.</param>
    /// <param name="color">The fill colour; black when omitted.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="typeface"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate or the font size is not a finite number, or the size is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains a line break.</exception>
    public PdfPageCanvas DrawText(
        string text,
        double x,
        double y,
        PdfTypeface typeface,
        double fontSize,
        PdfColor? color = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typeface);
        ValidatePosition(x, y, nameof(x), nameof(y));

        if (!double.IsFinite(fontSize) || fontSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "The font size must be a finite number greater than zero.");
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            throw new ArgumentException(
                "A text operation draws one line. Draw every line with its own call, at its own y coordinate.",
                nameof(text));
        }

        if (text.Length > 0)
        {
            operations.Add(new TextOperation(text, x, y, typeface, fontSize, color ?? PdfColor.Black));
        }

        return this;
    }

    /// <summary>Draws laid-out text, placing the top-left of its bounds at a point.</summary>
    /// <param name="layout">The layout to draw.</param>
    /// <param name="origin">The top-left corner the layout is drawn from, in points.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <remarks>
    /// Every run of every line is one text operation, so the characters can be extracted again. The
    /// style's character spacing, word spacing, horizontal scale and rise are written as PDF text state.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The origin is not finite.</exception>
    public PdfPageCanvas DrawTextLayout(PdfTextLayout layout, PdfPoint origin)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ValidatePosition(origin.X, origin.Y, nameof(origin), nameof(origin));

        var left = layout.Bounds.IsEmpty ? 0d : layout.Bounds.Left;
        var top = layout.Bounds.IsEmpty ? 0d : layout.Bounds.Top;
        var style = layout.Style;

        foreach (var line in layout.Lines)
        {
            foreach (var run in line.Runs)
            {
                if (run.Text.Length == 0)
                {
                    continue;
                }

                operations.Add(new TextOperation(
                    run.Text,
                    origin.X + run.Start.X - left,
                    origin.Y + line.Baseline - top,
                    style.Typeface,
                    style.FontSize,
                    style.Color,
                    style.CharacterSpacing,
                    style.WordSpacing,
                    style.HorizontalScale,
                    style.Rise));
            }
        }

        return this;
    }

    /// <summary>Draws one straight line.</summary>
    /// <param name="from">The start point, in points from the top-left corner.</param>
    /// <param name="to">The end point, in points from the top-left corner.</param>
    /// <param name="stroke">The line width, colour and dash pattern.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stroke"/> is <see langword="null"/>.</exception>
    public PdfPageCanvas DrawLine(PdfPoint from, PdfPoint to, PdfStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);

        return AddPath(
            new PdfPath([new PdfPathSegment.MoveTo(from), new PdfPathSegment.LineTo(to)]),
            fill: null,
            stroke);
    }

    /// <summary>Draws a rectangle, filled, stroked, or both.</summary>
    /// <param name="rectangle">The rectangle, in points from the top-left corner.</param>
    /// <param name="fill">The fill, or <see langword="null"/> for no fill.</param>
    /// <param name="stroke">The outline, or <see langword="null"/> for no outline.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The rectangle encloses no area, or neither a fill nor a stroke is supplied.</exception>
    public PdfPageCanvas DrawRectangle(PdfRectangle rectangle, PdfPaint? fill, PdfStroke? stroke)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        EnsurePaint(fill, stroke);

        if (rectangle.IsEmpty)
        {
            throw new ArgumentException(
                "A rectangle drawn on a page needs a width and a height greater than zero.",
                nameof(rectangle));
        }

        var path = new PdfPath(
            [
                new PdfPathSegment.MoveTo(new PdfPoint(rectangle.Left, rectangle.Bottom)),
                new PdfPathSegment.LineTo(new PdfPoint(rectangle.Right, rectangle.Bottom)),
                new PdfPathSegment.LineTo(new PdfPoint(rectangle.Right, rectangle.Top)),
                new PdfPathSegment.LineTo(new PdfPoint(rectangle.Left, rectangle.Top)),
            ],
            isClosed: true);

        return AddPath(path, fill, stroke);
    }

    /// <summary>Draws an ellipse that fills a rectangle.</summary>
    /// <param name="bounds">The box the ellipse fills, in points from the top-left corner.</param>
    /// <param name="fill">The fill, or <see langword="null"/> for no fill.</param>
    /// <param name="stroke">The outline, or <see langword="null"/> for no outline.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <remarks>The ellipse is four cubic Bézier curves, the approximation PDF viewers themselves use.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="bounds"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The box encloses no area, or neither a fill nor a stroke is supplied.</exception>
    public PdfPageCanvas DrawEllipse(PdfRectangle bounds, PdfPaint? fill, PdfStroke? stroke)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        EnsurePaint(fill, stroke);

        if (bounds.IsEmpty)
        {
            throw new ArgumentException(
                "An ellipse drawn on a page needs a width and a height greater than zero.",
                nameof(bounds));
        }

        var centerX = bounds.Left + (bounds.Width / 2.0);
        var centerY = bounds.Bottom + (bounds.Height / 2.0);
        var radiusX = bounds.Width / 2.0;
        var radiusY = bounds.Height / 2.0;
        var controlX = radiusX * EllipseControl;
        var controlY = radiusY * EllipseControl;

        var path = new PdfPath(
            [
                new PdfPathSegment.MoveTo(new PdfPoint(centerX + radiusX, centerY)),
                new PdfPathSegment.CubicTo(
                    new PdfPoint(centerX + radiusX, centerY + controlY),
                    new PdfPoint(centerX + controlX, centerY + radiusY),
                    new PdfPoint(centerX, centerY + radiusY)),
                new PdfPathSegment.CubicTo(
                    new PdfPoint(centerX - controlX, centerY + radiusY),
                    new PdfPoint(centerX - radiusX, centerY + controlY),
                    new PdfPoint(centerX - radiusX, centerY)),
                new PdfPathSegment.CubicTo(
                    new PdfPoint(centerX - radiusX, centerY - controlY),
                    new PdfPoint(centerX - controlX, centerY - radiusY),
                    new PdfPoint(centerX, centerY - radiusY)),
                new PdfPathSegment.CubicTo(
                    new PdfPoint(centerX + controlX, centerY - radiusY),
                    new PdfPoint(centerX + radiusX, centerY - controlY),
                    new PdfPoint(centerX + radiusX, centerY)),
            ],
            isClosed: true);

        return AddPath(path, fill, stroke);
    }

    /// <summary>Draws a path, filled, stroked, or both.</summary>
    /// <param name="path">The path, in points from the top-left corner.</param>
    /// <param name="fill">The fill, or <see langword="null"/> for no fill.</param>
    /// <param name="stroke">The outline, or <see langword="null"/> for no outline.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Neither a fill nor a stroke is supplied.</exception>
    public PdfPageCanvas DrawPath(PdfPath path, PdfPaint? fill, PdfStroke? stroke)
    {
        ArgumentNullException.ThrowIfNull(path);
        EnsurePaint(fill, stroke);

        return AddPath(path, fill, stroke);
    }

    /// <summary>Saves the graphics state so a transform, clip or paint can be undone.</summary>
    /// <returns>This canvas, so calls can be chained.</returns>
    public PdfPageCanvas SaveState()
    {
        operations.Add(new SaveStateOperation());
        stateDepth++;
        return this;
    }

    /// <summary>Restores the graphics state saved by the matching <see cref="SaveState"/>.</summary>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="InvalidOperationException">No save is waiting to be restored.</exception>
    public PdfPageCanvas RestoreState()
    {
        if (stateDepth == 0)
        {
            throw new InvalidOperationException(
                "The graphics state cannot be restored because no matching SaveState was made.");
        }

        operations.Add(new RestoreStateOperation());
        stateDepth--;
        return this;
    }

    /// <summary>Restricts subsequent drawing to the inside of a path.</summary>
    /// <param name="path">The clipping path, in points from the top-left corner.</param>
    /// <param name="fillRule">The rule that decides what is inside.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fillRule"/> is not defined.</exception>
    public PdfPageCanvas SetClip(PdfPath path, PdfFillRule fillRule = PdfFillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!Enum.IsDefined(fillRule))
        {
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not one the engine defines.");
        }

        operations.Add(new ClipOperation(path, fillRule));
        return this;
    }

    /// <summary>Concatenates a transform onto the current one for subsequent drawing.</summary>
    /// <param name="matrix">The transform, in the canvas's top-left coordinate system.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    public PdfPageCanvas SetTransform(SharpCorePDF.Domain.ValueObjects.PdfMatrix matrix)
    {
        operations.Add(new TransformOperation(matrix));
        return this;
    }

    /// <summary>Draws a PNG image at an explicit size.</summary>
    /// <param name="png">The bytes of a PNG file.</param>
    /// <param name="x">The x coordinate of the top-left corner, in points from the left edge.</param>
    /// <param name="y">The y coordinate of the top-left corner, in points from the top edge.</param>
    /// <param name="width">The width the image occupies on the page, in points.</param>
    /// <param name="height">The height the image occupies on the page, in points.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentException"><paramref name="png"/> is empty, or is not a PNG file.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate, the width or the height is not a finite number, or a size is not positive.</exception>
    /// <exception cref="NotSupportedException">The PNG is interlaced, sixteen bit, or of a colour type the engine does not embed.</exception>
    public PdfPageCanvas DrawImage(ReadOnlyMemory<byte> png, double x, double y, double width, double height)
    {
        ValidatePosition(x, y, nameof(x), nameof(y));
        ValidateSize(width, height);

        return AddImage(PngImage.Parse(png), x, y, width, height);
    }

    /// <summary>Draws a PNG image at its natural size.</summary>
    /// <param name="png">The bytes of a PNG file.</param>
    /// <param name="x">The x coordinate of the top-left corner, in points from the left edge.</param>
    /// <param name="y">The y coordinate of the top-left corner, in points from the top edge.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <remarks>
    /// The natural size comes from the <c>pHYs</c> chunk of the PNG, which states how many pixels the
    /// image has per metre. A PNG without that chunk, or with a unit it does not define, is drawn at one
    /// point per pixel.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="png"/> is empty, or is not a PNG file.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a finite number.</exception>
    /// <exception cref="NotSupportedException">The PNG is interlaced, sixteen bit, or of a colour type the engine does not embed.</exception>
    public PdfPageCanvas DrawImage(ReadOnlyMemory<byte> png, double x, double y)
    {
        ValidatePosition(x, y, nameof(x), nameof(y));

        // The image is parsed once: the natural size is a property of the parsed file, and parsing it a
        // second time would copy its IDAT data again.
        var image = PngImage.Parse(png);

        return AddImage(
            image,
            x,
            y,
            image.NaturalWidthPoints ?? image.Width,
            image.NaturalHeightPoints ?? image.Height);
    }

    /// <summary>Records a path and the paint that finishes it.</summary>
    /// <param name="path">The path.</param>
    /// <param name="fill">The fill, or null.</param>
    /// <param name="stroke">The stroke, or null.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    private PdfPageCanvas AddPath(PdfPath path, PdfPaint? fill, PdfStroke? stroke)
    {
        operations.Add(new PathOperation(path, fill, stroke));
        return this;
    }

    /// <summary>Refuses a path that would be constructed but never painted.</summary>
    /// <param name="fill">The fill, or null.</param>
    /// <param name="stroke">The stroke, or null.</param>
    /// <exception cref="ArgumentException">Neither a fill nor a stroke is supplied.</exception>
    private static void EnsurePaint(PdfPaint? fill, PdfStroke? stroke)
    {
        if (fill is null && stroke is null)
        {
            throw new ArgumentException("A path needs a fill, a stroke, or both before it can be drawn.");
        }
    }

    /// <summary>Validates the size an image is drawn at.</summary>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not a finite number greater than zero.</exception>
    private static void ValidateSize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The image width must be a finite number greater than zero.");
        }

        if (!double.IsFinite(height) || height <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The image height must be a finite number greater than zero.");
        }
    }

    /// <summary>Records an image operation.</summary>
    /// <param name="image">The parsed image.</param>
    /// <param name="x">The x coordinate of the top-left corner.</param>
    /// <param name="y">The y coordinate of the top-left corner.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    private PdfPageCanvas AddImage(PngImage image, double x, double y, double width, double height)
    {
        operations.Add(new ImageOperation(image, x, y, width, height));
        return this;
    }

    /// <summary>Checks that a position can be written to the page.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="xName">The parameter name of the x coordinate.</param>
    /// <param name="yName">The parameter name of the y coordinate.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a finite number.</exception>
    private static void ValidatePosition(double x, double y, string xName, string yName)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(xName, x, "The x coordinate must be a finite number.");
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(yName, y, "The y coordinate must be a finite number.");
        }
    }
}

/// <summary>One drawing operation of a page.</summary>
internal interface IPageOperation;

/// <summary>One line of text at a position.</summary>
/// <param name="Text">The text.</param>
/// <param name="X">The x coordinate of the baseline.</param>
/// <param name="Y">The y coordinate of the baseline.</param>
/// <param name="Typeface">The face the text is set in.</param>
/// <param name="Size">The font size in points.</param>
/// <param name="Color">The fill colour.</param>
/// <param name="CharacterSpacing">Extra space after every glyph, in points.</param>
/// <param name="WordSpacing">Extra space after every space character, in points.</param>
/// <param name="HorizontalScale">The horizontal scale as a percentage.</param>
/// <param name="Rise">The baseline offset, in points.</param>
internal sealed record TextOperation(
    string Text,
    double X,
    double Y,
    PdfTypeface Typeface,
    double Size,
    PdfColor Color,
    double CharacterSpacing = 0d,
    double WordSpacing = 0d,
    double HorizontalScale = 100d,
    double Rise = 0d) : IPageOperation;

/// <summary>One image at a position and size.</summary>
/// <param name="Image">The parsed image.</param>
/// <param name="X">The x coordinate of the top-left corner.</param>
/// <param name="Y">The y coordinate of the top-left corner.</param>
/// <param name="Width">The width in points.</param>
/// <param name="Height">The height in points.</param>
internal sealed record ImageOperation(
    PngImage Image,
    double X,
    double Y,
    double Width,
    double Height) : IPageOperation;

/// <summary>One path and the fill and stroke that finish it.</summary>
/// <param name="Path">The path.</param>
/// <param name="Fill">The fill, or null for no fill.</param>
/// <param name="Stroke">The stroke, or null for no outline.</param>
internal sealed record PathOperation(
    PdfPath Path,
    PdfPaint? Fill,
    PdfStroke? Stroke) : IPageOperation;

/// <summary>A saved graphics state (<c>q</c>).</summary>
internal sealed record SaveStateOperation : IPageOperation;

/// <summary>A restored graphics state (<c>Q</c>).</summary>
internal sealed record RestoreStateOperation : IPageOperation;

/// <summary>A clipping path (<c>W</c> or <c>W*</c> followed by <c>n</c>).</summary>
/// <param name="Path">The clipping path.</param>
/// <param name="FillRule">The rule that decides what is inside.</param>
internal sealed record ClipOperation(
    PdfPath Path,
    PdfFillRule FillRule) : IPageOperation;

/// <summary>A transform concatenated onto the current one (<c>cm</c>).</summary>
/// <param name="Matrix">The transform, in the canvas's top-left coordinate system.</param>
internal sealed record TransformOperation(SharpCorePDF.Domain.ValueObjects.PdfMatrix Matrix) : IPageOperation;
