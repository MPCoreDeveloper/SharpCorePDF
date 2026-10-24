namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// States the pixels of an image payload, when the payload is one the reader decoded itself.
/// </summary>
/// <remarks>
/// The rule is deliberately narrow: the bytes are exposed as pixels only when nothing is left to
/// interpret — eight bits per sample, a colour space whose samples are the channels, and a decoded
/// payload whose length matches the geometry the image states. Anything else stays a payload: a
/// palette image needs its palette, a sixteen-bit image needs a byte order, and a JPEG needs a decoder
/// this library does not ship.
/// </remarks>
internal static class ImagePixels
{
    /// <summary>The format of an image with one channel per sample.</summary>
    public const string Gray8 = "Gray8";

    /// <summary>The format of an image with three channels interleaved per pixel.</summary>
    public const string Rgb24 = "Rgb24";

    /// <summary>Describes the pixels of a decoded payload.</summary>
    /// <param name="decoded">Whether the library decoded the payload at all.</param>
    /// <param name="filter">The filter chain the image declares, or <see langword="null"/>.</param>
    /// <param name="colorSpace">The colour space the image declares, or <see langword="null"/>.</param>
    /// <param name="width">The width of the image in samples, or <see langword="null"/>.</param>
    /// <param name="height">The height of the image in samples, or <see langword="null"/>.</param>
    /// <param name="bitsPerComponent">The bits of each sample, or <see langword="null"/>.</param>
    /// <param name="data">The decoded payload, or <see langword="null"/> when it was not buffered.</param>
    /// <returns>The format and the pixels, or two <see langword="null"/> values.</returns>
    public static (string? Format, ReadOnlyMemory<byte>? Pixels) Of(
        bool decoded,
        string? filter,
        string? colorSpace,
        int? width,
        int? height,
        int? bitsPerComponent,
        ReadOnlyMemory<byte>? data)
    {
        if (!decoded
            || data is not { } payload
            || filter is null
            || filter.Contains("DCTDecode", StringComparison.Ordinal)
            || width is not > 0
            || height is not > 0
            || bitsPerComponent != 8)
        {
            return (null, null);
        }

        var format = Channels(colorSpace) switch
        {
            1 => Gray8,
            3 => Rgb24,
            _ => null,
        };

        if (format is null)
        {
            return (null, null);
        }

        // A payload that does not carry exactly one byte per sample is not pixels: an image whose
        // declared geometry disagrees with its data is reported as it is rather than reinterpreted.
        var expected = (long)width.Value * height.Value * Channels(colorSpace);

        return payload.Length == expected ? (format, payload) : (null, null);
    }

    /// <summary>Reads the number of channels a colour space has.</summary>
    /// <param name="colorSpace">The colour space as the document names it.</param>
    /// <returns>The number of channels, or zero for a space whose samples are not the channels.</returns>
    private static int Channels(string? colorSpace) => colorSpace switch
    {
        "DeviceGray" or "CalGray" or "G" => 1,
        "DeviceRGB" or "CalRGB" or "RGB" => 3,
        _ => 0,
    };
}
