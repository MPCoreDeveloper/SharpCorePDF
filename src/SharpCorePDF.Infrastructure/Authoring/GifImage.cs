using System.Buffers.Binary;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// A GIF image prepared for embedding: the first frame's LZW data is decoded and written with
/// <c>FlateDecode</c>, because PDF has no filter for the format.
/// </summary>
/// <remarks>
/// <para>
/// The reader honours the transparency a graphic control extension states and turns it into an
/// <c>/SMask</c>, which is what makes a GIF with a transparent background draw like the file it came
/// from. A palette — global or local — is expanded to RGB, and an interlaced frame is put back into
/// row order.
/// </para>
/// <para>
/// A GIF may hold an animation. Only the first frame is drawn, because a page draws one picture; the
/// frame count is reported so the caller can say so instead of leaving the reader to wonder.
/// </para>
/// </remarks>
internal sealed class GifImage : IEmbeddedImage
{
    private const int HeaderLength = 13;
    private const int ComponentsPerPixel = 3;
    private const int MaximumCodes = 4096;

    private GifImage(int width, int height, int frameCount, byte[] colorData, byte[]? alphaData)
    {
        Width = width;
        Height = height;
        FrameCount = frameCount;
        ColorData = colorData;
        AlphaData = alphaData;
        DecodeParms = EmbeddedImage.Predictor(ComponentsPerPixel, width);
    }

    /// <summary>Gets the width of the first frame in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the first frame in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of frames the file holds, which is one for a still image.</summary>
    public int FrameCount { get; }

    /// <summary>Gets the colour space of the samples, which the reader always writes as RGB.</summary>
    public PdfObject ColorSpace => new PdfName("DeviceRGB");

    /// <summary>Gets the colour space the file states, which is RGB once the palette is expanded.</summary>
    public PdfImageColorSpace SourceColorSpace => PdfImageColorSpace.Rgb;

    /// <summary>Gets the number of bits per sample, which is eight: the samples are written as bytes.</summary>
    public int BitsPerComponent => 8;

    /// <summary>Gets the filter that decodes <see cref="ColorData"/>.</summary>
    public string Filter => "FlateDecode";

    /// <summary>Gets the compressed RGB samples of the first frame, top row first.</summary>
    public byte[] ColorData { get; }

    /// <summary>Gets the predictor parameters that describe the filtered rows of <see cref="ColorData"/>.</summary>
    public PdfDictionary DecodeParms { get; }

    /// <summary>Gets the compressed alpha samples, or <see langword="null"/> when no pixel is transparent.</summary>
    public byte[]? AlphaData { get; }

    /// <summary>Gets the colour key, which a GIF states as a palette index instead: it is <see langword="null"/>.</summary>
    public IReadOnlyList<int>? ColorKey => null;

    /// <summary>Gets the <c>/Decode</c> array, which a GIF does not need: it is <see langword="null"/>.</summary>
    public IReadOnlyList<int>? Decode => null;

    /// <summary>Gets the natural width, which a GIF does not state: it is <see langword="null"/>.</summary>
    public double? NaturalWidthPoints => null;

    /// <summary>Gets the natural height, which a GIF does not state: it is <see langword="null"/>.</summary>
    public double? NaturalHeightPoints => null;

    /// <summary>Reads a GIF image and decodes its first frame.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The image, ready to be written as an image XObject.</returns>
    /// <exception cref="ArgumentException">The bytes are not a GIF, or the blocks and the data disagree.</exception>
    public static GifImage Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        if (span.Length < HeaderLength || !IsGif(span))
        {
            throw new ArgumentException("The bytes do not start with a GIF87a or GIF89a header.", nameof(data));
        }

        var logicalWidth = ReadUInt16(span, 6);
        var logicalHeight = ReadUInt16(span, 8);
        var packed = span[10];

        if (logicalWidth == 0 || logicalHeight == 0)
        {
            throw new ArgumentException(
                $"The GIF states a logical screen of {logicalWidth} x {logicalHeight}, which cannot be drawn.",
                nameof(data));
        }

        var position = HeaderLength;
        byte[]? globalPalette = null;

        if ((packed & 0x80) != 0)
        {
            globalPalette = ReadPalette(span, ref position, 1 << ((packed & 0x07) + 1));
        }

        var transparentIndex = -1;
        var frames = 0;
        var frameWidth = 0;
        var frameHeight = 0;
        byte[]? colorData = null;
        byte[]? alphaData = null;

        while (position < span.Length)
        {
            var block = span[position++];

            if (block == 0x3B)
            {
                break;
            }

            if (block == 0x21)
            {
                transparentIndex = ReadExtension(span, ref position, transparentIndex);
                continue;
            }

            if (block != 0x2C)
            {
                throw new ArgumentException(
                    $"The GIF holds block 0x{block:X2} where an extension, an image descriptor or the trailer belongs.",
                    nameof(data));
            }

            if (position + 9 > span.Length)
            {
                throw new ArgumentException("The GIF image descriptor is truncated.", nameof(data));
            }

            var imageWidth = ReadUInt16(span, position + 4);
            var imageHeight = ReadUInt16(span, position + 6);
            var imagePacked = span[position + 8];
            position += 9;

            var palette = globalPalette;

            if ((imagePacked & 0x80) != 0)
            {
                palette = ReadPalette(span, ref position, 1 << ((imagePacked & 0x07) + 1));
            }

            if (position >= span.Length)
            {
                throw new ArgumentException("The GIF image descriptor runs past the end of the file.", nameof(data));
            }

            var minimumCodeSize = span[position++];
            var compressed = ReadSubBlocks(span, ref position);
            frames++;

            if (frames > 1)
            {
                continue;
            }

            frameWidth = imageWidth;
            frameHeight = imageHeight;
            DecodeFrame(
                compressed,
                minimumCodeSize,
                frameWidth,
                frameHeight,
                (imagePacked & 0x40) != 0,
                palette ?? throw new ArgumentException(
                    "The GIF holds an image that selects no local palette and the file states no global one.",
                    nameof(data)),
                transparentIndex,
                out colorData,
                out alphaData);
        }

        if (colorData is null)
        {
            throw new ArgumentException("The GIF holds no image descriptor, so there is nothing to draw.", nameof(data));
        }

        return new GifImage(frameWidth, frameHeight, frames, colorData, alphaData);
    }

    /// <summary>Reads an extension block and reports the transparency it states.</summary>
    /// <param name="span">The bytes of the file.</param>
    /// <param name="position">The offset just past the extension's introducing byte.</param>
    /// <param name="transparentIndex">The transparency in force, which an extension without a graphic control keeps.</param>
    /// <returns>The palette index that is transparent, or minus one when nothing is.</returns>
    private static int ReadExtension(ReadOnlySpan<byte> span, ref int position, int transparentIndex)
    {
        if (position >= span.Length)
        {
            throw new ArgumentException("The GIF holds an extension block that runs past the end of the file.", nameof(span));
        }

        var label = span[position++];

        if (label != 0xF9)
        {
            // A comment, a plain text block or an application block says nothing about pixels.
            ReadSubBlocks(span, ref position);
            return transparentIndex;
        }

        var control = ReadSubBlocks(span, ref position);

        return control.Length >= 4 && (control[0] & 0x01) != 0 ? control[3] : -1;
    }

    /// <summary>Reads a chain of length-prefixed sub-blocks into one array.</summary>
    /// <param name="span">The bytes of the file.</param>
    /// <param name="position">The offset of the first length byte.</param>
    /// <returns>The bytes of the chain, without the length bytes and the terminator.</returns>
    private static byte[] ReadSubBlocks(ReadOnlySpan<byte> span, ref int position)
    {
        var buffer = new byte[Math.Min(span.Length, 4096)];
        var length = 0;

        while (true)
        {
            if (position >= span.Length)
            {
                throw new ArgumentException(
                    "The GIF ends inside a data block: the sub-block chain has no terminator.",
                    nameof(span));
            }

            var size = span[position++];

            if (size == 0)
            {
                return buffer.AsSpan(0, length).ToArray();
            }

            if (position + size > span.Length)
            {
                throw new ArgumentException(
                    $"The GIF states a sub-block of {size} bytes at offset {position}, which runs past the "
                    + $"{span.Length} bytes of the file.",
                    nameof(span));
            }

            if (length + size > buffer.Length)
            {
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + size));
            }

            span.Slice(position, size).CopyTo(buffer.AsSpan(length));
            length += size;
            position += size;
        }
    }

    /// <summary>Reads a colour table, which the format already stores as red, green and blue.</summary>
    /// <param name="span">The bytes of the file.</param>
    /// <param name="position">The offset of the first entry.</param>
    /// <param name="entries">The number of entries the size bits state.</param>
    /// <returns>The entries as red, green and blue triples.</returns>
    private static byte[] ReadPalette(ReadOnlySpan<byte> span, ref int position, int entries)
    {
        if (position + (entries * 3) > span.Length)
        {
            throw new ArgumentException(
                $"The GIF states {entries} palette entries at offset {position}, which runs past the "
                + $"{span.Length} bytes of the file.",
                nameof(span));
        }

        var palette = span.Slice(position, entries * 3).ToArray();
        position += entries * 3;

        return palette;
    }

    /// <summary>Reports whether the bytes start with either GIF signature.</summary>
    /// <param name="span">The bytes of the file.</param>
    /// <returns><see langword="true"/> for <c>GIF87a</c> and <c>GIF89a</c>.</returns>
    private static bool IsGif(ReadOnlySpan<byte> span) =>
        span[..6].SequenceEqual("GIF87a"u8) || span[..6].SequenceEqual("GIF89a"u8);

    /// <summary>Reads a little-endian unsigned sixteen bit integer.</summary>
    /// <param name="span">The bytes.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <returns>The value.</returns>
    private static int ReadUInt16(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);

    /// <summary>Decodes a frame into the samples a PDF image stream holds.</summary>
    /// <param name="compressed">The LZW data of the frame, joined from its sub-blocks.</param>
    /// <param name="minimumCodeSize">The LZW minimum code size the frame states.</param>
    /// <param name="width">The width of the frame in pixels.</param>
    /// <param name="height">The height of the frame in pixels.</param>
    /// <param name="interlaced">Whether the rows are stored in the four passes of an interlaced GIF.</param>
    /// <param name="palette">The colour table in force, as red, green and blue triples.</param>
    /// <param name="transparentIndex">The palette index that is transparent, or minus one when nothing is.</param>
    /// <param name="colorData">The compressed RGB samples.</param>
    /// <param name="alphaData">The compressed alpha samples, or <see langword="null"/> for an opaque frame.</param>
    private static void DecodeFrame(
        byte[] compressed,
        int minimumCodeSize,
        int width,
        int height,
        bool interlaced,
        byte[] palette,
        int transparentIndex,
        out byte[] colorData,
        out byte[]? alphaData)
    {
        if (minimumCodeSize is < 2 or > 8)
        {
            throw new ArgumentException(
                $"The GIF states an LZW minimum code size of {minimumCodeSize}; the format allows two through eight.",
                nameof(minimumCodeSize));
        }

        if (width == 0 || height == 0 || (long)width * height > int.MaxValue / ComponentsPerPixel)
        {
            throw new ArgumentException($"The GIF states a frame of {width} x {height}, which cannot be drawn.", nameof(width));
        }

        var pixels = width * height;
        var indices = DecodeLzw(compressed, minimumCodeSize, pixels);

        if (interlaced)
        {
            indices = ReorderInterlaced(indices, width, height);
        }

        var rgb = new byte[pixels * ComponentsPerPixel];
        var alpha = transparentIndex >= 0 ? new byte[pixels] : null;

        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var entry = indices[pixel];

            if ((entry * ComponentsPerPixel) + 2 >= palette.Length)
            {
                throw new ArgumentException(
                    $"The GIF states palette index {entry} where the palette holds "
                    + $"{palette.Length / ComponentsPerPixel} entries.",
                    nameof(palette));
            }

            rgb[pixel * ComponentsPerPixel] = palette[entry * ComponentsPerPixel];
            rgb[(pixel * ComponentsPerPixel) + 1] = palette[(entry * ComponentsPerPixel) + 1];
            rgb[(pixel * ComponentsPerPixel) + 2] = palette[(entry * ComponentsPerPixel) + 2];

            if (alpha is not null)
            {
                alpha[pixel] = entry == transparentIndex ? (byte)0 : (byte)255;
            }
        }

        colorData = EmbeddedImage.CompressWithPredictor(rgb, width, ComponentsPerPixel);
        alphaData = alpha is null ? null : EmbeddedImage.CompressWithPredictor(alpha, width, 1);
    }

    /// <summary>Decodes the variable-length LZW codes of a frame into palette indices.</summary>
    /// <param name="compressed">The LZW data of the frame.</param>
    /// <param name="minimumCodeSize">The LZW minimum code size the frame states.</param>
    /// <param name="pixels">The number of pixels the frame states.</param>
    /// <returns>One palette index per pixel, in row order.</returns>
    private static byte[] DecodeLzw(byte[] compressed, int minimumCodeSize, int pixels)
    {
        var clearCode = 1 << minimumCodeSize;
        var endCode = clearCode + 1;
        var codeSize = minimumCodeSize + 1;
        var next = endCode + 1;

        var prefix = new int[MaximumCodes];
        var suffix = new byte[MaximumCodes];
        var stack = new byte[MaximumCodes];

        for (var code = 0; code < clearCode; code++)
        {
            // A root code stands for itself, and its chain of prefixes ends here.
            prefix[code] = -1;
            suffix[code] = (byte)code;
        }

        var output = new byte[pixels];
        var written = 0;
        var buffer = 0;
        var bits = 0;
        var position = 0;
        var previous = -1;

        while (written < pixels)
        {
            while (bits < codeSize)
            {
                if (position >= compressed.Length)
                {
                    throw new ArgumentException(
                        $"The GIF pixel data ends after {written} of the {pixels} pixels the frame states.",
                        nameof(compressed));
                }

                buffer |= compressed[position++] << bits;
                bits += 8;
            }

            var code = buffer & ((1 << codeSize) - 1);
            buffer >>= codeSize;
            bits -= codeSize;

            if (code == clearCode)
            {
                codeSize = minimumCodeSize + 1;
                next = endCode + 1;
                previous = -1;
                continue;
            }

            if (code == endCode)
            {
                break;
            }

            if (code > next || (code == next && previous < 0))
            {
                throw new ArgumentException(
                    $"The GIF pixel data names code {code} before it is defined.",
                    nameof(compressed));
            }

            // A code equal to the next free entry names that entry, whose last byte is the first byte
            // of the string it extends; every other code names an entry that is already defined.
            var value = code < next ? code : previous;
            var top = 0;

            while (value >= 0)
            {
                if (top == stack.Length)
                {
                    throw new ArgumentException(
                        "The GIF pixel data defines a string longer than the format allows.",
                        nameof(compressed));
                }

                stack[top++] = suffix[value];
                value = prefix[value];
            }

            var first = stack[top - 1];

            while (top > 0 && written < pixels)
            {
                output[written++] = stack[--top];
            }

            if (code == next && written < pixels)
            {
                output[written++] = first;
            }

            if (previous >= 0 && next < MaximumCodes)
            {
                prefix[next] = previous;
                suffix[next] = first;
                next++;

                if (next == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previous = code;
        }

        if (written < pixels)
        {
            throw new ArgumentException(
                $"The GIF pixel data decodes {written} of the {pixels} pixels the frame states.",
                nameof(compressed));
        }

        return output;
    }

    /// <summary>Puts the four passes of an interlaced frame back into row order.</summary>
    /// <param name="indices">The palette indices in the order the file stored them.</param>
    /// <param name="width">The width of the frame in pixels.</param>
    /// <param name="height">The height of the frame in pixels.</param>
    /// <returns>One palette index per pixel, top row first.</returns>
    private static byte[] ReorderInterlaced(byte[] indices, int width, int height)
    {
        var ordered = new byte[indices.Length];
        var source = 0;

        Copy(0, 8);
        Copy(4, 8);
        Copy(2, 4);
        Copy(1, 2);

        return ordered;

        void Copy(int start, int step)
        {
            for (var row = start; row < height; row += step)
            {
                Buffer.BlockCopy(indices, source * width, ordered, row * width, width);
                source++;
            }
        }
    }
}
