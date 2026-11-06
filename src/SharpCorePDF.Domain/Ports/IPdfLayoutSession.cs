using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// A layout read that keeps one document open across several questions about its pages.
/// </summary>
/// <remarks>
/// <para>
/// The one-shot members of <see cref="IPdfLayoutReader"/> open the source, answer, and forget the
/// parse. A session is the same answers for a caller that already knows it will ask more than once —
/// letters of one range, then words of another, then an export — without paying the open cost again.
/// Nothing here changes the document.
/// </para>
/// <para>
/// A session is not thread-safe: one caller owns it from <see cref="IPdfLayoutReader.OpenAsync"/> to
/// dispose. Disposing releases the parse; it does not dispose the content source the session was
/// opened from.
/// </para>
/// </remarks>
public interface IPdfLayoutSession : IAsyncDisposable, IDisposable
{
    /// <summary>Gets the display name of the source the session was opened from.</summary>
    string SourceName { get; }

    /// <summary>Reads one entry per glyph the pages of a range draw, in content-stream order.</summary>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The letters of the range, page by page and in the order each page draws them.</returns>
    Task<PdfReadResult<PdfLetter>> ReadLettersAsync(
        PageRange range,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the words the pages of a range draw, grouped by the strategy the options select.</summary>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="options">The grouping rules; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The words of the range, page by page, each carrying the letters it was grouped from.</returns>
    Task<PdfReadResult<PdfWord>> ReadWordsAsync(
        PageRange range,
        PdfWordExtractionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the blocks the pages of a range are laid out in, in the reading order of each page.</summary>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="options">The segmentation, the reading order and the kinds of block to keep; <see langword="null"/> takes the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The blocks of the range, page by page, each carrying its words and its reading order.</returns>
    Task<PdfReadResult<PdfTextBlock>> ReadBlocksAsync(
        PageRange range,
        PdfLayoutOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the links the pages of a range carry: their clickable areas and their targets.</summary>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The links of the range, page by page.</returns>
    Task<PdfReadResult<PdfHyperlink>> ReadHyperlinksAsync(
        PageRange range,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the reading of the pages of a range as a layout document another tool can consume.</summary>
    /// <param name="range">The pages to export, written in ascending page order.</param>
    /// <param name="format">The document to write: ALTO, PAGE XML, hOCR or SVG text.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    /// <param name="options">The segmentation, the reading order and the kinds of block to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The blocks that were written, with the warnings the read made.</returns>
    Task<PdfReadResult<PdfTextBlock>> ExportAsync(
        PageRange range,
        PdfLayoutFormat format,
        Stream destination,
        PdfLayoutOptions? options = null,
        CancellationToken cancellationToken = default);
}
