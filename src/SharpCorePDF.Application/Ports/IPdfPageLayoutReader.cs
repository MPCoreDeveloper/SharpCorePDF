using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Reads what a PDF document's pages draw at the level of a layout — its letters today, its words, blocks
/// and links as the reading surface grows — from a stream. Implemented by the PDF engine.
/// </summary>
/// <remarks>
/// <para>
/// The engine-side counterpart of <see cref="Domain.Ports.IPdfLayoutReader"/>, exactly as
/// <see cref="IPdfPageContentReader"/> mirrors <see cref="Domain.Ports.IPdfContentReader"/>: the use case
/// opens a content source and hands the stream to this port, which keeps the engine free of the domain's
/// source abstraction and of I/O policy. Every member reads the stream from its current position to its
/// end and does not dispose it.
/// </para>
/// <para>
/// A page that draws no text, a font that cannot be mapped and a document that is encrypted and could not
/// be opened are all described through the warnings of the result; only content that is not a PDF at all
/// raises <see cref="Domain.Exceptions.PdfSourceFormatException"/>.
/// </para>
/// </remarks>
public interface IPdfPageLayoutReader
{
    /// <summary>Reads one entry per glyph the pages of a range draw, in content-stream order.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The letters of the range, page by page and in the order each page draws them.</returns>
    /// <remarks>
    /// A glyph the document cannot be mapped through is skipped and adds a
    /// <see cref="Domain.ValueObjects.PdfWarningKind.TextUnreadable"/> warning; the rest of the page is
    /// still returned.
    /// </remarks>
    Task<PdfReadResult<PdfLetter>> ReadLettersAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the words the pages of a range draw, grouped by the strategy the options select.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="options">The grouping rules; <see langword="null"/> takes the defaults.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The words of the range, page by page, each carrying the letters it was grouped from.</returns>
    Task<PdfReadResult<PdfWord>> ReadWordsAsync(
        string sourceName,
        Stream content,
        PageRange range,
        PdfWordExtractionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the blocks the pages of a range are laid out in, in the reading order of each page.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="options">The segmentation, the reading order and the kinds of block to keep; <see langword="null"/> takes the defaults.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The blocks of the range, page by page, each carrying its words and its reading order.</returns>
    /// <remarks>
    /// The strategies only group the words and order the groups; what a letter says is decided by the one
    /// read of the content that produces every level of the surface.
    /// </remarks>
    Task<PdfReadResult<PdfTextBlock>> ReadBlocksAsync(
        string sourceName,
        Stream content,
        PageRange range,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the links the pages of a range carry: their clickable areas and their targets.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The links of the range, page by page.</returns>
    Task<PdfReadResult<PdfHyperlink>> ReadHyperlinksAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the reading of the pages of a range as a layout document another tool can consume.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to export, written in ascending page order.</param>
    /// <param name="format">The document to write: ALTO, PAGE XML, hOCR or SVG text.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    /// <param name="options">The segmentation, the reading order and the kinds of block to write; <see langword="null"/> takes the defaults.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The blocks that were written, with the warnings the read made.</returns>
    /// <remarks>
    /// The engine writes the document itself, because the page geometry the coordinates are flipped
    /// against belongs to the read: the caller supplies a stream and never sees the intermediate blocks.
    /// </remarks>
    Task<PdfReadResult<PdfTextBlock>> ExportAsync(
        string sourceName,
        Stream content,
        PageRange range,
        PdfLayoutFormat format,
        Stream destination,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
