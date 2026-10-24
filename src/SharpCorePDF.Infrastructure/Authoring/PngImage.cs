using System.IO.Compression;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// A PNG image prepared for embedding: the colour samples, the optional soft mask, and the natural size
/// its <c>pHYs</c> chunk states.
/// </summary>
/// <remarks>
/// <para>
/// The reader understands eight bit, non-interlaced PNG images of colour types 0 (grey), 2 (RGB),
/// 3 (palette), 4 (grey and alpha) and 6 (RGBA). An interlaced or sixteen bit image is refused with an
/// explanation rather than half decoded: a page that silently shows the wrong picture is worse than a
/// build that stops.
/// </para>
/// <para>
/// Nothing is decoded when it does not have to be. An image without transparency keeps its <c>IDAT</c>
/// bytes and is embedded with <c>FlateDecode</c> and a PNG predictor, so the compressed data is never
/// inflated and deflated again. Only an image with an alpha channel — or a palette whose <c>tRNS</c>
/// chunk makes entries transparent — is decoded, because PDF stores transparency in a separate
/// <c>/SMask</c> stream and the samples have to be split.
/// </para>
/// <para>
/// An <c>iCCP</c> chunk is declared rather than dropped: the profile travels with the image and its
/// colour space becomes <c>/ICCBased</c>, so a reader converts the samples with the same profile the
/// file was written against. A profile that does not describe the samples the image is written with is
/// refused by name, because writing it would describe them wrongly.
/// </para>
/// </remarks>
internal sealed class PngImage : IEmbeddedImage
{
    private const int SignatureLength = 8;
    private const int MaximumPaletteEntries = 256;

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private PngImage(
        int width,
        int height,
        PdfObject colorSpace,
        PdfImageColorSpace sourceColorSpace,
        int colors,
        byte[] colorData,
        byte[]? alphaData,
        IReadOnlyList<int>? colorKey,
        double? widthPoints,
        double? heightPoints,
        IccColorProfile? iccProfile)
    {
        Width = width;
        Height = height;
        ColorSpace = colorSpace;
        SourceColorSpace = sourceColorSpace;
        Colors = colors;
        ColorData = colorData;
        AlphaData = alphaData;
        ColorKey = colorKey;
        DecodeParms = EmbeddedImage.Predictor(colors, width);
        NaturalWidthPoints = widthPoints;
        NaturalHeightPoints = heightPoints;
        IccProfile = iccProfile;
    }

    /// <summary>Gets the width in samples.</summary>
    public int Width { get; }

    /// <summary>Gets the height in samples.</summary>
    public int Height { get; }

    /// <summary>Gets the colour space of the colour stream.</summary>
    public PdfObject ColorSpace { get; }

    /// <summary>Gets the colour space the IHDR chunk and palette state.</summary>
    public PdfImageColorSpace SourceColorSpace { get; }

    /// <summary>Gets the number of colour components per sample: one for grey, three for RGB.</summary>
    public int Colors { get; }

    /// <summary>Gets the number of bits per sample, which is eight: other depths are refused.</summary>
    public int BitsPerComponent => 8;

    /// <summary>Gets the filter that decodes <see cref="ColorData"/>.</summary>
    public string Filter => "FlateDecode";

    /// <summary>Gets the compressed colour samples.</summary>
    public byte[] ColorData { get; }

    /// <summary>Gets the predictor parameters that describe the filtered rows of <see cref="ColorData"/>.</summary>
    public PdfDictionary DecodeParms { get; }

    /// <summary>Gets the compressed alpha samples, or <see langword="null"/> when the image is opaque.</summary>
    public byte[]? AlphaData { get; }

    /// <summary>Gets the colour key of an image whose <c>tRNS</c> chunk names one transparent colour.</summary>
    public IReadOnlyList<int>? ColorKey { get; }

    /// <summary>Gets the <c>/Decode</c> array, which a PNG never states: it is <see langword="null"/>.</summary>
    public IReadOnlyList<int>? Decode => null;

    /// <summary>
    /// Gets the profile an <c>iCCP</c> chunk states, which colour-corrects the samples, or
    /// <see langword="null"/> when the file states none and the samples are device values.
    /// </summary>
    public IccColorProfile? IccProfile { get; }

    /// <summary>Gets the width the <c>pHYs</c> chunk implies, in points, or <see langword="null"/>.</summary>
    public double? NaturalWidthPoints { get; }

    /// <summary>Gets the height the <c>pHYs</c> chunk implies, in points, or <see langword="null"/>.</summary>
    public double? NaturalHeightPoints { get; }

    /// <summary>Gets a value indicating whether the image carries transparency.</summary>
    public bool HasAlpha => AlphaData is not null;

    /// <summary>Parses a PNG image.</summary>
    /// <param name="data">The bytes of the PNG file.</param>
    /// <returns>The image, ready to be written as an image XObject.</returns>
    /// <exception cref="ArgumentException">The bytes are not a valid PNG file.</exception>
    /// <exception cref="NotSupportedException">The PNG uses interlacing, sixteen bit samples, or an unsupported colour type.</exception>
    public static PngImage Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < SignatureLength || !data.Span[..SignatureLength].SequenceEqual(Signature))
        {
            throw new ArgumentException("The image is not a PNG file: the eight byte signature is missing.", nameof(data));
        }

        var header = ReadHeader(data);
        var palette = default(byte[]);
        var transparency = default(byte[]);
        var compressedProfile = default(byte[]);
        var idat = new MemoryStream();
        var pixelsPerMeterX = 0;
        var pixelsPerMeterY = 0;
        var pixelsPerMeterUnit = 0;
        var seenEnd = false;
        var position = SignatureLength;

        while (position + 12 <= data.Length)
        {
            var length = ReadInt32(data.Span, position);

            if (length < 0 || position + 12 + length > data.Length)
            {
                throw new ArgumentException("The PNG is truncated: a chunk states a length beyond the end of the file.", nameof(data));
            }

            var type = System.Text.Encoding.ASCII.GetString(data.Span.Slice(position + 4, 4));
            var body = data.Span.Slice(position + 8, length);

            switch (type)
            {
                case "PLTE":
                    if (length % 3 != 0 || length / 3 is < 1 or > MaximumPaletteEntries)
                    {
                        throw new ArgumentException("The PNG palette is not a list of 1 to 256 RGB triples.", nameof(data));
                    }

                    palette = body.ToArray();
                    break;

                case "tRNS":
                    transparency = body.ToArray();
                    break;

                case "iCCP":
                    compressedProfile = body.ToArray();
                    break;

                case "pHYs":
                    if (length < 9)
                    {
                        throw new ArgumentException("The PNG pHYs chunk is shorter than nine bytes.", nameof(data));
                    }

                    pixelsPerMeterX = ReadInt32(body, 0);
                    pixelsPerMeterY = ReadInt32(body, 4);
                    pixelsPerMeterUnit = body[8];
                    break;

                case "IDAT":
                    idat.Write(body);
                    break;

                case "IEND":
                    seenEnd = true;
                    break;
            }

            position += 12 + length;

            if (seenEnd)
            {
                break;
            }
        }

        if (!seenEnd || idat.Length == 0)
        {
            throw new ArgumentException("The PNG has no IDAT chunk, or no IEND chunk.", nameof(data));
        }

        if (header.ColorType == 3 && palette is null)
        {
            throw new ArgumentException("The PNG is indexed but states no PLTE palette.", nameof(data));
        }

        var colorSpace = BuildColorSpace(header.ColorType, palette, out var colors);
        var sourceColorSpace = header.ColorType switch
        {
            0 or 4 => PdfImageColorSpace.Gray,
            3 => PdfImageColorSpace.Indexed,
            _ => PdfImageColorSpace.Rgb,
        };

        var colorData = idat.ToArray();
        var alphaData = default(byte[]);
        IReadOnlyList<int>? colorKey = null;

        if (header.ColorType is 4 or 6)
        {
            var samples = UnfilterRows(colorData, header);
            var channels = header.ColorType == 6 ? 4 : 2;
            var pixelCount = header.Width * header.Height;
            var colorChannels = channels - 1;
            var rgb = new byte[pixelCount * colorChannels];
            var alpha = new byte[pixelCount];

            for (var pixel = 0; pixel < pixelCount; pixel++)
            {
                for (var channel = 0; channel < colorChannels; channel++)
                {
                    rgb[(pixel * colorChannels) + channel] = samples[(pixel * channels) + channel];
                }

                alpha[pixel] = samples[(pixel * channels) + colorChannels];
            }

            colorData = EmbeddedImage.CompressWithPredictor(rgb, header.Width, colorChannels);
            alphaData = EmbeddedImage.CompressWithPredictor(alpha, header.Width, 1);
            colors = colorChannels;
        }
        else if (header.ColorType == 3 && transparency is not null)
        {
            var samples = UnfilterRows(colorData, header);
            ExpandPalette(samples, palette!, transparency, header.Width, out colorData, out alphaData);

            // The palette has been expanded to RGB, so the stream is no longer indexed.
            colorSpace = new PdfName("DeviceRGB");
            colors = 3;
            sourceColorSpace = PdfImageColorSpace.Rgb;
        }
        else if (header.ColorType is 0 or 2 && transparency is not null)
        {
            // A colour key is a mask, not an alpha channel: PDF states it in the image dictionary, so
            // the samples stay exactly as the PNG stored them.
            colorKey = ReadColorKey(header.ColorType, transparency);
        }

        var scale = pixelsPerMeterUnit == 1 && pixelsPerMeterX > 0 && pixelsPerMeterY > 0
            ? EmbeddedImage.PointsPerInch * 39.37007874015748d
            : 0d;

        // The profile is read last, because a palette is expanded to RGB before it is written: the
        // profile has to describe the samples the document will hold, not the indices the file holds.
        var iccProfile = compressedProfile is null ? null : ReadProfile(compressedProfile, colors);

        if (iccProfile is not null)
        {
            // A file that states a profile does not leave its samples as device values: that is what
            // PdfImage reports, and what the image dictionary says.
            sourceColorSpace = PdfImageColorSpace.IccBased;
        }

        return new PngImage(
            header.Width,
            header.Height,
            colorSpace,
            sourceColorSpace,
            colors,
            colorData,
            alphaData,
            colorKey,
            scale > 0d ? header.Width * scale / pixelsPerMeterX : null,
            scale > 0d ? header.Height * scale / pixelsPerMeterY : null,
            iccProfile);
    }

    /// <summary>Reads and validates the IHDR chunk, which must be the first one.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The header values the reader needs.</returns>
    private static PngHeader ReadHeader(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (data.Length < SignatureLength + 25 || !span.Slice(SignatureLength + 4, 4).SequenceEqual("IHDR"u8))
        {
            throw new ArgumentException("The PNG does not start with an IHDR chunk.", nameof(data));
        }

        var width = ReadInt32(span, SignatureLength + 8);
        var height = ReadInt32(span, SignatureLength + 12);
        var bitDepth = span[SignatureLength + 16];
        var colorType = span[SignatureLength + 17];
        var compression = span[SignatureLength + 18];
        var filter = span[SignatureLength + 19];
        var interlace = span[SignatureLength + 20];

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("The PNG states a width or height of zero.", nameof(data));
        }

        if (bitDepth != 8)
        {
            throw new NotSupportedException(
                $"The PNG uses {bitDepth} bits per sample; SharpCorePDF embeds eight bit PNG images only.");
        }

        if (compression != 0 || filter != 0)
        {
            throw new ArgumentException("The PNG states a compression or filter method that PNG does not define.", nameof(data));
        }

        if (interlace != 0)
        {
            throw new NotSupportedException(
                "The PNG is interlaced (Adam7); SharpCorePDF embeds non-interlaced PNG images only. Re-save the image without interlacing.");
        }

        if (colorType is not (0 or 2 or 3 or 4 or 6))
        {
            throw new NotSupportedException(
                $"The PNG uses colour type {colorType}, which SharpCorePDF does not embed. Supported types are 0, 2, 3, 4 and 6.");
        }

        return new PngHeader(width, height, colorType);
    }

    /// <summary>Builds the PDF colour space of an image.</summary>
    /// <param name="colorType">The PNG colour type.</param>
    /// <param name="palette">The palette of an indexed image.</param>
    /// <param name="colors">The number of colour components per sample.</param>
    /// <returns>The colour space object.</returns>
    private static PdfObject BuildColorSpace(int colorType, byte[]? palette, out int colors)
    {
        switch (colorType)
        {
            case 0:
            case 4:
                colors = 1;
                return new PdfName("DeviceGray");

            case 3:
                colors = 1;
                return new PdfArray(
                [
                    new PdfName("Indexed"),
                    new PdfName("DeviceRGB"),
                    new PdfInteger((palette!.Length / 3) - 1),
                    new PdfString(palette, isHexadecimal: true),
                ]);

            default:
                colors = 3;
                return new PdfName("DeviceRGB");
        }
    }

    /// <summary>Inflates the image data and removes the PNG row filters.</summary>
    /// <param name="compressed">The concatenated IDAT bytes.</param>
    /// <param name="header">The image header.</param>
    /// <returns>The unfiltered samples, without row filter bytes.</returns>
    private static byte[] UnfilterRows(byte[] compressed, PngHeader header)
    {
        var channels = header.ColorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            _ => 4,
        };

        var rowLength = header.Width * channels;
        var expected = header.Height * (rowLength + 1);
        byte[] inflated;

        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var decompressor = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(expected);

            decompressor.CopyTo(output);
            inflated = output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("The PNG image data could not be decompressed.", exception);
        }
        catch (IOException exception)
        {
            throw new ArgumentException("The PNG image data could not be decompressed.", exception);
        }

        if (inflated.Length < expected)
        {
            throw new ArgumentException(
                $"The PNG image data holds {inflated.Length} byte(s) after decompression, but {expected} are needed for {header.Width} x {header.Height} pixels.");
        }

        var samples = new byte[header.Height * rowLength];
        var previous = new byte[rowLength];
        var current = new byte[rowLength];

        for (var row = 0; row < header.Height; row++)
        {
            var offset = row * (rowLength + 1);
            Buffer.BlockCopy(inflated, offset + 1, current, 0, rowLength);
            UnfilterRow(inflated[offset], current, previous, channels, row);
            Buffer.BlockCopy(current, 0, samples, row * rowLength, rowLength);
            (previous, current) = (current, previous);
        }

        return samples;
    }

    /// <summary>Reverses one PNG row filter in place.</summary>
    /// <param name="filter">The filter type of the row.</param>
    /// <param name="row">The filtered row, replaced by the unfiltered one.</param>
    /// <param name="previous">The unfiltered row above.</param>
    /// <param name="bytesPerPixel">The number of bytes one pixel takes.</param>
    /// <param name="rowNumber">The zero-based row number, used in diagnostics.</param>
    private static void UnfilterRow(byte filter, byte[] row, byte[] previous, int bytesPerPixel, int rowNumber)
    {
        for (var index = 0; index < row.Length; index++)
        {
            var left = index >= bytesPerPixel ? row[index - bytesPerPixel] : (byte)0;
            var up = previous[index];
            var upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : (byte)0;

            row[index] = filter switch
            {
                0 => row[index],
                1 => (byte)(row[index] + left),
                2 => (byte)(row[index] + up),
                3 => (byte)(row[index] + ((left + up) / 2)),
                4 => (byte)(row[index] + Paeth(left, up, upperLeft)),
                _ => throw new ArgumentException(
                    $"The PNG uses filter type {filter} in row {rowNumber}; PNG defines filter types 0 to 4."),
            };
        }
    }

    /// <summary>Predicts the byte a Paeth filter would have removed.</summary>
    /// <param name="left">The byte to the left.</param>
    /// <param name="up">The byte above.</param>
    /// <param name="upperLeft">The byte above and to the left.</param>
    /// <returns>The predicted byte.</returns>
    private static byte Paeth(byte left, byte up, byte upperLeft)
    {
        var estimate = left + up - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);

        if (leftDistance <= upDistance && leftDistance <= upperLeftDistance)
        {
            return left;
        }

        return upDistance <= upperLeftDistance ? up : upperLeft;
    }
    /// <summary>The smallest ICC profile the specification allows, which is its 128 byte header.</summary>
    private const int ProfileHeaderLength = 128;

    /// <summary>
    /// Reads the profile an <c>iCCP</c> chunk carries: a Latin-1 name that ends in a null, the
    /// compression method, and the profile itself, deflated.
    /// </summary>
    /// <param name="chunk">The bytes of the chunk.</param>
    /// <param name="colors">The number of components the image is written with.</param>
    /// <returns>The profile, with the device colour space a reader falls back to.</returns>
    /// <exception cref="ArgumentException"><paramref name="chunk"/> does not hold a compressed ICC profile.</exception>
    /// <exception cref="NotSupportedException">The profile describes a colour space the samples cannot use.</exception>
    private static IccColorProfile ReadProfile(byte[] chunk, int colors)
    {
        var terminator = Array.IndexOf(chunk, (byte)0);

        if (terminator < 1 || terminator + 2 > chunk.Length)
        {
            throw new ArgumentException(
                "The PNG iCCP chunk does not state a profile name followed by a compression method.");
        }

        if (chunk[terminator + 1] != 0)
        {
            throw new NotSupportedException(
                $"The PNG states ICC compression method {chunk[terminator + 1]}; PNG defines method 0, deflate, only.");
        }

        byte[] profile;

        try
        {
            using var input = new MemoryStream(chunk, terminator + 2, chunk.Length - terminator - 2, writable: false);
            using var decompressor = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            decompressor.CopyTo(output);
            profile = output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("The PNG iCCP chunk could not be decompressed.", exception);
        }

        if (profile.Length < ProfileHeaderLength || !profile.AsSpan(36, 4).SequenceEqual("acsp"u8))
        {
            throw new ArgumentException(
                "The PNG iCCP chunk does not hold an ICC profile: the acsp signature is missing from its header.");
        }

        var signature = System.Text.Encoding.ASCII.GetString(profile, 16, 4);
        var (components, alternate) = signature switch
        {
            "RGB " => (3, "DeviceRGB"),
            "GRAY" => (1, "DeviceGray"),
            "CMYK" => (4, "DeviceCMYK"),
            _ => throw new NotSupportedException(
                $"The PNG states an ICC profile for the colour space '{signature}', which PDF has no device colour space for."),
        };

        if (components != colors)
        {
            throw new NotSupportedException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"The PNG states a '{signature}' ICC profile but writes {colors} component(s) per pixel."));
        }

        return new IccColorProfile(profile, components, alternate);
    }

    /// <summary>Reads the colour key a <c>tRNS</c> chunk states for a grey or RGB image.</summary>
    /// <param name="colorType">The PNG colour type.</param>
    /// <param name="transparency">The chunk bytes.</param>
    /// <returns>The samples a pixel must match to be transparent, as PDF writes them.</returns>
    private static int[] ReadColorKey(int colorType, byte[] transparency)
    {
        var sampleCount = colorType == 0 ? 1 : 3;

        if (transparency.Length < sampleCount * 2)
        {
            throw new ArgumentException("The PNG tRNS chunk is shorter than the image's colour needs.");
        }

        var key = new int[sampleCount * 2];

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var value = (transparency[sample * 2] << 8) | transparency[(sample * 2) + 1];

            if (value > 0xFF)
            {
                throw new NotSupportedException("The PNG states a tRNS colour key above 255, which only a sixteen bit image can use.");
            }

            key[sample * 2] = value;
            key[(sample * 2) + 1] = value;
        }

        return key;
    }

    /// <summary>Expands an indexed image to RGB and a soft mask, because its transparency is per palette entry.</summary>
    /// <param name="samples">The unfiltered palette indices.</param>
    /// <param name="palette">The palette.</param>
    /// <param name="transparency">The alpha value of each palette entry, when stated.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="colorData">The compressed RGB samples.</param>
    /// <param name="alphaData">The compressed alpha samples.</param>
    private static void ExpandPalette(
        byte[] samples,
        byte[] palette,
        byte[] transparency,
        int width,
        out byte[] colorData,
        out byte[] alphaData)
    {
        var entries = palette.Length / 3;
        var rgb = new byte[samples.Length * 3];
        var alpha = new byte[samples.Length];

        for (var pixel = 0; pixel < samples.Length; pixel++)
        {
            var index = samples[pixel];

            if (index >= entries)
            {
                throw new ArgumentException(
                    $"The PNG palette index {index} is outside the {entries} palette entries the file states.");
            }

            rgb[(pixel * 3) + 0] = palette[(index * 3) + 0];
            rgb[(pixel * 3) + 1] = palette[(index * 3) + 1];
            rgb[(pixel * 3) + 2] = palette[(index * 3) + 2];
            alpha[pixel] = index < transparency.Length ? transparency[index] : (byte)255;
        }

        colorData = EmbeddedImage.CompressWithPredictor(rgb, width, 3);
        alphaData = EmbeddedImage.CompressWithPredictor(alpha, width, 1);
    }

    /// <summary>Reads a big-endian 32 bit value.</summary>
    /// <param name="span">The bytes.</param>
    /// <param name="offset">The position of the value.</param>
    /// <returns>The value.</returns>
    private static int ReadInt32(ReadOnlySpan<byte> span, int offset) =>
        (span[offset] << 24) | (span[offset + 1] << 16) | (span[offset + 2] << 8) | span[offset + 3];

    /// <summary>The values of the IHDR chunk the reader needs.</summary>
    /// <param name="Width">The width in pixels.</param>
    /// <param name="Height">The height in pixels.</param>
    /// <param name="ColorType">The PNG colour type.</param>
    private readonly record struct PngHeader(int Width, int Height, int ColorType);
}
