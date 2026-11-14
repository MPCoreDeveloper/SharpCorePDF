using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// The engine's implementation of <see cref="IPdfDocumentFontInspector"/>: it walks the page tree, reads
/// every font resource the pages select, and reports what the document states about each one.
/// </summary>
/// <remarks>
/// <para>
/// A font is reported once, keyed by the object number its resource points at, so a document that sets one
/// face on two hundred pages yields one entry carrying two hundred page numbers. A resource that cannot be
/// resolved, and a font that states no descriptor, are reported as warnings: the read answers what the file
/// says, and a font without a program is a fact about the document rather than a failure to read it.
/// </para>
/// <para>
/// The adapter is a thin shell around <see cref="PdfPageContentReader"/> — the same page walk, the same
/// encryption outcome — and writes nothing. Each call opens its own read of the stream it is given, so it
/// keeps no state per document and can be shared between threads (NFR-7.7).
/// </para>
/// </remarks>
public sealed class PdfFontInspector : IPdfDocumentFontInspector
{
    private readonly PdfPageContentReader reader;

    /// <summary>Initializes a new instance of the <see cref="PdfFontInspector"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfFontInspector(PdfEngineOptions? options = null) => reader = new PdfPageContentReader(options);

    /// <summary>Gets the options this inspector was created with.</summary>
    public PdfEngineOptions Options => reader.Options;

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfFontInfo>> ReadFontsAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var document = await reader
            .OpenAsync(sourceName, content, password, cancellationToken)
            .ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        PdfPageContentReader.NoteClosedEncryption(document, warnings);

        return PdfPageContentReader.Result(document, Fonts(document, warnings), warnings);
    }

    /// <summary>Reads every font the pages of a document select.</summary>
    /// <param name="document">The opened document.</param>
    /// <param name="warnings">The list that receives the remarks the read makes.</param>
    /// <returns>The fonts, in the order the pages first select them; empty when nothing can be read.</returns>
    private static IReadOnlyList<PdfFontInfo> Fonts(PdfFileReader document, List<PdfWarning> warnings)
    {
        if (document.IsEncrypted && document.Security is null)
        {
            return [];
        }

        var accumulated = new FontAccumulator(document, warnings);
        var pages = document.Pages;

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];

            // SourcePage.Inherited already carries the attributes the page states itself together with the
            // ones its ancestors state, so this is the one lookup that covers both.
            var resources = document.ResolveDictionary(page.Inherited.Get("Resources"));

            accumulated.Visit(resources, index + 1, depth: 0, visited: []);
        }

        return accumulated.Build();
    }
}
