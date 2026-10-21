using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Reads what a PDF document already contains — images, annotations, links, form fields, XMP, embedded
/// files and bookmarks — without changing it.
/// </summary>
/// <remarks>
/// <para>
/// The port mirrors <see cref="IPdfInspector"/>: one member per capability, each answering exactly one
/// question about a page range, with the conveniences (a single-page shortcut, a materialised
/// payload, a file destination) declared as extension-block methods so this interface stays the
/// minimal surface a caller can mock. Page numbers are one-based throughout.
/// </para>
/// <para>
/// Nothing here throws for a document that is merely unusual or damaged. A filter this library does
/// not decode, a named destination that does not resolve, an image whose geometry cannot be read: each
/// is reported through the <see cref="PdfWarning"/> list of the result — or, for a page, of the
/// <see cref="PdfPageDetail"/> — and the rest of the read carries on (NFR-7.6, NFR-7.8). An encrypted
/// document that cannot be opened yields empty results plus the warning that says so, exactly as
/// inspection reports it. Malformed input still raises
/// <see cref="Exceptions.PdfSourceFormatException"/>.
/// </para>
/// <para>
/// No member writes a file or opens a destination the caller did not hand in (NFR-7.7).
/// </para>
/// </remarks>
public interface IPdfContentReader
{
    /// <summary>Reads every image the pages of a range draw.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// One entry per distinct image object, in the order the document draws them first, each carrying
    /// every page of the range that draws it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfImageInfo>> ReadImagesAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the text the pages of a range draw, grouped into blocks in reading order.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read; a range past the last page reads the pages that exist.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// One block per group of lines the read found, in reading order and page by page, each carrying its
    /// words and their glyphs. A page with no text contributes no block.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The read is geometric, not semantic: it runs the text operators of each page — including the text
    /// of the form objects the page paints — and turns every glyph into its Unicode text and its place in
    /// page space. Words and blocks are then grouped from those glyphs, and <see cref="ReadModels.PdfWord.Glyphs"/>
    /// keeps the run so the grouping can be redone by the caller.
    /// </para>
    /// <para>
    /// A font the library cannot map through — a missing font resource, an encoding it does not
    /// implement, a composite font without a <c>/ToUnicode</c> map — costs the glyphs it draws and adds a
    /// <see cref="PdfWarningKind.TextUnreadable"/> warning; a character is never guessed. The rest of the
    /// page, and the rest of the document, still reads.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfTextBlock>> ReadTextAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Copies the payload of one image to a stream the caller supplies, whatever its size.</summary>
    /// <param name="source">The content to read; it is opened again and left unmodified.</param>
    /// <param name="image">The image whose payload is wanted, as returned by <see cref="ReadImagesAsync"/>.</param>
    /// <param name="destination">A writable stream that receives the payload.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the payload has been written.</returns>
    /// <remarks>
    /// This is the bounded route: the payload is copied through a pooled scratch buffer, so the working
    /// set is the chunk rather than the image, and the bytes are exactly what the read would have
    /// buffered. The destination is flushed and left open.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task CopyImageDataAsync(
        IPdfContentSource source,
        PdfImageInfo image,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the payload of one image mask to a stream the caller supplies, whatever its size.
    /// </summary>
    /// <param name="source">The content to read; it is opened again and left unmodified.</param>
    /// <param name="mask">The mask whose payload is wanted, as returned beside an image of <see cref="ReadImagesAsync"/>.</param>
    /// <param name="destination">A writable stream that receives the payload.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the payload has been written.</returns>
    /// <remarks>
    /// The mask is resolved by the pair of its <see cref="PdfImageMask.ImageId"/> and its
    /// <see cref="PdfImageMask.Kind"/>, because a mask an image carries directly has no object number of
    /// its own. A colour key mask is not an image and stores no payload, so it is refused. The copy is
    /// the bounded route <see cref="CopyImageDataAsync"/> uses: a pooled scratch buffer, and a
    /// destination that is flushed and left open.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is not writable, the mask is a colour key, or the mask does not
    /// belong to the document.
    /// </exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task CopyImageMaskDataAsync(
        IPdfContentSource source,
        PdfImageMask mask,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the annotations of the pages of a range, links included.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Every annotation, in page order and then in the order the page states them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfAnnotationInfo>> ReadAnnotationsAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the fields of the interactive form, if the document has one.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// Every field with its fully-qualified name, or an empty result for a document without a form.
    /// Nothing is filled in: the form is read, never written.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfFormFieldInfo>> ReadFormFieldsAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the XMP packet of a document.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The packet, or <see langword="null"/> metadata for a document that carries none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfXmpResult> ReadMetadataAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the files embedded in a document.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Every embedded file, in the order the filename tree states them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfEmbeddedFileInfo>> ReadEmbeddedFilesAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Copies the payload of one embedded file to a stream the caller supplies.</summary>
    /// <param name="source">The content to read; it is opened again and left unmodified.</param>
    /// <param name="file">The file whose bytes are wanted, as returned by <see cref="ReadEmbeddedFilesAsync"/>.</param>
    /// <param name="destination">A writable stream that receives the bytes.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the bytes have been written.</returns>
    /// <remarks>
    /// The bounded route, with the same contract as <see cref="CopyImageDataAsync"/>: a pooled scratch
    /// buffer, the destination flushed and left open.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task CopyEmbeddedFileDataAsync(
        IPdfContentSource source,
        PdfEmbeddedFileInfo file,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the bookmark tree of a document.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The top-level bookmarks, each with its children in document order, or an empty result for a
    /// document without an outline.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfOutlineItem>> ReadOutlineAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Yields everything known about each page of a range, one page at a time.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="range">The pages to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A page detail per page of the range, in ascending order.</returns>
    /// <remarks>
    /// The enumeration is lazy: a page is read when it is asked for, so the memory a caller commits is
    /// one page's worth rather than one report's worth (NFR-7.2). A page that could not be read in
    /// full is still yielded, with its warnings. A document that cannot be opened at all — one that is
    /// encrypted and whose password was missing or wrong — yields no page; inspection describes why.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    IAsyncEnumerable<PdfPageDetail> ReadPageDetailsAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);
}
