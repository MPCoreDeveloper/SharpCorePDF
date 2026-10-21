using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Reads the content of a PDF document from a stream. Implemented by the PDF engine.
/// </summary>
/// <remarks>
/// <para>
/// The engine-side counterpart of <see cref="Domain.Ports.IPdfContentReader"/>: the use case opens a
/// content source and hands the stream to this port, which keeps the engine free of the domain's
/// source abstraction and of I/O policy. Every member reads the stream from its current position to
/// its end and does not dispose it.
/// </para>
/// <para>
/// A damaged document is described, not rejected: what could not be read is reported through the
/// warnings of the result. Only content that is not a PDF at all raises
/// <see cref="Domain.Exceptions.PdfSourceFormatException"/>.
/// </para>
/// </remarks>
public interface IPdfPageContentReader
{
    /// <summary>Reads every image the pages of a range draw.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One entry per distinct image, in the order the document draws them first.</returns>
    Task<PdfReadResult<PdfImageInfo>> ReadImagesAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the text the pages of a range draw, grouped into blocks in reading order.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One block per group of lines, in reading order and page by page.</returns>
    /// <remarks>
    /// A font that cannot be mapped costs the glyphs it draws and adds a
    /// <see cref="Domain.ValueObjects.PdfWarningKind.TextUnreadable"/> warning; the rest of the page is
    /// still returned.
    /// </remarks>
    Task<PdfReadResult<PdfTextBlock>> ReadTextAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Copies the payload of one image to a stream the caller supplies.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="image">The image whose payload is wanted.</param>
    /// <param name="destination">A writable stream that receives the payload.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the payload has been written.</returns>
    Task CopyImageDataAsync(
        string sourceName,
        Stream content,
        PdfImageInfo image,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Copies the payload of one image mask to a stream the caller supplies.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="mask">The mask whose payload is wanted.</param>
    /// <param name="destination">A writable stream that receives the payload.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the payload has been written.</returns>
    Task CopyImageMaskDataAsync(
        string sourceName,
        Stream content,
        PdfImageMask mask,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the annotations of the pages of a range.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Every annotation, in page order.</returns>
    Task<PdfReadResult<PdfAnnotationInfo>> ReadAnnotationsAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the fields of the interactive form.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Every field, or an empty result for a document without a form.</returns>
    Task<PdfReadResult<PdfFormFieldInfo>> ReadFormFieldsAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the XMP packet of a document.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The packet, or a result without one for a document that carries none.</returns>
    Task<PdfXmpResult> ReadMetadataAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the files embedded in a document.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Every embedded file, in the order the filename tree states them.</returns>
    Task<PdfReadResult<PdfEmbeddedFileInfo>> ReadEmbeddedFilesAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Copies the payload of one embedded file to a stream the caller supplies.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="file">The file whose bytes are wanted.</param>
    /// <param name="destination">A writable stream that receives the bytes.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the bytes have been written.</returns>
    Task CopyEmbeddedFileDataAsync(
        string sourceName,
        Stream content,
        PdfEmbeddedFileInfo file,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the bookmark tree of a document.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The top-level bookmarks, or an empty result for a document without an outline.</returns>
    Task<PdfReadResult<PdfOutlineItem>> ReadOutlineAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Yields everything known about each page of a range, one page at a time.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A page detail per page of the range, in ascending order.</returns>
    IAsyncEnumerable<PdfPageDetail> ReadPageDetailsAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);
}
