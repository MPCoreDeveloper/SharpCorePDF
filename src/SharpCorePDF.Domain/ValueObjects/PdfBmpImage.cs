namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A BMP image. Its pixels are decoded and compressed before the image is written, because PDF has
/// no filter that reads a BMP file directly.
/// </summary>
public sealed record PdfBmpImage : PdfImageSource
{
    /// <summary>Initializes a new instance of the <see cref="PdfBmpImage"/> record.</summary>
    /// <param name="bmp">The encoded BMP bytes.</param>
    /// <param name="widthPixels">The width in pixels.</param>
    /// <param name="heightPixels">The height in pixels.</param>
    /// <param name="colorSpace">The colour space of the samples.</param>
    /// <param name="bitsPerComponent">The bits per sample.</param>
    /// <param name="hasAlpha">Whether the image carries alpha.</param>
    /// <param name="isPassthrough">Whether the encoded bytes are embedded unchanged.</param>
    /// <param name="naturalWidthPoints">The width at the image's resolution, or null when it has none.</param>
    /// <param name="naturalHeightPoints">The height at the image's resolution, or null when it has none.</param>
    public PdfBmpImage(
        ReadOnlyMemory<byte> bmp,
        int widthPixels,
        int heightPixels,
        PdfImageColorSpace colorSpace = PdfImageColorSpace.Rgb,
        int bitsPerComponent = 8,
        bool hasAlpha = false,
        bool isPassthrough = false,
        double? naturalWidthPoints = null,
        double? naturalHeightPoints = null)
        : base(
            PdfImageFormat.Bmp,
            widthPixels,
            heightPixels,
            colorSpace,
            bitsPerComponent,
            hasAlpha,
            isPassthrough,
            bmp,
            "FlateDecode",
            naturalWidthPoints,
            naturalHeightPoints)
    {
    }
}
