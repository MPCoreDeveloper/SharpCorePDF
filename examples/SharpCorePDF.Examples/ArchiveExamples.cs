using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.Examples;

/// <summary>
/// Writes a document for archival delivery: a PDF/A profile, the structures it requires, and the honest
/// answer when the document cannot be made conformant.
/// </summary>
internal static class ArchiveExamples
{
    /// <summary>Converts an authored page to PDF/A-2b and reports what the writer did.</summary>
    /// <param name="context">The ports to write with and the folder to write to.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task ConvertToPdfAAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // The font travels with the application for the same reason it does in the authoring example: a
        // PDF/A document must carry the program of every font it uses, and a standard font has none.
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans-subset.ttf");
        var body = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync(fontPath, cancellationToken));

        var document = PdfDocumentBuilder
            .Create(PdfAuthoringOptions.Default with
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
            .AddPage(PdfPageSize.A4, page => page.DrawText(
                "Archival master, 2026",
                60,
                760,
                body,
                14))
            .Build("archive-source.pdf");

        using var destination = new MemoryStream();

        var options = new PdfAConversionOptions
        {
            ModificationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DocumentId = "00112233445566778899AABBCCDDEEFF",
        };

        // The same page is written for each profile. The writer decides the version of the file, adds
        // the XMP packet and the output intent, and refuses rather than mislabels.
        foreach (var profile in (PdfAProfile[])[PdfAProfile.PdfA1B, PdfAProfile.PdfA2B, PdfAProfile.PdfA3B])
        {
            destination.SetLength(0);

            var result = await context.ArchiveWriter.ConvertAsync(
                document,
                destination,
                profile,
                options,
                cancellationToken: cancellationToken);

            Report.Heading(PdfAProfileName(profile));

            if (!result.IsWritten)
            {
                Report.Item("written", false);

                foreach (var requirement in result.UnsatisfiedRequirements)
                {
                    Report.Line(requirement);
                }

                continue;
            }

            var output = context.OutputPath(FileName(profile));
            await File.WriteAllBytesAsync(output, destination.ToArray(), cancellationToken);

            Report.Item("pages", result.PageCount);
            Report.Item("bytes", result.BytesWritten);
            Report.Item("wrote", output);
            Report.Item("unsatisfied", result.UnsatisfiedRequirements.Count);
            Report.Item("remarks", result.Warnings.Count);
        }

        Report.Line("next: python docs/tools/validate-pdfa.py <file> --profile 2b");

        // A document that sets text in a standard font cannot become PDF/A: the profile requires the
        // program of every font, and a standard font has none. The writer refuses it and leaves the
        // destination empty rather than labelling it archival.
        var unembeddable = PdfDocumentBuilder
            .Create(PdfAuthoringOptions.Default with { CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) })
            .AddPage(PdfPageSize.A4, page => page.DrawText(
                "Set in Helvetica, which has no program to embed",
                60,
                760,
                PdfTypeface.Standard(StandardFont.Helvetica),
                12))
            .Build("unembeddable.pdf");

        using var refusedDestination = new MemoryStream();

        var refused = await context.ArchiveWriter.ConvertAsync(
            unembeddable,
            refusedDestination,
            PdfAProfile.PdfA2B,
            cancellationToken: cancellationToken);

        Report.Item("refused", !refused.IsWritten);
        Report.Item("bytes written when refused", refusedDestination.Length);

        foreach (var requirement in refused.UnsatisfiedRequirements)
        {
            Report.Line(requirement);
        }
    }

    /// <summary>Gets the name of a profile, as the manual spells it.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The name, for example <c>PDF/A-2b</c>.</returns>
    private static string PdfAProfileName(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => "PDF/A-1b",
        PdfAProfile.PdfA2B => "PDF/A-2b",
        PdfAProfile.PdfA3B => "PDF/A-3b",
        _ => profile.ToString(),
    };

    /// <summary>Gets the file the example writes a profile to.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The file name.</returns>
    private static string FileName(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => "archive-1b.pdf",
        PdfAProfile.PdfA2B => "archive-2b.pdf",
        PdfAProfile.PdfA3B => "archive-3b.pdf",
        _ => "archive.pdf",
    };
}
