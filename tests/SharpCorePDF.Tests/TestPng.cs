using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SharpCorePDF.Tests;

/// <summary>
/// Builds PNG files in memory, so a test can state the colour type, the transparency and the resolution
/// it exercises without shipping a binary fixture for each shape.
/// </summary>
internal static class TestPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Builds an eight bit RGBA image.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="pixel">The colour of a pixel.</param>
    /// <param name="pixelsPerMeter">The resolution written to <c>pHYs</c>, when one is wanted.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] Rgba(
        int width,
        int height,
        Func<int, int, (byte Red, byte Green, byte Blue, byte Alpha)> pixel,
        int? pixelsPerMeter = null)
    {
        var samples = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var colour = pixel(x, y);
                var offset = ((y * width) + x) * 4;
                samples[offset] = colour.Red;
                samples[offset + 1] = colour.Green;
                samples[offset + 2] = colour.Blue;
                samples[offset + 3] = colour.Alpha;
            }
        }

        return Encode(width, height, 6, samples, pixelsPerMeter);
    }

    /// <summary>Builds an eight bit RGB image.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="pixel">The colour of a pixel.</param>
    /// <param name="pixelsPerMeter">The resolution written to <c>pHYs</c>, when one is wanted.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] Rgb(
        int width,
        int height,
        Func<int, int, (byte Red, byte Green, byte Blue)> pixel,
        int? pixelsPerMeter = null)
    {
        var samples = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var colour = pixel(x, y);
                var offset = ((y * width) + x) * 3;
                samples[offset] = colour.Red;
                samples[offset + 1] = colour.Green;
                samples[offset + 2] = colour.Blue;
            }
        }

        return Encode(width, height, 2, samples, pixelsPerMeter);
    }

    /// <summary>Builds an eight bit greyscale image.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="pixel">The sample of a pixel.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] Gray(int width, int height, Func<int, int, byte> pixel)
    {
        var samples = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                samples[(y * width) + x] = pixel(x, y);
            }
        }

        return Encode(width, height, 0, samples, null);
    }

    /// <summary>Builds an indexed image whose first entry is transparent.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] PaletteWithTransparency(int width, int height)
    {
        var indices = new byte[width * height];

        for (var pixel = 0; pixel < indices.Length; pixel++)
        {
            indices[pixel] = (byte)(pixel % 2);
        }

        return Encode(
            width,
            height,
            3,
            indices,
            null,
            [255, 0, 0, 0, 255, 0],
            [255, 0]);
    }

    /// <summary>Builds an RGBA PNG whose header declares Adam7 interlacing.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] Interlaced(int width, int height) =>
        Encode(width, height, 6, new byte[width * height * 4], null, interlace: 1);

    /// <summary>Builds an RGBA PNG whose header declares sixteen bit samples.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] SixteenBit(int width, int height) =>
        Encode(width, height, 6, new byte[width * height * 8], null, bitDepth: 16);

    /// <summary>Builds an eight bit greyscale image with an alpha channel.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="pixel">The sample and alpha of a pixel.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] GrayAlpha(int width, int height, Func<int, int, (byte Value, byte Alpha)> pixel)
    {
        var samples = new byte[width * height * 2];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = pixel(x, y);
                var offset = ((y * width) + x) * 2;
                samples[offset] = value.Value;
                samples[offset + 1] = value.Alpha;
            }
        }

        return Encode(width, height, 4, samples, null);
    }

    /// <summary>Builds an RGB image whose green is a transparent colour key.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] RgbWithColorKey(int width, int height)
    {
        var samples = new byte[width * height * 3];

        for (var pixel = 0; pixel < width * height; pixel++)
        {
            samples[pixel * 3] = 255;
        }

        return Encode(width, height, 2, samples, null, transparency: [0, 0, 0, 255, 0, 0]);
    }

    /// <summary>Builds a PNG from raw samples.</summary>
    private static byte[] Encode(
        int width,
        int height,
        int colorType,
        byte[] samples,
        int? pixelsPerMeter,
        byte[]? palette = null,
        byte[]? transparency = null,
        int bitDepth = 8,
        int interlace = 0)
    {
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            _ => 4,
        };

        var rowLength = (width * channels * bitDepth) / 8;
        var filtered = new byte[(rowLength + 1) * height];

        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(samples, row * rowLength, filtered, (row * (rowLength + 1)) + 1, rowLength);
        }

        using var png = new MemoryStream();
        png.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)bitDepth;
        header[9] = (byte)colorType;
        header[12] = (byte)interlace;
        WriteChunk(png, "IHDR", header);

        if (palette is not null)
        {
            WriteChunk(png, "PLTE", palette);
        }

        if (transparency is not null)
        {
            WriteChunk(png, "tRNS", transparency);
        }

        if (pixelsPerMeter is { } resolution)
        {
            var physical = new byte[9];
            BinaryPrimitives.WriteInt32BigEndian(physical, resolution);
            BinaryPrimitives.WriteInt32BigEndian(physical.AsSpan(4), resolution);
            physical[8] = 1;
            WriteChunk(png, "pHYs", physical);
        }

        WriteChunk(png, "IDAT", Compress(filtered));
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    /// <summary>Writes one PNG chunk.</summary>
    private static void WriteChunk(Stream destination, string type, byte[] data)
    {
        var name = Encoding.ASCII.GetBytes(type);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        destination.Write(length);

        var crcInput = new byte[name.Length + data.Length];
        name.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, name.Length);

        destination.Write(name);
        destination.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        destination.Write(crc);
    }

    /// <summary>Computes the CRC-32 PNG defines.</summary>
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Compresses bytes with the zlib wrapper a PNG uses.</summary>
    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }
}
