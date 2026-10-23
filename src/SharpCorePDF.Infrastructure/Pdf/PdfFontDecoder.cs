using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// A font as far as reading text from it goes: how many bytes one of its codes takes, which text each
/// code stands for, and how wide each code draws.
/// </summary>
/// <remarks>
/// <para>
/// A font is the only thing that can decode a string: the bytes of a <c>Tj</c> are codes, and what a
/// code means is stated by a <c>/ToUnicode</c> map, by an encoding table, or by nothing at all. This
/// type holds what the document states, and it says so when the document states nothing usable instead
/// of falling back on a guess — a wrong character is worse than a missing one, because nothing
/// downstream can tell them apart (NFR-7.8).
/// </para>
/// <para>
/// Widths are kept in fractions of an em, which is how a PDF states them and how the caller needs them:
/// an advance in page space is a width in ems times the font size, and only then does the text matrix
/// and the page transform apply.
/// </para>
/// </remarks>
internal sealed class PdfFont
{
    private readonly Dictionary<int, double> widths;

    /// <summary>Initializes a new instance of the <see cref="PdfFont"/> class.</summary>
    /// <param name="resourceName">The name the resources give the font, without the leading slash.</param>
    /// <param name="baseFont">The base font the font dictionary states.</param>
    /// <param name="isComposite">Whether the font is a composite (<c>Type0</c>) font.</param>
    /// <param name="codeLength">The number of bytes one code takes.</param>
    /// <param name="defaultWidth">The width of a code the font states no width for, in ems.</param>
    /// <param name="toUnicode">The <c>/ToUnicode</c> map, when the font states one.</param>
    /// <param name="encoding">The encoding table of a simple font, when it has one.</param>
    /// <param name="widths">The widths the font states, in ems, keyed by code.</param>
    /// <param name="failureReason">
    /// Why the font cannot be read, or <see langword="null"/> when it can. The text is a clause the
    /// caller puts in a warning, so it reads as prose: \"carries no /ToUnicode map\".
    /// </param>
    public PdfFont(
        string resourceName,
        string? baseFont,
        bool isComposite,
        int codeLength,
        double defaultWidth,
        PdfToUnicodeCMap? toUnicode,
        string?[]? encoding,
        Dictionary<int, double> widths,
        string? failureReason)
    {
        ResourceName = resourceName;
        BaseFont = baseFont;
        IsComposite = isComposite;
        CodeLength = codeLength;
        DefaultWidth = defaultWidth;
        ToUnicode = toUnicode;
        Encoding = encoding;
        this.widths = widths;
        FailureReason = failureReason;
    }

    /// <summary>Gets the name the resources give the font, without the leading slash.</summary>
    public string ResourceName { get; }

    /// <summary>Gets the base font the font dictionary states, or <see langword="null"/>.</summary>
    public string? BaseFont { get; }

    /// <summary>Gets a value indicating whether the font is a composite (<c>Type0</c>) font.</summary>
    public bool IsComposite { get; }

    /// <summary>Gets the number of bytes one code of the font takes.</summary>
    public int CodeLength { get; }

    /// <summary>Gets the width of a code the font states no width for, in ems.</summary>
    public double DefaultWidth { get; }

    /// <summary>Gets the <c>/ToUnicode</c> map of the font, when it states one.</summary>
    public PdfToUnicodeCMap? ToUnicode { get; }

    /// <summary>Gets the encoding table of a simple font, when it has the 256 entries of one.</summary>
    public string?[]? Encoding { get; }

    /// <summary>Gets why the font cannot be read, or <see langword="null"/> when it can.</summary>
    public string? FailureReason { get; }

    /// <summary>Gets a value indicating whether the font can turn its codes into text.</summary>
    public bool CanDecode => FailureReason is null;

    /// <summary>Reads one code of a string: its text, its width and how many bytes it took.</summary>
    /// <param name="bytes">The bytes of the string being shown.</param>
    /// <param name="offset">The position of the code in the string.</param>
    /// <param name="text">The text of the code, empty when the font does not map it.</param>
    /// <param name="consumed">The number of bytes the code took.</param>
    /// <param name="width">The advance of the code in ems, stated even when the code has no text.</param>
    /// <returns><see langword="true"/> when the code has text.</returns>
    /// <remarks>
    /// A code without text still has a width, and the width is returned with a
    /// <see langword="false"/> result: the caller has to advance past a glyph it cannot read, or every
    /// character after it would be placed as if it were not there.
    /// </remarks>
    public bool TryDecodeGlyph(byte[] bytes, int offset, out string text, out int consumed, out double width)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        text = string.Empty;
        width = DefaultWidth;

        if (offset < 0 || offset >= bytes.Length)
        {
            consumed = 0;
            return false;
        }

        var length = Math.Min(CodeLength, bytes.Length - offset);
        var code = 0;

        for (var index = 0; index < length; index++)
        {
            code = (code << 8) | bytes[offset + index];
        }

        consumed = length;
        width = widths.TryGetValue(code, out var stated) ? stated : DefaultWidth;

        if (FailureReason is not null || length < CodeLength)
        {
            return false;
        }

        if (ToUnicode is not null && ToUnicode.TryGet(code, out var mapped) && mapped.Length > 0)
        {
            text = mapped;
            return true;
        }

        if (CodeLength == 1 && Encoding is not null && code < Encoding.Length && Encoding[code] is { } single)
        {
            text = single;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Builds the fonts a resource dictionary names, so the text walk can turn strings into text.
/// </summary>
/// <remarks>
/// <para>
/// The fonts of one resource dictionary are read once per content stream, which is what the walk needs:
/// a page states a handful of fonts, and a form states its own or inherits the page's. A font whose
/// dictionary does not resolve is left out and the walk reports the resource as missing when a
/// <c>Tf</c> selects it, so a broken font costs a warning and not the page.
/// </para>
/// <para>
/// Nothing here reads the font program. The widths, the encoding and the <c>/ToUnicode</c> map live in
/// the font dictionary, and those are exactly the three things text placement and text decoding need;
/// an embedded program would be needed to draw a glyph, which this library does not do.
/// </para>
/// </remarks>
internal static class PdfFontDecoder
{
    /// <summary>The width a simple font's code takes when the font states none, in ems.</summary>
    private const double SimpleDefaultWidth = 0.5d;

    /// <summary>The width a composite font's code takes when the font states none, in ems.</summary>
    private const double CompositeDefaultWidth = 1d;

    /// <summary>
    /// The width a Courier glyph takes, in ems. Courier is the one standard font whose glyphs are all
    /// one width, so its table collapses to this single number.
    /// </summary>
    private const double CourierWidth = 0.6d;

    /// <summary>Reads the fonts of a resource dictionary.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="resources">The resources in scope, which may be <see langword="null"/>.</param>
    /// <returns>The fonts, keyed by the name a <c>Tf</c> selects them with.</returns>
    public static Dictionary<string, PdfFont> Load(PdfFileReader reader, PdfDictionary? resources)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var fonts = new Dictionary<string, PdfFont>(StringComparer.Ordinal);
        var entries = reader.ResolveDictionary(resources?.Get("Font"));

        if (entries is null)
        {
            return fonts;
        }

        foreach (var entry in entries.Entries)
        {
            if (reader.Resolve(entry.Value) is not PdfDictionary dictionary)
            {
                continue;
            }

            fonts[entry.Key] = Create(reader, entry.Key, dictionary);
        }

        return fonts;
    }

    /// <summary>Reads one font dictionary.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="resourceName">The name the resources give the font.</param>
    /// <param name="dictionary">The font dictionary.</param>
    /// <returns>The font, which states why it cannot be read when that is the case.</returns>
    private static PdfFont Create(PdfFileReader reader, string resourceName, PdfDictionary dictionary)
    {
        var isComposite = string.Equals(dictionary.GetName("Subtype"), "Type0", StringComparison.Ordinal);
        var toUnicode = ReadToUnicode(reader, dictionary, out var mapFailure);

        string?[]? encoding = null;
        string? failure = null;

        if (isComposite)
        {
            failure = mapFailure ?? (toUnicode is null
                ? "carries no /ToUnicode map, so its codes name glyphs of a font program this library does not read"
                : null);
        }
        else if (!TryReadEncoding(reader, dictionary, out encoding, out var unsupported))
        {
            failure = unsupported;
        }

        var widths = isComposite
            ? ReadCompositeWidths(reader, dictionary, out var defaultWidth)
            : ReadSimpleWidths(reader, dictionary, out defaultWidth);

        return new PdfFont(
            resourceName,
            dictionary.GetName("BaseFont"),
            isComposite,
            isComposite ? toUnicode?.CodeLength ?? 2 : 1,
            defaultWidth,
            toUnicode,
            encoding,
            widths,
            failure);
    }


    /// <summary>Reads the <c>/ToUnicode</c> map of a font.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="dictionary">The font dictionary.</param>
    /// <param name="failure">Why the map cannot be read, or <see langword="null"/> when it can.</param>
    /// <returns>The map, or <see langword="null"/> when the font states none this library can read.</returns>
    /// <remarks>
    /// A map that cannot be decoded is reported separately from one that is absent, because a composite
    /// font whose map is broken has no other statement of what its codes mean: the two cases read the
    /// same to the caller — no text — but they are different facts about the document.
    /// </remarks>
    private static PdfToUnicodeCMap? ReadToUnicode(
        PdfFileReader reader,
        PdfDictionary dictionary,
        out string? failure)
    {
        failure = null;

        if (reader.Resolve(dictionary.Get("ToUnicode")) is not PdfStream stream)
        {
            return null;
        }

        byte[] data;

        try
        {
            data = PdfFilters.Decode(stream, reader.SourceName);
        }
        catch (PdfUnsupportedFeatureException)
        {
            failure = "states a /ToUnicode map that uses a filter this library does not decode";
            return null;
        }
        catch (PdfSourceFormatException)
        {
            failure = "states a /ToUnicode map that could not be decompressed";
            return null;
        }

        var map = PdfToUnicodeCMap.Parse(data);

        if (map is null)
        {
            failure = "states a /ToUnicode map that names no code";
        }

        return map;
    }


    /// <summary>Reads the encoding of a simple font, including the glyph names of its <c>/Differences</c>.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="dictionary">The font dictionary.</param>
    /// <param name="encoding">The table of 256 entries, or <see langword="null"/> when none was read.</param>
    /// <param name="failure">Why the encoding cannot be read, or <see langword="null"/> when it can.</param>
    /// <returns><see langword="true"/> when the encoding is one this library implements.</returns>
    private static bool TryReadEncoding(
        PdfFileReader reader,
        PdfDictionary dictionary,
        out string?[]? encoding,
        out string? failure)
    {
        failure = null;

        switch (reader.Resolve(dictionary.Get("Encoding")))
        {
            case null:
                encoding = PdfGlyphEncodings.TryGetBaseEncoding(null, out var fallback) ? fallback : null;
                return true;

            case PdfName name:
                if (PdfGlyphEncodings.TryGetBaseEncoding(name.Value, out var stated))
                {
                    encoding = stated;
                    return true;
                }

                encoding = null;
                failure = $"states the encoding /{name.Value}, which this library does not implement";
                return false;

            case PdfDictionary entries:
                return TryReadEncodingDictionary(reader, entries, out encoding, out failure);

            default:
                encoding = null;
                failure = "states an /Encoding that is neither a name nor a dictionary";
                return false;
        }
    }

    /// <summary>Reads an <c>/Encoding</c> dictionary: a base encoding and the names that replace its codes.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="entries">The encoding dictionary.</param>
    /// <param name="encoding">The table of 256 entries, or <see langword="null"/> when none was read.</param>
    /// <param name="failure">Why the encoding cannot be read, or <see langword="null"/> when it can.</param>
    /// <returns><see langword="true"/> when the encoding is one this library implements.</returns>
    /// <remarks>
    /// A name in <c>/Differences</c> that this library does not know leaves its code unmapped rather
    /// than mapping it to the character of a code that happens to be near it: the caller then reports
    /// the glyph as unreadable, which is a fact, instead of drawing a character the document never
    /// stated.
    /// </remarks>
    private static bool TryReadEncodingDictionary(
        PdfFileReader reader,
        PdfDictionary entries,
        out string?[]? encoding,
        out string? failure)
    {
        failure = null;

        string? baseName = null;

        if (reader.Resolve(entries.Get("BaseEncoding")) is PdfName stated)
        {
            baseName = stated.Value;
        }

        if (!PdfGlyphEncodings.TryGetBaseEncoding(baseName, out var table))
        {
            encoding = null;
            failure = $"states the base encoding /{baseName}, which this library does not implement";
            return false;
        }

        if (reader.ResolveArray(entries.Get("Differences")) is { } differences)
        {
            ApplyDifferences(reader, differences, table);
        }

        encoding = table;
        return true;
    }


    /// <summary>Applies the glyph names of a <c>/Differences</c> array to an encoding table.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="differences">The array, which alternates code numbers and glyph names.</param>
    /// <param name="table">The table the names replace entries in.</param>
    private static void ApplyDifferences(PdfFileReader reader, PdfArray differences, string?[] table)
    {
        var code = 0;

        foreach (var item in differences.Items)
        {
            switch (reader.Resolve(item))
            {
                case PdfInteger number:
                    code = (int)number.Value;
                    break;

                case PdfName name:
                    if (code >= 0 && code < table.Length)
                    {
                        table[code] = PdfGlyphEncodings.FromGlyphName(name.Value)?.ToString();
                    }

                    code++;
                    break;
            }
        }
    }

    /// <summary>Reads the widths of a simple font, which are stated one per code.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="dictionary">The font dictionary.</param>
    /// <param name="defaultWidth">The width of a code the font states no width for, in ems.</param>
    /// <returns>The widths, in ems, keyed by code.</returns>
    /// <remarks>
    /// <c>/Widths</c> covers the codes from <c>/FirstChar</c> onwards, and a font may leave it out: the
    /// standard fonts do, and their widths are then the table the specification prints for them. Only
    /// Courier is reproduced here, because it is the one standard font whose glyphs are all one width
    /// and the one a monospaced page is set in; the others fall back on the default width, which the
    /// reader states rather than passing off as the font's own.
    /// </remarks>
    private static Dictionary<int, double> ReadSimpleWidths(
        PdfFileReader reader,
        PdfDictionary dictionary,
        out double defaultWidth)
    {
        var widths = new Dictionary<int, double>();
        var baseFont = dictionary.GetName("BaseFont");

        defaultWidth = baseFont is not null && baseFont.Contains("Courier", StringComparison.Ordinal)
            ? CourierWidth
            : SimpleDefaultWidth;

        if (reader.ResolveArray(dictionary.Get("Widths")) is not { } stated)
        {
            return widths;
        }

        var first = (int)(dictionary.GetInteger("FirstChar") ?? 0);

        for (var index = 0; index < stated.Count; index++)
        {
            if (NumberOf(reader.Resolve(stated.Items[index])) is { } width)
            {
                widths[first + index] = width / 1000d;
            }
        }

        return widths;
    }


    /// <summary>Reads the widths of a composite font, which a descendant font states as spans.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="dictionary">The font dictionary.</param>
    /// <param name="defaultWidth">The width of a code the font states no width for, in ems.</param>
    /// <returns>The widths, in ems, keyed by code.</returns>
    /// <remarks>
    /// <c>/W</c> is a list of spans, each either a start code and an array of widths or a first code, a
    /// last code and one width for the whole span. Both forms are read here; a span is clipped to the
    /// range a two byte code can name, so a malformed one cannot ask for a table the size of memory.
    /// </remarks>
    private static Dictionary<int, double> ReadCompositeWidths(
        PdfFileReader reader,
        PdfDictionary dictionary,
        out double defaultWidth)
    {
        var widths = new Dictionary<int, double>();
        defaultWidth = CompositeDefaultWidth;

        if (reader.ResolveArray(dictionary.Get("DescendantFonts")) is not { Count: > 0 } descendants
            || reader.Resolve(descendants.Items[0]) is not PdfDictionary descendant)
        {
            return widths;
        }

        if (NumberOf(reader.Resolve(descendant.Get("DW"))) is { } statedDefault && statedDefault > 0d)
        {
            defaultWidth = statedDefault / 1000d;
        }

        if (reader.ResolveArray(descendant.Get("W")) is not { } entries)
        {
            return widths;
        }

        var index = 0;

        while (index + 1 < entries.Count)
        {
            if (NumberOf(reader.Resolve(entries.Items[index])) is not { } first)
            {
                index++;
                continue;
            }

            if (reader.ResolveArray(entries.Items[index + 1]) is { } listed)
            {
                AddStatedWidths(reader, widths, (int)first, listed);
                index += 2;
                continue;
            }

            if (index + 2 < entries.Count
                && NumberOf(reader.Resolve(entries.Items[index + 1])) is { } last
                && NumberOf(reader.Resolve(entries.Items[index + 2])) is { } width)
            {
                AddWidthSpan(widths, (int)first, (int)last, width);
                index += 3;
                continue;
            }

            index++;
        }

        return widths;
    }

    /// <summary>Adds the widths a span of codes lists one by one.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="widths">The table that receives them.</param>
    /// <param name="first">The code the first width belongs to.</param>
    /// <param name="listed">The widths of the span.</param>
    private static void AddStatedWidths(
        PdfFileReader reader,
        Dictionary<int, double> widths,
        int first,
        PdfArray listed)
    {
        for (var offset = 0; offset < listed.Count; offset++)
        {
            if (NumberOf(reader.Resolve(listed.Items[offset])) is { } width)
            {
                AddWidth(widths, first + offset, width);
            }
        }
    }

    /// <summary>Adds one width to every code of a span.</summary>
    /// <param name="widths">The table that receives them.</param>
    /// <param name="first">The first code of the span.</param>
    /// <param name="last">The last code of the span.</param>
    /// <param name="width">The width of the span, in thousandths of an em.</param>
    private static void AddWidthSpan(Dictionary<int, double> widths, int first, int last, double width)
    {
        var from = Math.Clamp(first, 0, ushort.MaxValue);
        var to = Math.Clamp(last, from, ushort.MaxValue);

        for (var code = from; code <= to; code++)
        {
            AddWidth(widths, code, width);
        }
    }

    /// <summary>Adds one width, in thousandths of an em, when the code is one a two byte code can be.</summary>
    /// <param name="widths">The table that receives it.</param>
    /// <param name="code">The code.</param>
    /// <param name="width">The width, in thousandths of an em.</param>
    private static void AddWidth(Dictionary<int, double> widths, int code, double width)
    {
        if (code >= 0 && code <= ushort.MaxValue)
        {
            widths[code] = width / 1000d;
        }
    }

    /// <summary>Reads a value as a number, which an array of numbers states as an integer or a real.</summary>
    /// <param name="value">The value, already resolved.</param>
    /// <returns>The number, or <see langword="null"/> when the value is not one.</returns>
    private static double? NumberOf(PdfObject? value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => null,
    };
}
