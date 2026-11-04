namespace SharpCorePDF.Tests;

/// <summary>
/// Builds LZW streams the way a PDF writer does, so the tests that need an encoded payload do not each
/// carry an encoder.
/// </summary>
/// <remarks>
/// The width a code is written at is decided against the count the decoder holds, which is one entry
/// behind the encoder — that lag is what <c>EarlyChange</c> absorbs. Getting it wrong on both sides of a
/// round trip is exactly the fault a self-consistent test cannot see, which is why
/// <see cref="PdfLzwFilterTests"/> also decodes a stream written by another implementation.
/// </remarks>
internal static class TestLzw
{
    /// <summary>Encodes bytes with LZW.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="earlyChange">Whether the code width grows one code early, which is the default.</param>
    /// <returns>The encoded stream, beginning with a clear code and ending with the end-of-data code.</returns>
    public static byte[] Encode(byte[] data, int earlyChange = 1)
    {
        var codes = new List<(int Code, int Width)> { (256, 9) };
        var table = new Dictionary<(int Prefix, byte Suffix), int>();
        var width = 9;
        var nextCode = 258;
        var prefix = -1;

        foreach (var value in data)
        {
            if (prefix < 0)
            {
                prefix = value;
                continue;
            }

            if (table.TryGetValue((prefix, value), out var known))
            {
                prefix = known;
                continue;
            }

            codes.Add((prefix, width));
            table[(prefix, value)] = nextCode++;

            if (width < 12 && nextCode - 1 + earlyChange >= 1 << width)
            {
                width++;
            }

            // The table has no slot left, so the encoder empties it and starts again at nine bits.
            if (nextCode >= 4096)
            {
                codes.Add((256, width));
                table.Clear();
                nextCode = 258;
                width = 9;
            }

            prefix = value;
        }

        if (prefix >= 0)
        {
            codes.Add((prefix, width));
        }

        codes.Add((257, width));

        return Pack(codes);
    }

    /// <summary>Packs codes most significant bit first, across byte boundaries.</summary>
    /// <param name="codes">The codes and the width of each, in the order they are written.</param>
    /// <returns>The packed bytes, padded with zero bits.</returns>
    public static byte[] Pack(IEnumerable<(int Code, int Width)> codes)
    {
        var bytes = new List<byte>();
        var bits = 0;
        var count = 0;

        foreach (var (code, width) in codes)
        {
            bits = (bits << width) | (code & ((1 << width) - 1));
            count += width;

            while (count >= 8)
            {
                count -= 8;
                bytes.Add((byte)(bits >> count));
                bits &= (1 << count) - 1;
            }
        }

        if (count > 0)
        {
            bytes.Add((byte)(bits << (8 - count)));
        }

        return [.. bytes];
    }
}
