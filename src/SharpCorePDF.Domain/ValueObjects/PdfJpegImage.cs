namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A JPEG image. The encoded bytes can be embedded unchanged with <c>DCTDecode</c> unless EXIF
/// orientation or a CMYK transform forces the pixels to be rewritten.
/// </summary>
public sealed record PdfJpegImage : PdfImageSource
{
    /// <summary>Initializes a new instance of the <see cref="PdfJpegImage"/> record.</summary>
    /// <param name="jpeg">The encoded JPEG bytes.</param>
    /// <param name="widthPixels">The width in pixels.</param>
    /// <param name="heightPixels">The height in pixels.</param>
    /// <param name="colorSpace">The colour space of the samples.</param>
    /// <param name="bitsPerComponent">The bits per sample; JPEG normally uses 8.</param>
    /// <param name="hasAlpha">Whether the image carries alpha.</param>
    /// <param name="isPassthrough">Whether the JPEG bytes are embedded unchanged.</param>
    /// <param name="naturalWidthPoints">The width at the image's resolution, or null when it has none.</param>
    /// <param name="naturalHeightPoints">The height at the image's resolution, or null when it has none.</param>
    public PdfJpegImage(
        ReadOnlyMemory<byte> jpeg,
        int widthPixels,
        int heightPixels,
        PdfImageColorSpace colorSpace = PdfImageColorSpace.Rgb,
        int bitsPerComponent = 8,
        bool hasAlpha = false,
        bool isPassthrough = true,
        double? naturalWidthPoints = null,
        double? naturalHeightPoints = null)
        : base(
            PdfImageFormat.Jpeg,
            widthPixels,
            heightPixels,
            colorSpace,
            bitsPerComponent,
            hasAlpha,
            isPassthrough,
            jpeg,
            "DCTDecode",
            naturalWidthPoints,
            naturalHeightPoints)
    {
    }
}
