using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Extensions;

/// <summary>
/// Convenience members for <see cref="IPdfContentReader"/>, so the common shapes — one page, the bytes
/// of one entry, a file on disk — do not need a range or a destination stream to be built by hand.
/// </summary>
/// <remarks>
/// These members are declared with C# extension blocks: they read as if they were part of
/// <see cref="IPdfContentReader"/> while the interface stays at its minimal, mockable surface.
/// </remarks>
public static class PdfContentReaderExtensions
{
    extension(IPdfContentReader reader)
    {
        /// <summary>Reads the images a single page draws.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="page">The one-based page number.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The images of that page, carrying that page alone.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is less than one.</exception>
        public Task<PdfReadResult<PdfImageInfo>> ReadImagesAsync(
            IPdfContentSource source,
            int page,
            string? password = null,
            CancellationToken cancellationToken = default) =>
            reader.ReadImagesAsync(source, PageRange.Single(page), password, cancellationToken);

        /// <summary>Reads the text a single page draws.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="page">The one-based page number.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The blocks of that page, in reading order.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is less than one.</exception>
        public Task<PdfReadResult<PdfTextBlock>> ReadTextAsync(
            IPdfContentSource source,
            int page,
            string? password = null,
            CancellationToken cancellationToken = default) =>
            reader.ReadTextAsync(source, PageRange.Single(page), password, cancellationToken);

        /// <summary>Reads the annotations a single page carries.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="page">The one-based page number.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The annotations of that page.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is less than one.</exception>
        public Task<PdfReadResult<PdfAnnotationInfo>> ReadAnnotationsAsync(
            IPdfContentSource source,
            int page,
            string? password = null,
            CancellationToken cancellationToken = default) =>
            reader.ReadAnnotationsAsync(source, PageRange.Single(page), password, cancellationToken);

        /// <summary>Reads the payload of one image into memory.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="image">The image whose payload is wanted.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The payload, exactly as the copy route would have written it.</returns>
        /// <remarks>
        /// This member materialises the payload: the result is one array of <c>ByteLength</c> bytes, so
        /// it is only appropriate when that fits in memory. For a large image use
        /// <see cref="Domain.Ports.IPdfContentReader.CopyImageDataAsync"/>, which writes to a stream of
        /// the caller's choosing through a bounded buffer.
        /// </remarks>
        /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
        public async Task<ReadOnlyMemory<byte>> ReadImageDataAsync(
            IPdfContentSource source,
            PdfImageInfo image,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(image);

            using var buffer = new MemoryStream(
                image.ByteLength is > 0 and <= int.MaxValue ? (int)image.ByteLength : 0);

            await reader
                .CopyImageDataAsync(source, image, buffer, password, cancellationToken)
                .ConfigureAwait(false);

            return buffer.ToArray();
        }

        /// <summary>Writes the payload of one image to a file.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="image">The image whose payload is wanted.</param>
        /// <param name="destinationPath">The path of the file to create or overwrite.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that completes when the file is written and flushed.</returns>
        /// <remarks>The file is written through the copy route, so the image is never held in memory.</remarks>
        /// <exception cref="ArgumentException"><paramref name="destinationPath"/> is null, empty or whitespace.</exception>
        public async Task SaveImageDataAsync(
            IPdfContentSource source,
            PdfImageInfo image,
            string destinationPath,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(image);

            await using var file = OpenDestination(destinationPath);

            await reader
                .CopyImageDataAsync(source, image, file, password, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Reads the payload of one embedded file into memory.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="file">The file whose bytes are wanted.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The bytes of the file.</returns>
        /// <remarks>
        /// This member materialises the payload; for an attachment of unknown size use
        /// <see cref="Domain.Ports.IPdfContentReader.CopyEmbeddedFileDataAsync"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
        public async Task<ReadOnlyMemory<byte>> ReadEmbeddedFileDataAsync(
            IPdfContentSource source,
            PdfEmbeddedFileInfo file,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(file);

            using var buffer = new MemoryStream(
                file.ByteLength is > 0 and <= int.MaxValue ? (int)file.ByteLength : 0);

            await reader
                .CopyEmbeddedFileDataAsync(source, file, buffer, password, cancellationToken)
                .ConfigureAwait(false);

            return buffer.ToArray();
        }

        /// <summary>Writes the payload of one embedded file to a file.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="file">The file whose bytes are wanted.</param>
        /// <param name="destinationPath">The path of the file to create or overwrite.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that completes when the file is written and flushed.</returns>
        /// <remarks>The file is written through the copy route, so the attachment is never held in memory.</remarks>
        /// <exception cref="ArgumentException"><paramref name="destinationPath"/> is null, empty or whitespace.</exception>
        public async Task SaveEmbeddedFileAsync(
            IPdfContentSource source,
            PdfEmbeddedFileInfo file,
            string destinationPath,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(file);

            await using var destination = OpenDestination(destinationPath);

            await reader
                .CopyEmbeddedFileDataAsync(source, file, destination, password, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Reads everything known about a single page.</summary>
        /// <param name="source">The content to read.</param>
        /// <param name="page">The one-based page number.</param>
        /// <param name="password">The user or owner password, when the document is encrypted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The page's detail, or <see langword="null"/> when the document has no such page.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is less than one.</exception>
        public async Task<PdfPageDetail?> ReadPageDetailAsync(
            IPdfContentSource source,
            int page,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            var detail = reader.ReadPageDetailsAsync(source, PageRange.Single(page), password, cancellationToken);
            var pages = detail.GetAsyncEnumerator(cancellationToken);

            try
            {
                return await pages.MoveNextAsync().ConfigureAwait(false) ? pages.Current : null;
            }
            finally
            {
                await pages.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static FileStream OpenDestination(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
    }
}
