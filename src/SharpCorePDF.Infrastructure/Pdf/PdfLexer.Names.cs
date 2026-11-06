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
    private string DecodeName(int start, int end) =>
        PdfNameDecoder.Decode(data.AsSpan(start, end - start));

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

            if (PdfNameDecoder.TryParseHexDigit(data[index], out _))
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

            if (!PdfNameDecoder.TryParseHexDigit(current, out var digit))
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
}
