namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A GIF image. Only the first frame is written, and the later frames are reported through a
/// warning rather than being silently discarded.
/// </summary>
public sealed record PdfGifImage : PdfImageSource
{
    /// <summary>Initializes a new instance of the <see cref="PdfGifImage"/> record.</summary>
    /// <param name="gif">The encoded GIF bytes.</param>
    /// <param name="widthPixels">The width in pixels.</param>
    /// <param name="heightPixels">The height in pixels.</param>
    /// <param name="colorSpace">The colour space of the samples.</param>
    /// <param name="bitsPerComponent">The bits per sample.</param>
    /// <param name="hasAlpha">Whether the first frame carries transparency.</param>
    /// <param name="isPassthrough">Whether the encoded bytes are embedded unchanged.</param>
    /// <param name="naturalWidthPoints">The width at the image's resolution, or null when it has none.</param>
    /// <param name="naturalHeightPoints">The height at the image's resolution, or null when it has none.</param>
    public PdfGifImage(
        ReadOnlyMemory<byte> gif,
        int widthPixels,
        int heightPixels,
        PdfImageColorSpace colorSpace = PdfImageColorSpace.Rgb,
        int bitsPerComponent = 8,
        bool hasAlpha = false,
        bool isPassthrough = false,
        double? naturalWidthPoints = null,
        double? naturalHeightPoints = null)
        : base(
            PdfImageFormat.Gif,
            widthPixels,
            heightPixels,
            colorSpace,
            bitsPerComponent,
            hasAlpha,
            isPassthrough,
            gif,
            "FlateDecode",
            naturalWidthPoints,
            naturalHeightPoints)
    {
    }
}
