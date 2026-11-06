using System.Buffers.Text;

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
    // The scan reuses one instance and yields it again and again, so per-operator allocations stay off
    // the hot path of a page walk. The instance is consumed before the next MoveNext — a foreach over
    // the scan does exactly that — which is why a mutable, non-copying shape is safe here and nowhere
    // public: <see cref="PdfTextScanner.Scan"/> is internal to this assembly.

    /// <summary>Gets the operator, for example <c>Tj</c>, <c>TJ</c> or <c>Tf</c>.</summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>Gets the numeric operands, in the order they were written.</summary>
    public List<double> Numbers { get; } = [];

    /// <summary>
    /// Gets the strings shown or named by the operator, in the order they were written. Most are a
    /// slice of the stream itself: a string with no escape is the bytes between its parentheses.
    /// </summary>
    public List<ReadOnlyMemory<byte>> Strings { get; } = [];

    /// <summary>Gets the name operands, in the order they were written, without the leading slash.</summary>
    public List<string> Names { get; } = [];

    /// <summary>
    /// Gets the items of an array operand — the numbers and strings of a <c>TJ</c> — in the order they
    /// were written, or <see langword="null"/> when the operator takes no array.
    /// </summary>
    public List<TjItem>? Items { get; set; }
}

/// <summary>The two kinds of value a <c>TJ</c> array can carry.</summary>
internal enum TjItemKind : byte
{
    /// <summary>A spacing adjustment, in thousandths of a unit of text space.</summary>
    Number,

    /// <summary>A string to show.</summary>
    String,
}

/// <summary>
/// One item of a <c>TJ</c> array: a spacing adjustment or a string.
/// </summary>
/// <remarks>
/// A struct rather than <see cref="object"/>, because the array is rebuilt for every <c>TJ</c> a page
/// states and boxing a number or a slice per item is the allocation this type exists to remove.
/// </remarks>
internal readonly struct TjItem
{
    private TjItem(TjItemKind kind, double number, ReadOnlyMemory<byte> bytes)
    {
        Kind = kind;
        Number = number;
        Bytes = bytes;
    }

    /// <summary>Gets the kind of value this item carries.</summary>
    public TjItemKind Kind { get; }

    /// <summary>Gets the spacing adjustment, when <see cref="Kind"/> is <see cref="TjItemKind.Number"/>.</summary>
    public double Number { get; }

    /// <summary>Gets the string to show, when <see cref="Kind"/> is <see cref="TjItemKind.String"/>.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Creates a spacing adjustment.</summary>
    /// <param name="value">The adjustment in thousandths of a unit of text space.</param>
    /// <returns>The item.</returns>
    public static TjItem FromNumber(double value) => new(TjItemKind.Number, value, default);

    /// <summary>Creates a string to show.</summary>
    /// <param name="bytes">The raw bytes of the string.</param>
    /// <returns>The item.</returns>
    public static TjItem FromString(ReadOnlyMemory<byte> bytes) => new(TjItemKind.String, 0d, bytes);
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
    /// <summary>The capacity a decoded string buffer starts with; the pool grows it when a string is longer.</summary>
    private const int InitialStringCapacity = 32;

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
        var operation = new PdfTextOperation();
        var numbers = operation.Numbers;
        var strings = operation.Strings;
        var names = operation.Names;
        List<TjItem>? items = null;

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

            operation.Operator = keyword;
            operation.Items = items;

            yield return operation;

            numbers.Clear();
            strings.Clear();
            names.Clear();
            items = null;
            operation.Items = null;
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

            // A comment runs to the end of its line, and that line ending is whitespace the next
            // iteration skips.
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

        return PdfNameDecoder.Decode(data.AsSpan(start, index - start));
    }

    private static string ReadKeyword(byte[] data, ref int index)
    {
        var start = index;

        while (index < data.Length && !PdfLexer.IsWhitespaceOrDelimiter(data[index]))
        {
            index++;
        }

        return PdfKeywords.Read(data.AsSpan(start, index - start));
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

        // The scan already bounded the number, so it is parsed where it lies rather than copied into
        // a string first: a content stream carries a number before almost every operator, and the
        // digits are ASCII, which Utf8Parser reads in place. Full consumption is required because the
        // parser is a prefix parser: "3.25" read as a long would otherwise report 3.
        return sawDigit
            && Utf8Parser.TryParse(data.AsSpan(start, index - start), out value, out var consumed)
            && consumed == index - start;
    }

    /// <summary>Reads a literal string, decoding its escapes and its balanced parentheses.</summary>
    /// <param name="data">The stream.</param>
    /// <param name="index">The position of the opening parenthesis; moved past the closing one.</param>
    /// <returns>The bytes of the string, as a slice of the stream when it needs no decoding.</returns>
    private static ReadOnlyMemory<byte> ReadLiteralString(byte[] data, ref int index)
    {
        index++;

        var start = index;

        // The common string has no escape, no nested parenthesis and no bare carriage return: what it
        // holds is the bytes between its parentheses, so it is a slice of the stream and costs nothing.
        while (index < data.Length)
        {
            var current = data[index];

            if (current == (byte)')')
            {
                var length = index - start;
                index++;
                return data.AsMemory(start, length);
            }

            if (current is (byte)'\\' or (byte)'(' or (byte)'\r')
            {
                return ReadEscapedLiteralString(data, ref index, start);
            }

            index++;
        }

        return ReadEscapedLiteralString(data, ref index, start);
    }

    /// <summary>Reads a literal string that has to be decoded, into a pooled buffer.</summary>
    /// <param name="data">The stream.</param>
    /// <param name="index">The position of the first byte that needs decoding.</param>
    /// <param name="start">The position just past the opening parenthesis.</param>
    /// <returns>The decoded bytes, as an exactly sized array.</returns>
    private static ReadOnlyMemory<byte> ReadEscapedLiteralString(byte[] data, ref int index, int start)
    {
        using var bytes = new PooledByteBuffer(InitialStringCapacity);
        var depth = 1;

        index = start;

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

            if (current == (byte)'\r')
            {
                // An unescaped end of line inside a string is a single line feed.
                if (index < data.Length && data[index] == (byte)'\n')
                {
                    index++;
                }

                bytes.WriteByte((byte)'\n');
                continue;
            }

            bytes.WriteByte(current);
        }

        return bytes.ToExactArray();
    }

    private static void ReadEscape(byte[] data, ref int index, PooledByteBuffer bytes)
    {
        if (index >= data.Length)
        {
            return;
        }

        var escaped = data[index++];

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

                bytes.WriteByte((byte)value);
                break;

            default:
                bytes.WriteByte(escaped);
                break;
        }
    }

    /// <summary>Reads a hexadecimal string, padding an odd number of digits with a trailing zero.</summary>
    /// <param name="data">The stream.</param>
    /// <param name="index">The position of the opening angle bracket; moved past the closing one.</param>
    /// <returns>The bytes of the string.</returns>
    /// <remarks>
    /// The digits are counted before the bytes are written, so the result is one array of exactly the
    /// right size rather than a growing list and the array copied out of it.
    /// </remarks>
    private static ReadOnlyMemory<byte> ReadHexString(byte[] data, ref int index)
    {
        index++;

        var first = index;
        var digits = 0;
        var end = data.Length;

        for (var cursor = index; cursor < data.Length; cursor++)
        {
            if (data[cursor] == (byte)'>')
            {
                end = cursor + 1;
                break;
            }

            if (HexValue(data[cursor]) >= 0)
            {
                digits++;
            }
        }

        index = end;

        var bytes = new byte[(digits + 1) / 2];
        var written = 0;
        var pending = -1;

        for (var cursor = first; cursor < end; cursor++)
        {
            var current = data[cursor];

            if (current == (byte)'>')
            {
                break;
            }

            var digit = HexValue(current);

            if (digit < 0)
            {
                continue;
            }

            if (pending < 0)
            {
                pending = digit;
                continue;
            }

            bytes[written++] = (byte)((pending << 4) | digit);
            pending = -1;
        }

        if (pending >= 0)
        {
            // An odd number of digits is treated as if the last digit were followed by a zero.
            bytes[written] = (byte)(pending << 4);
        }

        return bytes;
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
    private static List<TjItem> ReadArray(byte[] data, ref int index)
    {
        index++;

        var items = new List<TjItem>(8);

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
                items.Add(TjItem.FromString(ReadLiteralString(data, ref index)));
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
                    items.Add(TjItem.FromString(ReadHexString(data, ref index)));
                }

                continue;
            }

            if (IsNumberStart(current))
            {
                if (TryReadNumber(data, ref index, out var number))
                {
                    items.Add(TjItem.FromNumber(number));
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

        // The data between ID and EI can be hundreds of kilobytes, so the search for the E of EI is a
        // vector one: see the note on the same search in PdfContentScanner.
        while (index + 1 < data.Length)
        {
            var found = SimdByteScan.IndexOf(data.AsSpan(index), (byte)'E');

            if (found < 0)
            {
                break;
            }

            index += found;

            if (index + 1 < data.Length && data[index + 1] == (byte)'I'
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
