namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The CCITT fax half of <see cref="PdfFilters"/>: Group 3 (T.4) and Group 4 (T.6) coding for bilevel
/// images, which is the shape a scanned page usually has.
/// </summary>
/// <remarks>
/// <para>
/// The decoder reads runs, not bytes. A line is a sequence of white and black runs whose lengths are a
/// Huffman code; Group 4 codes every line relative to the line above it, and Group 3 codes a line on its
/// own or relative to the one above depending on the tag bit after its end-of-line code.
/// </para>
/// <para>
/// The bitmap is built with one bit per pixel, a set bit for black, rows padded to a whole byte. A
/// <c>BlackIs1</c> of false — the default — inverts it at the end, which is the value a DeviceGray image
/// states: 0 is black and 1 is white.
/// </para>
/// </remarks>
internal static partial class PdfFilters
{
    /// <summary>The code tables T.4 fixes, as <c>code:run</c> pairs whose code length is their length.</summary>
    private static readonly string[] CcittWhiteRuns =
    [
        "00110101:0", "000111:1", "0111:2", "1000:3", "1011:4", "1100:5", "1110:6", "1111:7",
        "10011:8", "10100:9", "00111:10", "01000:11", "001000:12", "000011:13", "110100:14", "110101:15",
        "101010:16", "101011:17", "0100111:18", "0001100:19", "0001000:20", "0010111:21", "0000011:22", "0000100:23",
        "0101000:24", "0101011:25", "0010011:26", "0100100:27", "0011000:28", "00000010:29", "00000011:30", "00011010:31",
        "00011011:32", "00010010:33", "00010011:34", "00010100:35", "00010101:36", "00010110:37", "00010111:38", "00101000:39",
        "00101001:40", "00101010:41", "00101011:42", "00101100:43", "00101101:44", "00000100:45", "00000101:46", "00001010:47",
        "00001011:48", "01010010:49", "01010011:50", "01010100:51", "01010101:52", "00100100:53", "00100101:54", "01011000:55",
        "01011001:56", "01011010:57", "01011011:58", "01001010:59", "01001011:60", "00110010:61", "00110011:62", "00110100:63",
        "11011:64", "10010:128", "010111:192", "0110111:256", "00110110:320", "00110111:384", "01100100:448", "01100101:512",
        "01101000:576", "01100111:640", "011001100:704", "011001101:768", "011010010:832", "011010011:896", "011010100:960", "011010101:1024",
        "011010110:1088", "011010111:1152", "011011000:1216", "011011001:1280", "011011010:1344", "011011011:1408", "010011000:1472", "010011001:1536",
        "010011010:1600", "011000:1664", "010011011:1728", "00000001000:1792", "00000001100:1856", "00000001101:1920",
        "000000010010:1984", "000000010011:2048", "000000010100:2112", "000000010101:2176", "000000010110:2240",
        "000000010111:2304", "000000011100:2368", "000000011101:2432", "000000011110:2496", "000000011111:2560",
    ];

    private static readonly string[] CcittBlackRuns =
    [
        "0000110111:0", "010:1", "11:2", "10:3", "011:4", "0011:5", "0010:6", "00011:7",
        "000101:8", "000100:9", "0000100:10", "0000101:11", "0000111:12", "00000100:13", "00000111:14", "000011000:15",
        "0000010111:16", "0000011000:17", "0000001000:18", "00001100111:19", "00001101000:20", "00001101100:21", "00000110111:22", "00000101000:23",
        "00000010111:24", "00000011000:25", "000011001010:26", "000011001011:27", "000011001100:28", "000011001101:29", "000001101000:30", "000001101001:31",
        "000001101010:32", "000001101011:33", "000011010010:34", "000011010011:35", "000011010100:36", "000011010101:37", "000011010110:38", "000011010111:39",
        "000001101100:40", "000001101101:41", "000011011010:42", "000011011011:43", "000001010100:44", "000001010101:45", "000001010110:46", "000001010111:47",
        "000001100100:48", "000001100101:49", "000001010010:50", "000001010011:51", "000000100100:52", "000000110111:53", "000000111000:54", "000000100111:55",
        "000000101000:56", "000001011000:57", "000001011001:58", "000000101011:59", "000000101100:60", "000001011010:61", "000001100110:62", "000001100111:63",
        "0000001111:64", "000011001000:128", "000011001001:192", "000001011011:256", "000000110011:320", "000000110100:384",
        "000000110101:448", "0000001101100:512", "0000001101101:576", "0000001001010:640", "0000001001011:704", "0000001001100:768",
        "0000001001101:832", "0000001110010:896", "0000001110011:960", "0000001110100:1024", "0000001110101:1088", "0000001110110:1152",
        "0000001110111:1216", "0000001010010:1280", "0000001010011:1344", "0000001010100:1408", "0000001010101:1472",
        "0000001011010:1536", "0000001011011:1600", "0000001100100:1664", "0000001100101:1728", "00000001000:1792",
        "00000001100:1856", "00000001101:1920", "000000010010:1984", "000000010011:2048", "000000010100:2112", "000000010101:2176",
        "000000010110:2240", "000000010111:2304", "000000011100:2368", "000000011101:2432", "000000011110:2496", "000000011111:2560",
    ];

    private static readonly Dictionary<int, int> CcittWhiteTable = BuildCcittTable(CcittWhiteRuns);
    private static readonly Dictionary<int, int> CcittBlackTable = BuildCcittTable(CcittBlackRuns);

    /// <summary>Decodes a CCITT Group 3 or Group 4 stream.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="parameters">The decode parameters, or null for the defaults.</param>
    /// <param name="image">The image dictionary, which states the width and height the parameters may omit.</param>
    /// <param name="maxBytes">The largest bitmap that is kept.</param>
    /// <param name="bytes">The packed bitmap, or null above the cap.</param>
    /// <param name="length">The bitmap length in bytes, whether or not it was kept.</param>
    /// <returns>True when the stream decoded.</returns>
    public static bool TryDecodeCcitt(
        ReadOnlyMemory<byte> data,
        PdfDictionary? parameters,
        PdfDictionary? image,
        int maxBytes,
        out byte[]? bytes,
        out long length)
    {
        bytes = null;
        length = 0;

        var k = (int)(parameters?.GetInteger("K") ?? 0);
        var columns = (int)(parameters?.GetInteger("Columns") ?? image?.GetInteger("Width") ?? 1728);
        var rows = (int)(parameters?.GetInteger("Rows") ?? image?.GetInteger("Height") ?? 0);
        var blackIs1 = parameters?.Get("BlackIs1") is PdfBoolean { Value: true };
        var align = parameters?.Get("EncodedByteAlign") is PdfBoolean { Value: true };
        var eol = parameters?.Get("EndOfLine") is not PdfBoolean { Value: false };

        if (columns is <= 0 or > (1 << 20) || rows < 0)
        {
            return false;
        }

        var rowBytes = (columns + 7) / 8;
        var total = rows > 0 ? (long)rowBytes * rows : 0;
        var output = total > 0 && total <= maxBytes ? new byte[total] : null;
        var stream = rows == 0 ? new MemoryStream() : null;
        var keep = rows <= 0 || output is not null;
        var row = new byte[rowBytes];
        var transitions = new List<int>();
        var previous = new List<int>();
        var bits = new CcittBits(data.Span);
        var decoded = 0;
        long produced = 0;

        while (rows == 0 || decoded < rows)
        {
            if (k >= 0 && eol)
            {
                if (!TrySkipEol(ref bits))
                {
                    break;
                }

                if (align)
                {
                    bits.AlignByte();
                }
            }

            var oneDimensional = k == 0;

            if (k > 0)
            {
                if (!bits.TryReadBit(out var tag))
                {
                    break;
                }

                oneDimensional = tag == 1;
            }

            Array.Clear(row);
            transitions.Clear();

            if (!TryDecodeLine(ref bits, row, oneDimensional ? null : previous, columns, transitions))
            {
                if (rows == 0 && decoded > 0)
                {
                    break;
                }

                return false;
            }

            if (output is not null && decoded < rows)
            {
                row.CopyTo(output.AsSpan(decoded * rowBytes));
            }
            else if (stream is not null)
            {
                if (stream.Length + rowBytes <= maxBytes)
                {
                    stream.Write(row);
                }
                else
                {
                    keep = false;
                }
            }

            produced += rowBytes;
            (previous, transitions) = (transitions, previous);
            decoded++;
        }

        if (decoded == 0)
        {
            return false;
        }

        length = produced;
        bytes = output ?? (keep ? stream?.ToArray() : null);

        if (bytes is { } kept)
        {
            if (!blackIs1)
            {
                for (var index = 0; index < kept.Length; index++)
                {
                    kept[index] = (byte)~kept[index];
                }
            }

            // The bits past the last pixel are padding. They are cleared so a caller comparing the
            // bitmap to another decoder's does not see a difference in a bit that is not a pixel.
            var remainder = columns & 7;

            if (remainder != 0)
            {
                var mask = (byte)(0xFF << (8 - remainder));

                for (var index = 0; index < kept.Length; index += rowBytes)
                {
                    kept[index + rowBytes - 1] &= mask;
                }
            }
        }

        return true;
    }

    private static bool TryDecodeLine(
        ref CcittBits bits,
        byte[] row,
        List<int>? reference,
        int columns,
        List<int> transitions)
    {
        var position = 0;
        var black = false;
        var a0 = -1;
        var guard = 0;

        while (position < columns)
        {
            if (++guard > (columns * 2) + 64)
            {
                return false;
            }

            if (reference is null)
            {
                if (!TryReadRun(ref bits, black, out var run))
                {
                    return false;
                }

                var end = position + run;

                if (end > columns)
                {
                    return false;
                }

                if (black)
                {
                    Paint(row, position, end);
                }

                // A zero-length run is a transition at the same position: it is what a line that
                // starts black states, and leaving it out shifts the parity of every later b1.
                transitions.Add(end);
                a0 = end;
                position = end;
                black = !black;
                continue;
            }

            var b1 = FindB1(reference, a0, black, columns, out var b1Index);
            var b2 = NextTransition(reference, b1Index, columns);

            if (!TryReadMode(ref bits, out var mode, out var delta))
            {
                return false;
            }

            if (mode == 2)
            {
                if (b2 <= position)
                {
                    return false;
                }

                position = b2;
                a0 = b2;
                continue;
            }

            if (mode == 1)
            {
                if (!TryReadRun(ref bits, black, out var run1)
                    || !TryReadRun(ref bits, !black, out var run2))
                {
                    return false;
                }

                var a1 = position + run1;
                var a2 = a1 + run2;

                if (a2 > columns)
                {
                    return false;
                }

                if (black)
                {
                    Paint(row, position, a1);
                }
                else
                {
                    Paint(row, a1, a2);
                }

                transitions.Add(a1);
                transitions.Add(a2);
                position = a2;
                a0 = a2;
                continue;
            }

            var transition = b1 + delta;

            if (transition < position || transition > columns)
            {
                return false;
            }

            if (black)
            {
                Paint(row, position, transition);
            }

            transitions.Add(transition);
            position = transition;
            a0 = transition;
            black = !black;
        }

        return true;
    }

    private static bool TryReadRun(ref CcittBits bits, bool black, out int run)
    {
        run = 0;

        while (true)
        {
            if (!TryReadHuffman(ref bits, black ? CcittBlackTable : CcittWhiteTable, out var value))
            {
                return false;
            }

            run += value;

            if (value < 64)
            {
                return true;
            }

            if (run > 2560)
            {
                return false;
            }
        }
    }

    private static bool TryReadHuffman(ref CcittBits bits, Dictionary<int, int> table, out int value)
    {
        var code = 0;

        for (var length = 1; length <= 13; length++)
        {
            if (!bits.TryReadBit(out var bit))
            {
                value = 0;
                return false;
            }

            code = (code << 1) | bit;

            if (table.TryGetValue((length << 16) | code, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryReadMode(ref CcittBits bits, out int mode, out int delta)
    {
        mode = 0;
        delta = 0;
        var code = 0;

        for (var length = 1; length <= 7; length++)
        {
            if (!bits.TryReadBit(out var bit))
            {
                return false;
            }

            code = (code << 1) | bit;

            switch (length, code)
            {
                case (1, 0b1):
                    return true;
                case (3, 0b011):
                    delta = 1;
                    return true;
                case (3, 0b010):
                    delta = -1;
                    return true;
                case (6, 0b000011):
                    delta = 2;
                    return true;
                case (6, 0b000010):
                    delta = -2;
                    return true;
                case (7, 0b0000011):
                    delta = 3;
                    return true;
                case (7, 0b0000010):
                    delta = -3;
                    return true;
                case (3, 0b001):
                    mode = 1;
                    return true;
                case (4, 0b0001):
                    mode = 2;
                    return true;
                case (7, 0b0000001):
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the first reference-line transition of the opposite colour to the current one, strictly to the
    /// right of <paramref name="a0"/>.
    /// </summary>
    /// <remarks>
    /// The comparison is against <paramref name="a0"/> — the last changing element — and not against the
    /// paint position: after a vertical or horizontal mode the last transition is the new position, so
    /// <c>&gt;= position</c> would select the transition the decoder has just consumed. That is the
    /// difference between a Group 4 line that completes and one whose vertical mode resolves to the left.
    /// </remarks>
    private static int FindB1(List<int> reference, int a0, bool black, int columns, out int index)
    {
        // A reference line starts white, so an even transition is a change to black and an odd one to
        // white. b1 is the first transition of the opposite colour strictly after a0.
        for (var candidate = black ? 1 : 0; candidate < reference.Count; candidate += 2)
        {
            if (reference[candidate] > a0)
            {
                index = candidate;
                return reference[candidate];
            }
        }

        index = -1;
        return columns;
    }

    private static int NextTransition(List<int> reference, int index, int columns) =>
        index >= 0 && index + 1 < reference.Count ? reference[index + 1] : columns;

    private static bool TrySkipEol(ref CcittBits bits)
    {
        var zeros = 0;

        while (true)
        {
            if (!bits.TryReadBit(out var bit))
            {
                return false;
            }

            if (bit == 1)
            {
                if (zeros >= 11)
                {
                    return true;
                }

                zeros = 0;
                continue;
            }

            zeros++;
        }
    }

    private static void Paint(byte[] row, int from, int to)
    {
        for (var pixel = from; pixel < to; pixel++)
        {
            row[pixel >> 3] |= (byte)(1 << (7 - (pixel & 7)));
        }
    }

    private static Dictionary<int, int> BuildCcittTable(string[] pairs)
    {
        var table = new Dictionary<int, int>(pairs.Length);

        foreach (var pair in pairs)
        {
            var colon = pair.IndexOf(':');
            var code = Convert.ToInt32(pair[..colon], 2);
            var run = int.Parse(pair[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            table[(colon << 16) | code] = run;
        }

        return table;
    }

    private ref struct CcittBits
    {
        private readonly ReadOnlySpan<byte> data;
        private int offset;
        private int shift;

        public CcittBits(ReadOnlySpan<byte> data)
        {
            this.data = data;
            offset = 0;
            shift = 7;
        }

        public bool TryReadBit(out int value)
        {
            if (offset >= data.Length)
            {
                value = 0;
                return false;
            }

            value = (data[offset] >> shift) & 1;

            if (--shift < 0)
            {
                shift = 7;
                offset++;
            }

            return true;
        }

        public void AlignByte()
        {
            if (shift != 7)
            {
                shift = 7;
                offset++;
            }
        }
    }
}

