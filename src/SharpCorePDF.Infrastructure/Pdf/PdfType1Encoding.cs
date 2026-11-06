using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Reads the encoding a Type1 font program declares in its clear-text header.
/// </summary>
/// <remarks>
/// <para>
/// A Type1 font may carry its own encoding in the part of the program that precedes <c>eexec</c>: a run of
/// <c>dup &lt;code&gt; /&lt;glyph&gt; put</c> lines, or a statement that the encoding is
/// <c>StandardEncoding</c>. A pdfTeX subset of a Computer Modern symbol or math font states no
/// <c>/Encoding</c> and no <c>/ToUnicode</c> in its dictionary at all, so the dictionary alone cannot say
/// what its codes mean. Without the program's table those codes map to nothing and the text they draw is
/// lost even though the document states it.
/// </para>
/// <para>
/// Only the clear-text header is read: the encrypted part of the program is what a renderer needs to draw a
/// glyph, and this library does not draw. A program whose header states no encoding yields
/// <see langword="null"/> and the caller keeps the table its dictionary implied.
/// </para>
/// </remarks>
internal static class PdfType1Encoding
{
    /// <summary>The largest clear-text header that is searched, as a bound on a hostile program.</summary>
    private const int MaximumHeaderBytes = 65_536;

    /// <summary>The marker that ends the clear-text header of a Type1 program.</summary>
    private const string EncryptedSectionMarker = "eexec";

    /// <summary>Reads the built-in encoding of the Type1 program a font dictionary embeds.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="fontDictionary">The font dictionary.</param>
    /// <returns>
    /// The table, keyed by code, or <see langword="null"/> when the font embeds no Type1 program or its
    /// header states no encoding this library can read.
    /// </returns>
    public static string?[]? TryRead(PdfFileReader reader, PdfDictionary fontDictionary)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(fontDictionary);

        if (reader.ResolveDictionary(fontDictionary.Get("FontDescriptor")) is not { } descriptor
            || reader.Resolve(descriptor.Get("FontFile")) is not PdfStream program)
        {
            return null;
        }

        byte[] bytes;

        try
        {
            bytes = PdfFilters.Decode(program, reader.SourceName);
        }
        catch (PdfUnsupportedFeatureException)
        {
            return null;
        }
        catch (PdfSourceFormatException)
        {
            return null;
        }

        return Parse(bytes);
    }

    /// <summary>Parses the clear-text header of a Type1 program.</summary>
    /// <param name="program">The bytes of the program.</param>
    /// <returns>The table, or <see langword="null"/> when the header states no encoding.</returns>
    internal static string?[]? Parse(byte[] program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var length = Math.Min(program.Length, MaximumHeaderBytes);
        var header = Encoding.Latin1.GetString(program, 0, length);
        var encrypted = header.IndexOf(EncryptedSectionMarker, StringComparison.Ordinal);

        if (encrypted >= 0)
        {
            header = header[..encrypted];
        }

        // A program that says its encoding is StandardEncoding states nothing of its own.
        if (header.Contains("StandardEncoding def", StringComparison.Ordinal))
        {
            return PdfGlyphEncodings.TryGetBaseEncoding("StandardEncoding", out var standard)
                ? standard
                : null;
        }

        var table = new string?[256];
        var mapped = false;
        var index = 0;

        // The header lists its encoding as "dup <code> /<glyph> put". Anything else on a dup line — a
        // ".notdef" filler, a name this library does not know — leaves that code unmapped.
        while ((index = header.IndexOf("dup ", index, StringComparison.Ordinal)) >= 0)
        {
            var cursor = index + 4;
            var start = cursor;

            while (cursor < header.Length && char.IsAsciiDigit(header[cursor]))
            {
                cursor++;
            }

            if (cursor == start
                || !int.TryParse(header.AsSpan(start, cursor - start), NumberStyles.None, CultureInfo.InvariantCulture, out var code)
                || code is < 0 or > 255
                || cursor >= header.Length
                || header[cursor] != ' ')
            {
                index = cursor;
                continue;
            }

            cursor++;

            if (cursor >= header.Length || header[cursor] != '/')
            {
                index = cursor;
                continue;
            }

            cursor++;

            var nameStart = cursor;

            while (cursor < header.Length && !char.IsWhiteSpace(header[cursor]))
            {
                cursor++;
            }

            if (PdfGlyphEncodings.FromGlyphName(header[nameStart..cursor]) is { } text)
            {
                table[code] = text.ToString();
                mapped = true;
            }

            index = cursor;
        }

        return mapped ? table : null;
    }
}
