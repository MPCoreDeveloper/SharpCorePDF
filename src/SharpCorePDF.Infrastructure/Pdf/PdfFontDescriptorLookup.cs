using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Reads the font dictionary entries that describe where a font's program lives, so the font inspector and
/// the archival rules answer that question the same way.
/// </summary>
/// <remarks>
/// A composite font states its descriptor and its descendant on the <c>/Type0</c> dictionary, not the other
/// way round, and a subset says so in the base font name rather than in an entry of its own. Both are
/// conventions of the file format, which is why they live here rather than in the domain.
/// </remarks>
internal static class PdfFontDescriptorLookup
{
    /// <summary>Finds the font descriptor of a simple or composite font.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <returns>The descriptor, or <see langword="null"/> when the font states none.</returns>
    public static PdfDictionary? Find(PdfFileReader reader, PdfDictionary font)
    {
        var descriptor = reader.ResolveDictionary(font.Get("FontDescriptor"));

        if (descriptor is null && Descendant(reader, font) is { } descendant)
        {
            // A composite font states its descriptor on the descendant, not on the Type0 dictionary.
            descriptor = reader.ResolveDictionary(descendant.Get("FontDescriptor"));
        }

        return descriptor;
    }

    /// <summary>Reads where the program of a font is stored.</summary>
    /// <param name="descriptor">The descriptor of the font, or <see langword="null"/> when it has none.</param>
    /// <returns>The kind of program the descriptor states.</returns>
    public static PdfEmbeddedFontProgramKind Program(PdfDictionary? descriptor)
    {
        if (descriptor is null)
        {
            return PdfEmbeddedFontProgramKind.None;
        }

        if (descriptor.Get("FontFile2") is not null)
        {
            return PdfEmbeddedFontProgramKind.FontFile2;
        }

        if (descriptor.Get("FontFile3") is not null)
        {
            return PdfEmbeddedFontProgramKind.FontFile3;
        }

        return descriptor.Get("FontFile") is not null
            ? PdfEmbeddedFontProgramKind.FontFile
            : PdfEmbeddedFontProgramKind.None;
    }

    /// <summary>Reads the kind of font a font dictionary states.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <returns>The kind, with a composite font resolved to the descendant it points at.</returns>
    public static PdfFontKind Kind(PdfFileReader reader, PdfDictionary font)
    {
        var subtype = font.GetName("Subtype");

        if (string.Equals(subtype, "Type0", StringComparison.Ordinal)
            && Descendant(reader, font)?.GetName("Subtype") is { Length: > 0 } descendantSubtype)
        {
            subtype = descendantSubtype;
        }

        return subtype switch
        {
            "Type1" => PdfFontKind.Type1,
            "MMType1" => PdfFontKind.MMType1,
            "Type3" => PdfFontKind.Type3,
            "TrueType" => PdfFontKind.TrueType,
            "Type0" => PdfFontKind.Type0,
            "CIDFontType0" => PdfFontKind.CIDFontType0,
            "CIDFontType2" => PdfFontKind.CIDFontType2,
            _ => PdfFontKind.Unknown,
        };
    }

    /// <summary>Reads the encoding a font states.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <returns>
    /// The name of the encoding, <c>custom</c> for an encoding dictionary that states no base encoding, or
    /// <see langword="null"/> when the font states none.
    /// </returns>
    public static string? Encoding(PdfFileReader reader, PdfDictionary font)
    {
        return reader.Resolve(font.Get("Encoding")) switch
        {
            PdfName name => name.Value,
            PdfDictionary dictionary => dictionary.GetName("BaseEncoding") ?? "custom",
            _ => null,
        };
    }

    /// <summary>Reads the name a font is reported under.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <param name="resourceName">The resource name the page selects the font with.</param>
    /// <returns>The base font name, or the resource name when the font states none.</returns>
    public static string BaseFont(PdfFileReader reader, PdfDictionary font, string resourceName)
    {
        if (font.GetName("BaseFont") is { Length: > 0 } name)
        {
            return name;
        }

        return Descendant(reader, font)?.GetName("BaseFont") is { Length: > 0 } descendantName
            ? descendantName
            : resourceName;
    }

    /// <summary>Determines whether a base font name carries the subset tag the specification defines.</summary>
    /// <param name="baseFont">The base font name.</param>
    /// <returns><see langword="true"/> when the name starts with six capital letters and a plus sign.</returns>
    public static bool IsSubset(string? baseFont) =>
        baseFont is { Length: >= 8 } && baseFont[6] == '+' && baseFont[..6].All(char.IsAsciiLetterUpper);

    /// <summary>Resolves the descendant of a composite font.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <returns>The descendant dictionary, or <see langword="null"/> when the font states none.</returns>
    private static PdfDictionary? Descendant(PdfFileReader reader, PdfDictionary font) =>
        reader.ResolveArray(font.Get("DescendantFonts")) is { Items.Count: > 0 } descendants
            ? reader.ResolveDictionary(descendants.Items[0])
            : null;
}
