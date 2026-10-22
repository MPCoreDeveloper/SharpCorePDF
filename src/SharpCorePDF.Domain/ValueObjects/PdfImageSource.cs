namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// An image that can be placed on a page: its encoded bytes and the dimensions, colour space and
/// filter a writer needs to embed it.
/// </summary>
/// <remarks>
/// The hierarchy is closed: a source is a JPEG, PNG, BMP or GIF, and the factory in the
/// infrastructure layer is what decides which. The bytes are kept read-only so the source can be
/// drawn on several pages without being copied.
/// </remarks>
public abstract record PdfImageSource
{
    /// <summary>Initializes a new instance of the <see cref="PdfImageSource"/> record.</summary>
    /// <param name="format">The encoded format.</param>
    /// <param name="widthPixels">The width in pixels.</param>
    /// <param name="heightPixels">The height in pixels.</param>
    /// <param name="colorSpace">The colour space of the samples.</param>
    /// <param name="bitsPerComponent">The bits per sample: 1, 2, 4, 8 or 16.</param>
    /// <param name="hasAlpha">Whether the image carries an alpha channel or a soft mask.</param>
    /// <param name="isPassthrough">Whether the encoded bytes are embedded unchanged.</param>
    /// <param name="encoded">The encoded image bytes.</param>
    /// <param name="filter">The PDF filter that decodes the bytes.</param>
    /// <param name="naturalWidthPoints">The width at the image's resolution, or null when it has none.</param>
    /// <param name="naturalHeightPoints">The height at the image's resolution, or null when it has none.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension, sample size or natural size is outside its range.</exception>
    /// <exception cref="ArgumentException">The bytes are empty or the filter is blank.</exception>
    private protected PdfImageSource(
        PdfImageFormat format,
        int widthPixels,
        int heightPixels,
        PdfImageColorSpace colorSpace,
        int bitsPerComponent,
        bool hasAlpha,
        bool isPassthrough,
        ReadOnlyMemory<byte> encoded,
        string filter,
        double? naturalWidthPoints = null,
        double? naturalHeightPoints = null)
    {
        if (!Enum.IsDefined(format) || format == PdfImageFormat.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "The image format is not one the engine reads.");
        }

        if (widthPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPixels), widthPixels, "An image width must be greater than zero.");
        }

        if (heightPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightPixels), heightPixels, "An image height must be greater than zero.");
        }

        if (!Enum.IsDefined(colorSpace))
        {
            throw new ArgumentOutOfRangeException(nameof(colorSpace), colorSpace, "The colour space is not one the engine writes.");
        }

        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitsPerComponent),
                bitsPerComponent,
                "An image must have 1, 2, 4, 8 or 16 bits per component.");
        }

        if (encoded.IsEmpty)
        {
            throw new ArgumentException("An image source must carry its encoded bytes.", nameof(encoded));
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            throw new ArgumentException("An image source must name the filter that decodes it.", nameof(filter));
        }

        ValidateNaturalSize(naturalWidthPoints, nameof(naturalWidthPoints));
        ValidateNaturalSize(naturalHeightPoints, nameof(naturalHeightPoints));

        Format = format;
        WidthPixels = widthPixels;
        HeightPixels = heightPixels;
        ColorSpace = colorSpace;
        BitsPerComponent = bitsPerComponent;
        HasAlpha = hasAlpha;
        IsPassthrough = isPassthrough;
        Encoded = encoded;
        Filter = filter;
        NaturalWidthPoints = naturalWidthPoints;
        NaturalHeightPoints = naturalHeightPoints;
    }

    /// <summary>Gets the encoded format.</summary>
    public PdfImageFormat Format { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int WidthPixels { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int HeightPixels { get; }

    /// <summary>Gets the colour space of the samples.</summary>
    public PdfImageColorSpace ColorSpace { get; }

    /// <summary>Gets the number of bits per sample.</summary>
    public int BitsPerComponent { get; }

    /// <summary>Gets a value indicating whether the image carries alpha, as an alpha channel or a soft mask.</summary>
    public bool HasAlpha { get; }

    /// <summary>Gets a value indicating whether the encoded bytes are embedded unchanged.</summary>
    public bool IsPassthrough { get; }

    /// <summary>Gets the encoded image bytes.</summary>
    public ReadOnlyMemory<byte> Encoded { get; }

    /// <summary>Gets the PDF filter that decodes the bytes.</summary>
    public string Filter { get; }

    /// <summary>Gets the width at the image's resolution, or null when the image has no resolution metadata.</summary>
    public double? NaturalWidthPoints { get; }

    /// <summary>Gets the height at the image's resolution, or null when the image has no resolution metadata.</summary>
    public double? NaturalHeightPoints { get; }

    private static void ValidateNaturalSize(double? value, string parameterName)
    {
        if (value is { } size && (!double.IsFinite(size) || size <= 0.0))
        {
            throw new ArgumentOutOfRangeException(parameterName, size, "A natural image size must be finite and greater than zero.");
        }
    }
}
