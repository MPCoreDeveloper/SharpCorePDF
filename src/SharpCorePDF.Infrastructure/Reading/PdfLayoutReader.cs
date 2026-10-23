using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Reading.Exporters;
using SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// The engine's implementation of <see cref="IPdfPageLayoutReader"/>: it opens a document, reads the glyphs
/// its pages draw and hands them back as letters, as the words those letters make, as the blocks those words
/// are laid out in, as the links the pages carry and as the layout documents an export writes.
/// </summary>
/// <remarks>
/// <para>
/// The class is a thin shell around <see cref="PdfPageContentReader"/> — the same walk of the page tree,
/// the same text state and the same font decoding every read of the content uses — so that the layout
/// surface and the content surface cannot disagree about what a page draws. The grouping levels above the
/// letter (words, blocks) are built from this same read, which is what the reading contract means by "a
/// strategy never changes the glyph data; it only groups it".
/// </para>
/// <para>
/// Nothing here writes, renders or decodes an image; the object is stateless and can be shared between
/// threads, because each call opens its own read of the stream it is given.
/// </para>
/// </remarks>
public sealed class PdfLayoutReader : IPdfPageLayoutReader
{
    private readonly PdfPageContentReader reader;

    /// <summary>Initializes a new instance of the <see cref="PdfLayoutReader"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfLayoutReader(PdfEngineOptions? options = null) => reader = new PdfPageContentReader(options);

    /// <summary>Gets the options this reader was created with.</summary>
    public PdfEngineOptions Options => reader.Options;

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfLetter>> ReadLettersAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var glyphs = await reader
            .ReadGlyphsAsync(sourceName, content, range, password, cancellationToken)
            .ConfigureAwait(false);

        return LetterExtractor.Extract(glyphs);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfWord>> ReadWordsAsync(
        string sourceName,
        Stream content,
        PageRange range,
        PdfWordExtractionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var rules = options ?? new PdfWordExtractionOptions();
        var glyphs = await reader
            .ReadGlyphsAsync(sourceName, content, range, password, cancellationToken)
            .ConfigureAwait(false);

        return WordExtractor.Extract(glyphs, rules);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfTextBlock>> ReadBlocksAsync(
        string sourceName,
        Stream content,
        PageRange range,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var rules = options ?? new PdfLayoutOptions();
        var glyphs = await reader
            .ReadGlyphsAsync(sourceName, content, range, password, cancellationToken)
            .ConfigureAwait(false);

        return BlockExtractor.Extract(glyphs, rules);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfHyperlink>> ReadHyperlinksAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var annotations = await reader
            .ReadAnnotationsAsync(sourceName, content, range, password, cancellationToken)
            .ConfigureAwait(false);

        return HyperlinkReader.Extract(annotations);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfTextBlock>> ExportAsync(
        string sourceName,
        Stream content,
        PageRange range,
        PdfLayoutFormat format,
        Stream destination,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        // Nothing about the document is read until the destination and the format are known to be usable.
        LayoutExportWriter.Validate(format, destination);

        var rules = options ?? new PdfLayoutOptions();
        var read = await reader
            .ReadGlyphPagesAsync(sourceName, content, range, password, cancellationToken)
            .ConfigureAwait(false);

        var blocks = BlockExtractor.Extract(read.Glyphs, rules);

        LayoutExportWriter.Write(blocks, read.Pages, format, destination);

        return blocks;
    }
}
