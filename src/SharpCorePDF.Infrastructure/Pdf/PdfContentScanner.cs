using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>One operator of a content stream, with the operands that preceded it.</summary>
internal sealed class PdfContentOperation
{
    /// <summary>Gets the operator, for example <c>Do</c>, <c>cm</c> or <c>BI</c>.</summary>
    public required string Operator { get; init; }

    /// <summary>Gets the numeric operands, in the order they were written.</summary>
    public List<double> Numbers { get; } = [];

    /// <summary>Gets the name operands, in the order they were written, without the leading slash.</summary>
    public List<string> Names { get; } = [];

    /// <summary>Gets the dictionary of an inline image, or <see langword="null"/>.</summary>
    public PdfDictionary? InlineImage { get; init; }

    /// <summary>Gets the bytes of an inline image, as the content stream holds them.</summary>
    public ReadOnlyMemory<byte> InlineData { get; init; }
}

/// <summary>
/// Scans a content stream into operators, tracking nothing but what the scan itself needs.
/// </summary>
/// <remarks>
/// <para>
/// The scanner is deliberately small: it reports operators and their simple operands, and skips the
/// parts it does not need — strings, arrays, dictionaries — without interpreting them. That is enough
/// to follow a page's painting, and it means an operator this library has never heard of costs one
/// keyword read rather than a failure.
/// </para>
/// <para>
/// An inline image is the one place where the stream stops being text: after <c>ID</c> the bytes are
/// image data, which may contain anything, including the letters <c>EI</c>. The scan therefore takes
/// the same delimited-keyword reading every PDF reader takes, and treats the bytes in between as data.
/// </para>
/// </remarks>
internal static class PdfContentScanner
{
    /// <summary>Scans a content stream.</summary>
    /// <param name="data">The decoded bytes of the content stream.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="pageNumber">The one-based page the stream belongs to, used in diagnostics.</param>
    /// <param name="warnings">The list that receives the remarks the scan makes, or <see langword="null"/>.</param>
    /// <returns>The operators, in the order the stream states them.</returns>
    public static IEnumerable<PdfContentOperation> Scan(
        byte[] data,
        string? sourceName = null,
        int pageNumber = 0,
        List<PdfWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        return ScanCore(data, sourceName, pageNumber, warnings);
    }

    private static IEnumerable<PdfContentOperation> ScanCore(
        byte[] data,
        string? sourceName,
        int pageNumber,
        List<PdfWarning>? warnings)
    {
        var index = 0;
        var numbers = new List<double>();
        var names = new List<string>();

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
                    SkipLiteralString(data, ref index);
                    continue;

                case (byte)'<':
                    if (index + 1 < data.Length && data[index + 1] == (byte)'<')
                    {
                        SkipDictionary(data, ref index);
                    }
                    else
                    {
                        SkipHexString(data, ref index);
                    }

                    continue;

                case (byte)'[':
                    SkipArray(data, ref index);
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
                var inline = ReadInlineImage(data, ref index, sourceName, pageNumber, warnings);

                numbers.Clear();
                names.Clear();

                if (inline is not null)
                {
                    yield return inline;
                }

                continue;
            }

            var operation = new PdfContentOperation { Operator = keyword };
            operation.Numbers.AddRange(numbers);
            operation.Names.AddRange(names);

            numbers.Clear();
            names.Clear();

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

        return raw.IndexOf('#') < 0 ? raw : DecodeNameEscapes(raw);
    }

    private static string DecodeNameEscapes(string raw)
    {
        var builder = new System.Text.StringBuilder(raw.Length);
        var position = 0;

        while (position < raw.Length)
        {
            if (raw[position] == '#' && position + 2 < raw.Length
                && byte.TryParse(raw.AsSpan(position + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
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

    private static void SkipLiteralString(byte[] data, ref int index)
    {
        var depth = 0;
        index++;

        while (index < data.Length)
        {
            var current = data[index];

            if (current == (byte)'\\')
            {
                index += 2;
                continue;
            }

            if (current == (byte)'(')
            {
                depth++;
            }
            else if (current == (byte)')')
            {
                if (depth == 0)
                {
                    index++;
                    return;
                }

                depth--;
            }

            index++;
        }
    }

    private static void SkipHexString(byte[] data, ref int index)
    {
        index++;

        while (index < data.Length && data[index] != (byte)'>')
        {
            index++;
        }

        if (index < data.Length)
        {
            index++;
        }
    }

    private static void SkipArray(byte[] data, ref int index)
    {
        index++;

        while (index < data.Length && data[index] != (byte)']')
        {
            if (data[index] == (byte)'(')
            {
                SkipLiteralString(data, ref index);
                continue;
            }

            if (data[index] == (byte)'<' && index + 1 < data.Length && data[index + 1] == (byte)'<')
            {
                SkipDictionary(data, ref index);
                continue;
            }

            if (data[index] == (byte)'[')
            {
                SkipArray(data, ref index);
                continue;
            }

            index++;
        }

        if (index < data.Length)
        {
            index++;
        }
    }

    private static void SkipDictionary(byte[] data, ref int index)
    {
        var depth = 0;
        index += 2;

        while (index < data.Length)
        {
            if (data[index] == (byte)'(')
            {
                SkipLiteralString(data, ref index);
                continue;
            }

            if (data[index] == (byte)'<' && index + 1 < data.Length && data[index + 1] == (byte)'<')
            {
                depth++;
                index += 2;
                continue;
            }

            if (data[index] == (byte)'>' && index + 1 < data.Length && data[index + 1] == (byte)'>')
            {
                if (depth == 0)
                {
                    index += 2;
                    return;
                }

                depth--;
                index += 2;
                continue;
            }

            index++;
        }
    }

    private static PdfContentOperation? ReadInlineImage(
        byte[] data,
        ref int index,
        string? sourceName,
        int pageNumber,
        List<PdfWarning>? warnings)
    {
        var dictionary = new PdfDictionary();
        string? key = null;

        while (true)
        {
            SkipWhitespaceAndComments(data, ref index);

            if (index >= data.Length)
            {
                return null;
            }

            var current = data[index];

            if (current == (byte)'/')
            {
                var name = ReadName(data, ref index);

                // A name that follows a name is the value of the entry, not a new key: "/CS /RGB" states
                // a colour space, and reading the second name as a key loses it.
                if (key is not null)
                {
                    dictionary.Set(key, new PdfName(name));
                    key = null;
                    continue;
                }

                key = name;
                continue;
            }

            if (current is (byte)'[' or (byte)'<' or (byte)'(')
            {
                var value = ReadInlineValue(data, ref index);

                if (key is not null && value is not null)
                {
                    dictionary.Set(key, value);
                }

                key = null;
                continue;
            }

            if (IsNumberStart(current))
            {
                if (TryReadNumber(data, ref index, out var number) && key is not null)
                {
                    dictionary.Set(
                        key,
                        PdfNumberFormat.IsWholeNumber(number)
                            ? new PdfInteger((long)number)
                            : new PdfReal(number));

                    key = null;
                }
                else
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
                continue;
            }

            if (keyword is "true" or "false" && key is not null)
            {
                dictionary.Set(key, PdfBoolean.From(keyword == "true"));
                key = null;
            }
        }

        // Exactly one separator follows ID — a single whitespace byte, with a CR LF pair counting as
        // one — and the separator is not part of the data.
        if (index < data.Length && PdfLexer.IsWhitespace(data[index]))
        {
            index += data[index] == (byte)'\r'
                && index + 1 < data.Length
                && data[index + 1] == (byte)'\n'
                    ? 2
                    : 1;
        }

        var start = index;
        var normalized = NormalizeInlineDictionary(dictionary);

        if (!TryFindEndOfInlineData(data, start, ExpectedInlineLength(normalized), out var end, out var next))
        {
            warnings?.Add(new PdfWarning(
                PdfWarningKind.ImageDataUnreadable,
                $"'{sourceName}': the inline image on page {pageNumber} is not terminated by EI, so its bytes were read to the end of the content stream."));

            index = data.Length;

            return new PdfContentOperation
            {
                Operator = "BI",
                InlineImage = normalized,
                InlineData = new ReadOnlyMemory<byte>(data, start, data.Length - start),
            };
        }

        index = next;

        return new PdfContentOperation
        {
            Operator = "BI",
            InlineImage = normalized,
            InlineData = new ReadOnlyMemory<byte>(data, start, end - start),
        };
    }

    // The bytes of an inline image may contain the letters "EI", so the end is the length its header
    // states when it states one — a sample stream may hold a space followed by "EI" — and otherwise the
    // first "EI" that is a keyword of its own: preceded by whitespace and followed by whitespace, a
    // delimiter or the end of the stream.
    private static bool TryFindEndOfInlineData(
        byte[] data,
        int start,
        int expectedLength,
        out int end,
        out int next)
    {
        if (expectedLength > 0 && start + expectedLength < data.Length)
        {
            var separator = start + expectedLength;
            var terminator = separator;

            if (data[terminator] == (byte)'\r'
                && terminator + 1 < data.Length
                && data[terminator + 1] == (byte)'\n')
            {
                terminator += 2;
            }
            else if (PdfLexer.IsWhitespace(data[terminator]))
            {
                terminator++;
            }

            if (terminator + 1 < data.Length
                && data[terminator] == (byte)'E'
                && data[terminator + 1] == (byte)'I'
                && IsInlineDelimiter(data, terminator + 2))
            {
                end = separator;
                next = terminator + 2;
                return true;
            }
        }

        for (var scan = start + 1; scan + 1 < data.Length; scan++)
        {
            if (data[scan] != (byte)'E' || data[scan + 1] != (byte)'I')
            {
                continue;
            }

            if (!PdfLexer.IsWhitespace(data[scan - 1]) || !IsInlineDelimiter(data, scan + 2))
            {
                continue;
            }

            end = scan - 1;
            next = scan + 2;
            return true;
        }

        end = start;
        next = start;
        return false;
    }

    // The keyword after ID may be followed immediately by a delimiter, as in "EI/Im1 Do": a delimiter
    // ends a keyword just as whitespace does.
    private static bool IsInlineDelimiter(byte[] data, int position) =>
        position >= data.Length
        || PdfLexer.IsWhitespace(data[position])
        || PdfLexer.IsDelimiter(data[position]);

    // An inline image states the size of its samples, so the bytes it stores can be counted instead of
    // searched for. A filtered stream stores compressed bytes, whose length the header does not state,
    // so it falls back to the search.
    private static int ExpectedInlineLength(PdfDictionary dictionary)
    {
        if (dictionary.Get("Filter") is not null)
        {
            return 0;
        }

        var width = dictionary.GetInteger("Width");
        var height = dictionary.GetInteger("Height");
        var bits = dictionary.Get("ImageMask") is PdfBoolean { Value: true }
            ? 1
            : dictionary.GetInteger("BitsPerComponent");

        if (width is not > 0 || height is not > 0 || bits is not > 0)
        {
            return 0;
        }

        var components = ColorComponents(dictionary.Get("ColorSpace"));

        if (components <= 0)
        {
            return 0;
        }

        var total = width.Value * height.Value * components * bits.Value;

        return total is > 0 and <= int.MaxValue ? (int)((total + 7) / 8) : 0;
    }

    private static int ColorComponents(PdfObject? colorSpace) => colorSpace switch
    {
        PdfName name => name.Value switch
        {
            "DeviceGray" or "G" or "Indexed" or "I" => 1,
            "DeviceRGB" or "RGB" or "CalRGB" => 3,
            "DeviceCMYK" or "CMYK" => 4,
            _ => 0,
        },
        PdfArray { Count: > 0 } array => array.Items[0] is PdfName { Value: "Indexed" or "I" } ? 1 : 0,
        _ => 0,
    };

    private static PdfObject? ReadInlineValue(byte[] data, ref int index)
    {
        if (data[index] == (byte)'[')
        {
            index++;

            var array = new PdfArray();

            while (index < data.Length && data[index] != (byte)']')
            {
                SkipWhitespaceAndComments(data, ref index);

                if (index >= data.Length || data[index] == (byte)']')
                {
                    break;
                }

                if (data[index] == (byte)'/')
                {
                    array.Add(new PdfName(ReadName(data, ref index)));
                    continue;
                }

                if (IsNumberStart(data[index]) && TryReadNumber(data, ref index, out var number))
                {
                    array.Add(PdfNumberFormat.IsWholeNumber(number)
                        ? new PdfInteger((long)number)
                        : new PdfReal(number));

                    continue;
                }

                if (data[index] == (byte)'[' && ReadInlineValue(data, ref index) is { } nested)
                {
                    array.Add(nested);
                    continue;
                }

                index++;
            }

            if (index < data.Length)
            {
                index++;
            }

            return array;
        }

        if (data[index] == (byte)'<')
        {
            var start = index + 1;
            var end = start;

            while (end < data.Length && data[end] != (byte)'>')
            {
                end++;
            }

            index = Math.Min(data.Length, end + 1);

            return new PdfString(
                System.Text.Encoding.Latin1.GetBytes(System.Text.Encoding.Latin1.GetString(data, start, end - start)),
                isHexadecimal: true);
        }

        var textStart = index + 1;
        var textEnd = textStart;

        while (textEnd < data.Length && data[textEnd] != (byte)')')
        {
            textEnd++;
        }

        index = Math.Min(data.Length, textEnd + 1);

        return new PdfString(data[textStart..textEnd], isHexadecimal: false);
    }

    // An inline image names its entries with two-letter abbreviations, which is fine for the scanner
    // but not for the code that decodes a stream, so the dictionary is normalised once, here.
    private static PdfDictionary NormalizeInlineDictionary(PdfDictionary source)
    {
        var result = new PdfDictionary();

        foreach (var entry in source.Entries)
        {
            result.Set(
                entry.Key switch
                {
                    "W" => "Width",
                    "H" => "Height",
                    "BPC" => "BitsPerComponent",
                    "CS" => "ColorSpace",
                    "F" => "Filter",
                    "DP" => "DecodeParms",
                    "IM" => "ImageMask",
                    "D" => "Decode",
                    "I" => "Interpolate",
                    _ => entry.Key,
                },
                entry.Value);
        }

        result.Set("Subtype", new PdfName("Image"));

        return result;
    }
}
