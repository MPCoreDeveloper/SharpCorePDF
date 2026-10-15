namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The ASCII filter half of <see cref="PdfFilters"/>.
/// </summary>
internal static partial class PdfFilters
{
    private static byte[] DecodeAsciiHex(ReadOnlyMemory<byte> data)
    {
        // Two hex digits make one byte, so the input is a tight upper bound for the output and the
        // buffer is the right size the first time.
        using var result = new PooledByteBuffer((data.Length / 2) + 1);
        var pending = -1;

        foreach (var value in data.Span)
        {
            if (value == (byte)'>')
            {
                break;
            }

            var nibble = value switch
            {
                >= (byte)'0' and <= (byte)'9' => value - (byte)'0',
                >= (byte)'a' and <= (byte)'f' => value - (byte)'a' + 10,
                >= (byte)'A' and <= (byte)'F' => value - (byte)'A' + 10,
                _ => -1,
            };

            if (nibble < 0)
            {
                continue;
            }

            if (pending < 0)
            {
                pending = nibble;
                continue;
            }

            result.WriteByte((byte)((pending << 4) | nibble));
            pending = -1;
        }

        if (pending >= 0)
        {
            result.WriteByte((byte)(pending << 4));
        }

        return result.ToExactArray();
    }

    private static byte[] DecodeAscii85(ReadOnlyMemory<byte> data)
    {
        // Every five input characters make four bytes or fewer, so the input length is an upper bound.
        using var result = new PooledByteBuffer(data.Length);
        Span<int> group = stackalloc int[5];
        var count = 0;

        foreach (var value in data.Span)
        {
            if (PdfLexer.IsWhitespace(value))
            {
                continue;
            }

            if (value == (byte)'~')
            {
                break;
            }

            if (value == (byte)'z' && count == 0)
            {
                result.Write([0, 0, 0, 0]);
                continue;
            }

            if (value is < 33 or > 117)
            {
                continue;
            }

            group[count++] = value - 33;

            if (count == 5)
            {
                AppendAscii85Group(result, group, 4);
                count = 0;
            }
        }

        if (count > 0)
        {
            for (var index = count; index < 5; index++)
            {
                group[index] = 84;
            }

            AppendAscii85Group(result, group, count - 1);
        }

        return result.ToExactArray();
    }

    private static void AppendAscii85Group(PooledByteBuffer result, ReadOnlySpan<int> group, int byteCount)
    {
        var value = 0u;

        foreach (var digit in group)
        {
            value = (value * 85u) + (uint)digit;
        }

        for (var index = 0; index < byteCount; index++)
        {
            result.WriteByte((byte)(value >> (24 - (index * 8))));
        }
    }
}
