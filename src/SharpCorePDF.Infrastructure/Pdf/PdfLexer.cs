using System.Globalization;
using System.Text;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Turns PDF bytes into <see cref="PdfToken"/> values.
/// </summary>
/// <remarks>
/// The lexer is deliberately tolerant. Real world PDF files are written by hundreds of different
/// producers and contain stray whitespace, unbalanced strings and out of spec escapes; rejecting
/// them would make the library useless on the documents people actually have. Anything the lexer
/// cannot make sense of surfaces as a best effort token rather than an exception.
/// </remarks>
internal sealed partial class PdfLexer
{
    private readonly byte[] data;

    /// <summary>Initializes a new instance of the <see cref="PdfLexer"/> class.</summary>
    /// <param name="data">The bytes of the document.</param>
    /// <param name="position">The offset to start reading at.</param>
    public PdfLexer(byte[] data, int position = 0)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        Position = Math.Clamp(position, 0, data.Length);
    }

    /// <summary>Gets the bytes being read.</summary>
    public byte[] Data => data;

    /// <summary>Gets the bytes being read as a memory, so a stream body can be a view of it.</summary>
    public ReadOnlyMemory<byte> Memory => data;

    /// <summary>Gets or sets the current offset.</summary>
    public int Position { get; set; }

    /// <summary>Gets the total length of the data.</summary>
    public int Length => data.Length;

    /// <summary>Gets a value indicating whether the end of the data has been reached.</summary>
    public bool AtEnd => Position >= data.Length;

    /// <summary>Determines whether a byte is PDF whitespace.</summary>
    /// <param name="value">The byte to test.</param>
    /// <returns><see langword="true"/> for the six whitespace characters of the PDF specification.</returns>
    public static bool IsWhitespace(byte value) =>
        value is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

    /// <summary>Determines whether a byte is a PDF delimiter.</summary>
    /// <param name="value">The byte to test.</param>
    /// <returns><see langword="true"/> for the PDF delimiter characters.</returns>
    public static bool IsDelimiter(byte value) =>
        value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
            or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    /// <summary>Determines whether a byte ends a token.</summary>
    /// <param name="value">The byte to test.</param>
    /// <returns><see langword="true"/> when the byte is whitespace or a delimiter.</returns>
    public static bool IsWhitespaceOrDelimiter(byte value) => IsWhitespace(value) || IsDelimiter(value);

    /// <summary>Skips whitespace and comments.</summary>
    public void SkipWhitespaceAndComments()
    {
        while (Position < data.Length)
        {
            var current = data[Position];

            if (IsWhitespace(current))
            {
                Position++;
                continue;
            }

            if (current == (byte)'%')
            {
                Position++;

                while (Position < data.Length && data[Position] is not ((byte)'\n' or (byte)'\r'))
                {
                    Position++;
                }

                continue;
            }

            return;
        }
    }

    /// <summary>Reads a keyword when it is next, for example <c>obj</c> or <c>stream</c>.</summary>
    /// <param name="keyword">The keyword to match, without whitespace.</param>
    /// <returns><see langword="true"/> when the keyword was read and consumed.</returns>
    public bool TryReadKeyword(string keyword)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyword);

        SkipWhitespaceAndComments();

        if (Position + keyword.Length > data.Length)
        {
            return false;
        }

        for (var index = 0; index < keyword.Length; index++)
        {
            if (data[Position + index] != (byte)keyword[index])
            {
                return false;
            }
        }

        var end = Position + keyword.Length;

        if (end < data.Length && !IsWhitespaceOrDelimiter(data[end]))
        {
            return false;
        }

        Position = end;
        return true;
    }

    /// <summary>Determines whether a keyword is next without consuming it.</summary>
    /// <param name="keyword">The keyword to match.</param>
    /// <returns><see langword="true"/> when the keyword is next.</returns>
    public bool PeekKeyword(string keyword)
    {
        var position = Position;
        var found = TryReadKeyword(keyword);
        Position = position;
        return found;
    }

    /// <summary>Reads an integer when one is next.</summary>
    /// <param name="value">The integer that was read.</param>
    /// <returns><see langword="true"/> when an integer was read.</returns>
    public bool TryReadInteger(out long value)
    {
        var position = Position;
        var token = ReadToken();

        if (token.Type == PdfTokenType.Number && token.IsInteger)
        {
            value = token.IntegerValue;
            return true;
        }

        Position = position;
        value = 0;
        return false;
    }

    /// <summary>Reads the next token.</summary>
    /// <returns>The token, or a <see cref="PdfTokenType.None"/> token at the end of the data.</returns>
    public PdfToken ReadToken()
    {
        SkipWhitespaceAndComments();

        var start = Position;

        if (Position >= data.Length)
        {
            return new PdfToken { Type = PdfTokenType.None, Start = start, End = start };
        }

        var current = data[Position];

        switch (current)
        {
            case (byte)'[':
                Position++;
                return new PdfToken { Type = PdfTokenType.ArrayOpen, Start = start, End = Position };

            case (byte)']':
                Position++;
                return new PdfToken { Type = PdfTokenType.ArrayClose, Start = start, End = Position };

            case (byte)'/':
                return ReadName(start);

            case (byte)'(':
                return ReadLiteralString(start);

            case (byte)'<':
                if (Position + 1 < data.Length && data[Position + 1] == (byte)'<')
                {
                    Position += 2;
                    return new PdfToken { Type = PdfTokenType.DictionaryOpen, Start = start, End = Position };
                }

                return ReadHexString(start);

            case (byte)'>':
                if (Position + 1 < data.Length && data[Position + 1] == (byte)'>')
                {
                    Position += 2;
                    return new PdfToken { Type = PdfTokenType.DictionaryClose, Start = start, End = Position };
                }

                Position++;
                return new PdfToken { Type = PdfTokenType.Keyword, Text = ">", Start = start, End = Position };

            default:
                return current is (byte)'+' or (byte)'-' or (byte)'.'
                    || (current >= (byte)'0' && current <= (byte)'9')
                        ? ReadNumber(start)
                        : ReadKeyword(start);
        }
    }

    private PdfToken ReadNumber(int start)
    {
        if (data[Position] is (byte)'+' or (byte)'-')
        {
            Position++;
        }

        var sawDigit = false;
        var sawDot = false;
        var sawExponent = false;

        while (Position < data.Length)
        {
            var current = data[Position];

            if (current >= (byte)'0' && current <= (byte)'9')
            {
                sawDigit = true;
                Position++;
                continue;
            }

            if (current == (byte)'.')
            {
                if (sawDot)
                {
                    break;
                }

                sawDot = true;
                Position++;
                continue;
            }

            if (current is (byte)'e' or (byte)'E')
            {
                if (sawExponent || !sawDigit)
                {
                    break;
                }

                sawExponent = true;
                Position++;

                if (Position < data.Length && data[Position] is (byte)'+' or (byte)'-')
                {
                    Position++;
                }

                continue;
            }

            break;
        }

        var text = Encoding.Latin1.GetString(data, start, Position - start);

        if (!sawExponent && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return new PdfToken
            {
                Type = PdfTokenType.Number,
                IsInteger = true,
                IntegerValue = integer,
                RealValue = integer,
                Start = start,
                End = Position,
            };
        }

        var real = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0d;

        return new PdfToken
        {
            Type = PdfTokenType.Number,
            IsInteger = false,
            RealValue = real,
            Start = start,
            End = Position,
        };
    }
}
