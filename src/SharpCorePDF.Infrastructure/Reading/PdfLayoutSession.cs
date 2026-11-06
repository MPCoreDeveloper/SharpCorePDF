using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Reading.Exporters;
using SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// A layout read that keeps one opened <see cref="PdfFileReader"/> across several questions about its pages.
/// </summary>
/// <remarks>
/// The session owns the parse, not the content source. Disposing it drops the reader; it does not dispose
/// anything the caller still holds. It is not thread-safe: one caller owns it from open to dispose.
/// </remarks>
internal sealed class PdfLayoutSession : IPdfLayoutSession
{
    private PdfFileReader? reader;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="PdfLayoutSession"/> class.</summary>
    /// <param name="reader">The opened document.</param>
    public PdfLayoutSession(PdfFileReader reader)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        SourceName = reader.SourceName;
    }

    /// <inheritdoc />
    public string SourceName { get; }

    /// <inheritdoc />
    public Task<PdfReadResult<PdfLetter>> ReadLettersAsync(
        PageRange range,
        CancellationToken cancellationToken = default)
    {
        var glyphs = ReadGlyphs(range, cancellationToken);
        return Task.FromResult(LetterExtractor.Extract(glyphs));
    }

    /// <inheritdoc />
    public Task<PdfReadResult<PdfWord>> ReadWordsAsync(
        PageRange range,
        PdfWordExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rules = options ?? new PdfWordExtractionOptions();
        var glyphs = ReadGlyphs(range, cancellationToken);
        return Task.FromResult(WordExtractor.Extract(glyphs, rules));
    }

    /// <inheritdoc />
    public Task<PdfReadResult<PdfTextBlock>> ReadBlocksAsync(
        PageRange range,
        PdfLayoutOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rules = options ?? new PdfLayoutOptions();
        var glyphs = ReadGlyphs(range, cancellationToken);
        return Task.FromResult(BlockExtractor.Extract(glyphs, rules));
    }

    /// <inheritdoc />
    public Task<PdfReadResult<PdfHyperlink>> ReadHyperlinksAsync(
        PageRange range,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Document();
        var annotations = PdfPageContentReader.ReadAnnotations(document, range);
        return Task.FromResult(HyperlinkReader.Extract(annotations));
    }

    /// <inheritdoc />
    public Task<PdfReadResult<PdfTextBlock>> ExportAsync(
        PageRange range,
        PdfLayoutFormat format,
        Stream destination,
        PdfLayoutOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        LayoutExportWriter.Validate(format, destination);

        var rules = options ?? new PdfLayoutOptions();
        var read = ReadGlyphPages(range, cancellationToken);
        var blocks = BlockExtractor.Extract(read.Glyphs, rules);

        LayoutExportWriter.Write(blocks, read.Pages, format, destination);

        return Task.FromResult(blocks);
    }

    /// <inheritdoc />
    public void Dispose() => Release();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    private PdfReadResult<PdfGlyph> ReadGlyphs(PageRange range, CancellationToken cancellationToken) =>
        ReadGlyphPages(range, cancellationToken).Glyphs;

    private PdfGlyphRead ReadGlyphPages(PageRange range, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PdfPageContentReader.ReadGlyphPages(Document(), range, cancellationToken);
    }

    private PdfFileReader Document()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return reader ?? throw new ObjectDisposedException(nameof(PdfLayoutSession));
    }

    private void Release()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        reader = null;
    }
}