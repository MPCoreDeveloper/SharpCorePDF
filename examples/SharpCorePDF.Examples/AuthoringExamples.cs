using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Writes a page that did not exist: a cover with a logo, a standard font, an embedded font and
/// accents, then merges it in front of a document that already exists.
/// </summary>
internal static class AuthoringExamples
{
    /// <summary>A 152 x 87 RGBA logo with a transparent left edge, which is what makes an /SMask necessary.</summary>
    private const string Logo =
        "iVBORw0KGgoAAAANSUhEUgAAAJgAAABXCAYAAAD4fIxAAAAA6klEQVR42u3awQkAIAwEwevEPmwm/VehLzsQDDgH20CYZ5LLG7OWdApgAkyAASbABJgAA0yACTABBpgAE2ACDDABJsAcVYAJMAEGmAATYAIMMAEmwAQYYAJMgEmACTABBpgAE2ACDDABJsAEGGACTIC16vUAAwwwwAADDDDAAAMMMMAAAwwwwAADDDDAAAMMMMAAAwwwwAADDDDAAAPMP5gAE2CACTABJsAAE2ACTIABJsAEmAADTIAJMAkwASbAABNgAkyAASbABJgAA0yACTABBpgAE2CACTABJsAAE2ACTIABJsAEmP5sA9V2M2TFwZ3JAAAAAElFTkSuQmCC";

    /// <summary>Builds the cover of a statement, saves it and puts it in front of the summary document.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task WriteCoverAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // The font travels with the application; nothing is looked up in the machine's font folder, which is
        // what makes this work in a container that has no fonts installed at all. The typeface parses the
        // program once and may be shared by every page and every builder that follows.
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans-subset.ttf");
        var body = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync(fontPath, cancellationToken));
        var heading = PdfTypeface.Standard(StandardFont.Helvetica);

        // The fixed date and identifier make two runs produce the same bytes, which is what a test that
        // compares files needs; in production both are left out and the current moment is used.
        var cover = PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with
        {
            CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DocumentId = "00112233445566778899AABBCCDDEEFF",
        })
            .WithMetadata(new PdfMetadata
            {
                Title = "Décompte 12345",
                Author = "Décomptes SPRL",
                Creator = "SharpCorePDF examples",
            })
            .AddPage(PdfPageSize.A4, page => page
                .DrawImage(Convert.FromBase64String(Logo), x: 40, y: 30, width: 152, height: 87)
                .DrawText("Décomptes SPRL", 330, 180, heading, 12)
                .DrawText("Rue de l'Église 1", 330, 194, heading, 12)
                .DrawText("1000 Bruxelles", 330, 208, heading, 12)
                .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", 150, 400, body, 12)
                .DrawText("pour la période du 01/01/2025 au 31/12/2025.", 150, 414, body, 12)
                .DrawText("Sending 1/3", 150, 550, body, 12)
                .DrawText("Référence : é è à ç ô ï ' / |", 150, 570, body, 12))
            .Build("cover.pdf");

        var coverPath = context.OutputPath("cover.pdf");
        await File.WriteAllBytesAsync(coverPath, cover.Bytes.ToArray(), cancellationToken);
        var info = await context.Inspector.InspectAsync(cover, cancellationToken: cancellationToken);

        Report.Item("wrote", coverPath);
        Report.Item("pages", info.PageCount);
        Report.Item("size", info.Pages[0].Size);
        Report.Item("bytes", cover.Length);

        var plan = MergePlan.Create()
            .AddSource(PdfSource.Create(cover))
            .AddFile(context.Summary);

        var merged = await context.Merger.MergeToBytesAsync(plan, cancellationToken);
        var mergedPath = context.OutputPath("cover-and-summary.pdf");
        await File.WriteAllBytesAsync(mergedPath, merged, cancellationToken);

        var result = await context.Inspector.InspectAsync(
            new PdfByteArraySource(merged, "cover-and-summary.pdf"),
            cancellationToken: cancellationToken);

        Report.Item("merged", mergedPath);
        Report.Item("pages after merging", result.PageCount);
        Report.Item("first page", result.Pages[0].Size);

        // The text comes back through the same reader a caller would use, accents included, which is the
        // round trip the embedded font's ToUnicode map exists for.
        var text = await context.ContentReader.ReadTextAsync(
            new PdfByteArraySource(merged, "cover-and-summary.pdf"),
            PageRange.Single(1),
            cancellationToken: cancellationToken);

        Report.Line("page 1 text:");

        foreach (var line in text.Items.SelectMany(block => block.Text.Split('\n')))
        {
            Report.Line(line);
        }
    }
}
