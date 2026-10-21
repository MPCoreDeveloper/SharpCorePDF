using System.Runtime.CompilerServices;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case of reading what a document contains: open a content source, read one capability from
/// it, and hand the caller the items together with the remarks the engine made on the way.
/// </summary>
/// <remarks>
/// <para>
/// The class is stateless and thread safe: each call opens its own stream, so two reads of the same
/// source do not interfere and a read can run beside a merge of the same file.
/// </para>
/// <para>
/// Nothing is written. The two copy members write only to the destination the caller hands in, and
/// leave it open, exactly as a merge leaves the stream it writes.
/// </para>
/// </remarks>
public sealed class PdfContentReaderService : IPdfContentReader
{
    private readonly IPdfPageContentReader reader;

    /// <summary>Initializes a new instance of the <see cref="PdfContentReaderService"/> class.</summary>
    /// <param name="reader">The reader used to read the document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public PdfContentReaderService(IPdfPageContentReader reader) =>
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfImageInfo>> ReadImagesAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadImagesAsync(source.DisplayName, content, range, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfTextBlock>> ReadTextAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadTextAsync(source.DisplayName, content, range, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CopyImageDataAsync(
        IPdfContentSource source,
        PdfImageInfo image,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(destination);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        await reader
            .CopyImageDataAsync(source.DisplayName, content, image, destination, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CopyImageMaskDataAsync(
        IPdfContentSource source,
        PdfImageMask mask,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(destination);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        await reader
            .CopyImageMaskDataAsync(source.DisplayName, content, mask, destination, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfAnnotationInfo>> ReadAnnotationsAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadAnnotationsAsync(source.DisplayName, content, range, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfFormFieldInfo>> ReadFormFieldsAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadFormFieldsAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfXmpResult> ReadMetadataAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadMetadataAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfEmbeddedFileInfo>> ReadEmbeddedFilesAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadEmbeddedFilesAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CopyEmbeddedFileDataAsync(
        IPdfContentSource source,
        PdfEmbeddedFileInfo file,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        await reader
            .CopyEmbeddedFileDataAsync(source.DisplayName, content, file, destination, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfOutlineItem>> ReadOutlineAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadOutlineAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PdfPageDetail> ReadPageDetailsAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return ReadPageDetailsCore(source, range, password, cancellationToken);
    }

    /// <summary>Opens the source and yields a detail per page, keeping it open while it is enumerated.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A page detail per page of the range.</returns>
    private async IAsyncEnumerable<PdfPageDetail> ReadPageDetailsCore(
        IPdfContentSource source,
        PageRange range,
        string? password,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var page in reader
            .ReadPageDetailsAsync(source.DisplayName, content, range, password, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return page;
        }
    }
}
