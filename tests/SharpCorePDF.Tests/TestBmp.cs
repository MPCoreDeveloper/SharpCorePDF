namespace SharpCorePDF.Tests;

/// <summary>
/// Builds BMP files in memory, so a test can state the depth, the row order, the palette and the
/// resolution it exercises without shipping a binary fixture for each shape.
/// </summary>
internal static class TestBmp
{
    private const int FileHeaderLength = 14;
    private const int InfoHeaderLength = 40;

    /// <summary>Builds an uncompressed BMP with twenty-four or thirty-two bits per pixel.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels, which is stored bottom row first unless <paramref name="topDown"/> is set.</param>
    /// <param name="pixel">Supplies the colour of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="bitsPerPixel">The depth, twenty-four or thirty-two.</param>
    /// <param name="topDown">Whether the top row is stored first, which a negative height states.</param>
    /// <param name="pixelsPerMeter">The resolution the header states, or zero for an unspecified one.</param>
    /// <returns>The bytes of the BMP.</returns>
    public static byte[] TrueColor(
        int width,
        int height,
        Func<int, int, (byte Red, byte Green, byte Blue)> pixel,
        int bitsPerPixel = 24,
        bool topDown = false,
        int pixelsPerMeter = 0)
    {
        var stride = (((width * bitsPerPixel) + 31) / 32) * 4;
        var samples = new byte[stride * height];
        var bytesPerPixel = bitsPerPixel / 8;

        for (var row = 0; row < height; row++)
        {
            var target = (topDown ? row : height - 1 - row) * stride;

            for (var column = 0; column < width; column++)
            {
                var (red, green, blue) = pixel(column, row);
                var offset = target + (column * bytesPerPixel);

                // A pixel is stored blue, green and red; the fourth byte of a thirty-two bit pixel is
                // padding unless a channel mask names it.
                samples[offset] = blue;
                samples[offset + 1] = green;
                samples[offset + 2] = red;
            }
        }

        return Assemble(width, height, bitsPerPixel, topDown, [], samples, pixelsPerMeter);
    }

    /// <summary>Builds an uncompressed eight bit BMP whose pixels are palette indices.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels, which is stored bottom row first unless <paramref name="topDown"/> is set.</param>
    /// <param name="index">Supplies the palette index of a pixel, given its column from the left and its row from the top.</param>
    /// <param name="palette">The colours of the palette, in index order.</param>
    /// <param name="topDown">Whether the top row is stored first, which a negative height states.</param>
    /// <returns>The bytes of the BMP.</returns>
    public static byte[] Indexed(
        int width,
        int height,
        Func<int, int, byte> index,
        (byte Red, byte Green, byte Blue)[] palette,
        bool topDown = false)
    {
        var stride = ((width + 3) / 4) * 4;
        var samples = new byte[stride * height];

        for (var row = 0; row < height; row++)
        {
            var target = (topDown ? row : height - 1 - row) * stride;

            for (var column = 0; column < width; column++)
            {
                samples[target + column] = index(column, row);
            }
        }

        var entries = new byte[palette.Length * 4];

        for (var entry = 0; entry < palette.Length; entry++)
        {
            // A palette entry is blue, green, red and a reserved byte.
            entries[(entry * 4)] = palette[entry].Blue;
            entries[(entry * 4) + 1] = palette[entry].Green;
            entries[(entry * 4) + 2] = palette[entry].Red;
        }

        return Assemble(width, height, 8, topDown, entries, samples, pixelsPerMeter: 0);
    }

    /// <summary>Writes the file header, the information header, the palette and the pixels.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="bitsPerPixel">The depth.</param>
    /// <param name="topDown">Whether the rows are stored top row first.</param>
    /// <param name="palette">The palette entries, four bytes each, or an empty array for a true colour file.</param>
    /// <param name="samples">The pixel data, already padded to the row stride.</param>
    /// <param name="pixelsPerMeter">The resolution the header states, or zero for an unspecified one.</param>
    /// <returns>The bytes of the BMP.</returns>
    private static byte[] Assemble(
        int width,
        int height,
        int bitsPerPixel,
        bool topDown,
        byte[] palette,
        byte[] samples,
        int pixelsPerMeter)
    {
        var pixelOffset = FileHeaderLength + InfoHeaderLength + palette.Length;
        var fileSize = pixelOffset + samples.Length;
        var bytes = new byte[fileSize];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        Write(bytes, 2, fileSize);
        Write(bytes, 10, pixelOffset);
        Write(bytes, 14, InfoHeaderLength);
        Write(bytes, 18, width);
        Write(bytes, 22, topDown ? -height : height);

        // One colour plane, the depth, no compression, and an image size that covers the padded rows.
        bytes[26] = 1;
        bytes[28] = (byte)bitsPerPixel;
        Write(bytes, 34, samples.Length);
        Write(bytes, 38, pixelsPerMeter);
        Write(bytes, 42, pixelsPerMeter);
        Write(bytes, 46, palette.Length / 4);

        palette.CopyTo(bytes, FileHeaderLength + InfoHeaderLength);
        samples.CopyTo(bytes, pixelOffset);

        return bytes;
    }

    /// <summary>Writes a little-endian signed thirty-two bit value.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <param name="value">The value to write.</param>
    private static void Write(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
