namespace SharpCorePDF.Tests;

/// <summary>
/// Builds GIF files in memory: the header, one global colour table and LZW-compressed frames, so a
/// test can state the interlacing, the transparency and the frame count it exercises.
/// </summary>
/// <remarks>
/// The LZW stream restarts with a clear code before every pixel, which holds the code width at its
/// initial value and makes a hand-written encoder possible: a decoder adds no table entry when the
/// code that follows a clear code is a literal, so the stream only ever names literals.
/// </remarks>
internal static class TestGif
{
    /// <summary>Builds a still image whose first frame is stored row by row.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="index">Supplies the palette index of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="palette">The colours of the global colour table, in index order.</param>
    /// <returns>The bytes of the GIF.</returns>
    public static byte[] Still(
        int width,
        int height,
        Func<int, int, byte> index,
        (byte Red, byte Green, byte Blue)[] palette)
    {
        return Build(width, height, index, palette, interlaced: false, transparentIndex: -1, frames: 1);
    }

    /// <summary>Builds a still image whose graphic control states a transparent palette entry.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="index">Supplies the palette index of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="palette">The colours of the global colour table, in index order.</param>
    /// <param name="transparentIndex">The palette index that draws nothing.</param>
    /// <returns>The bytes of the GIF.</returns>
    public static byte[] Transparent(
        int width,
        int height,
        Func<int, int, byte> index,
        (byte Red, byte Green, byte Blue)[] palette,
        int transparentIndex)
    {
        return Build(width, height, index, palette, interlaced: false, transparentIndex, frames: 1);
    }

    /// <summary>Builds a still image whose frame is stored in the four passes of an interlaced GIF.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="index">Supplies the palette index of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="palette">The colours of the global colour table, in index order.</param>
    /// <returns>The bytes of the GIF.</returns>
    public static byte[] Interlaced(
        int width,
        int height,
        Func<int, int, byte> index,
        (byte Red, byte Green, byte Blue)[] palette)
    {
        return Build(width, height, index, palette, interlaced: true, transparentIndex: -1, frames: 1);
    }

    /// <summary>Builds an animation of several identical frames.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="index">Supplies the palette index of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="palette">The colours of the global colour table, in index order.</param>
    /// <param name="frames">The number of frames to store.</param>
    /// <returns>The bytes of the GIF.</returns>
    public static byte[] Animated(
        int width,
        int height,
        Func<int, int, byte> index,
        (byte Red, byte Green, byte Blue)[] palette,
        int frames)
    {
        return Build(width, height, index, palette, interlaced: false, transparentIndex: -1, frames);
    }

    /// <summary>Writes the header, the colour table and the frames.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="index">Supplies the palette index of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="palette">The colours of the global colour table, in index order.</param>
    /// <param name="interlaced">Whether the frame is stored in the four interlaced passes.</param>
    /// <param name="transparentIndex">The palette index that draws nothing, or minus one for an opaque image.</param>
    /// <param name="frames">The number of frames to store.</param>
    /// <returns>The bytes of the GIF.</returns>
    private static byte[] Build(
        int width,
        int height,
        Func<int, int, byte> index,
        (byte Red, byte Green, byte Blue)[] palette,
        bool interlaced,
        int transparentIndex,
        int frames)
    {
        var entries = 2;

        while (entries < palette.Length)
        {
            entries <<= 1;
        }

        var sizeBits = 0;

        while ((1 << sizeBits) < entries)
        {
            sizeBits++;
        }

        var minimumCodeSize = Math.Max(2, sizeBits);
        var bytes = new List<byte> { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' };

        AddShort(bytes, width);
        AddShort(bytes, height);

        // A global colour table is present, its entries are eight bits of the three primaries, and the
        // size field counts one less than the exponent of the entry count.
        bytes.Add((byte)(0x80 | 0x70 | (sizeBits - 1)));

        // The background index, then the pixel aspect ratio, which zero states as unspecified.
        bytes.Add(0);
        bytes.Add(0);

        for (var entry = 0; entry < entries; entry++)
        {
            var (red, green, blue) = entry < palette.Length
                ? palette[entry]
                : (Red: (byte)0, Green: (byte)0, Blue: (byte)0);

            bytes.Add(red);
            bytes.Add(green);
            bytes.Add(blue);
        }

        var indices = Indices(width, height, index, interlaced);
        var compressed = Compress(indices, minimumCodeSize);

        for (var frame = 0; frame < frames; frame++)
        {
            if (transparentIndex >= 0)
            {
                // A graphic control extension, whose packed field says the table names a transparent
                // entry and whose fourth byte states which one.
                bytes.AddRange([0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, (byte)transparentIndex, 0x00]);
            }

            bytes.Add(0x2C);
            AddShort(bytes, 0);
            AddShort(bytes, 0);
            AddShort(bytes, width);
            AddShort(bytes, height);
            bytes.Add((byte)(interlaced ? 0x40 : 0x00));
            bytes.Add((byte)minimumCodeSize);
            AddSubBlocks(bytes, compressed);
        }

        bytes.Add(0x3B);

        return [.. bytes];
    }

    /// <summary>Collects the palette indices in the order the file stores them.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="index">Supplies the palette index of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="interlaced">Whether to store the rows in the four interlaced passes.</param>
    /// <returns>One palette index per pixel, in file order.</returns>
    private static byte[] Indices(int width, int height, Func<int, int, byte> index, bool interlaced)
    {
        var rows = new List<int>();

        if (interlaced)
        {
            AddRows(0, 8);
            AddRows(4, 8);
            AddRows(2, 4);
            AddRows(1, 2);
        }
        else
        {
            AddRows(0, 1);
        }

        var indices = new byte[width * rows.Count];

        for (var pass = 0; pass < rows.Count; pass++)
        {
            for (var column = 0; column < width; column++)
            {
                indices[(pass * width) + column] = index(column, rows[pass]);
            }
        }

        return indices;

        void AddRows(int start, int step)
        {
            for (var row = start; row < height; row += step)
            {
                rows.Add(row);
            }
        }
    }

    /// <summary>Compresses palette indices with a clear code before every literal.</summary>
    /// <param name="indices">One palette index per pixel.</param>
    /// <param name="minimumCodeSize">The LZW minimum code size the frame states.</param>
    /// <returns>The LZW data of the frame.</returns>
    private static byte[] Compress(byte[] indices, int minimumCodeSize)
    {
        var codes = new List<byte>();
        var codeSize = minimumCodeSize + 1;
        var clearCode = 1 << minimumCodeSize;
        var accumulator = 0;
        var bits = 0;

        foreach (var pixel in indices)
        {
            Emit(clearCode);
            Emit(pixel);
        }

        Emit(clearCode + 1);

        if (bits > 0)
        {
            codes.Add((byte)accumulator);
        }

        return [.. codes];

        // Codes are packed least significant bit first, a byte at a time.
        void Emit(int code)
        {
            accumulator |= code << bits;
            bits += codeSize;

            while (bits >= 8)
            {
                codes.Add((byte)accumulator);
                accumulator >>= 8;
                bits -= 8;
            }
        }
    }

    /// <summary>Writes data as a chain of length-prefixed sub-blocks with a terminator.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="data">The data to write.</param>
    private static void AddSubBlocks(List<byte> bytes, byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += 255)
        {
            var size = Math.Min(255, data.Length - offset);
            var block = new byte[size];
            Array.Copy(data, offset, block, 0, size);

            bytes.Add((byte)size);
            bytes.AddRange(block);
        }

        bytes.Add(0);
    }

    /// <summary>Writes a little-endian unsigned sixteen bit value.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="value">The value to write.</param>
    private static void AddShort(List<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }
}
