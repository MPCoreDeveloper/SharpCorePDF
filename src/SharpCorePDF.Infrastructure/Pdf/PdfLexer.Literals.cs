namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The literal string scanner of <see cref="PdfLexer"/>.
/// </summary>
internal sealed partial class PdfLexer
{
    /// <summary>The capacity a string buffer starts with; the pool grows it when a string is longer.</summary>
    private const int InitialStringCapacity = 64;

    private PdfToken ReadLiteralString(int start)
    {
        Position++;

        // The decoded bytes are collected in a borrowed buffer: a document holds thousands of strings,
        // and a fresh list per string is the same amount of garbage as a fresh string per string.
        using var bytes = new PooledByteBuffer(InitialStringCapacity);
        var depth = 1;

        while (Position < data.Length)
        {
            var current = data[Position++];

            if (current == (byte)'\\')
            {
                if (Position >= data.Length)
                {
                    break;
                }

                var escaped = data[Position++];

                switch (escaped)
                {
                    case (byte)'n':
                        bytes.WriteByte((byte)'\n');
                        break;

                    case (byte)'r':
                        bytes.WriteByte((byte)'\r');
                        break;

                    case (byte)'t':
                        bytes.WriteByte((byte)'\t');
                        break;

                    case (byte)'b':
                        bytes.WriteByte((byte)'\b');
                        break;

                    case (byte)'f':
                        bytes.WriteByte((byte)'\f');
                        break;

                    case (byte)'\r':
                        if (Position < data.Length && data[Position] == (byte)'\n')
                        {
                            Position++;
                        }

                        break;

                    case (byte)'\n':
                        // A backslash directly before an end of line continues the string.
                        break;

                    case >= (byte)'0' and <= (byte)'7':
                        var value = escaped - (byte)'0';
                        var digits = 1;

                        while (digits < 3
                            && Position < data.Length
                            && data[Position] >= (byte)'0'
                            && data[Position] <= (byte)'7')
                        {
                            value = (value * 8) + (data[Position++] - (byte)'0');
                            digits++;
                        }

                        bytes.WriteByte((byte)value);
                        break;

                    default:
                        bytes.WriteByte(escaped);
                        break;
                }

                continue;
            }

            if (current == (byte)'(')
            {
                depth++;
                bytes.WriteByte(current);
                continue;
            }

            if (current == (byte)')')
            {
                depth--;

                if (depth == 0)
                {
                    break;
                }

                bytes.WriteByte(current);
                continue;
            }

            // An unescaped end of line inside a string is a line feed per the specification.
            if (current == (byte)'\r')
            {
                if (Position < data.Length && data[Position] == (byte)'\n')
                {
                    Position++;
                }

                bytes.WriteByte((byte)'\n');
                continue;
            }

            bytes.WriteByte(current);
        }

        return new PdfToken
        {
            Type = PdfTokenType.LiteralString,
            Bytes = bytes.ToExactArray(),
            Start = start,
            End = Position,
        };
    }
}
