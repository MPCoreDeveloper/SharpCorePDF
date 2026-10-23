using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The glyph half of <see cref="PdfPageContentReader"/>: the letters a page draws, before anything groups
/// them.
/// </summary>
/// <remarks>
/// <para>
/// The read is the same walk <see cref="PdfPageContentReader"/> performs for its text read — the page tree,
/// the resources, the text state, the fonts — stopped one step earlier: where the text read groups glyphs
/// into words and blocks, this one hands back every glyph the content stream draws, in drawing order, so
/// that a layout read can group them by a rule of its own.
/// </para>
/// <para>
/// The member is internal because the glyph list is an implementation detail of the engine: a caller asks
/// the domain's <see cref="Domain.Ports.IPdfLayoutReader"/> for letters, words or blocks, and this is the
/// step all three share.
/// </para>
/// </remarks>
public sealed partial class PdfPageContentReader
{
    /// <summary>Reads one entry per glyph the pages of a range draw, in content-stream order.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The glyphs of the range, page by page and in the order each page draws them.</returns>
    internal async Task<PdfReadResult<PdfGlyph>> ReadGlyphsAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password,
        CancellationToken cancellationToken)
    {
        var read = await ReadGlyphPagesAsync(sourceName, content, range, password, cancellationToken)
            .ConfigureAwait(false);

        return read.Glyphs;
    }

    /// <summary>Reads the glyphs of a range together with the box of every page it visited.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The glyphs of the range and the boxes of its pages, in ascending page order.</returns>
    /// <remarks>
    /// The glyph read and the page sizes come from the same walk of the page tree, so a caller that exports
    /// the read does not have to open the document twice to learn how tall its pages are. A page whose
    /// content cannot be read keeps its box and contributes no glyph.
    /// </remarks>
    internal async Task<PdfGlyphRead> ReadGlyphPagesAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password,
        CancellationToken cancellationToken)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();
        var glyphs = new List<PdfGlyph>();
        var pages = new List<PdfPageBox>();

        NoteClosedEncryption(reader, warnings);

        foreach (var page in SelectPages(reader, range))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageNumber = page.Index + 1;
            var size = ReadPageSize(reader, page, warnings, pageNumber);

            pages.Add(new PdfPageBox(pageNumber, size.WidthPoints, size.HeightPoints));

            var pageContent = ReadPageContent(reader, page, warnings, pageNumber);

            if (pageContent is null)
            {
                continue;
            }

            var collector = new PdfTextCollector(reader, pageNumber, warnings);

            glyphs.AddRange(collector.Collect(pageContent, PageResources(reader, page)));
        }

        return new PdfGlyphRead(Result(reader, glyphs, warnings), pages);
    }
}
