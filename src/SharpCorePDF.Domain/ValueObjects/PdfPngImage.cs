namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A PNG image. Transparency becomes a soft mask; palette, interlaced and 16-bit samples are
/// expanded before the image is written when the encoded bytes cannot be embedded as they are.
/// </summary>
public sealed record PdfPngImage : PdfImageSource
{
    /// <summary>Initializes a new instance of the <see cref="PdfPngImage"/> record.</summary>
    /// <param name="png">The encoded PNG bytes.</param>
    /// <param name="widthPixels">The width in pixels.</param>
    /// <param name="heightPixels">The height in pixels.</param>
    /// <param name="colorSpace">The colour space of the samples.</param>
    /// <param name="bitsPerComponent">The bits per sample.</param>
    /// <param name="hasAlpha">Whether the image carries alpha.</param>
    /// <param name="isPassthrough">Whether the encoded bytes are embedded unchanged.</param>
    /// <param name="naturalWidthPoints">The width at the image's resolution, or null when it has none.</param>
    /// <param name="naturalHeightPoints">The height at the image's resolution, or null when it has none.</param>
    public PdfPngImage(
        ReadOnlyMemory<byte> png,
        int widthPixels,
        int heightPixels,
        PdfImageColorSpace colorSpace = PdfImageColorSpace.Rgb,
        int bitsPerComponent = 8,
        bool hasAlpha = false,
        bool isPassthrough = false,
        double? naturalWidthPoints = null,
        double? naturalHeightPoints = null)
        : base(
            PdfImageFormat.Png,
            widthPixels,
            heightPixels,
            colorSpace,
            bitsPerComponent,
            hasAlpha,
            isPassthrough,
            png,
            "FlateDecode",
            naturalWidthPoints,
            naturalHeightPoints)
    {
    }
}
