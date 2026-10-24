using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the form write path through the shape a caller uses: a source, an immutable plan, a
/// result, and a read-back through the public read side.
/// </summary>
/// <remarks>
/// The engine options enable <see cref="PdfEngineOptions.VerifyOutput"/>, so every edit also proves
/// that the document it wrote can be read back. The assertions then go through
/// <see cref="PdfPageContentReader.ReadFormFieldsAsync"/>, which is the same field tree a caller reads,
/// so a form that only looks right in the bytes it was built from does not pass.
/// </remarks>
public sealed partial class PdfFormEditorTests
{
    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default with { VerifyOutput = true };
    private static readonly PdfDocumentEditorService Editor = new(new PdfDocumentReader(Options), new PdfEditEngine(Options));
    private static readonly PdfContentReaderService Content = new(new PdfPageContentReader(Options));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfTypeface Helvetica => PdfTypeface.Standard(StandardFont.Helvetica);

    /// <summary>A one page document with no form at all.</summary>
    /// <returns>The bytes of the document.</returns>
    private static byte[] BlankPage() => PdfDocumentBuilder.Create()
        .AddPage(PdfPageSize.A4, page => page.DrawText("body", 72, 700, Helvetica, 12))
        .ToArray();

    private static PdfByteArraySource Source(byte[] bytes) => new(bytes, "form.pdf");

    private static async Task<(byte[] Bytes, PdfEditResult Result)> ApplyAsync(byte[] bytes, PdfEditPlan plan)
    {
        using var destination = new MemoryStream();
        var result = await Editor.ApplyAsync(Source(bytes), plan, destination, cancellationToken: Token);
        return (destination.ToArray(), result);
    }

    private static async Task<List<PdfFormFieldInfo>> FieldsAsync(byte[] bytes) =>
        [.. (await Content.ReadFormFieldsAsync(Source(bytes), cancellationToken: Token)).Items];

    /// <summary>The catalog of a document, as the bytes state it.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The catalog dictionary.</returns>
    private static PdfDictionary Catalog(byte[] bytes) =>
        Reader(bytes).Catalog ?? throw new InvalidOperationException("The document states no catalog.");

    /// <summary>The form of a document, as the catalog names it.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The form, or <see langword="null"/> for a document that keeps none.</returns>
    private static PdfDictionary? AcroForm(byte[] bytes) =>
        Reader(bytes).ResolveDictionary(Catalog(bytes).Get("AcroForm"));

    /// <summary>Opens a document the way the engine does.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The reader.</returns>
    private static PdfFileReader Reader(byte[] bytes) => new(bytes, "form.pdf", Options);

    /// <summary>A text field, a check box, a choice and a push button are written and read back.</summary>
    [Fact]
    public async Task Fills_and_reads_back_form_values()
    {
        var (edited, result) = await ApplyAsync(
            BlankPage(),
            PdfEditPlan.Create().SetForm(PdfForm.Create(
                PdfFormFields.Text(1, "person.name", new PdfRectangle(72, 640, 300, 664), "Ada Lovelace"),
                PdfFormFields.CheckBox(1, "agree", new PdfRectangle(72, 600, 88, 616), isChecked: true),
                PdfFormFields.Choice(1, "country", new PdfRectangle(72, 560, 220, 584), ["NL", "BE", "DE"], "BE", isCombo: true),
                PdfFormFields.PushButton(1, "submit", new PdfRectangle(72, 520, 200, 548), "Send"))));

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.FormAppearanceGenerated);

        var fields = await FieldsAsync(edited);

        Assert.Equal(["person.name", "agree", "country", "submit"], fields.Select(field => field.Name));
        Assert.Equal(["Tx", "Btn", "Ch", "Btn"], fields.Select(field => field.FieldType));
        Assert.Equal("Ada Lovelace", fields[0].Value);
        Assert.Equal("BE", fields[2].Value);
        Assert.Equal(1, fields[3].Widgets[0].PageNumber);
        Assert.Equal(1, fields[0].Widgets[0].PageNumber);

        // A form the engine wrote states that it does not need appearances a viewer generates: every
        // widget carries the one the engine drew.
        var form = AcroForm(edited);

        Assert.NotNull(form);
        Assert.False(((PdfBoolean)form.Get("NeedAppearances")!).Value);
        Assert.Equal(4, ((PdfArray)form.Get("Fields")!).Count);
    }

    /// <summary>A field the document carries is filled where it stands, and the rest is untouched.</summary>
    [Fact]
    public async Task Fills_a_field_the_document_already_carries()
    {
        var (edited, _) = await ApplyAsync(
            PdfContentFixtures.Form(),
            PdfEditPlan.Create().SetFormField(
                PdfFormFields.Text(1, "empty", new PdfRectangle(0, 0, 100, 20), "filled in place")));

        var fields = await FieldsAsync(edited);

        Assert.Equal("filled in place", Assert.Single(fields, field => field.Name == "empty").Value);
        Assert.Equal("Alice", Assert.Single(fields, field => field.Name == "person.name").Value);
        Assert.Equal(["person.name", "group", "sig", "empty"], fields.Select(field => field.Name));

        // The widget that was already there is kept: filling a field does not move it.
        Assert.Equal(
            new PdfRectangle(10, 10, 110, 30),
            Assert.Single(fields, field => field.Name == "person.name").Widgets[0].Rectangle);
    }

    /// <summary>A field the document states as read-only is refused, with the name of the field.</summary>
    [Fact]
    public async Task Refuses_a_read_only_field()
    {
        using var destination = new MemoryStream();
        var refusal = await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            Source(PdfContentFixtures.Form()),
            PdfEditPlan.Create().SetFormField(
                PdfFormFields.Text(1, "person.name", new PdfRectangle(0, 0, 100, 20), "Mallory")),
            destination,
            cancellationToken: Token));

        Assert.Contains("person.name", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("read-only", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, destination.Length);
    }

    /// <summary>A signature field is not filled: signing is a surface of its own.</summary>
    [Fact]
    public async Task Refuses_an_unsupported_field_type()
    {
        using var destination = new MemoryStream();
        var refusal = await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            Source(PdfContentFixtures.Form()),
            PdfEditPlan.Create().SetFormField(
                PdfFormFields.Text(1, "sig", new PdfRectangle(0, 0, 100, 20), "signed")),
            destination,
            cancellationToken: Token));

        Assert.Contains("sig", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("signature", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, destination.Length);
    }

    /// <summary>Flattening draws every widget into the page and takes the form off the document.</summary>
    [Fact]
    public async Task Flattens_a_form_without_a_form_viewer()
    {
        var (edited, result) = await ApplyAsync(PdfContentFixtures.Form(), PdfEditPlan.Create().FlattenForm());

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.OutputVerified);

        // The fields that stood on the page are gone from the form. Two stay: the field that is drawn
        // nowhere has no widget to flatten, and a signature field has no static appearance this writer
        // can draw, so it is left interactive rather than dropped without a mark.
        Assert.Equal(["sig", "empty"], (await FieldsAsync(edited)).Select(field => field.Name));

        var page = FirstPage(edited);

        // Only the signature widget is still an annotation: every other widget became content.
        Assert.Equal(1, page.GetArray("Annots")!.Count);
        Assert.Equal(2, page.GetArray("Contents")!.Count);

        // The operators that draw the widgets are in the document, which is what makes the value
        // visible to a reader that knows nothing about forms.
        var bytes = System.Text.Encoding.Latin1.GetString(edited);

        Assert.Contains("/SCField1 Do", bytes, StringComparison.Ordinal);
        Assert.Contains("/SCField4 Do", bytes, StringComparison.Ordinal);
    }

    /// <summary>A reset puts every field back to the value the document states as its default.</summary>
    [Fact]
    public async Task Resets_the_form_to_its_default_values()
    {
        var (edited, _) = await ApplyAsync(PdfContentFixtures.Form(), PdfEditPlan.Create().ResetForm());
        var fields = await FieldsAsync(edited);

        // The field carried /V (Alice) and /DV (Bob): the reset restores the default, even though the
        // field is read-only, because a reset restores what the document said.
        Assert.Equal("Bob", Assert.Single(fields, field => field.Name == "person.name").Value);

        // A field with no default is emptied rather than left with a value nobody stated.
        Assert.Null(Assert.Single(fields, field => field.Name == "empty").Value);
    }

    /// <summary>The XFA packet of a source is removed and the removal is reported.</summary>
    [Fact]
    public async Task Removes_the_xfa_form_and_says_so()
    {
        var (edited, result) = await ApplyAsync(
            FormWithXfa(),
            PdfEditPlan.Create().SetFormField(
                PdfFormFields.Text(1, "name", new PdfRectangle(0, 0, 100, 20), "new value")));

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.XfaFormRemoved);
        Assert.Equal("new value", Assert.Single(await FieldsAsync(edited), field => field.Name == "name").Value);
        Assert.Null(AcroForm(edited)!.Get("XFA"));
    }

    /// <summary>The first page of a document, as the bytes state it.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The page dictionary.</returns>
    private static PdfDictionary FirstPage(byte[] bytes) => Reader(bytes).Pages[0].Dictionary;

    /// <summary>A one page document whose form carries an XFA packet beside one text field.</summary>
    /// <returns>The bytes of the document.</returns>
    private static byte[] FormWithXfa()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var field = builder.Reserve();
        var widget = builder.Reserve();
        var form = builder.Reserve();

        builder.Set(widget, $" << /Type /Annot /Subtype /Widget /Rect [10 10 110 30] /Parent {field} 0 R >>");
        builder.Set(field, $"<< /T (name) /FT /Tx /V (old) /Kids [{widget} 0 R] >>");
        builder.Set(form, $"<< /Fields [{field} 0 R] /XFA (an xdp packet) >>");
        builder.Set(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Annots [{widget} 0 R] >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /AcroForm {form} 0 R >>");

        return builder.Build(catalog);
    }
}

