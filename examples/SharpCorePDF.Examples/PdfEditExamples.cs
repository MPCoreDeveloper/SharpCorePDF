using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Edits a document that already exists: an authored page goes in front of it, one page is deleted,
/// one is rotated, one is cropped, and the result is written with a fixed modification date.
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
}
