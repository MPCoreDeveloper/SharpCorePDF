namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The <c>/ToUnicode</c> map of a font: the table a producer ships so a reader can turn the codes of
/// its own encoding into Unicode.
/// </summary>
/// <remarks>
/// <para>
/// A composite font is the reason this type exists. Its strings hold CIDs — numbers a subset font picks
/// for itself — so nothing outside the document can say what they mean; the <c>/ToUnicode</c> stream is
/// the only statement of intent the document makes. Simple fonts carry one too when they use an
/// encoding a reader would otherwise have to guess, which is why the map is consulted before the
/// encoding table for both kinds of font.
/// </para>
/// <para>
/// Only the <c>bfchar</c> and <c>bfrange</c> sections are read. They hold one entry per code, and they
/// are the only sections that map codes to text: <c>codespacerange</c> says how many bytes a code has,
/// which this type infers from the codes it maps instead, so that a stream which omits the section — as
/// many do — still reads.
/// </para>
/// <para>
/// A destination is UTF-16BE, which is how a ligature or a code that stands for several characters is
/// written. A destination of odd length is malformed; it is read as Latin-1 rather than dropped, because
/// a producer that wrote one byte meant the character that byte names.
/// </para>
/// </remarks>
internal sealed class PdfToUnicodeCMap
{
    private readonly Dictionary<int, string> mappings;

    private PdfToUnicodeCMap(Dictionary<int, string> mappings, int codeLength)
    {
        this.mappings = mappings;
        CodeLength = codeLength;
    }

    /// <summary>Gets the number of bytes a code of this map is written in, one or two.</summary>
    public int CodeLength { get; }

    /// <summary>Gets the number of codes the map states.</summary>
    public int Count => mappings.Count;

    /// <summary>Parses the bytes of a <c>/ToUnicode</c> stream.</summary>
    /// <param name="data">The decoded bytes of the stream.</param>
    /// <returns>
    /// The map, or <see langword="null"/> when the stream states no mapping at all — which is different
    /// from a map that maps nothing, because the caller has to warn in the first case and stay quiet in
    /// the second.
    /// </returns>
    public static PdfToUnicodeCMap? Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var tokens = CMapToken.ReadAll(data);
        var mappings = new Dictionary<int, string>();
        var codeLength = 0;
        var index = 0;

        while (index < tokens.Count)
        {
            if (tokens[index].Keyword == "beginbfchar")
            {
                index = ReadCharacterSection(tokens, index + 1, mappings, ref codeLength);
            }
            else if (tokens[index].Keyword == "beginbfrange")
            {
                index = ReadRangeSection(tokens, index + 1, mappings, ref codeLength);
            }
            else
            {
                index++;
            }
        }

        if (mappings.Count == 0)
        {
            return null;
        }

        var width = codeLength == 0 ? 2 : codeLength;

        return new PdfToUnicodeCMap(mappings, width);
    }

    /// <summary>Looks up the text a code stands for.</summary>
    /// <param name="code">The character code, as the font's code length reads it.</param>
    /// <param name="text">The text, when the map states one for the code.</param>
    /// <returns><see langword="true"/> when the map states text for the code.</returns>
    public bool TryGet(int code, out string text) => mappings.TryGetValue(code, out text!);

    private static int ReadCharacterSection(
        List<CMapToken> tokens,
        int index,
        Dictionary<int, string> mappings,
        ref int codeLength)
    {
        while (index < tokens.Count)
        {
            if (tokens[index].Keyword == "endbfchar")
            {
                return index;
            }

            if (!tokens[index].TryGetBytes(out var source))
            {
                index++;
                continue;
            }

            index++;

            if (index < tokens.Count && tokens[index].TryGetBytes(out var destination))
            {
                mappings[Code(source)] = Decode(destination);
                codeLength = Math.Max(codeLength, source.Length);
                index++;
                continue;
            }

            index++;
        }

        return index;
    }

    private static int ReadRangeSection(
        List<CMapToken> tokens,
        int index,
        Dictionary<int, string> mappings,
        ref int codeLength)
    {
        while (index < tokens.Count)
        {
            if (tokens[index].Keyword == "endbfrange")
            {
                return index;
            }

            if (!tokens[index].TryGetBytes(out var low))
            {
                index++;
                continue;
            }

            index++;

            if (index >= tokens.Count || !tokens[index].TryGetBytes(out var high))
            {
                index++;
                continue;
            }

            index++;

            var first = Code(low);
            var last = Code(high);

            codeLength = Math.Max(codeLength, Math.Max(low.Length, high.Length));

            if (index < tokens.Count && tokens[index].IsArrayStart)
            {
                index++;
                var code = first;

                while (index < tokens.Count && !tokens[index].IsArrayEnd)
                {
                    if (code <= last && tokens[index].TryGetBytes(out var destination))
                    {
                        mappings[code] = Decode(destination);
                    }

                    code++;
                    index++;
                }

                if (index < tokens.Count && tokens[index].IsArrayEnd)
                {
                    index++;
                }

                continue;
            }

            if (index < tokens.Count && tokens[index].TryGetBytes(out var rangeDestination))
            {
                index++;

                var text = Decode(rangeDestination);

                for (var code = first; code <= last && code - first < 65535; code++)
                {
                    mappings[code] = Increment(text, code - first);
                }

                continue;
            }

            index++;
        }

        return index;
    }

    private static int Code(byte[] bytes)
    {
        var code = 0;

        foreach (var value in bytes)
        {
            code = (code << 8) | value;
        }

        return code;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        return bytes.Length % 2 == 0
            ? System.Text.Encoding.BigEndianUnicode.GetString(bytes)
            : System.Text.Encoding.Latin1.GetString(bytes);
    }

    /// <summary>Adds a delta to the last character of a destination, the way a <c>bfrange</c> states it.</summary>
    /// <param name="text">The destination of the first code of the range.</param>
    /// <param name="delta">The offset of the code from the first of the range.</param>
    /// <returns>The destination of the code.</returns>
    private static string Increment(string text, int delta)
    {
        if (delta == 0 || text.Length == 0)
        {
            return text;
        }

        var characters = text.ToCharArray();

        characters[^1] = (char)(characters[^1] + delta);

        return new string(characters);
    }

    /// <summary>One token of a CMap: a hexadecimal string, a keyword, or an array bracket.</summary>
    /// <param name="Bytes">The bytes of a hexadecimal string.</param>
    /// <param name="Keyword">The text of a keyword.</param>
    /// <param name="Delimiter">The bracket character, or <c>'\0'</c> when the token is not a bracket.</param>
    private readonly record struct CMapToken(byte[]? Bytes, string? Keyword, char Delimiter)
    {
        /// <summary>Gets a value indicating whether the token opens an array.</summary>
        public bool IsArrayStart => Delimiter == '[';

        /// <summary>Gets a value indicating whether the token closes an array.</summary>
        public bool IsArrayEnd => Delimiter == ']';

        /// <summary>Takes the bytes of a hexadecimal string token.</summary>
        /// <param name="bytes">The bytes, when the token is a hexadecimal string.</param>
        /// <returns><see langword="true"/> when the token is a hexadecimal string.</returns>
        public bool TryGetBytes(out byte[] bytes)
        {
            bytes = Bytes!;

            return Bytes is not null;
        }

        /// <summary>Reads every token of a CMap.</summary>
        /// <param name="data">The bytes of the CMap stream.</param>
        /// <returns>The tokens, in order.</returns>
        /// <remarks>
        /// Numbers are dropped rather than tokenized: no section this type reads states one, and
        /// dropping them keeps a stray number from being mistaken for a code.
        /// </remarks>
        public static List<CMapToken> ReadAll(byte[] data)
        {
            var tokens = new List<CMapToken>();
            var index = 0;

            while (index < data.Length)
            {
                var current = data[index];

                if (PdfLexer.IsWhitespace(current))
                {
                    index++;
                    continue;
                }

                if (current == (byte)'%')
                {
                    while (index < data.Length && data[index] is not ((byte)'\n' or (byte)'\r'))
                    {
                        index++;
                    }

                    continue;
                }

                if (current == (byte)'[')
                {
                    tokens.Add(new CMapToken(null, null, '['));
                    index++;
                    continue;
                }

                if (current == (byte)']')
                {
                    tokens.Add(new CMapToken(null, null, ']'));
                    index++;
                    continue;
                }

                if (current == (byte)'<')
                {
                    tokens.Add(new CMapToken(ReadHex(data, ref index), null, '\0'));
                    continue;
                }

                if (current == (byte)'>')
                {
                    index++;
                    continue;
                }

                if (current is >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'+')
                {
                    while (index < data.Length && !PdfLexer.IsWhitespaceOrDelimiter(data[index]))
                    {
                        index++;
                    }

                    continue;
                }

                var start = index;

                while (index < data.Length && !PdfLexer.IsWhitespaceOrDelimiter(data[index]))
                {
                    index++;
                }

                if (index == start)
                {
                    index++;
                    continue;
                }

                tokens.Add(new CMapToken(
                    null,
                    System.Text.Encoding.Latin1.GetString(data, start, index - start),
                    '\0'));
            }

            return tokens;
        }

        private static byte[] ReadHex(byte[] data, ref int index)
        {
            index++;

            var bytes = new List<byte>(8);
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
    }
}
