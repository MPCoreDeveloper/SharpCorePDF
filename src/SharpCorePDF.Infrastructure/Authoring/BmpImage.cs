using System.Buffers.Binary;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// A BMP image prepared for embedding: the uncompressed samples are decoded and written with
/// <c>FlateDecode</c>, because PDF has no filter for the format.
/// </summary>
/// <remarks>
/// <para>
/// The reader covers what Windows and most editors write: a <c>BITMAPINFOHEADER</c> or one of its
/// later, longer forms, uncompressed samples of eight bits through a palette or twenty-four or
/// thirty-two bits per pixel, and either row order. A bottom-up file — the usual one — is turned over,
/// and the rows are trimmed of their four byte padding.
/// </para>
/// <para>
/// <c>BI_RLE8</c>, <c>BI_RLE4</c>, <c>BI_BITFIELDS</c> and the headers that embed a JPEG or a PNG are
/// refused by name. So are one, four and sixteen bit samples: a one bit image would have to be expanded
/// from its bit packing and the sixteen bit variants state their channels in masks, and neither has a
/// test image in the repository to prove the result against.
/// </para>
/// </remarks>
internal sealed class BmpImage : IEmbeddedImage
{
    private const int FileHeaderLength = 14;
    private const int InfoHeaderLength = 40;
    private const int MaximumPaletteEntries = 256;
    private const int ComponentsPerPixel = 3;

    private BmpImage(int width, int height, byte[] colorData, double? widthPoints, double? heightPoints)
    {
        Width = width;
        Height = height;
        ColorData = colorData;
        NaturalWidthPoints = widthPoints;
        NaturalHeightPoints = heightPoints;
        DecodeParms = EmbeddedImage.Predictor(ComponentsPerPixel, width);
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels, which is never negative.</summary>
    public int Height { get; }

    /// <summary>Gets the colour space of the samples, which the reader always writes as RGB.</summary>
    public PdfObject ColorSpace => new PdfName("DeviceRGB");

    /// <summary>Gets the colour space the file states, which is RGB once the palette is expanded.</summary>
    public PdfImageColorSpace SourceColorSpace => PdfImageColorSpace.Rgb;

    /// <summary>Gets the number of bits per sample, which is eight: the samples are written as bytes.</summary>
    public int BitsPerComponent => 8;

    /// <summary>Gets the filter that decodes <see cref="ColorData"/>.</summary>
    public string Filter => "FlateDecode";

    /// <summary>Gets the compressed RGB samples, top row first.</summary>
    public byte[] ColorData { get; }

    /// <summary>Gets the predictor parameters that describe the filtered rows of <see cref="ColorData"/>.</summary>
    public PdfDictionary DecodeParms { get; }

    /// <summary>Gets the alpha samples, which a BMP does not state: they are <see langword="null"/>.</summary>
    public byte[]? AlphaData => null;

    /// <summary>Gets the colour key, which a BMP does not state: it is <see langword="null"/>.</summary>
    public IReadOnlyList<int>? ColorKey => null;

    /// <summary>Gets the <c>/Decode</c> array, which a BMP does not need: it is <see langword="null"/>.</summary>
    public IReadOnlyList<int>? Decode => null;

    /// <summary>Gets the width the resolution header implies, in points, or <see langword="null"/>.</summary>
    public double? NaturalWidthPoints { get; }

    /// <summary>Gets the height the resolution header implies, in points, or <see langword="null"/>.</summary>
    public double? NaturalHeightPoints { get; }

    /// <summary>Reads a BMP image.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The image, ready to be written as an image XObject.</returns>
    /// <exception cref="ArgumentException">The bytes are not a BMP, or the header and the pixel data disagree.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">The file uses a header, a coding or a depth the reader refuses by name.</exception>
    public static BmpImage Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < FileHeaderLength + InfoHeaderLength || span[0] != (byte)'B' || span[1] != (byte)'M')
        {
            throw new ArgumentException("The bytes do not start with a BMP file header.", nameof(data));
        }

        var pixelOffset = ReadInt32(span, 10);
        var headerSize = ReadInt32(span, 14);

        if (headerSize < InfoHeaderLength)
        {
            throw new PdfUnsupportedFeatureException(
                "BMP information header",
                $"the file states a {headerSize} byte header (the OS/2 BITMAPCOREHEADER), whose palette and channel layout "
                + "SharpCorePDF does not read. Re-save the image as a Windows BMP.");
        }

        if (FileHeaderLength + headerSize > span.Length)
        {
            throw new ArgumentException("The BMP information header runs past the end of the file.", nameof(data));
        }

        var width = ReadInt32(span, 18);
        var heightField = ReadInt32(span, 22);
        var planes = ReadInt16(span, 26);
        var bitsPerPixel = ReadInt16(span, 28);
        var compression = ReadInt32(span, 30);
        var pixelsPerMeterX = ReadInt32(span, 38);
        var pixelsPerMeterY = ReadInt32(span, 42);
        var colorsUsed = ReadInt32(span, 46);

        if (width <= 0 || heightField == 0 || heightField == int.MinValue)
        {
            throw new ArgumentException(
                $"The BMP states a size of {width} x {heightField}, which cannot be drawn.",
                nameof(data));
        }

        if (planes != 1)
        {
            throw new ArgumentException($"The BMP states {planes} colour planes; a BMP has one.", nameof(data));
        }

        if (compression != 0)
        {
            throw new PdfUnsupportedFeatureException(
                "BMP compression",
                $"the file states {DescribeCompression(compression)}, and SharpCorePDF reads uncompressed BMP images only. "
                + "Re-save the image as an uncompressed BMP.");
        }

        if (bitsPerPixel is not (8 or 24 or 32))
        {
            throw new PdfUnsupportedFeatureException(
                "BMP pixel depth",
                $"the file states {bitsPerPixel} bits per pixel; SharpCorePDF reads eight bit palettes and twenty-four or "
                + "thirty-two bit colour. Re-save the image as a 24 or 32 bit BMP.");
        }

        var topDown = heightField < 0;
        var height = Math.Abs(heightField);
        var rowStride = (int)(((((long)width * bitsPerPixel) + 31) / 32) * 4);
        var end = pixelOffset + ((long)rowStride * height);

        if (pixelOffset < FileHeaderLength + headerSize || end > span.Length)
        {
            throw new ArgumentException(
                $"The BMP states {rowStride} byte rows for {height} rows at offset {pixelOffset}, which runs past the "
                + $"{span.Length} bytes of the file.",
                nameof(data));
        }

        var palette = bitsPerPixel == 8 ? ReadPalette(span, FileHeaderLength + headerSize, colorsUsed) : null;
        var samples = new byte[width * height * ComponentsPerPixel];

        for (var row = 0; row < height; row++)
        {
            // A positive height stores the bottom row first; a negative one stores the top row first.
            var source = pixelOffset + ((topDown ? row : height - 1 - row) * rowStride);
            var target = row * width * ComponentsPerPixel;

            for (var column = 0; column < width; column++)
            {
                switch (bitsPerPixel)
                {
                    case 8:
                        var entry = span[source + column];
                        samples[target + (column * 3)] = palette![entry * 3];
                        samples[target + (column * 3) + 1] = palette[(entry * 3) + 1];
                        samples[target + (column * 3) + 2] = palette[(entry * 3) + 2];
                        break;

                    case 24:
                        samples[target + (column * 3)] = span[source + (column * 3) + 2];
                        samples[target + (column * 3) + 1] = span[source + (column * 3) + 1];
                        samples[target + (column * 3) + 2] = span[source + (column * 3)];
                        break;

                    default:
                        // The fourth byte of a 32 bit pixel is unused padding unless a mask names it.
                        samples[target + (column * 3)] = span[source + (column * 4) + 2];
                        samples[target + (column * 3) + 1] = span[source + (column * 4) + 1];
                        samples[target + (column * 3) + 2] = span[source + (column * 4)];
                        break;
                }
            }
        }

        var scale = EmbeddedImage.PointsPerCentimetre * 100d;

        return new BmpImage(
            width,
            height,
            EmbeddedImage.CompressWithPredictor(samples, width, ComponentsPerPixel),
            pixelsPerMeterX > 0 ? width * scale / pixelsPerMeterX : null,
            pixelsPerMeterY > 0 ? height * scale / pixelsPerMeterY : null);
    }

    /// <summary>Reads the palette of an eight bit image.</summary>
    /// <param name="span">The bytes of the file.</param>
    /// <param name="offset">The offset of the first entry.</param>
    /// <param name="colorsUsed">The entry count the header states, or zero for a full palette.</param>
    /// <returns>The entries as red, green and blue triples.</returns>
    private static byte[] ReadPalette(ReadOnlySpan<byte> span, int offset, int colorsUsed)
    {
        var entries = colorsUsed is > 0 and <= MaximumPaletteEntries ? colorsUsed : MaximumPaletteEntries;

        if (offset + (entries * 4) > span.Length)
        {
            throw new ArgumentException(
                $"The BMP states {entries} palette entries at offset {offset}, which runs past the {span.Length} bytes of the file.",
                nameof(span));
        }

        var palette = new byte[entries * 3];

        for (var entry = 0; entry < entries; entry++)
        {
            // A palette entry is blue, green, red and a reserved byte.
            palette[(entry * 3)] = span[offset + (entry * 4) + 2];
            palette[(entry * 3) + 1] = span[offset + (entry * 4) + 1];
            palette[(entry * 3) + 2] = span[offset + (entry * 4)];
        }

        return palette;
    }

    /// <summary>Names a BMP compression method for the message that refuses it.</summary>
    /// <param name="compression">The method the header states.</param>
    /// <returns>The name of the method.</returns>
    private static string DescribeCompression(int compression) => compression switch
    {
        1 => "BI_RLE8 run-length encoding",
        2 => "BI_RLE4 run-length encoding",
        3 => "BI_BITFIELDS channel masks",
        4 => "an embedded JPEG (BI_JPEG)",
        5 => "an embedded PNG (BI_PNG)",
        _ => $"compression method {compression}",
    };

    /// <summary>Reads a little-endian signed thirty-two bit integer.</summary>
    /// <param name="span">The bytes.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <returns>The value.</returns>
    private static int ReadInt32(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);

    /// <summary>Reads a little-endian unsigned sixteen bit integer.</summary>
    /// <param name="span">The bytes.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <returns>The value.</returns>
    private static int ReadInt16(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
}
