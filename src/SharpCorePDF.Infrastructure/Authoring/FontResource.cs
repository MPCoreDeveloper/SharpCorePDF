using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// One font object of an authored document: a standard font dictionary, or a subsetted composite font
/// with its descriptor, font program and <c>ToUnicode</c> map.
/// </summary>
internal sealed class FontResource
{
    private static readonly Lazy<Dictionary<char, byte>> WinAnsiCodes = new(BuildWinAnsi);

    private readonly TrueTypeFont? trueType;
    private readonly int descendantNumber;
    private readonly int descriptorNumber;
    private readonly int fontFileNumber;
    private readonly int toUnicodeNumber;
    private readonly Dictionary<ushort, int> glyphText = [];

    /// <summary>Initializes a new instance of the <see cref="FontResource"/> class.</summary>
    /// <param name="writer">The writer of the document, which allocates the object numbers.</param>
    /// <param name="typeface">The face.</param>
    /// <param name="name">The resource name, for example <c>F1</c>.</param>
    public FontResource(PdfWriter writer, PdfTypeface typeface, string name)
    {
        Name = name;
        Typeface = typeface;

        if (typeface.IsEmbedded)
        {
            trueType = typeface.Embedded;
            ObjectNumber = writer.AllocateObject();
            descendantNumber = writer.AllocateObject();
            descriptorNumber = writer.AllocateObject();
            fontFileNumber = writer.AllocateObject();
            toUnicodeNumber = writer.AllocateObject();
            Fingerprint = SHA256.HashData(trueType.Data);
        }
        else
        {
            Family = typeface.Family!.Value;
            ObjectNumber = writer.AllocateObject();
            Fingerprint = Encoding.UTF8.GetBytes(BaseFontOf(Family.Value));
        }
    }

    /// <summary>Gets the resource name the pages select the font with.</summary>
    public string Name { get; }

    /// <summary>Gets the face.</summary>
    public PdfTypeface Typeface { get; }

    /// <summary>Gets the standard family, or <see langword="null"/> for an embedded face.</summary>
    public StandardFont? Family { get; }

    /// <summary>Gets the object number of the font dictionary.</summary>
    public int ObjectNumber { get; }

    /// <summary>Gets a digest of the font program, which keeps two different faces apart in the document identifier.</summary>
    public byte[] Fingerprint { get; }

    /// <summary>Gets the glyphs the document draws with an embedded face.</summary>
    public HashSet<ushort> UsedGlyphs { get; } = [];

    /// <summary>Encodes a line of text into the codes the font dictionary expects.</summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="hexadecimal">Whether the codes must be written as a hexadecimal string.</param>
    /// <returns>The codes.</returns>
    /// <exception cref="ArgumentException">The face has no glyph or code for a character of the text.</exception>
    public byte[] Encode(string text, out bool hexadecimal)
    {
        hexadecimal = trueType is not null;

        if (trueType is null)
        {
            return EncodeStandard(text);
        }

        var codes = new List<byte>(text.Length * 2);

        foreach (var rune in text.EnumerateRunes())
        {
            if (!trueType.TryGetGlyph(rune.Value, out var glyph))
            {
                throw new ArgumentException(
                    $"The font '{trueType.Name}' has no glyph for '{rune}' (U+{rune.Value:X4}). Supply a font that covers it, or use a different character.",
                    nameof(text));
            }

            UsedGlyphs.Add(glyph);
            glyphText.TryAdd(glyph, rune.Value);
            codes.Add((byte)(glyph >> 8));
            codes.Add((byte)(glyph & 0xFF));
        }

        return [.. codes];
    }

    /// <summary>Writes the font objects of this resource.</summary>
    /// <param name="writer">The writer of the document.</param>
    public void Write(PdfWriter writer)
    {
        if (trueType is null)
        {
            writer.WriteObject(
                ObjectNumber,
                new PdfDictionary()
                    .SetName("Type", "Font")
                    .SetName("Subtype", "Type1")
                    .SetName("BaseFont", BaseFontOf(Family!.Value))
                    .SetName("Encoding", "WinAnsiEncoding"));

            return;
        }

        var program = trueType.BuildSubset(UsedGlyphs);
        var baseFont = $"{SubsetTag(trueType)}+{trueType.Name}";

        writer.WriteObject(
            fontFileNumber,
            new PdfStream(
                new PdfDictionary()
                    .SetInteger("Length1", program.Length)
                    .SetName("Filter", "FlateDecode"),
                Compress(program)));

        writer.WriteObject(
            toUnicodeNumber,
            new PdfStream(new PdfDictionary(), BuildToUnicode()));

        writer.WriteObject(descriptorNumber, BuildDescriptor(trueType, baseFont));
        writer.WriteObject(descendantNumber, BuildDescendant(trueType, baseFont));
        writer.WriteObject(ObjectNumber, BuildComposite(baseFont));
    }

    /// <summary>Encodes text in <c>WinAnsiEncoding</c>.</summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The bytes of the string.</returns>
    /// <exception cref="ArgumentException">A character has no code in Windows-1252.</exception>
    private byte[] EncodeStandard(string text)
    {
        var bytes = new byte[text.Length];

        for (var index = 0; index < text.Length; index++)
        {
            if (!WinAnsiCodes.Value.TryGetValue(text[index], out var code))
            {
                throw new ArgumentException(
                    $"The standard font {BaseFontOf(Family!.Value)} is written with WinAnsiEncoding, which has no code for '{text[index]}' (U+{(int)text[index]:X4}). Use an embedded TrueType font for text outside that encoding.",
                    nameof(text));
            }

            bytes[index] = code;
        }

        return bytes;
    }

    /// <summary>Builds the descriptor of an embedded font.</summary>
    /// <param name="font">The font program.</param>
    /// <param name="baseFont">The subset name written as <c>/BaseFont</c>.</param>
    /// <returns>The descriptor dictionary.</returns>
    private PdfDictionary BuildDescriptor(TrueTypeFont font, string baseFont)
    {
        var (left, bottom, right, top) = font.BoundingBox;

        return new PdfDictionary()
            .SetName("Type", "FontDescriptor")
            .SetName("FontName", baseFont)
            .SetInteger("Flags", 32)
            .Set(
                "FontBBox",
                new PdfArray(
                [
                    new PdfInteger(Scale(font, left)),
                    new PdfInteger(Scale(font, bottom)),
                    new PdfInteger(Scale(font, right)),
                    new PdfInteger(Scale(font, top)),
                ]))
            .Set("ItalicAngle", new PdfReal(font.ItalicAngle))
            .SetInteger("Ascent", Scale(font, font.Ascender))
            .SetInteger("Descent", Scale(font, font.Descender))
            .SetInteger("CapHeight", Scale(font, font.CapHeight))
            .SetInteger("StemV", 80)
            .Set("FontFile2", new PdfReference(fontFileNumber, 0));
    }

    /// <summary>Builds the descendant font of a composite font.</summary>
    /// <param name="font">The font program.</param>
    /// <param name="baseFont">The subset name written as <c>/BaseFont</c>.</param>
    /// <returns>The descendant dictionary.</returns>
    private PdfDictionary BuildDescendant(TrueTypeFont font, string baseFont)
    {
        var widths = new PdfArray();

        foreach (var glyph in UsedGlyphs.Order())
        {
            widths.Add(new PdfInteger(glyph));
            widths.Add(new PdfArray([new PdfInteger(Scale(font, font.GetAdvanceWidth(glyph)))]));
        }

        return new PdfDictionary()
            .SetName("Type", "Font")
            .SetName("Subtype", "CIDFontType2")
            .SetName("BaseFont", baseFont)
            .Set(
                "CIDSystemInfo",
                new PdfDictionary()
                    .Set("Registry", PdfString.FromText("Adobe"))
                    .Set("Ordering", PdfString.FromText("Identity"))
                    .SetInteger("Supplement", 0))
            .Set("FontDescriptor", new PdfReference(descriptorNumber, 0))
            .SetInteger("DW", 1000)
            .Set("W", widths)
            .SetName("CIDToGIDMap", "Identity");
    }

    /// <summary>Builds the composite font dictionary the pages select.</summary>
    /// <param name="baseFont">The subset name written as <c>/BaseFont</c>.</param>
    /// <returns>The font dictionary.</returns>
    private PdfDictionary BuildComposite(string baseFont) =>
        new PdfDictionary()
            .SetName("Type", "Font")
            .SetName("Subtype", "Type0")
            .SetName("BaseFont", baseFont)
            .SetName("Encoding", "Identity-H")
            .Set("DescendantFonts", new PdfArray([new PdfReference(descendantNumber, 0)]))
            .Set("ToUnicode", new PdfReference(toUnicodeNumber, 0));

    /// <summary>Builds the <c>ToUnicode</c> map of the glyphs the document draws.</summary>
    /// <returns>The bytes of the CMap stream.</returns>
    private byte[] BuildToUnicode()
    {
        var map = new StringBuilder();
        map.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        map.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        map.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        map.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        var entries = glyphText.OrderBy(entry => entry.Key).ToList();

        for (var start = 0; start < entries.Count; start += 100)
        {
            var count = Math.Min(100, entries.Count - start);
            map.Append(System.Globalization.CultureInfo.InvariantCulture, $"{count} beginbfchar\n");

            for (var index = start; index < start + count; index++)
            {
                map.Append('<')
                    .Append(entries[index].Key.ToString("X4", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("> <")
                    .Append(UnicodeHex(entries[index].Value))
                    .Append(">\n");
            }

            map.Append("endbfchar\n");
        }

        map.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(map.ToString());
    }

    /// <summary>Formats a code point as the UTF-16BE hexadecimal a CMap destination uses.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The hexadecimal destination.</returns>
    private static string UnicodeHex(int codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            return codePoint.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
        }

        var value = codePoint - 0x10000;
        var high = 0xD800 + (value >> 10);
        var low = 0xDC00 + (value & 0x3FF);

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{high:X4}{low:X4}");
    }

    /// <summary>Converts a font unit to the thousandths of an em a PDF font states.</summary>
    /// <param name="font">The font program.</param>
    /// <param name="value">The value in font units.</param>
    /// <returns>The value in thousandths of an em.</returns>
    private static int Scale(TrueTypeFont font, int value) =>
        (int)Math.Round(value * 1000d / font.UnitsPerEm, MidpointRounding.AwayFromZero);

    /// <summary>Gets the name the standard font dictionary states.</summary>
    /// <param name="family">The standard family.</param>
    /// <returns>The base font name.</returns>
    private static string BaseFontOf(StandardFont family) => family switch
    {
        StandardFont.Helvetica => "Helvetica",
        StandardFont.HelveticaBold => "Helvetica-Bold",
        StandardFont.HelveticaOblique => "Helvetica-Oblique",
        StandardFont.HelveticaBoldOblique => "Helvetica-BoldOblique",
        StandardFont.TimesRoman => "Times-Roman",
        StandardFont.TimesBold => "Times-Bold",
        StandardFont.TimesItalic => "Times-Italic",
        StandardFont.TimesBoldItalic => "Times-BoldItalic",
        StandardFont.Courier => "Courier",
        StandardFont.CourierBold => "Courier-Bold",
        StandardFont.CourierOblique => "Courier-Oblique",
        StandardFont.CourierBoldOblique => "Courier-BoldOblique",
        _ => "Helvetica",
    };

    /// <summary>Builds the six letter subset tag of a font program, deterministically.</summary>
    /// <param name="font">The font program.</param>
    /// <returns>The tag.</returns>
    private static string SubsetTag(TrueTypeFont font)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(font.Data, hash);
        Span<char> tag = stackalloc char[6];

        for (var index = 0; index < tag.Length; index++)
        {
            tag[index] = (char)('A' + (hash[index] % 26));
        }

        return new string(tag);
    }

    /// <summary>Compresses a font program with the zlib wrapper a Flate stream uses.</summary>
    /// <param name="data">The bytes to compress.</param>
    /// <returns>The compressed bytes.</returns>
    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Builds the reverse table of <c>WinAnsiEncoding</c>, from character to code.</summary>
    /// <returns>The table.</returns>
    private static Dictionary<char, byte> BuildWinAnsi()
    {
        if (!PdfGlyphEncodings.TryGetBaseEncoding("WinAnsiEncoding", out var table))
        {
            throw new InvalidOperationException("The engine states that it knows WinAnsiEncoding but cannot build its table.");
        }

        var codes = new Dictionary<char, byte>(256);

        for (var code = 0; code < table.Length; code++)
        {
            if (table[code] is { Length: 1 } text)
            {
                codes.TryAdd(text[0], (byte)code);
            }
        }

        return codes;
    }
}
