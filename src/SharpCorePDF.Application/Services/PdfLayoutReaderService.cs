using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case of reading how a document is laid out: open a content source, read one level of the
/// reading surface from it, and hand the caller the letters together with the remarks the engine made on
/// the way.
/// </summary>
/// <remarks>
/// <para>
/// The class is stateless and thread safe: each call opens its own stream, so two reads of the same source
/// do not interfere and a read can run beside a merge or an edit of the same file.
/// </para>
/// <para>
/// Nothing is written and nothing is rendered. The reading surface describes what the document states —
/// positions, fonts, baselines — and never decodes an image or a font program to guess at a glyph.
/// </para>
/// </remarks>
public sealed class PdfLayoutReaderService : IPdfLayoutReader
{
    private readonly IPdfPageLayoutReader reader;

    /// <summary>Initializes a new instance of the <see cref="PdfLayoutReaderService"/> class.</summary>
    /// <param name="reader">The reader used to read the layout of the document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public PdfLayoutReaderService(IPdfPageLayoutReader reader) =>
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfLetter>> ReadLettersAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadLettersAsync(source.DisplayName, content, range, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfWord>> ReadWordsAsync(
        IPdfContentSource source,
        PageRange range,
        PdfWordExtractionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var rules = options ?? new PdfWordExtractionOptions();

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadWordsAsync(source.DisplayName, content, range, rules, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfTextBlock>> ReadBlocksAsync(
        IPdfContentSource source,
        PageRange range,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var rules = options ?? new PdfLayoutOptions();

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadBlocksAsync(source.DisplayName, content, range, rules, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfHyperlink>> ReadHyperlinksAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadHyperlinksAsync(source.DisplayName, content, range, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfTextBlock>> ExportAsync(
        IPdfContentSource source,
        PageRange range,
        PdfLayoutFormat format,
        Stream destination,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        // The destination and the format are checked before the source is opened, so a caller that asks
        // for a document it cannot write or a format this release does not produce learns so without the
        // cost of the read (and without a half-read document).
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "A layout export format must be one this library writes.");
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        var rules = options ?? new PdfLayoutOptions();

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ExportAsync(source.DisplayName, content, range, format, destination, rules, password, cancellationToken)
            .ConfigureAwait(false);
    }
}
