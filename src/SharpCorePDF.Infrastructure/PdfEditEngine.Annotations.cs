using System.Globalization;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Annotations;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The annotation half of <see cref="PdfEditEngine"/>: what a page carries on top of its content, added,
/// replaced and removed in the page's <c>/Annots</c> array.
/// </summary>
/// <remarks>
/// <para>
/// The annotations a page already had are copied, by reference, exactly as the copier copies them
/// anywhere else; the ones the plan creates are written here, each with an appearance stream of its
/// own. The content of the page is never touched, which is the guarantee the contract states: an
/// annotation is something a viewer draws over a page, not something a page draws.
/// </para>
/// <para>
/// The page's array is rebuilt rather than mutated in place, because the objects it names belong to the
/// source document and are never modified: a page that shared an annotation with another page keeps it
/// there, and an annotation the plan removes simply stops being reachable — and therefore stops being
/// written at all.
/// </para>
/// </remarks>
public sealed partial class PdfEditEngine
{
    /// <summary>Adds one annotation change to a page, validating it against that page's own array.</summary>
    /// <param name="layout">The pages in the state the operation leaves them in.</param>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="change">The change to record.</param>
    /// <param name="index">The index of the operation, for diagnostics.</param>
    /// <param name="sourceName">The name of the document being edited.</param>
    /// <exception cref="PdfEditPlanValidationException">The annotation an update or a removal names is not on the page.</exception>
    private static void ApplyAnnotation(
        List<EditedPage> layout,
        int pageNumber,
        AnnotationChange change,
        int index,
        string sourceName)
    {
        EnsurePage(layout, pageNumber, index, sourceName);

        var page = layout[pageNumber - 1];
        page.AnnotationChanges.Add(change);

        // The slots are what the page will actually carry, so resolving them now proves the update or
        // the removal names an annotation that exists before a single byte is written.
        page.AnnotationSlots = ResolveSlots(page, pageNumber);
    }

    /// <summary>Writes the annotation objects of one page and resolves the array the page will carry.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="page">The page.</param>
    /// <param name="context">The annotations of the whole edit.</param>
    /// <param name="layout">The pages in output order.</param>
    /// <param name="pageNumber">The one-based number of the page in the result.</param>
    /// <param name="warnings">The list that receives the remarks made while writing.</param>
    private static void WritePageAnnotations(
        PdfWriter writer,
        EditedPage page,
        AnnotationContext context,
        List<EditedPage> layout,
        int pageNumber,
        List<PdfWarning> warnings)
    {
        if (page.AnnotationChanges.Count == 0)
        {
            // Nothing was asked of this page, so its array is copied by the copier along with the rest
            // of the dictionary.
            return;
        }

        var slots = page.AnnotationSlots ??= ResolveSlots(page, pageNumber);
        var kept = new List<AnnotationSlot>(slots.Count);
        var regenerated = false;

        foreach (var slot in slots)
        {
            if (slot.Change is not { } change)
            {
                kept.Add(slot);
                continue;
            }

            if (change.WrittenNumber == 0)
            {
                change.WrittenNumber = WriteAnnotation(
                    writer, page, change, context, layout, pageNumber, warnings, ref regenerated);
            }

            if (change.WrittenNumber != 0)
            {
                kept.Add(slot);
            }
        }

        if (regenerated)
        {
            warnings.Add(new(
                PdfWarningKind.AnnotationAppearanceRegenerated,
                $"the annotations of page {pageNumber} carry an appearance stream the engine drew, so a viewer that does not synthesise one still shows them."));
        }

        page.AnnotationSlots = kept;
    }

    /// <summary>Builds the <c>/Annots</c> array of a page from the slots that survived the edit.</summary>
    /// <param name="copier">The copier of the document the page comes from.</param>
    /// <param name="page">The page.</param>
    /// <returns>The array, or <see langword="null"/> for a page that carries no annotation.</returns>
    private static PdfArray? BuildAnnots(PdfObjectCopier copier, EditedPage page)
    {
        if (page.AnnotationSlots is not { } slots)
        {
            return null;
        }

        var array = new PdfArray();

        foreach (var slot in slots)
        {
            if (slot.Change is { WrittenNumber: > 0 } written)
            {
                array.Add(new PdfReference(written.WrittenNumber, 0));
            }
            else if (slot.DirectNumber > 0)
            {
                array.Add(new PdfReference(slot.DirectNumber, 0));
            }
            else if (slot.Source is { } source)
            {
                array.Add(copier.Clone(source));
            }
        }

        return array.Count == 0 ? null : array;
    }

    /// <summary>
    /// Resolves the annotations a page carries after its changes: the ones the source states, minus the
    /// ones removed, with the ones replaced and added in their place.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="pageNumber">The one-based page number, for diagnostics.</param>
    /// <returns>The slots, in the order the page states them.</returns>
    /// <exception cref="PdfEditPlanValidationException">An update or a removal names an annotation the page does not carry.</exception>
    private static List<AnnotationSlot> ResolveSlots(EditedPage page, int pageNumber)
    {
        var slots = new List<AnnotationSlot>();
        var annots = page.Reader.ResolveArray(page.Source.Dictionary.Get("Annots"));

        if (annots is not null)
        {
            foreach (var item in annots.Items)
            {
                slots.Add(new AnnotationSlot(item, NameOf(page.Reader, item)));
            }
        }

        foreach (var change in page.AnnotationChanges)
        {
            var index = IndexOf(slots, change.Id);

            switch (change.Kind)
            {
                case AnnotationChangeKind.Add:
                    slots.Add(new AnnotationSlot(null, change.Name) { Change = change });
                    break;

                case AnnotationChangeKind.Update when index >= 0:
                    slots[index] = new AnnotationSlot(null, change.Id) { Change = change };
                    break;

                case AnnotationChangeKind.Remove when index >= 0:
                    slots.RemoveAt(index);
                    break;

                case AnnotationChangeKind.Update:
                    throw new PdfEditPlanValidationException(
                        $"page {pageNumber} carries no annotation '{change.Id}', so there is nothing to update. The identifier is the one the annotation was written with, its /NM entry.");

                case AnnotationChangeKind.Remove:
                    throw new PdfEditPlanValidationException(
                        $"page {pageNumber} carries no annotation '{change.Id}', so there is nothing to remove. The identifier is the one the annotation was written with, its /NM entry.");
            }
        }

        return slots;
    }

    /// <summary>Finds the slot that carries an identifier.</summary>
    /// <param name="slots">The slots of a page.</param>
    /// <param name="name">The identifier.</param>
    /// <returns>The index of the first slot that carries it, or <c>-1</c>.</returns>
    private static int IndexOf(List<AnnotationSlot> slots, string? name) =>
        name is null ? -1 : slots.FindIndex(slot => string.Equals(slot.Name, name, StringComparison.Ordinal));

    /// <summary>Reads the identifier a source annotation states in its <c>/NM</c> entry.</summary>
    /// <param name="reader">The document the annotation belongs to.</param>
    /// <param name="item">The annotation as the page's array states it.</param>
    /// <returns>The identifier, or <see langword="null"/> for an annotation that states none.</returns>
    private static string? NameOf(PdfFileReader reader, PdfObject item) =>
        reader.ResolveDictionary(item) is { } dictionary
            && reader.Resolve(dictionary.Get("NM")) is PdfString name
            ? name.ToText()
            : null;

    /// <summary>Writes one annotation and its appearance stream.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="page">The page the annotation belongs to.</param>
    /// <param name="change">The change that created or replaced the annotation.</param>
    /// <param name="context">The annotations of the whole edit.</param>
    /// <param name="layout">The pages in output order.</param>
    /// <param name="pageNumber">The one-based number of the page in the result.</param>
    /// <param name="warnings">The list that receives the remarks made while writing.</param>
    /// <param name="regenerated">Set when an appearance was drawn by the engine.</param>
    /// <returns>The object number of the annotation, or zero when it cannot be written.</returns>
    private static int WriteAnnotation(
        PdfWriter writer,
        EditedPage page,
        AnnotationChange change,
        AnnotationContext context,
        List<EditedPage> layout,
        int pageNumber,
        List<PdfWarning> warnings,
        ref bool regenerated)
    {
        if (change.Annotation is not { } annotation)
        {
            return 0;
        }

        var area = page.CropBox ?? page.MediaBox;
        var rectangle = Intersect(annotation.Rectangle, area);

        if (rectangle is null)
        {
            warnings.Add(new(
                PdfWarningKind.AnnotationClipped,
                $"the {annotation.Kind} annotation '{change.Name}' lies outside page {pageNumber} and was not written."));
            return 0;
        }

        if (!Covers(rectangle, annotation.Rectangle))
        {
            warnings.Add(new(
                PdfWarningKind.AnnotationClipped,
                $"the {annotation.Kind} annotation '{change.Name}' reached outside page {pageNumber} and was clipped to {rectangle}."));
        }

        var destination = annotation.Kind == PdfAnnotationKind.Link ? ResolveDestination(annotation, layout) : null;

        if (annotation.Kind == PdfAnnotationKind.Link && destination is null)
        {
            warnings.Add(new(
                PdfWarningKind.AnnotationDestinationUnresolved,
                $"the link annotation '{change.Name}' on page {pageNumber} points at a page the edit removed, so the link was not written."));
            return 0;
        }

        var clipped = annotation with { Rectangle = rectangle };

        // A caller-supplied appearance names its own face, and that is the face written as /Helv; the text
        // style is what the engine's own appearance draws with.
        var font = UsesFont(clipped)
            ? context.FontOf(clipped.Appearance?.Font ?? clipped.TextStyle?.Font ?? StandardFont.Helvetica)
            : 0;
        var state = clipped.Opacity < 1.0 ? WriteOpacity(writer, clipped.Opacity) : 0;
        var appearance = AppearanceStreams.Build(clipped, font, state);
        var appearanceNumber = 0;

        if (appearance is not null)
        {
            if (clipped.Appearance is null)
            {
                regenerated = true;
            }

            appearanceNumber = writer.AllocateObject();
            writer.WriteObject(appearanceNumber, appearance);
        }

        var number = writer.AllocateObject();
        writer.WriteObject(
            number,
            BuildAnnotationDictionary(clipped, change.Name, appearanceNumber, destination, context));
        return number;
    }

    /// <summary>Builds the dictionary of one annotation.</summary>
    /// <param name="annotation">The annotation.</param>
    /// <param name="name">The identifier it is written with.</param>
    /// <param name="appearanceNumber">The object number of its appearance, or zero.</param>
    /// <param name="destination">The action of a link, or <see langword="null"/>.</param>
    /// <param name="context">The annotations of the whole edit.</param>
    /// <returns>The dictionary.</returns>
    private static PdfDictionary BuildAnnotationDictionary(
        PdfAnnotation annotation,
        string name,
        int appearanceNumber,
        PdfDictionary? destination,
        AnnotationContext context)
    {
        var flags = annotation.Flags | PdfAnnotationFlags.Print;
        var dictionary = new PdfDictionary()
            .SetName("Type", "Annot")
            .SetName("Subtype", SubtypeName(annotation.Kind))
            .Set("Rect", AppearanceStreams.ToArray(annotation.Rectangle))
            .SetInteger("F", (int)flags)
            .Set("NM", PdfString.FromText(name))
            .Set("M", PdfString.FromText(PdfDate.Format(context.Modified)));

        if (annotation.Contents is { } contents)
        {
            dictionary.Set("Contents", PdfString.FromText(contents));
        }

        if (annotation.Color is { } color)
        {
            dictionary.Set("C", ColorArray(color));
        }

        if (annotation.Opacity < 1.0)
        {
            dictionary.Set("CA", new PdfReal(annotation.Opacity));
        }

        if (annotation.Border is { } border)
        {
            dictionary.Set("Border", new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfReal(border.Width)]));

            if (border.Style != PdfBorderStyle.Solid || border.Dash.Count > 0)
            {
                var style = new PdfDictionary()
                    .Set("W", new PdfReal(border.Width))
                    .SetName("S", border.Style switch
                    {
                        PdfBorderStyle.Dashed => "D",
                        PdfBorderStyle.Beveled => "B",
                        PdfBorderStyle.Inset => "I",
                        PdfBorderStyle.Underline => "U",
                        _ => "S",
                    });

                if (border.Dash.Count > 0)
                {
                    style.Set("D", new PdfArray(border.Dash.Select(length => (PdfObject)new PdfReal(length))));
                }

                dictionary.Set("BS", style);
            }
        }

        if (appearanceNumber != 0)
        {
            dictionary.Set("AP", new PdfDictionary().Set("N", new PdfReference(appearanceNumber, 0)));
        }

        if (destination is { } action)
        {
            dictionary.Set("A", action);
        }

        if (annotation.IconName is { Length: > 0 } icon)
        {
            dictionary.Set("Name", new PdfName(icon));
        }

        if (annotation.Quads.Count > 0)
        {
            dictionary.Set("QuadPoints", QuadPoints(annotation.Quads));
        }

        if (annotation.LineFrom is { } from && annotation.LineTo is { } to)
        {
            dictionary.Set("L", new PdfArray(
            [
                new PdfReal(from.X),
                new PdfReal(from.Y),
                new PdfReal(to.X),
                new PdfReal(to.Y),
            ]));
        }

        if (annotation.Strokes.Count > 0)
        {
            dictionary.Set(
                "InkList",
                new PdfArray(annotation.Strokes.Select(stroke => (PdfObject)new PdfArray(
                    stroke.SelectMany(point => new PdfObject[] { new PdfReal(point.X), new PdfReal(point.Y) })))));
        }

        return dictionary;
    }

    /// <summary>Builds the action a link annotation carries.</summary>
    /// <param name="annotation">The link annotation.</param>
    /// <param name="layout">The pages in output order.</param>
    /// <returns>The action, or <see langword="null"/> when the destination no longer exists.</returns>
    private static PdfDictionary? ResolveDestination(PdfAnnotation annotation, List<EditedPage> layout)
    {
        if (annotation.Destination is not { } destination)
        {
            return null;
        }

        if (destination.Uri is { Length: > 0 } uri)
        {
            return new PdfDictionary()
                .SetName("S", "URI")
                .Set("URI", PdfString.FromText(uri));
        }

        if (destination.PageNumber is not { } page || page < 1 || page > layout.Count)
        {
            return null;
        }

        var target = new PdfReference(layout[page - 1].TargetNumber, 0);
        PdfArray viewer;

        if (destination.Area is { } area && area.Width > 0 && area.Height > 0)
        {
            viewer = new PdfArray(
            [
                target,
                new PdfName("FitR"),
                new PdfReal(area.Left),
                new PdfReal(area.Bottom),
                new PdfReal(area.Right),
                new PdfReal(area.Top),
            ]);
        }
        else if (destination.Area is { } corner)
        {
            viewer = new PdfArray(
            [
                target,
                new PdfName("XYZ"),
                new PdfReal(corner.Left),
                new PdfReal(corner.Top),
                PdfNull.Instance,
            ]);
        }
        else
        {
            viewer = new PdfArray([target, new PdfName("Fit")]);
        }

        return new PdfDictionary().SetName("S", "GoTo").Set("D", viewer);
    }

    /// <summary>Writes the graphics state that applies an annotation's opacity.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="opacity">The opacity, from zero to one.</param>
    /// <returns>The object number of the graphics state.</returns>
    private static int WriteOpacity(PdfWriter writer, double opacity)
    {
        var number = writer.AllocateObject();
        writer.WriteObject(
            number,
            new PdfDictionary()
                .SetName("Type", "ExtGState")
                .Set("ca", new PdfReal(opacity))
                .Set("CA", new PdfReal(opacity)));
        return number;
    }

    /// <summary>Gets the part of a rectangle that lies inside the visible area of a page.</summary>
    /// <param name="rectangle">The rectangle, in page space.</param>
    /// <param name="area">The visible area, in page space.</param>
    /// <returns>The intersection, or <see langword="null"/> when the two do not overlap.</returns>
    private static PdfRectangle? Intersect(PdfRectangle rectangle, PdfRectangle area)
    {
        var left = Math.Max(rectangle.Left, area.Left);
        var bottom = Math.Max(rectangle.Bottom, area.Bottom);
        var right = Math.Min(rectangle.Right, area.Right);
        var top = Math.Min(rectangle.Top, area.Top);

        return right <= left || top <= bottom ? null : new PdfRectangle(left, bottom, right, top);
    }

    /// <summary>Tells whether a rectangle covers the whole of another.</summary>
    /// <param name="outer">The rectangle that may cover.</param>
    /// <param name="inner">The rectangle that may be covered.</param>
    /// <returns><see langword="true"/> when no part of <paramref name="inner"/> lies outside.</returns>
    private static bool Covers(PdfRectangle outer, PdfRectangle inner) =>
        outer.Left <= inner.Left && outer.Bottom <= inner.Bottom
        && outer.Right >= inner.Right && outer.Top >= inner.Top;

    /// <summary>Gets the PDF subtype name of an annotation kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name, as the document spells it.</returns>
    private static string SubtypeName(PdfAnnotationKind kind) => kind switch
    {
        PdfAnnotationKind.Text => "Text",
        PdfAnnotationKind.Link => "Link",
        PdfAnnotationKind.FreeText => "FreeText",
        PdfAnnotationKind.Stamp => "Stamp",
        PdfAnnotationKind.Highlight => "Highlight",
        PdfAnnotationKind.Underline => "Underline",
        PdfAnnotationKind.StrikeOut => "StrikeOut",
        PdfAnnotationKind.Square => "Square",
        PdfAnnotationKind.Circle => "Circle",
        PdfAnnotationKind.Line => "Line",
        PdfAnnotationKind.Ink => "Ink",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The annotation kind has no PDF subtype."),
    };

    /// <summary>Tells whether an appearance draws text and therefore names a font.</summary>
    /// <param name="annotation">The annotation.</param>
    /// <returns><see langword="true"/> when the appearance names a font.</returns>
    private static bool UsesFont(PdfAnnotation annotation) =>
        annotation.Appearance?.Font is not null
        || (annotation.Appearance is null && annotation.Kind is PdfAnnotationKind.FreeText or PdfAnnotationKind.Stamp);

    /// <summary>Writes a colour as the three components PDF states.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The array.</returns>
    private static PdfArray ColorArray(PdfColor color) => new(
    [
        new PdfReal(color.Red / 255.0),
        new PdfReal(color.Green / 255.0),
        new PdfReal(color.Blue / 255.0),
    ]);

    /// <summary>Writes the quads of a markup annotation as the eight numbers per quad PDF states.</summary>
    /// <param name="quads">The quads, in page space.</param>
    /// <returns>The array.</returns>
    /// <remarks>
    /// Each quad is written as its upper-left, upper-right, lower-left and lower-right corner, which is
    /// the order the specification states and the order a reader that highlights the text expects.
    /// </remarks>
    private static PdfArray QuadPoints(IReadOnlyList<PdfRectangle> quads)
    {
        var array = new PdfArray();

        foreach (var quad in quads)
        {
            array
                .Add(new PdfReal(quad.Left)).Add(new PdfReal(quad.Top))
                .Add(new PdfReal(quad.Right)).Add(new PdfReal(quad.Top))
                .Add(new PdfReal(quad.Left)).Add(new PdfReal(quad.Bottom))
                .Add(new PdfReal(quad.Right)).Add(new PdfReal(quad.Bottom));
        }

        return array;
    }

    /// <summary>What a plan does to one annotation of a page.</summary>
    private enum AnnotationChangeKind
    {
        /// <summary>An annotation is written on the page.</summary>
        Add,

        /// <summary>An annotation of the page is replaced.</summary>
        Update,

        /// <summary>An annotation leaves the page.</summary>
        Remove,
    }

    /// <summary>
    /// One change of a page's annotations, and the object it was written as.
    /// </summary>
    /// <remarks>
    /// The change travels with the page it was applied to, so a duplicate made after it carries the same
    /// annotation; <see cref="WrittenNumber"/> is what keeps that annotation one object rather than two.
    /// </remarks>
    private sealed class AnnotationChange
    {
        /// <summary>Gets what the plan does.</summary>
        public required AnnotationChangeKind Kind { get; init; }

        /// <summary>Gets the annotation the change writes, for an addition or a replacement.</summary>
        public PdfAnnotation? Annotation { get; init; }

        /// <summary>Gets the identifier an update or a removal names.</summary>
        public string? Id { get; init; }

        /// <summary>Gets the identifier the annotation is written with.</summary>
        public required string Name { get; init; }

        /// <summary>Gets or sets the object number the annotation was written as, or zero before it is.</summary>
        public int WrittenNumber { get; set; }
    }

    /// <summary>One annotation a page carries: either one of the source's, or one the plan wrote.</summary>
    /// <param name="source">The annotation as the source page states it, or <see langword="null"/> for one the plan wrote.</param>
    /// <param name="name">The identifier the annotation carries.</param>
    /// <param name="directNumber">The object number of an object this edit wrote, or zero for one the copier copies.</param>
    private sealed class AnnotationSlot(PdfObject? source, string? name, int directNumber = 0)
    {
        /// <summary>Gets the annotation of the source, which the copier copies.</summary>
        public PdfObject? Source { get; } = source;

        /// <summary>Gets the identifier the annotation carries.</summary>
        public string? Name { get; } = name;

        /// <summary>
        /// Gets the object number of an object this edit wrote, or zero for one that is copied from the
        /// source. A form widget the plan created is written by the form half of the engine, so its
        /// number is already known and must not be copied from a document that never carried it.
        /// </summary>
        public int DirectNumber { get; } = directNumber;

        /// <summary>Gets or sets the change that wrote the annotation, or <see langword="null"/> for one of the source's.</summary>
        public AnnotationChange? Change { get; set; }
    }

    /// <summary>The annotations of one edit: the fonts their appearances share and the moment they state.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="modified">The moment written as the modification date of every annotation.</param>
    private sealed class AnnotationContext(PdfWriter writer, DateTimeOffset modified)
    {
        private readonly Dictionary<StandardFont, int> fonts = [];

        /// <summary>Gets the moment written as the modification date of every annotation.</summary>
        public DateTimeOffset Modified { get; } = modified;

        /// <summary>Gets the object number of a standard font, writing it the first time it is asked for.</summary>
        /// <param name="font">The font.</param>
        /// <returns>The object number.</returns>
        public int FontOf(StandardFont font)
        {
            if (fonts.TryGetValue(font, out var existing))
            {
                return existing;
            }

            var number = writer.AllocateObject();
            writer.WriteObject(
                number,
                new PdfDictionary()
                    .SetName("Type", "Font")
                    .SetName("Subtype", "Type1")
                    .SetName("BaseFont", BaseFont(font))
                    .SetName("Encoding", "WinAnsiEncoding"));
            fonts[font] = number;
            return number;
        }

        /// <summary>Gets the name a standard font is written as.</summary>
        /// <param name="font">The font.</param>
        /// <returns>The base font name.</returns>
        private static string BaseFont(StandardFont font) => font switch
        {
            StandardFont.Helvetica => "Helvetica",
            StandardFont.HelveticaBold => "Helvetica-Bold",
            StandardFont.HelveticaOblique => "Helvetica-Oblique",
            StandardFont.HelveticaBoldOblique => "Helvetica-BoldOblique",
            StandardFont.TimesRoman => "Times-Roman",
            StandardFont.TimesBold => "Times-Bold",
            StandardFont.TimesItalic => "Times-Italic",
            StandardFont.TimesBoldItalic => "Times-BoldItalic",
            StandardFont.Courier => "Courier",
            StandardFont.CourierBold => "Courier-Bold",
            StandardFont.CourierOblique => "Courier-Oblique",
            StandardFont.CourierBoldOblique => "Courier-BoldOblique",
            _ => throw new ArgumentOutOfRangeException(nameof(font), font, "The font is not a standard font."),
        };
    }
}
