using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Edits a document that already exists: an authored page goes in front of it, one page is deleted,
/// one is rotated, one is cropped, and a watermark written from nothing goes over every page.
/// </summary>
/// <remarks>
/// Every operation is recorded in an immutable plan and applied in one pass, so the source is never
/// touched and a plan that cannot be applied is refused before anything is written.
/// </remarks>
internal static class PdfEditExamples
{
    /// <summary>Applies a plan of five operations to the three page sample and reads the result back.</summary>
    /// <param name="context">The documents to edit and the ports to edit them with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task EditPagesAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var heading = PdfTypeface.Standard(StandardFont.Helvetica);
        var output = context.OutputPath("edited.pdf");

        var plan = PdfEditPlan.Create()
            .InsertPage(1, PdfPageSize.A4, page => page
                // Everything on this page is drawn by the library: a stroked frame, a dashed rule, a
                // translucent band and an ellipse, so the example shows vector output and not only text.
                .DrawRectangle(
                    new PdfRectangle(36, 36, 523, 770),
                    fill: null,
                    stroke: new PdfStroke(PdfPaint.Black, width: 1.5))
                .DrawRectangle(
                    new PdfRectangle(60, 640, 360, 700),
                    fill: new PdfPaint(PdfColor.FromRgb(0xEC, 0xEF, 0xF4), alpha: 0.65),
                    stroke: null)
                .DrawLine(
                    new PdfPoint(36, 610),
                    new PdfPoint(559, 610),
                    new PdfStroke(PdfPaint.Black, width: 0.75, dashArray: [3, 3]))
                .DrawEllipse(
                    new PdfRectangle(430, 620, 520, 680),
                    fill: null,
                    stroke: new PdfStroke(PdfPaint.Black, width: 1))
                .DrawText("Edited by SharpCorePDF", 60, 670, heading, 18))
            .RotatePage(2, PageRotation.Clockwise90)
            .DeletePage(4)
            .CropPage(3, new PdfRectangle(36, 36, 806, 559))
            .WithOutput(options => options with
            {
                // A fixed date and producer make two runs produce the same bytes.
                ModificationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Producer = "SharpCorePDF examples",
            });

        PdfEditResult result;

        // The destination is closed before the result is read back: the editor writes the file it is
        // handed, and a reader opened while it is still ours would be the example's own bug.
        await using (var destination = File.Create(output))
        {
            result = await context.Editor.ApplyAsync(
                new PdfFileSource(context.Appendix),
                plan,
                destination,
                cancellationToken: cancellationToken);
        }

        Report.Item("wrote", output);
        Report.Item("operations applied", string.Join(", ", result.Operations));
        Report.Item("pages", result.PageCount);
        Report.Item("bytes written", result.BytesWritten);
        Report.Item("output version", result.OutputVersion);
        Report.Item("warnings", result.Warnings.Count);

        foreach (var warning in result.Warnings)
        {
            Report.Line($"  {warning.Kind}: {warning.Message}");
        }

        var edited = await context.Inspector.InspectAsync(
            new PdfFileSource(output),
            cancellationToken: cancellationToken);

        Report.Item("pages read back", edited.PageCount);
        Report.Item("page 2 rotation", edited.Pages[1].Rotation);
    }

    /// <summary>Stamps a watermark written from nothing over every page, without rewriting their content.</summary>
    /// <param name="context">The document to stamp and the ports to stamp it with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task WatermarkAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var heading = PdfTypeface.Standard(StandardFont.HelveticaBold);
        var output = context.OutputPath("watermarked.pdf");

        // The watermark is a document of its own: one A4 page with the words turned forty-five degrees about
        // the middle of the page. Nothing in it is a PDF feature the document it lands on has to have — it is
        // drawn as content, so the same page could be opened, printed or filed on its own.
        var watermark = PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with
        {
            CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DocumentId = "00112233445566778899AABBCCDDEEFF",
        })
            .AddPage(PdfPageSize.A4, page => page

                // Rotating about the page origin and then moving the group keeps the word on the page's
                // diagonal, which is where a reader expects a stamp to be, without a coordinate per glyph.
                .SaveState()
                .SetTransform(PdfMatrix.Rotate(45).Multiply(PdfMatrix.Translate(120, 290)))
                .DrawText("DRAFT", 0, 0, heading, 96)
                .RestoreState())
            .Build("watermark.pdf");

        // The stamp goes on every page, so the plan holds one operation per page: each names the page it
        // lands on, and the document is opened once for the whole plan rather than once per operation.
        var summary = await context.Inspector.InspectAsync(
            new PdfFileSource(context.Summary),
            cancellationToken: cancellationToken);

        var plan = PdfEditPlan.Create();

        for (var pageNumber = 1; pageNumber <= summary.PageCount; pageNumber++)
        {
            plan = plan.Overlay(
                watermark,
                pageNumber,
                new PdfOverlayOptions
                {
                    // Opacity and blend mode travel in an ExtGState of the layer's own, so neither reaches
                    // the page underneath: its text stays as dark as it was, and the stamp goes behind it.
                    Opacity = 0.18,
                    Placement = PdfOverlayPlacement.Fit,
                    BlendMode = PdfOverlayBlendMode.Multiply,
                });
        }

        plan = plan.WithOutput(options => options with
        {
            ModificationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Producer = "SharpCorePDF examples",
        });

        PdfEditResult result;

        await using (var destination = File.Create(output))
        {
            result = await context.Editor.ApplyAsync(
                new PdfFileSource(context.Summary),
                plan,
                destination,
                cancellationToken: cancellationToken);
        }

        Report.Item("wrote", output);
        Report.Item("operations applied", string.Join(", ", result.Operations));
        Report.Item("pages", result.PageCount);
        Report.Item("bytes written", result.BytesWritten);
        Report.Item("warnings", result.Warnings.Count);

        foreach (var warning in result.Warnings)
        {
            Report.Line($"  {warning.Kind}: {warning.Message}");
        }

        // The text reader walks the form a page paints, so the stamp is part of the text of the page it was
        // placed on: that is what makes the layer content and not a comment. A turned glyph has its own
        // baseline in page space, so the stamp comes back a letter per block, while the sentence the page
        // drew for itself comes back whole — the layer did not rewrite it.
        var stamped = await context.ContentReader.ReadTextAsync(
            new PdfFileSource(output),
            PageRange.Single(1),
            cancellationToken: cancellationToken);

        var words = stamped.Items
            .SelectMany(block => block.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        Report.Item("text blocks on page 1", words.Length);
        Report.Item("stamp letters read back", string.Concat(words.Where(word => word.Length == 1)));
        Report.Line("page 1 text after stamping:");

        foreach (var word in words)
        {
            Report.Line($"  {word}");
        }
    }

    /// <summary>Comments on the document without touching a byte of its content.</summary>
    /// <param name="context">The document to comment on and the ports to comment with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task AnnotateAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var output = context.OutputPath("annotated.pdf");

        // An annotation is something a viewer draws over a page, not something the page draws: the
        // content stream of every page keeps its bytes, and the page's /Annots array is the only entry
        // that changes. Each mark gets an appearance stream of its own, so a viewer that does not
        // synthesise one — and a printer, which never does — still shows it.
        var plan = PdfEditPlan.Create()
            .AddAnnotation(1, PdfAnnotations.Highlight(
                [new PdfRectangle(72, 700, 320, 716)],
                PdfColor.FromHex("#FFF176")) with { Name = "summary-highlight" })
            .AddAnnotation(1, PdfAnnotations.Text(
                new PdfRectangle(560, 720, 582, 742),
                "Reviewed against the checklist on 24 October."))
            .AddAnnotation(1, PdfAnnotations.FreeText(
                new PdfRectangle(72, 640, 340, 672),
                "Free text drawn in the margin",
                new PdfStandardTextStyle(StandardFont.HelveticaOblique, 11, PdfColor.FromHex("#1F3A5F"))))
            .AddAnnotation(1, PdfAnnotations.Stamp(
                new PdfRectangle(420, 560, 560, 610),
                "Approved"))
            .AddAnnotation(1, PdfAnnotations.Link(
                new PdfRectangle(72, 520, 300, 540),
                "https://github.com/MPCoreDeveloper/SharpCorePDF"))
            .AddAnnotation(1, PdfAnnotations.Link(
                new PdfRectangle(72, 495, 300, 515),
                new PdfDestination { PageNumber = 2 }))

            // An update names the annotation it replaces, and a removal takes one away: both refer to the
            // identifier the annotation was written with, and neither rewrites a page that is not named.
            .UpdateAnnotation(
                1,
                "summary-highlight",
                PdfAnnotations.Highlight([new PdfRectangle(72, 700, 320, 716)], PdfColor.FromHex("#FFE082"))
                    with { Name = "summary-highlight", Contents = "Reviewed" })
            .WithOutput(options => options with
            {
                ModificationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Producer = "SharpCorePDF examples",
            });

        PdfEditResult result;

        await using (var destination = File.Create(output))
        {
            result = await context.Editor.ApplyAsync(
                new PdfFileSource(context.Summary),
                plan,
                destination,
                cancellationToken: cancellationToken);
        }

        Report.Item("wrote", output);
        Report.Item("operations applied", string.Join(", ", result.Operations));
        Report.Item("bytes written", result.BytesWritten);
        Report.Item("warnings", result.Warnings.Count);

        foreach (var warning in result.Warnings)
        {
            Report.Line($"  {warning.Kind}: {warning.Message}");
        }

        var annotations = await context.ContentReader.ReadAnnotationsAsync(
            new PdfFileSource(output),
            PageRange.All,
            cancellationToken: cancellationToken);

        Report.Item("annotations read back", annotations.Items.Count);

        foreach (var annotation in annotations.Items)
        {
            Report.Line(
                $"  {annotation.Subtype,-10} {annotation.Name ?? "(unnamed)",-18} {annotation.Rectangle} {(annotation.HasAppearance ? "appearance" : "no appearance")} {annotation.Target?.ToString() ?? annotation.Contents}");
        }
    }

    /// <summary>Writes a form, fills it by name, and flattens it so a viewer without forms still shows it.</summary>
    /// <param name="context">The document to fill and the ports to fill it with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <remarks>
    /// The three passes are three plans over three files, which is how a caller works in practice: one
    /// document designs the form, another fills the fields the first one named, and a third turns the
    /// result into a page anybody can print. Filling merges by name — a name the document carries is
    /// filled where it stands — so the second pass touches three of the five fields and leaves the
    /// rest alone.
    /// </remarks>
    public static async Task FillFormAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var created = context.OutputPath("form-created.pdf");
        var filled = context.OutputPath("form-filled.pdf");
        var flattened = context.OutputPath("form-flattened.pdf");
        var style = new PdfStandardTextStyle(StandardFont.Helvetica, 10);

        var create = PdfEditPlan.Create().SetForm(
            PdfForm.Create(
                PdfFormFields.Text(1, "person.name", new PdfRectangle(72, 700, 320, 724), "Ada Lovelace"),
                PdfFormFields.Text(1, "person.email", new PdfRectangle(72, 668, 320, 692), "ada@example.org"),
                PdfFormFields.CheckBox(1, "agree", new PdfRectangle(72, 636, 90, 654), isChecked: true),
                PdfFormFields.Choice(1, "country", new PdfRectangle(72, 600, 260, 624), ["NL", "BE", "DE"], "NL", isCombo: true),
                PdfFormFields.PushButton(1, "submit", new PdfRectangle(72, 560, 200, 588), "Send"))
            .WithDefaultStyle(style));

        var wrote = await ApplyAsync(context, create, context.Summary, created, cancellationToken);

        Report.Item("wrote", created);
        Report.Item("fields written", 5);
        Report.Item("appearance warnings", wrote.Warnings.Count(warning => warning.Kind == PdfWarningKind.FormAppearanceGenerated));
        await ReportFieldsAsync(context, created, cancellationToken);

        // The second pass names three fields and states a value for each. The form is merged by name,
        // so the two fields this plan does not name keep the values the first pass wrote.
        var fill = PdfEditPlan.Create()
            .SetFormField(1, PdfFormFields.Text(1, "person.name", new PdfRectangle(72, 700, 320, 724), "Grace Hopper"))
            .SetFormField(1, PdfFormFields.CheckBox(1, "agree", new PdfRectangle(72, 636, 90, 654), isChecked: false))
            .SetFormField(1, PdfFormFields.Choice(1, "country", new PdfRectangle(72, 600, 260, 624), ["NL", "BE", "DE"], "BE", isCombo: true));

        var updated = await ApplyAsync(context, fill, created, filled, cancellationToken);

        Report.Item("wrote", filled);
        Report.Item("operations applied", string.Join(", ", updated.Operations));
        await ReportFieldsAsync(context, filled, cancellationToken);

        // Flattening draws every widget into the page content and removes the form, so the values are
        // part of the page: a viewer that knows nothing about forms, and a printer, still show them.
        var flat = await ApplyAsync(context, PdfEditPlan.Create().FlattenForm(), filled, flattened, cancellationToken);

        Report.Item("wrote", flattened);
        Report.Item("bytes", $"{flat.BytesWritten:N0} of {new FileInfo(filled).Length:N0}");
        await ReportFieldsAsync(context, flattened, cancellationToken);
    }

    /// <summary>Applies a plan to a file and writes the result to another file.</summary>
    /// <param name="context">The ports to edit with.</param>
    /// <param name="plan">The plan to apply.</param>
    /// <param name="source">The document to read.</param>
    /// <param name="output">The document to write.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>What the editor reports about the result.</returns>
    private static async Task<PdfEditResult> ApplyAsync(
        ExampleContext context,
        PdfEditPlan plan,
        string source,
        string output,
        CancellationToken cancellationToken)
    {
        await using var destination = File.Create(output);

        return await context.Editor.ApplyAsync(
            new PdfFileSource(source),
            plan,
            destination,
            cancellationToken: cancellationToken);
    }

    /// <summary>Reads the form of a document back and prints one line per field.</summary>
    /// <param name="context">The ports to read with.</param>
    /// <param name="path">The document to read.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    private static async Task ReportFieldsAsync(ExampleContext context, string path, CancellationToken cancellationToken)
    {
        var fields = await context.ContentReader.ReadFormFieldsAsync(
            new PdfFileSource(path),
            cancellationToken: cancellationToken);

        Report.Item("fields read back", fields.Items.Count);

        foreach (var field in fields.Items)
        {
            Report.Line($"  {field.Name,-14} {field.FieldType,-4} {field.Value ?? "(no value)",-16} {field.Widgets.Count} widget(s)");
        }
    }
}
