using System.Buffers.Binary;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// A JPEG image passed through to the document: the file's own bytes become the image stream, decoded by
/// <c>DCTDecode</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is re-encoded, so a photographic image costs exactly what the file costs and the document
/// shows the samples the file holds. That is only safe when the file's colour model is one PDF names
/// directly, which is why the reader walks the marker segments: the frame header states the size and
/// the components, the Adobe <c>APP14</c> segment states whether four components are inverted CMYK, and
/// the <c>APP0</c> and <c>APP1</c> segments state the density and the EXIF orientation.
/// </para>
/// <para>
/// An image is refused rather than shown wrongly. A sixteen bit or arithmetic-coded frame, a
/// four-component file with no colour transform, and a file whose EXIF orientation is not "top-left"
/// all name what is wrong: the pixels of the last one would be rotated or mirrored by a viewer, and the
/// bytes cannot be turned by metadata.
/// </para>
/// </remarks>
internal sealed class JpegImage : IEmbeddedImage
{
    private const byte Prefix = 0xFF;

    private JpegImage(
        JpegFrame frame,
        PdfObject colorSpace,
        PdfImageColorSpace sourceColorSpace,
        IReadOnlyList<int>? decode,
        double? widthPoints,
        double? heightPoints)
    {
        Width = frame.Width;
        Height = frame.Height;
        ColorSpace = colorSpace;
        SourceColorSpace = sourceColorSpace;
        BitsPerComponent = frame.Precision;
        Data = frame.Data;
        Decode = decode;
        NaturalWidthPoints = widthPoints;
        NaturalHeightPoints = heightPoints;
    }

    /// <summary>Gets the width in pixels, as the frame header states it.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels, as the frame header states it.</summary>
    public int Height { get; }

    /// <summary>Gets the colour space of the encoded samples.</summary>
    public PdfObject ColorSpace { get; }

    /// <summary>Gets the colour space the frame header states.</summary>
    public PdfImageColorSpace SourceColorSpace { get; }

    /// <summary>Gets the number of bits per sample, which is eight for the frames that are embedded.</summary>
    public int BitsPerComponent { get; }

    /// <summary>Gets the filter that decodes <see cref="Data"/>.</summary>
    public string Filter => "DCTDecode";

    /// <summary>Gets the bytes of the file, exactly as they were read.</summary>
    public byte[] Data { get; }

    /// <summary>Gets the bytes of the file, exactly as they were read.</summary>
    public byte[] ColorData => Data;

    /// <summary>Gets the decode parameters, which <c>DCTDecode</c> does not need: they are <see langword="null"/>.</summary>
    public PdfDictionary? DecodeParms => null;

    /// <summary>Gets the alpha samples, which a JPEG does not carry: they are <see langword="null"/>.</summary>
    public byte[]? AlphaData => null;

    /// <summary>Gets the colour key, which a JPEG does not carry: it is <see langword="null"/>.</summary>
    public IReadOnlyList<int>? ColorKey => null;

    /// <summary>
    /// Gets the <c>/Decode</c> array that flips an inverted CMYK JPEG, or <see langword="null"/> when the
    /// samples are already in the PDF convention.
    /// </summary>
    public IReadOnlyList<int>? Decode { get; }

    /// <summary>Gets the width the density metadata implies, in points, or <see langword="null"/>.</summary>
    public double? NaturalWidthPoints { get; }

    /// <summary>Gets the height the density metadata implies, in points, or <see langword="null"/>.</summary>
    public double? NaturalHeightPoints { get; }

    /// <summary>Reads a JPEG, keeping its bytes.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The image, ready to be written as an image XObject.</returns>
    /// <exception cref="ArgumentException">The bytes are not a JPEG, or the markers are inconsistent.</exception>
    /// <exception cref="NotSupportedException">The frame states a component count PDF cannot name.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">The file names a feature the engine refuses by name.</exception>
    public static JpegImage Parse(ReadOnlyMemory<byte> data)
    {
        var frame = ReadFrame(data);

        if (frame.Orientation != 1)
        {
            throw new PdfUnsupportedFeatureException(
                "JPEG EXIF orientation",
                $"the image states EXIF orientation {frame.Orientation}, so it must be {DescribeOrientation(frame.Orientation)} "
                + "to be shown upright. The JPEG is embedded as it is stored, so the picture would appear wrong; "
                + "rotate it in an image editor and save it without an orientation tag.");
        }

        PdfObject colorSpace;
        PdfImageColorSpace sourceColorSpace;
        IReadOnlyList<int>? decode = null;

        switch (frame.Components)
        {
            case 1:
                colorSpace = new PdfName("DeviceGray");
                sourceColorSpace = PdfImageColorSpace.Gray;
                break;

            case 3:
                colorSpace = new PdfName("DeviceRGB");
                sourceColorSpace = PdfImageColorSpace.Rgb;
                break;

            case 4 when frame.AdobeTransform is 0 or 2:
                // Adobe writes four-component samples inverted, so PDF has to be told to flip them. A
                // transform of 2 states YCCK, which DCTDecode converts before the flip applies.
                colorSpace = new PdfName("DeviceCMYK");
                sourceColorSpace = PdfImageColorSpace.Cmyk;
                decode = [1, 0, 1, 0, 1, 0, 1, 0];
                break;

            case 4:
                throw new PdfUnsupportedFeatureException(
                    "JPEG colour transform",
                    "the frame states four components but the file carries no Adobe APP14 transform, so SharpCorePDF "
                    + "cannot tell CMYK samples from YCCK ones. Re-save the image as an RGB or a tagged CMYK JPEG.");

            default:
                throw new NotSupportedException(
                    $"The JPEG frame states {frame.Components} colour components; SharpCorePDF embeds grayscale, RGB and CMYK JPEG images.");
        }

        var scale = frame.DensityUnits switch
        {
            1 => EmbeddedImage.PointsPerInch,
            2 => EmbeddedImage.PointsPerCentimetre,
            _ => 0d,
        };

        return new JpegImage(
            frame,
            colorSpace,
            sourceColorSpace,
            decode,
            scale > 0d && frame.DensityX > 0 ? frame.Width * scale / frame.DensityX : null,
            scale > 0d && frame.DensityY > 0 ? frame.Height * scale / frame.DensityY : null);
    }

    /// <summary>Walks the marker segments of the file and collects what the writer needs.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The frame values.</returns>
    private static JpegFrame ReadFrame(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < 4 || span[0] != Prefix || span[1] != 0xD8)
        {
            throw new ArgumentException("The bytes do not start with a JPEG SOI marker.", nameof(data));
        }

        var position = 2;
        var header = default(JpegHeader);
        var frameRead = false;
        var densityUnits = 0;
        var densityX = 0;
        var densityY = 0;
        var adobeTransform = -1;
        var orientation = 1;

        while (position < span.Length)
        {
            if (span[position] != Prefix)
            {
                throw new ArgumentException(
                    "The JPEG holds bytes outside a marker segment before its image data.",
                    nameof(data));
            }

            // Any number of 0xFF bytes may pad a marker, which the format calls fill bytes.
            while (position < span.Length && span[position] == Prefix)
            {
                position++;
            }

            if (position >= span.Length)
            {
                break;
            }

            var marker = span[position++];

            if (marker == 0xD9)
            {
                break;
            }

            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (position + 2 > span.Length)
            {
                throw new ArgumentException("The JPEG is truncated: a marker states no length.", nameof(data));
            }

            var length = (span[position] << 8) | span[position + 1];

            if (length < 2 || position + length > span.Length)
            {
                throw new ArgumentException(
                    $"The JPEG is truncated: a marker states a length of {length} bytes, which runs past the end of the file.",
                    nameof(data));
            }

            var body = span.Slice(position + 2, length - 2);
            position += length;

            switch (marker)
            {
                case 0xE0 when body.Length >= 12 && body[..5].SequenceEqual("JFIF\0"u8):
                    densityUnits = body[7];
                    densityX = (body[8] << 8) | body[9];
                    densityY = (body[10] << 8) | body[11];
                    break;

                case 0xE1 when body.Length >= 14 && body[..6].SequenceEqual("Exif\0\0"u8):
                    orientation = ReadOrientation(body[6..], orientation);
                    break;

                case 0xEE when body.Length >= 12 && body[..5].SequenceEqual("Adobe"u8):
                    adobeTransform = body[11];
                    break;

                // SOF0, SOF1 and SOF2 are the frames DCTDecode reads. 0xC4 is the Huffman table, 0xC8 the
                // reserved extension and 0xCC the arithmetic coding conditioning, none of which is a frame.
                case >= 0xC0 and <= 0xCF when marker is not (0xC4 or 0xC8 or 0xCC):
                    header = ReadFrameHeader(marker, body);
                    frameRead = true;
                    break;

                case 0xDA:
                    // Entropy-coded scan data follows, which is not a marker walk any more.
                    position = span.Length;
                    break;
            }
        }

        if (!frameRead)
        {
            throw new ArgumentException("The JPEG holds no frame header (SOF), so its size cannot be read.", nameof(data));
        }

        return new JpegFrame(
            header.Width,
            header.Height,
            header.Components,
            header.Precision,
            orientation,
            adobeTransform,
            densityUnits,
            densityX,
            densityY,
            data.ToArray());
    }

    /// <summary>Reads the frame header of a JPEG.</summary>
    /// <param name="marker">The marker byte, which states how the frame is coded.</param>
    /// <param name="body">The segment body.</param>
    /// <returns>The frame values.</returns>
    private static JpegHeader ReadFrameHeader(byte marker, ReadOnlySpan<byte> body)
    {
        if (marker is not (0xC0 or 0xC1 or 0xC2))
        {
            throw new PdfUnsupportedFeatureException(
                "JPEG frame type",
                $"the file holds a {DescribeFrame(marker)} frame (marker 0xFF{marker:X2}), which PDF's DCTDecode filter does not "
                + "decode. Re-save the image as a baseline or progressive JPEG.");
        }

        if (body.Length < 6)
        {
            throw new ArgumentException("The JPEG frame header is truncated.", nameof(body));
        }

        var precision = body[0];
        var height = (body[1] << 8) | body[2];
        var width = (body[3] << 8) | body[4];
        var components = body[5];

        if (precision != 8)
        {
            throw new PdfUnsupportedFeatureException(
                "JPEG sample precision",
                $"the frame states {precision} bits per sample, and SharpCorePDF embeds eight bit JPEG images. "
                + "Re-save the image with eight bit samples.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException(
                $"The JPEG frame states a size of {width} x {height}, which cannot be drawn.",
                nameof(body));
        }

        return new JpegHeader(width, height, components, precision);
    }

    /// <summary>Reads the EXIF orientation tag of an APP1 segment.</summary>
    /// <param name="tiff">The segment body after the <c>Exif\0\0</c> signature.</param>
    /// <param name="fallback">The orientation to keep when the tag is absent or unreadable.</param>
    /// <returns>The orientation tag, one for "top-left".</returns>
    private static int ReadOrientation(ReadOnlySpan<byte> tiff, int fallback)
    {
        if (tiff.Length < 8)
        {
            return fallback;
        }

        var littleEndian = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        var bigEndian = tiff[0] == (byte)'M' && tiff[1] == (byte)'M';

        if (!littleEndian && !bigEndian)
        {
            return fallback;
        }

        if (ReadShort(tiff, 2, littleEndian) != 42)
        {
            return fallback;
        }

        var directory = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);

        if (directory + 2 > tiff.Length)
        {
            return fallback;
        }

        var entries = ReadShort(tiff, (int)directory, littleEndian);

        for (var index = 0; index < entries; index++)
        {
            var entry = (int)directory + 2 + (index * 12);

            if (entry + 12 > tiff.Length)
            {
                return fallback;
            }

            // Tag 0x0112 is the orientation, and it is stored as a SHORT, whose value sits in the first
            // two bytes of the entry's value field.
            if (ReadShort(tiff, entry, littleEndian) == 0x0112 && ReadShort(tiff, entry + 2, littleEndian) == 3)
            {
                return ReadShort(tiff, entry + 8, littleEndian);
            }
        }

        return fallback;
    }

    /// <summary>Reads a big- or little-endian sixteen bit value from a TIFF block.</summary>
    /// <param name="tiff">The TIFF block.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <param name="littleEndian">Whether the block states the little-endian byte order.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// The read is a static method rather than a local function because a local function may not capture
    /// a <see cref="ReadOnlySpan{T}"/>.
    /// </remarks>
    private static ushort ReadShort(ReadOnlySpan<byte> tiff, int offset, bool littleEndian)
    {
        return littleEndian
            ? (ushort)(tiff[offset] | (tiff[offset + 1] << 8))
            : (ushort)((tiff[offset] << 8) | tiff[offset + 1]);
    }

    /// <summary>Names the transform an EXIF orientation tag states, for the message that refuses it.</summary>
    /// <param name="orientation">The orientation tag.</param>
    /// <returns>The way the stored pixels are transformed to appear upright.</returns>
    private static string DescribeOrientation(int orientation) => orientation switch
    {
        2 => "mirrored horizontally",
        3 => "rotated by 180 degrees",
        4 => "mirrored vertically",
        5 => "mirrored horizontally and rotated by 90 degrees",
        6 => "rotated by 90 degrees clockwise",
        7 => "mirrored horizontally and rotated by 270 degrees",
        8 => "rotated by 270 degrees clockwise",
        _ => "transformed in a way EXIF does not define",
    };

    /// <summary>Names a frame type for the message that refuses it.</summary>
    /// <param name="marker">The frame marker.</param>
    /// <returns>The name of the coding process.</returns>
    private static string DescribeFrame(byte marker) => marker switch
    {
        0xC3 => "lossless sequential",
        0xC5 or 0xC6 or 0xC7 => "differential",
        0xC9 or 0xCA or 0xCB => "arithmetic coded",
        0xCD or 0xCE or 0xCF => "arithmetic coded differential",
        _ => "non-baseline",
    };

    /// <summary>The size and component count of a JPEG frame.</summary>
    private readonly record struct JpegHeader(int Width, int Height, int Components, int Precision);

    /// <summary>Everything the writer needs from a JPEG file.</summary>
    private readonly record struct JpegFrame(
        int Width,
        int Height,
        int Components,
        int Precision,
        int Orientation,
        int AdobeTransform,
        int DensityUnits,
        int DensityX,
        int DensityY,
        byte[] Data);
}
