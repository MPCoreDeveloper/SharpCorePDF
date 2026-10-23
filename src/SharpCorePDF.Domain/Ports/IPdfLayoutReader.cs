using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Reads the text of a PDF document with the geometry a consumer of a page needs — one entry per
/// letter, the words and blocks those letters make, the links the page carries — and exports that
/// reading as a document another tool can consume.
/// </summary>
/// <remarks>
/// <para>
/// The port is additive beside <see cref="IPdfContentReader"/>: that one answers what a document
/// contains, this one answers how a page is laid out. Nothing here changes the document, and nothing
/// here renders it — a letter carries the position the document states, not a pixel
/// (see the reading contract in <c>specs/003-pdf-platform-parity/contracts/reading-analysis.md</c>).
/// </para>
/// <para>
/// Every member reports what it could not read through the warnings of the result rather than by
/// throwing: a font without a <c>/ToUnicode</c> map costs the letters it draws and adds a
/// <see cref="PdfWarningKind.TextUnreadable"/> warning, a destination that does not resolve is kept
/// without a page, and a page with no text yields an empty result rather than an error (NFR-7.8).
/// </para>
/// <para>
/// Page numbers are one-based throughout, and the members follow the reader's habit of taking a
/// <see cref="PageRange"/> so that a caller can read one page, a chapter or the whole document without
/// opening it twice.
/// </para>
/// </remarks>
public interface IPdfLayoutReader
{
    /// <summary>Reads one entry per glyph the pages of a range draw, in content-stream order.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The letters of the range, page by page and in the order each page draws them, each carrying its
    /// Unicode value, its font, its box, its baseline and its direction.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The order is the content stream's, not the reading order of the page: a producer is free to draw
    /// the footer before the title, and a caller that wants the page's own order asks for words or blocks
    /// instead. Whitespace glyphs are reported with <see cref="PdfLetter.IsWhitespace"/>, because the gaps
    /// between words are what a caller's own segmentation measures.
    /// </para>
    /// <para>
    /// A glyph the document cannot be mapped through is skipped and the font that was in the way is named
    /// in a <see cref="PdfWarningKind.TextUnreadable"/> warning; no character is guessed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfLetter>> ReadLettersAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the words the pages of a range draw, grouped by the strategy the options select.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="options">The grouping rules; the default takes the page's own order and a quarter em as a space.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The words of the range, page by page, each carrying the letters it was grouped from and the
    /// rectangle those letters cover.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The strategy groups; it never changes what a letter says. <see cref="PdfWordExtractionStrategy.Raw"/>
    /// keeps the order of the content stream, which is the only order that reproduces a producer exactly,
    /// and <see cref="PdfWordExtractionStrategy.NearestNeighbour"/> takes the page's order — top to bottom,
    /// then left to right — whatever order the content used.
    /// </para>
    /// <para>
    /// A page with no text yields no word, and a page whose font could not be read yields no word plus the
    /// <see cref="PdfWarningKind.TextUnreadable"/> warning the letter read made.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfWord>> ReadWordsAsync(
        IPdfContentSource source,
        PageRange range,
        PdfWordExtractionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the blocks the pages of a range are laid out in, in the reading order of each page.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="options">The segmentation, the reading order and the kinds of block to keep.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The blocks of the range, page by page, each carrying its text, the words it is made of, the area it
    /// covers and its one-based position in the reading order of its page.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A block is a group of words the selected segmentation decided belong together: the run the producer
    /// drew, a paragraph, a column of a page or the leaves of a recursive cut. The order the blocks are
    /// reported in is the selected reading order, which is a reading of the page rather than a claim about its
    /// structure — nothing here knows what a heading is.
    /// </para>
    /// <para>
    /// The segmentation and the order that produced the blocks are recorded in the result, so an empty page
    /// and an untried one stay different answers. A page with no text yields no block, and a page whose font
    /// could not be read yields no block plus the <see cref="PdfWarningKind.TextUnreadable"/> warning the
    /// letter read made (NFR-7.8).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfTextBlock>> ReadBlocksAsync(
        IPdfContentSource source,
        PageRange range,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the links the pages of a range carry: their clickable areas and their targets.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The links of the range, page by page, each with a URI or an internal destination.</returns>
    /// <remarks>
    /// A named destination that does not resolve keeps its name and adds a
    /// <see cref="PdfWarningKind.NamedDestinationUnresolved"/> warning; the link is never dropped and the
    /// call does not throw. Only annotations of the link subtype are read, so a note or a stamp is not
    /// reported as a link.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfHyperlink>> ReadHyperlinksAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the reading of the pages of a range as a layout document another tool can consume.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to export, written in ascending page order.</param>
    /// <param name="format">The document to write: ALTO, PAGE XML, hOCR or SVG text.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    /// <param name="options">The segmentation, the reading order and the kinds of block to write.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The blocks that were written, page by page in reading order, with the warnings the read made and
    /// the strategy that produced them.
    /// </returns>
    /// <remarks>
    /// <para>
    /// An export is the read, written down: the same blocks a block read would return, in the same order,
    /// placed in page space with a top-left origin and the text escaped for the format. The blocks travel
    /// back with the call so that an export of a page whose font could not be read is not silent — it
    /// writes an empty page and returns the <see cref="PdfWarningKind.TextUnreadable"/> warning that
    /// explains why (FR-018).
    /// </para>
    /// <para>
    /// A page with no text still produces its page element, because the page exists and its size is part
    /// of what the export says; a range that selects no page at all — a document with no pages, or a range
    /// past the last one — writes a document with no page element. Images and decorations are not written by
    /// this release: an export describes text, and a block never carries an image.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="destination"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">The destination stream cannot be written to.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is not a format this release writes.
    /// </exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfTextBlock>> ExportAsync(
        IPdfContentSource source,
        PageRange range,
        PdfLayoutFormat format,
        Stream destination,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
