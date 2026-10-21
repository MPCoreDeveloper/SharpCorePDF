namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One image a page draws, as the document stores it: the bytes without re-encoding and the geometry
/// the document declares.
/// </summary>
/// <remarks>
/// <para>
/// An image is identified by the object that holds it, not by where it is drawn: an image that a page
/// paints four times, or that two pages share, is one entry carrying every page that draws it. An
/// inline image (<c>BI … ID … EI</c>) has no object number, so it carries a synthetic
/// <see cref="Id"/> instead and <see cref="ObjectNumber"/> is <see langword="null"/>.
/// </para>
/// <para>
/// The payload follows one rule for every read that returns bytes. <see cref="Data"/> is non-null only
/// while the payload is at most <c>PdfEngineOptions.MaxInlineContentBytes</c>; above that budget it is
/// <see langword="null"/>, <see cref="IsBuffered"/> is <see langword="false"/>, and
/// <see cref="ByteLength"/> still states the true length, because nothing failed — the caller fetches
/// the bytes with <c>CopyImageDataAsync</c>. A <see langword="null"/> here is therefore not the
/// "absent" <see langword="null"/> of a metadata entry, and <see cref="ByteLength"/> plus
/// <see cref="IsBuffered"/> are what tell the two cases apart.
/// </para>
/// <para>
/// The bytes are never rasterised, resampled or converted. A <c>DCTDecode</c> image is returned exactly
/// as the file holds it, because a JPEG is a format the caller can already read; every other supported
/// filter is decoded to the samples the image declares. A filter the library does not decode leaves
/// <see cref="IsDecodable"/> cleared and <see cref="Filter"/> naming it, and the raw bytes are still
/// returned rather than the entry being skipped.
/// </para>
/// </remarks>
public sealed record PdfImageInfo
{
    /// <summary>
    /// Gets the identifier of the image: <c>"12 0"</c> for an object, or <c>"inline:3:2"</c> for the
    /// second inline image drawn by page 3. It is stable across calls for the same document.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the object number of the image, or <see langword="null"/> for an inline image, which the
    /// document does not store as an object.
    /// </summary>
    public long? ObjectNumber { get; init; }

    /// <summary>Gets the generation of the image object, or <see langword="null"/> for an inline image.</summary>
    public int? Generation { get; init; }

    /// <summary>
    /// Gets the one-based numbers of every page that draws the image, in ascending order. An image
    /// outside the requested range that is also drawn inside it reports only the pages inside it.
    /// </summary>
    public IReadOnlyList<int> Pages { get; init; } = [];

    /// <summary>Gets the width of the image in samples, or <see langword="null"/> when it is not stated.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the height of the image in samples, or <see langword="null"/> when it is not stated.</summary>
    public int? Height { get; init; }

    /// <summary>Gets the number of bits of each sample, or <see langword="null"/> when it is not stated.</summary>
    public int? BitsPerComponent { get; init; }

    /// <summary>
    /// Gets the colour space as the document names it — <c>DeviceRGB</c>, <c>Indexed</c>,
    /// <c>ICCBased</c> — or <see langword="null"/> when it states none.
    /// </summary>
    public string? ColorSpace { get; init; }

    /// <summary>
    /// Gets the filter the image stream declares, or <see langword="null"/> when it declares none.
    /// A chain of filters is joined with <c>" "</c> in the order the document applies them.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Gets a value indicating whether the payload is in the form the caller asked for. It is
    /// <see langword="false"/> for a filter the library does not decode — <c>JPXDecode</c>,
    /// <c>JBIG2Decode</c>, <c>CCITTFaxDecode</c>, <c>LZWDecode</c>, <c>RunLengthDecode</c> — whose raw
    /// bytes are returned as they are. A <c>DCTDecode</c> image is decodable: it is already in a form a
    /// consumer reads.
    /// </summary>
    public bool IsDecodable { get; init; } = true;

    /// <summary>
    /// Gets the length of the payload in bytes. It is always the true length, whether or not
    /// <see cref="Data"/> carries it, so a caller can size a destination before copying.
    /// </summary>
    public long ByteLength { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Data"/> carries the payload.</summary>
    public bool IsBuffered { get; init; }

    /// <summary>
    /// Gets the payload, or <see langword="null"/> when it was not buffered because it exceeds
    /// <c>PdfEngineOptions.MaxInlineContentBytes</c>. Fetch it with <c>CopyImageDataAsync</c>, which
    /// writes it to a stream of the caller's choosing through a bounded buffer.
    /// </summary>
    public ReadOnlyMemory<byte>? Data { get; init; }

    /// <summary>Gets the mask of the image, or <see langword="null"/> when it has none.</summary>
    public PdfImageMask? Mask { get; init; }

    /// <summary>
    /// Gets the area the image occupies on the page that draws it first, in page space, or
    /// <see langword="null"/> when the content stream does not state a transform that can be read.
    /// </summary>
    public ValueObjects.PdfRectangle? Position { get; init; }

    /// <summary>
    /// Gets a value indicating whether the image is stored inline in a content stream. It is derived
    /// from the synthetic <c>"inline:"</c> identifier rather than from a missing object number, because
    /// an image a resource dictionary holds directly has no object number either.
    /// </summary>
    public bool IsInline => Id.StartsWith("inline:", StringComparison.Ordinal);

    /// <summary>Formats the image for diagnostics.</summary>
    /// <returns>The identifier, the size and the pages that draw it.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"image {Id}: {Width ?? 0} x {Height ?? 0}, {ByteLength} byte(s), page(s) {string.Join(", ", Pages)}");
}
