namespace SharpCorePDF.Domain.ReadModels;

/// <summary>How a mask is attached to the image it belongs to.</summary>
public enum PdfImageMaskKind
{
    /// <summary>
    /// A soft mask (<c>/SMask</c>): a separate image whose samples state the opacity of the image.
    /// </summary>
    SoftMask,

    /// <summary>
    /// An explicit mask (<c>/Mask</c> naming a stream): a separate image that states, in the image's
    /// own bit depth, which samples of the image are painted.
    /// </summary>
    ExplicitMask,

    /// <summary>
    /// A colour key mask (<c>/Mask</c> naming an array): ranges of sample values that are not painted.
    /// It is not a separate image and therefore carries no payload.
    /// </summary>
    ColorKey,
}

/// <summary>
/// The mask of an image, returned beside it rather than flattened into it, so a caller that needs the
/// transparency can apply it and a caller that does not is not handed a second, larger image.
/// </summary>
/// <remarks>
/// A mask obeys the same payload rule as the image it belongs to: <see cref="Data"/> is non-null only
/// while the payload is at most <c>PdfEngineOptions.MaxInlineContentBytes</c>, and above that budget
/// the caller fetches it with the copy route. <see cref="ByteLength"/> is truthful either way.
/// </remarks>
public sealed record PdfImageMask
{
    /// <summary>
    /// Gets the identifier of the image this mask belongs to. A mask is resolved by the pair of this
    /// identifier and <see cref="Kind"/>, because a mask an image carries directly has no object number
    /// of its own.
    /// </summary>
    public required string ImageId { get; init; }

    /// <summary>Gets how the mask is attached to its image.</summary>
    public required PdfImageMaskKind Kind { get; init; }

    /// <summary>Gets the object number of the mask image, or <see langword="null"/> when it has none.</summary>
    public long? ObjectNumber { get; init; }

    /// <summary>Gets the generation of the mask image, or <see langword="null"/> when it has none.</summary>
    public int? Generation { get; init; }

    /// <summary>Gets the width of the mask in samples, or <see langword="null"/> when it is not stated.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the height of the mask in samples, or <see langword="null"/> when it is not stated.</summary>
    public int? Height { get; init; }

    /// <summary>Gets the number of bits of each sample, or <see langword="null"/> when it is not stated.</summary>
    public int? BitsPerComponent { get; init; }

    /// <summary>Gets the colour space of the mask as the document names it, or <see langword="null"/>.</summary>
    public string? ColorSpace { get; init; }

    /// <summary>Gets the filter the mask stream declares, or <see langword="null"/> when it declares none.</summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Gets a value indicating whether the payload was produced. It is <see langword="false"/> for a
    /// filter the library does not decode, whose raw bytes are returned instead.
    /// </summary>
    public bool IsDecodable { get; init; } = true;

    /// <summary>
    /// Gets the length of the payload in bytes, which is the decoded length whatever
    /// <see cref="Data"/> holds — zero for a colour key mask, which has no payload.
    /// </summary>
    public long ByteLength { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Data"/> carries the payload.</summary>
    public bool IsBuffered { get; init; }

    /// <summary>
    /// Gets the payload, or <see langword="null"/> when it was not buffered because it exceeds the
    /// inline budget. A <see langword="null"/> here does not mean the mask is absent: the entry
    /// exists, and <see cref="ByteLength"/> states how large it is.
    /// </summary>
    public ReadOnlyMemory<byte>? Data { get; init; }

    /// <summary>
    /// Gets the sample ranges a colour key mask excludes, in the order the document states them.
    /// Empty for a mask that is an image.
    /// </summary>
    public IReadOnlyList<double> ColorKey { get; init; } = [];
}
