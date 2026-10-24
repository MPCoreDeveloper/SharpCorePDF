using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// An ICC profile a source file states for its own samples, with the number of components it describes.
/// </summary>
/// <param name="Data">The profile as the file carries it, decompressed.</param>
/// <param name="Components">The number of components the profile describes: one for grey, three for RGB.</param>
/// <param name="Alternate">The device colour space a reader falls back to when it cannot use the profile.</param>
internal sealed record IccColorProfile(byte[] Data, int Components, string Alternate);

/// <summary>
/// An image prepared for embedding: the samples a page draws, the filter that decodes them, and the
/// mask or colour key that makes part of them transparent.
/// </summary>
/// <remarks>
/// The four formats SharpCorePDF reads land here in two shapes. A PNG keeps its compressed
/// <c>IDAT</c> data and a JPEG keeps its encoded bytes — both are embedded with the filter that reads
/// them, so nothing is inflated and deflated again. A BMP and a GIF are pixel formats PDF has no
/// filter for, so their samples are decoded and written with <c>FlateDecode</c> and a PNG predictor.
/// </remarks>
internal interface IEmbeddedImage
{
    /// <summary>Gets the width in samples.</summary>
    int Width { get; }

    /// <summary>Gets the height in samples.</summary>
    int Height { get; }

    /// <summary>Gets the colour space of the colour stream.</summary>
    PdfObject ColorSpace { get; }

    /// <summary>Gets the colour space the file itself states, which is what a <see cref="PdfImageSource"/> reports.</summary>
    PdfImageColorSpace SourceColorSpace { get; }

    /// <summary>Gets the number of bits per sample.</summary>
    int BitsPerComponent { get; }

    /// <summary>Gets the PDF filter that decodes <see cref="ColorData"/>.</summary>
    string Filter { get; }

    /// <summary>Gets the encoded or compressed colour samples.</summary>
    byte[] ColorData { get; }

    /// <summary>Gets the decode parameters of the colour stream, or <see langword="null"/> when the filter needs none.</summary>
    PdfDictionary? DecodeParms { get; }

    /// <summary>
    /// Gets the compressed alpha samples of an eight bit <c>DeviceGray</c> soft mask, compressed with a
    /// PNG predictor, or <see langword="null"/> when the image is opaque.
    /// </summary>
    byte[]? AlphaData { get; }

    /// <summary>Gets the colour key of an image whose transparency is stated as one sample per component.</summary>
    IReadOnlyList<int>? ColorKey { get; }

    /// <summary>
    /// Gets the <c>/Decode</c> array the colour stream needs, which is how an inverted CMYK JPEG is
    /// written, or <see langword="null"/> when the default decoding applies.
    /// </summary>
    IReadOnlyList<int>? Decode { get; }

    /// <summary>
    /// Gets the ICC profile the file states for its samples, or <see langword="null"/> when it states
    /// none — in which case the device colour space of <see cref="ColorSpace"/> is what a reader uses.
    /// </summary>
    IccColorProfile? IccProfile => null;

    /// <summary>Gets the width the file's resolution metadata implies, in points, or <see langword="null"/>.</summary>
    double? NaturalWidthPoints { get; }

    /// <summary>Gets the height the file's resolution metadata implies, in points, or <see langword="null"/>.</summary>
    double? NaturalHeightPoints { get; }
}

/// <summary>Reads an image file of any supported format and keys the result for the resource registry.</summary>
internal static class EmbeddedImage
{
    /// <summary>The number of points in an inch, which is how a resolution in dots per inch converts.</summary>
    public const double PointsPerInch = 72d;

    /// <summary>The number of points in a centimetre, which is how a resolution in dots per centimetre converts.</summary>
    public const double PointsPerCentimetre = 28.346456692913385d;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Tests whether bytes start with the PNG signature.</summary>
    /// <param name="data">The first bytes of the file.</param>
    /// <returns>Whether the file is a PNG.</returns>
    public static bool IsPng(ReadOnlySpan<byte> data)
    {
        return data.Length >= PngSignature.Length && data[..PngSignature.Length].SequenceEqual(PngSignature);
    }

    /// <summary>Tests whether bytes start with a JPEG marker.</summary>
    /// <param name="data">The first bytes of the file.</param>
    /// <returns>Whether the file is a JPEG.</returns>
    /// <remarks>Every JPEG starts with the start-of-image marker <c>FF D8</c> followed by another marker.</remarks>
    public static bool IsJpeg(ReadOnlySpan<byte> data)
    {
        return data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
    }

    /// <summary>Tests whether bytes start with the BMP signature.</summary>
    /// <param name="data">The first bytes of the file.</param>
    /// <returns>Whether the file is a BMP.</returns>
    public static bool IsBmp(ReadOnlySpan<byte> data)
    {
        return data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M';
    }

    /// <summary>Tests whether bytes start with a GIF signature.</summary>
    /// <param name="data">The first bytes of the file.</param>
    /// <returns>Whether the file is a GIF, of either version.</returns>
    public static bool IsGif(ReadOnlySpan<byte> data)
    {
        return data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8));
    }

    /// <summary>Reads an image, deciding the format from its signature.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The prepared image.</returns>
    /// <exception cref="ArgumentException">The bytes are empty, or their signature is not one of the four formats.</exception>
    /// <exception cref="NotSupportedException">The format is recognised but the variant is not embedded.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">The file names a feature the engine refuses by name.</exception>
    public static IEmbeddedImage Parse(ReadOnlyMemory<byte> data)
    {
        if (IsPng(data.Span))
        {
            return PngImage.Parse(data);
        }

        if (IsJpeg(data.Span))
        {
            return JpegImage.Parse(data);
        }

        if (IsBmp(data.Span))
        {
            return BmpImage.Parse(data);
        }

        if (IsGif(data.Span))
        {
            return GifImage.Parse(data);
        }

        throw new ArgumentException(
            "The image format is not one SharpCorePDF reads; a PNG, JPEG, BMP or GIF signature is required. "
            + $"The first bytes are {Describe(data.Span)}.",
            nameof(data));
    }

    /// <summary>Builds a stable key for the prepared bytes of an image.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The key as a hexadecimal string.</returns>
    /// <remarks>
    /// Two draws of the same bytes produce the same key, so the document carries one image object for
    /// them. Everything that changes what a reader shows is part of the key: the samples, the filter,
    /// the colour space, the mask and the decoding.
    /// </remarks>
    public static string KeyFor(IEmbeddedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[8];

        BinaryPrimitives.WriteInt32BigEndian(number, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(number[4..], image.Height);
        hash.AppendData(number);

        hash.AppendData(Encoding.UTF8.GetBytes(image.Filter));
        hash.AppendData(Encoding.UTF8.GetBytes(image.ColorSpace.ToString() ?? string.Empty));
        hash.AppendData(image.ColorData);

        if (image.IccProfile is { } profile)
        {
            hash.AppendData(profile.Data);
        }

        if (image.AlphaData is { } alpha)
        {
            hash.AppendData(alpha);
        }

        if (image.DecodeParms is { } parms)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(parms.ToString() ?? string.Empty));
        }

        foreach (var value in image.ColorKey ?? [])
        {
            BinaryPrimitives.WriteInt32BigEndian(number, value);
            hash.AppendData(number[..4]);
        }

        foreach (var value in image.Decode ?? [])
        {
            BinaryPrimitives.WriteInt32BigEndian(number, value);
            hash.AppendData(number[..4]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Compresses bytes with the zlib wrapper the <c>FlateDecode</c> filter reads.</summary>
    /// <param name="data">The bytes to compress.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] Compress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();

        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Compresses samples with one filter byte per row, which is what the PDF predictor expects.</summary>
    /// <param name="samples">The unfiltered samples, row by row.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="colors">The number of components per pixel.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] CompressWithPredictor(byte[] samples, int width, int colors)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var rowLength = width * colors;
        var rows = rowLength == 0 ? 0 : samples.Length / rowLength;
        var filtered = new byte[(rowLength + 1) * rows];

        for (var row = 0; row < rows; row++)
        {
            // Filter type 0 is "None": the row is stored as it is, and only the filter byte is added.
            filtered[row * (rowLength + 1)] = 0;
            Buffer.BlockCopy(samples, row * rowLength, filtered, (row * (rowLength + 1)) + 1, rowLength);
        }

        using var output = new MemoryStream();

        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(filtered);
        }

        return output.ToArray();
    }

    /// <summary>Builds the decode parameters of a PNG predictor.</summary>
    /// <param name="colors">The number of components per sample.</param>
    /// <param name="columns">The width in pixels.</param>
    /// <returns>The dictionary.</returns>
    public static PdfDictionary Predictor(int colors, int columns) =>
        new PdfDictionary()
            .SetInteger("Predictor", 15)
            .SetInteger("Colors", colors)
            .SetInteger("BitsPerComponent", 8)
            .SetInteger("Columns", columns);

    /// <summary>Describes the leading bytes of an unrecognised file, for the message that refuses it.</summary>
    /// <param name="span">The bytes.</param>
    /// <returns>The bytes as hexadecimal, separated by spaces.</returns>
    private static string Describe(ReadOnlySpan<byte> span) =>
        span.Length == 0
            ? "no bytes at all"
            : string.Join(
                ' ',
                span[..Math.Min(span.Length, 8)]
                    .ToArray()
                    .Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
}

