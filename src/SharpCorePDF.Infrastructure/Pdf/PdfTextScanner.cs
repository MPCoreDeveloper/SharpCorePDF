namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// One operator of a content stream as the text reader needs it: the operator, the numbers and names
/// that preceded it, the strings it shows, and the array a <c>TJ</c> hands over.
/// </summary>
/// <remarks>
/// The scanner used for painting (<see cref="PdfContentScanner"/>) skips strings and arrays because an
/// image walk does not need them. This one keeps them, and in order, because a text walk needs nothing
/// else: a page's text is exactly the strings its <c>Tj</c>, <c>TJ</c>, <c>'</c> and <c>"</c> operators
/// carry, and the numbers that move them.
/// </remarks>
internal sealed class PdfTextOperation
{
    /// <summary>Gets the operator, for example <c>Tj</c>, <c>TJ</c> or <c>Tf</c>.</summary>
    public required string Operator { get; init; }

    /// <summary>Gets the numeric operands, in the order they were written.</summary>
    public List<double> Numbers { get; } = [];

    /// <summary>Gets the strings shown or named by the operator, in the order they were written.</summary>
    public List<byte[]> Strings { get; } = [];

    /// <summary>Gets the name operands, in the order they were written, without the leading slash.</summary>
    public List<string> Names { get; } = [];

    /// <summary>
    /// Gets the items of an array operand — the numbers and strings of a <c>TJ</c> — in the order they
    /// were written, or <see langword="null"/> when the operator takes no array.
    /// </summary>
    public List<object>? Items { get; init; }
}

/// <summary>
/// Scans a content stream into the operators a text walk needs, keeping the bytes of every string.
/// </summary>
/// <remarks>
/// <para>
/// The scan is deliberately mechanical: it reports operators and their operands and never interprets
/// them, so an operator this library has never heard of costs one keyword read. Strings keep their raw
/// bytes, because only a font knows how to decode them: a byte is a character code in a simple font and
/// half a code in a composite one, and decoding belongs with the font, not with the scan.
/// </para>
/// <para>
/// An inline image is the one place where the stream stops being text: after <c>ID</c> the bytes are
/// image data, which may contain anything, including the word <c>EI</c>. The scan therefore steps over
/// the inline image the way every PDF reader does and resumes at the delimiter that closes it.
/// </para>
/// </remarks>
internal static class PdfTextScanner
{
    /// <summary>Scans a content stream.</summary>
    /// <param name="data">The decoded bytes of the content stream.</param>
    /// <returns>The operators, in the order the stream states them.</returns>
    public static IEnumerable<PdfTextOperation> Scan(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return ScanCore(data);
    }

    private static IEnumerable<PdfTextOperation> ScanCore(byte[] data)
    {
        var index = 0;
        var numbers = new List<double>();
        var strings = new List<byte[]>();
        var names = new List<string>();
        List<object>? items = null;

        while (index < data.Length)
        {
            SkipWhitespaceAndComments(data, ref index);

            if (index >= data.Length)
            {
                yield break;
            }

            var current = data[index];

            switch (current)
            {
                case (byte)'/':
                    names.Add(ReadName(data, ref index));
                    continue;

                case (byte)'(':
                    strings.Add(ReadLiteralString(data, ref index));
                    continue;

                case (byte)'<':
                    if (index + 1 < data.Length && data[index + 1] == (byte)'<')
                    {
                        SkipDictionary(data, ref index);
                    }
                    else
                    {
                        strings.Add(ReadHexString(data, ref index));
                    }

                    continue;

                case (byte)'[':
                    items = ReadArray(data, ref index);
                    continue;

                case (byte)']' or (byte)'>' or (byte)'{' or (byte)'}' or (byte)')':
                    index++;
                    continue;
            }

            if (IsNumberStart(current))
            {
                if (TryReadNumber(data, ref index, out var number))
                {
                    numbers.Add(number);
                }
                else
                {
                    index++;
                }

                continue;
            }

            var keyword = ReadKeyword(data, ref index);

            if (keyword.Length == 0)
            {
                index++;
                continue;
            }

            if (keyword is "true" or "false" or "null")
            {
                continue;
            }

            if (keyword == "BI")
            {
                SkipInlineImage(data, ref index);

                numbers.Clear();
                strings.Clear();
                names.Clear();
                items = null;

                continue;
            }

            var operation = new PdfTextOperation { Operator = keyword, Items = items };

            operation.Numbers.AddRange(numbers);
            operation.Strings.AddRange(strings);
            operation.Names.AddRange(names);

            numbers.Clear();
            strings.Clear();
            names.Clear();
            items = null;

            yield return operation;
        }
    }

    private static void SkipWhitespaceAndComments(byte[] data, ref int index)
    {
        while (index < data.Length)
        {
            if (PdfLexer.IsWhitespace(data[index]))
            {
                index++;
                continue;
            }

            if (data[index] != (byte)'%')
            {
                return;
            }

            while (index < data.Length && data[index] is not ((byte)'\n' or (byte)'\r'))
            {
                index++;
            }
        }
    }

    private static string ReadName(byte[] data, ref int index)
    {
        index++;

        var start = index;

        while (index < data.Length && !PdfLexer.IsWhitespaceOrDelimiter(data[index]))
        {
            index++;
        }

        var raw = System.Text.Encoding.Latin1.GetString(data, start, index - start);

        if (raw.IndexOf('#') < 0)
        {
            return raw;
        }

        var builder = new System.Text.StringBuilder(raw.Length);
        var position = 0;

        while (position < raw.Length)
        {
            if (raw[position] == '#' && position + 2 < raw.Length
                && byte.TryParse(
                    raw.AsSpan(position + 1, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                builder.Append((char)value);
                position += 3;
                continue;
            }

            builder.Append(raw[position]);
            position++;
        }

        return builder.ToString();
    }

    private static string ReadKeyword(byte[] data, ref int index)
    {
        var start = index;

        while (index < data.Length && !PdfLexer.IsWhitespaceOrDelimiter(data[index]))
        {
            index++;
        }

        return System.Text.Encoding.Latin1.GetString(data, start, index - start);
    }

    private static bool IsNumberStart(byte value) =>
        value is (byte)'+' or (byte)'-' or (byte)'.' || (value >= (byte)'0' && value <= (byte)'9');

    private static bool TryReadNumber(byte[] data, ref int index, out double value)
    {
        var start = index;
        var sawDigit = false;
        var sawDot = false;

        if (data[index] is (byte)'+' or (byte)'-')
        {
            index++;
        }

        while (index < data.Length)
        {
            var current = data[index];

            if (current >= (byte)'0' && current <= (byte)'9')
            {
                sawDigit = true;
                index++;
                continue;
            }

            if (current == (byte)'.' && !sawDot)
            {
                sawDot = true;
                index++;
                continue;
            }

            break;
        }

        value = 0d;

        return sawDigit
            && double.TryParse(
                System.Text.Encoding.Latin1.GetString(data, start, index - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
    }

    /// <summary>Reads a literal string, decoding its escapes and its balanced parentheses.</summary>
    /// <param name="data">The stream.</param>
    /// <param name="index">The position of the opening parenthesis; moved past the closing one.</param>
    /// <returns>The bytes of the string.</returns>
    private static byte[] ReadLiteralString(byte[] data, ref int index)
    {
        index++;

        var bytes = new List<byte>(32);
        var depth = 1;

        while (index < data.Length)
        {
            var current = data[index++];

            if (current == (byte)'\\')
            {
                ReadEscape(data, ref index, bytes);
                continue;
            }

            if (current == (byte)'(')
            {
                depth++;
                bytes.Add(current);
                continue;
            }

            if (current == (byte)')')
            {
                depth--;

                if (depth == 0)
                {
                    break;
                }

                bytes.Add(current);
                continue;
            }

            if (current == (byte)'\r')
            {
                // An unescaped end of line inside a string is a single line feed.
                if (index < data.Length && data[index] == (byte)'\n')
                {
                    index++;
                }

                bytes.Add((byte)'\n');
                continue;
            }

            bytes.Add(current);
        }

        return [.. bytes];
    }

    private static void ReadEscape(byte[] data, ref int index, List<byte> bytes)
    {
        if (index >= data.Length)
        {
            return;
        }

        var escaped = data[index++];

        switch (escaped)
        {
            case (byte)'n':
                bytes.Add((byte)'\n');
                break;

            case (byte)'r':
                bytes.Add((byte)'\r');
                break;

            case (byte)'t':
                bytes.Add((byte)'\t');
                break;

            case (byte)'b':
                bytes.Add((byte)'\b');
                break;

            case (byte)'f':
                bytes.Add((byte)'\f');
                break;

            case (byte)'\r':
                if (index < data.Length && data[index] == (byte)'\n')
                {
                    index++;
                }

                break;

            case (byte)'\n':
                break;

            case >= (byte)'0' and <= (byte)'7':
                var value = escaped - (byte)'0';

                for (var count = 0; count < 2 && index < data.Length; count++)
                {
                    var digit = data[index];

                    if (digit is < (byte)'0' or > (byte)'7')
                    {
                        break;
                    }

                    value = (value * 8) + (digit - (byte)'0');
                    index++;
                }

                bytes.Add((byte)value);
                break;

            default:
                bytes.Add(escaped);
                break;
        }
    }

    /// <summary>Reads a hexadecimal string, padding an odd number of digits with a trailing zero.</summary>
    /// <param name="data">The stream.</param>
    /// <param name="index">The position of the opening angle bracket; moved past the closing one.</param>
    /// <returns>The bytes of the string.</returns>
    private static byte[] ReadHexString(byte[] data, ref int index)
    {
        index++;

        var bytes = new List<byte>(16);
        var high = -1;

        while (index < data.Length && data[index] != (byte)'>')
        {
            var digit = HexValue(data[index++]);

            if (digit < 0)
            {
                continue;
            }

            if (high < 0)
            {
                high = digit;
                continue;
            }

            bytes.Add((byte)((high << 4) | digit));
            high = -1;
        }

        if (index < data.Length)
        {
            index++;
        }

        if (high >= 0)
        {
            bytes.Add((byte)(high << 4));
        }

        return [.. bytes];
    }

    private static int HexValue(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - (byte)'0',
        >= (byte)'a' and <= (byte)'f' => value - (byte)'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - (byte)'A' + 10,
        _ => -1,
    };

    /// <summary>Reads an array of numbers and strings, skipping anything else it holds.</summary>
    /// <param name="data">The stream.</param>
    /// <param name="index">The position of the opening bracket; moved past the closing one.</param>
    /// <returns>The items of the array, as <see cref="double"/> values and <see cref="byte"/> arrays.</returns>
    private static List<object> ReadArray(byte[] data, ref int index)
    {
        index++;

        var items = new List<object>();

        while (index < data.Length)
        {
            SkipWhitespaceAndComments(data, ref index);

            if (index >= data.Length)
            {
                break;
            }

            var current = data[index];

            if (current == (byte)']')
            {
                index++;
                break;
            }

            if (current == (byte)'(')
            {
                items.Add(ReadLiteralString(data, ref index));
                continue;
            }

            if (current == (byte)'<')
            {
                if (index + 1 < data.Length && data[index + 1] == (byte)'<')
                {
                    SkipDictionary(data, ref index);
                }
                else
                {
                    items.Add(ReadHexString(data, ref index));
                }

                continue;
            }

            if (IsNumberStart(current))
            {
                if (TryReadNumber(data, ref index, out var number))
                {
                    items.Add(number);
                }
                else
                {
                    index++;
                }

                continue;
            }

            if (current == (byte)'/')
            {
                _ = ReadName(data, ref index);
                continue;
            }

            if (current == (byte)'[')
            {
                _ = ReadArray(data, ref index);
                continue;
            }

            index++;
        }

        return items;
    }

    private static void SkipDictionary(byte[] data, ref int index)
    {
        index += 2;

        var depth = 1;

        while (index < data.Length && depth > 0)
        {
            var current = data[index];

            if (current == (byte)'<' && index + 1 < data.Length && data[index + 1] == (byte)'<')
            {
                depth++;
                index += 2;
                continue;
            }

            if (current == (byte)'>' && index + 1 < data.Length && data[index + 1] == (byte)'>')
            {
                depth--;
                index += 2;
                continue;
            }

            if (current == (byte)'(')
            {
                _ = ReadLiteralString(data, ref index);
                continue;
            }

            index++;
        }
    }

    /// <summary>
    /// Steps over an inline image: its dictionary, the <c>ID</c> that starts the data, the data itself
    /// and the <c>EI</c> that ends it.
    /// </summary>
    /// <param name="data">The stream.</param>
    /// <param name="index">The position just past <c>BI</c>; moved past <c>EI</c>.</param>
    /// <remarks>
    /// The data may contain the letters <c>EI</c>, so the delimiter is taken only where a keyword can
    /// start: after whitespace and before whitespace or a delimiter, which is the same rule a viewer
    /// applies. A stream that ends inside the image simply ends the scan.
    /// </remarks>
    private static void SkipInlineImage(byte[] data, ref int index)
    {
        while (index < data.Length)
        {
            SkipWhitespaceAndComments(data, ref index);

            if (index >= data.Length)
            {
                return;
            }

            var current = data[index];

            if (current == (byte)'/')
            {
                _ = ReadName(data, ref index);
                continue;
            }

            if (current == (byte)'<')
            {
                if (index + 1 < data.Length && data[index + 1] == (byte)'<')
                {
                    SkipDictionary(data, ref index);
                }
                else
                {
                    _ = ReadHexString(data, ref index);
                }

                continue;
            }

            if (current == (byte)'[')
            {
                _ = ReadArray(data, ref index);
                continue;
            }

            if (IsNumberStart(current))
            {
                if (!TryReadNumber(data, ref index, out _))
                {
                    index++;
                }

                continue;
            }

            var keyword = ReadKeyword(data, ref index);

            if (keyword == "ID")
            {
                break;
            }

            if (keyword.Length == 0)
            {
                index++;
            }
        }

        // One whitespace byte separates ID from the data and is not part of it.
        if (index < data.Length)
        {
            index++;
        }

        while (index + 1 < data.Length)
        {
            if (data[index] == (byte)'E' && data[index + 1] == (byte)'I'
                && index > 0 && PdfLexer.IsWhitespace(data[index - 1])
                && (index + 2 >= data.Length || PdfLexer.IsWhitespaceOrDelimiter(data[index + 2])))
            {
                index += 2;
                return;
            }

            index++;
        }

        index = data.Length;
    }
}
