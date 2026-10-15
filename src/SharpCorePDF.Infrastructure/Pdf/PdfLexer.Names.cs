using System.Globalization;
using System.Text;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The name, keyword and hexadecimal string scanners of <see cref="PdfLexer"/>.
/// </summary>
/// <remarks>
/// Split from the main file because each scanner carries its own quirks: names escape bytes as
/// <c>#20</c>, hexadecimal strings may contain whitespace and an odd number of digits, and a stray
/// delimiter must still advance the reader so a damaged file cannot make it loop forever.
/// </remarks>
internal sealed partial class PdfLexer
{
    private PdfToken ReadName(int start)
    {
        Position++;
        var nameStart = Position;

        while (Position < data.Length && !IsWhitespaceOrDelimiter(data[Position]))
        {
            Position++;
        }

        var end = Position;

        return new PdfToken
        {
            Type = PdfTokenType.Name,
            Text = DecodeName(nameStart, end),
            Start = start,
            End = end,
        };
    }

    /// <summary>Decodes the bytes of a name.</summary>
    /// <param name="start">The offset of the first byte of the name.</param>
    /// <param name="end">The offset just behind the last byte of the name.</param>
    /// <returns>The decoded name.</returns>
    /// <remarks>
    /// A name is decoded in one pass over the bytes, straight into the string: names that contain no
    /// escape at all — which is nearly all of them — cost one string and nothing else, instead of a
    /// list, its growing array and a second array behind it.
    /// </remarks>
    private string DecodeName(int start, int end)
    {
        var span = data.AsSpan(start, end - start);

        if (!span.Contains((byte)'#'))
        {
            return Encoding.Latin1.GetString(span);
        }

        var decoded = new byte[span.Length];
        var written = 0;
        var remaining = span;

        while (!remaining.IsEmpty)
        {
            var escape = remaining.IndexOf((byte)'#');

            if (escape < 0)
            {
                remaining.CopyTo(decoded.AsSpan(written));
                written += remaining.Length;
                break;
            }

            remaining[..escape].CopyTo(decoded.AsSpan(written));
            written += escape;
            remaining = remaining[(escape + 1)..];

            if (remaining.Length >= 2
                && TryParseHexDigit(remaining[0], out var high)
                && TryParseHexDigit(remaining[1], out var low))
            {
                decoded[written++] = (byte)((high << 4) | low);
                remaining = remaining[2..];
                continue;
            }

            decoded[written++] = (byte)'#';
        }

        return Encoding.Latin1.GetString(decoded.AsSpan(0, written));
    }

    private PdfToken ReadKeyword(int start)
    {
        if (IsDelimiter(data[Position]))
        {
            Position++;
        }
        else
        {
            while (Position < data.Length && !IsWhitespaceOrDelimiter(data[Position]))
            {
                Position++;
            }
        }

        return new PdfToken
        {
            Type = PdfTokenType.Keyword,
            Text = Encoding.Latin1.GetString(data, start, Position - start),
            Start = start,
            End = Position,
        };
    }

    private PdfToken ReadHexString(int start)
    {
        Position++;

        // The digits are counted first so the bytes can be written into an array of exactly the right
        // size: half the digits, rounded up, because an odd number of digits pads the last byte.
        var first = Position;
        var digits = 0;
        var end = data.Length;

        for (var index = Position; index < data.Length; index++)
        {
            if (data[index] == (byte)'>')
            {
                end = index + 1;
                break;
            }

            if (TryParseHexDigit(data[index], out _))
            {
                digits++;
            }
        }

        Position = end;

        var bytes = new byte[(digits + 1) / 2];
        var written = 0;
        var pendingNibble = -1;

        for (var index = first; index < end; index++)
        {
            var current = data[index];

            if (current == (byte)'>')
            {
                break;
            }

            if (!TryParseHexDigit(current, out var digit))
            {
                continue;
            }

            if (pendingNibble < 0)
            {
                pendingNibble = digit;
                continue;
            }

            bytes[written++] = (byte)((pendingNibble << 4) | digit);
            pendingNibble = -1;
        }

        if (pendingNibble >= 0)
        {
            // An odd number of digits is treated as if the last digit were followed by a zero.
            bytes[written] = (byte)(pendingNibble << 4);
        }

        return new PdfToken
        {
            Type = PdfTokenType.HexString,
            Bytes = bytes,
            Start = start,
            End = end,
        };
    }

    private static bool TryParseHexDigit(byte value, out int digit)
    {
        switch (value)
        {
            case >= (byte)'0' and <= (byte)'9':
                digit = value - (byte)'0';
                return true;

            case >= (byte)'a' and <= (byte)'f':
                digit = value - (byte)'a' + 10;
                return true;

            case >= (byte)'A' and <= (byte)'F':
                digit = value - (byte)'A' + 10;
                return true;

            default:
                digit = 0;
                return false;
        }
    }
}
