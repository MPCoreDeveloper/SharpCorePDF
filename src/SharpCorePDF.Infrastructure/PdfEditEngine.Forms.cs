using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Annotations;
using SharpCorePDF.Infrastructure.Forms;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The form half of <see cref="PdfEditEngine"/>: the fields the plan fills, creates, resets and
/// flattens, written into a form of the result.
/// </summary>
/// <remarks>
/// <para>
/// The form is written before the pages are, because a page names the widgets that stand on it in its
/// <c>/Annots</c> array, and a flattened field draws an appearance the page's resources must also
/// name. The catalog then names the <c>/AcroForm</c> dictionary, or names none when the last field was
/// flattened away.
/// </para>
/// <para>
/// A field the document carries is filled in place — the document's own rectangle, ancestors and
/// widgets survive — and a name it does not carry becomes a new field with a generated appearance.
/// </para>
/// </remarks>
public sealed partial class PdfEditEngine
{
    /// <summary>What a plan asks of the form, as the engine resolves it against the pages.</summary>
    private sealed class FormPlan
    {
        /// <summary>Gets a value indicating whether the plan states a form operation at all.</summary>
        public bool Touched { get; set; }

        /// <summary>Gets the fields to fill, with the index of the operation that stated them.</summary>
        public List<(int Index, PdfFormField Field)> Fields { get; } = [];

        /// <summary>Gets or sets the style a field that states none of its own is drawn in.</summary>
        public PdfStandardTextStyle? DefaultStyle { get; set; }

        /// <summary>Gets or sets a value indicating whether every field goes back to its default value.</summary>
        public bool Reset { get; set; }

        /// <summary>Gets the one-based pages whose widgets are drawn into the page.</summary>
        public HashSet<int> FlattenPages { get; } = [];

        /// <summary>Gets or sets a value indicating whether every page is flattened.</summary>
        public bool FlattenAll { get; set; }
    }

    /// <summary>Gets the pages whose widgets are drawn into the page.</summary>
    /// <param name="plan">What the plan asks of the form.</param>
    /// <param name="layout">The pages in the state the plan leaves them in.</param>
    /// <returns>The one-based page numbers, in order.</returns>
    private static IEnumerable<int> FlattenTargets(FormPlan plan, List<EditedPage> layout) => plan.FlattenAll
        ? Enumerable.Range(1, layout.Count)
        : plan.FlattenPages.Where(pageNumber => pageNumber <= layout.Count).Order();

    /// <summary>Adds a widget the form wrote to the page that carries it.</summary>
    /// <param name="layout">The pages in the state the plan leaves them in.</param>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="widget">The object number of the widget.</param>
    private static void AddWidget(List<EditedPage> layout, int pageNumber, int widget)
    {
        var page = layout[pageNumber - 1];
        var slots = page.AnnotationSlots ??= ResolveSlots(page, pageNumber);
        slots.Add(new AnnotationSlot(null, null, widget));
    }

    /// <summary>Writes the form the plan states.</summary>
    /// <param name="reader">The document being edited.</param>
    /// <param name="copier">The copier that writes that document's objects into the result.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="layout">The pages in the state the plan leaves them in.</param>
    /// <param name="plan">What the plan asks of the form.</param>
    /// <param name="warnings">The list that receives the remarks made while writing.</param>
    /// <returns>The object number of the <c>/AcroForm</c> dictionary, or zero when the result keeps no form.</returns>
    private static int WriteForm(
        PdfFileReader reader,
        PdfObjectCopier copier,
        PdfWriter writer,
        List<EditedPage> layout,
        FormPlan plan,
        List<PdfWarning> warnings)
    {
        if (!plan.Touched)
        {
            return 0;
        }

        var form = new AcroFormWriter(reader, copier, writer);
        var style = plan.DefaultStyle ?? PdfStandardTextStyle.Default;
        var pageTargets = new Dictionary<int, int>(layout.Count);

        for (var index = 0; index < layout.Count; index++)
        {
            pageTargets[index + 1] = layout[index].TargetNumber;
        }

        if (form.HasXfa)
        {
            warnings.Add(new(
                PdfWarningKind.XfaFormRemoved,
                $"'{reader.SourceName}' carried an XFA form, which was removed: the fields this edit writes are AcroForm fields, and a viewer that honoured the XFA packet would show its fields instead."));
        }

        foreach (var (index, field) in plan.Fields)
        {
            var widget = form.Fill(index, reader.SourceName, field, field.TextStyle ?? style, pageTargets);

            if (widget != 0)
            {
                AddWidget(layout, field.PageNumber, widget);
            }
        }

        foreach (var entry in form.Fields.Where(entry => entry.HasActions))
        {
            // An action is copied with the field it belongs to and never run: this library has no
            // JavaScript engine, which is a promise the contract makes rather than a limitation.
            warnings.Add(new(
                PdfWarningKind.FormFieldActionPreserved,
                $"the field '{entry.Name}' of '{reader.SourceName}' carries an action, which was copied as it stands and is never executed."));
        }

        foreach (var pageNumber in plan.Fields.Select(entry => entry.Field.PageNumber).Distinct().Order())
        {
            warnings.Add(new(
                PdfWarningKind.FormAppearanceGenerated,
                $"the fields of page {pageNumber} carry an appearance stream the engine drew, so a viewer that does not synthesise one still shows their values."));
        }

        if (plan.Reset)
        {
            form.Reset(style);
        }

        foreach (var pageNumber in FlattenTargets(plan, layout))
        {
            FlattenPage(form, writer, layout[pageNumber - 1], pageNumber, style);
        }

        return form.Write();
    }

    /// <summary>Draws the widgets of one page into its content and takes them off the page.</summary>
    /// <param name="form">The form being written.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="page">The page.</param>
    /// <param name="pageNumber">The one-based page number, for diagnostics.</param>
    /// <param name="style">The style an appearance is generated in when a widget states none.</param>
    private static void FlattenPage(
        AcroFormWriter form,
        PdfWriter writer,
        EditedPage page,
        int pageNumber,
        PdfStandardTextStyle style)
    {
        var slots = page.AnnotationSlots ??= ResolveSlots(page, pageNumber);
        var declared = DeclaredResources(page.Reader, page.Source);
        var xObjects = page.Reader.ResolveDictionary(declared?.Get("XObject")) ?? new PdfDictionary();
        var next = 0;
        var kept = new List<AnnotationSlot>(slots.Count);
        var drawn = new List<(string Name, Flattenable Appearance)>();

        foreach (var slot in slots)
        {
            if (slot.Source is PdfReference reference && form.TryDraw(reference.ObjectNumber, style) is { } appearance)
            {
                drawn.Add((FreeName(xObjects, "SCField", ref next), appearance));
                form.Remove(reference.ObjectNumber);
                continue;
            }

            kept.Add(slot);
        }

        page.AnnotationSlots = kept;

        if (drawn.Count == 0)
        {
            return;
        }

        var content = new AppearanceContent();
        content.Line("q");

        foreach (var (name, appearance) in drawn)
        {
            var box = appearance.Box;
            var rectangle = appearance.Rectangle;
            var scaleX = box.Width > 0.0 ? rectangle.Width / box.Width : 1.0;
            var scaleY = box.Height > 0.0 ? rectangle.Height / box.Height : 1.0;

            // The appearance is written in its own box, so it is scaled onto the rectangle the widget
            // stood on and moved so the corner of the box lands on the corner of that rectangle.
            content
                .Number(scaleX).Number(0).Number(0).Number(scaleY)
                .Number(rectangle.Left - (box.Left * scaleX))
                .Number(rectangle.Bottom - (box.Bottom * scaleY))
                .Line(" cm")
                .Name(name).Line("Do");
        }

        content.Line("Q");

        page.FlattenStreamNumber = writer.AllocateObject();
        writer.WriteObject(page.FlattenStreamNumber, new PdfStream(new PdfDictionary(), content.ToBytes()));

        foreach (var (name, appearance) in drawn)
        {
            page.FlattenAppearances.Add((name, appearance.StreamNumber));
        }
    }
}
