namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The character sets a simple font can be encoded in, and the glyph names a <c>/Differences</c> array
/// uses in place of them.
/// </summary>
/// <remarks>
/// <para>
/// A simple font draws one byte per glyph and states which character set the byte means. Two of the
/// three sets the specification names are reproduced here exactly — <c>WinAnsiEncoding</c>, which is what
/// every Latin producer uses, and <c>StandardEncoding</c>, the specification's default — and a font that
/// states any other set is reported as unreadable rather than mapped through a table that would be
/// close but wrong. Text that is guessed is worse than text that is missing: nothing downstream can tell
/// the two apart.
/// </para>
/// <para>
/// <c>/Differences</c> names glyphs one by one, which is how a subset font states that its code 3 is
/// called <c>eacute</c>. The names are resolved through the standard glyph-name list below and through
/// the <c>uniXXXX</c> and <c>uXXXX</c> conventions, which is where the names come from when a font is
/// converted from a format that numbers its glyphs.
/// </para>
/// </remarks>
internal static class PdfGlyphEncodings
{
    /// <summary>The number of codes a simple font's encoding table holds.</summary>
    public const int TableLength = 256;

    private const string WinAnsiHigh =
        "\u20AC\uFFFD\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\uFFFD\u017D\uFFFD"
        + "\uFFFD\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\uFFFD\u017E\u0178";

    private static readonly Dictionary<string, char> GlyphNames = new(StringComparer.Ordinal)
    {
        ["space"] = ' ',
        ["exclam"] = '!',
        ["quotedbl"] = '"',
        ["numbersign"] = '#',
        ["dollar"] = '$',
        ["percent"] = '%',
        ["ampersand"] = '&',
        ["quotesingle"] = '\'',
        ["parenleft"] = '(',
        ["parenright"] = ')',
        ["asterisk"] = '*',
        ["plus"] = '+',
        ["comma"] = ',',
        ["hyphen"] = '-',
        ["period"] = '.',
        ["slash"] = '/',
        ["zero"] = '0',
        ["one"] = '1',
        ["two"] = '2',
        ["three"] = '3',
        ["four"] = '4',
        ["five"] = '5',
        ["six"] = '6',
        ["seven"] = '7',
        ["eight"] = '8',
        ["nine"] = '9',
        ["colon"] = ':',
        ["semicolon"] = ';',
        ["less"] = '<',
        ["equal"] = '=',
        ["greater"] = '>',
        ["question"] = '?',
        ["at"] = '@',
        ["bracketleft"] = '[',
        ["backslash"] = '\\',
        ["bracketright"] = ']',
        ["asciicircum"] = '^',
        ["underscore"] = '_',
        ["grave"] = '`',
        ["braceleft"] = '{',
        ["bar"] = '|',
        ["braceright"] = '}',
        ["asciitilde"] = '~',
        ["quoteleft"] = '\u2018',
        ["quoteright"] = '\u2019',
        ["quotedblleft"] = '\u201C',
        ["quotedblright"] = '\u201D',
        ["quotesinglbase"] = '\u201A',
        ["quotedblbase"] = '\u201E',
        ["guilsinglleft"] = '\u2039',
        ["guilsinglright"] = '\u203A',
        ["guillemotleft"] = '\u00AB',
        ["guillemotright"] = '\u00BB',
        ["endash"] = '\u2013',
        ["emdash"] = '\u2014',
        ["bullet"] = '\u2022',
        ["ellipsis"] = '\u2026',
        ["dagger"] = '\u2020',
        ["daggerdbl"] = '\u2021',
        ["trademark"] = '\u2122',
        ["copyright"] = '\u00A9',
        ["registered"] = '\u00AE',
        ["section"] = '\u00A7',
        ["paragraph"] = '\u00B6',
        ["degree"] = '\u00B0',
        ["plusminus"] = '\u00B1',
        ["multiply"] = '\u00D7',
        ["divide"] = '\u00F7',
        ["mu"] = '\u00B5',
        ["periodcentered"] = '\u00B7',
        ["middot"] = '\u00B7',
        ["nbspace"] = '\u00A0',
        ["nonbreakingspace"] = '\u00A0',
        ["cent"] = '\u00A2',
        ["sterling"] = '\u00A3',
        ["yen"] = '\u00A5',
        ["Euro"] = '\u20AC',
        ["florin"] = '\u0192',
        ["exclamdown"] = '\u00A1',
        ["questiondown"] = '\u00BF',
        ["acute"] = '\u00B4',
        ["circumflex"] = '\u02C6',
        ["tilde"] = '\u02DC',
        ["dieresis"] = '\u00A8',
        ["macron"] = '\u00AF',
        ["cedilla"] = '\u00B8',
        ["ring"] = '\u02DA',
        ["germandbls"] = '\u00DF',
        ["ae"] = '\u00E6',
        ["AE"] = '\u00C6',
        ["oe"] = '\u0153',
        ["OE"] = '\u0152',
        ["oslash"] = '\u00F8',
        ["Oslash"] = '\u00D8',
        ["dotlessi"] = '\u0131',
        ["lslash"] = '\u0142',
        ["Lslash"] = '\u0141',
        ["fi"] = '\uFB01',
        ["fl"] = '\uFB02',
        ["ff"] = '\uFB00',
        ["ffi"] = '\uFB03',
        ["ffl"] = '\uFB04',
        ["minus"] = '\u2212',
        ["fraction"] = '\u2044',
        ["currency"] = '\u00A4',
        ["brokenbar"] = '\u00A6',
        ["ordfeminine"] = '\u00AA',
        ["ordmasculine"] = '\u00BA',
        ["logicalnot"] = '\u00AC',
        ["onesuperior"] = '\u00B9',
        ["twosuperior"] = '\u00B2',
        ["threesuperior"] = '\u00B3',
        ["onequarter"] = '\u00BC',
        ["onehalf"] = '\u00BD',
        ["threequarters"] = '\u00BE',
        ["Agrave"] = '\u00C0',
        ["Aacute"] = '\u00C1',
        ["Acircumflex"] = '\u00C2',
        ["Atilde"] = '\u00C3',
        ["Adieresis"] = '\u00C4',
        ["Aring"] = '\u00C5',
        ["Ccedilla"] = '\u00C7',
        ["Egrave"] = '\u00C8',
        ["Eacute"] = '\u00C9',
        ["Ecircumflex"] = '\u00CA',
        ["Edieresis"] = '\u00CB',
        ["Igrave"] = '\u00CC',
        ["Iacute"] = '\u00CD',
        ["Icircumflex"] = '\u00CE',
        ["Idieresis"] = '\u00CF',
        ["Eth"] = '\u00D0',
        ["Ntilde"] = '\u00D1',
        ["Ograve"] = '\u00D2',
        ["Oacute"] = '\u00D3',
        ["Ocircumflex"] = '\u00D4',
        ["Otilde"] = '\u00D5',
        ["Odieresis"] = '\u00D6',
        ["Yacute"] = '\u00DD',
        ["Thorn"] = '\u00DE',
        ["agrave"] = '\u00E0',
        ["aacute"] = '\u00E1',
        ["acircumflex"] = '\u00E2',
        ["atilde"] = '\u00E3',
        ["adieresis"] = '\u00E4',
        ["aring"] = '\u00E5',
        ["ccedilla"] = '\u00E7',
        ["egrave"] = '\u00E8',
        ["eacute"] = '\u00E9',
        ["ecircumflex"] = '\u00EA',
        ["edieresis"] = '\u00EB',
        ["igrave"] = '\u00EC',
        ["iacute"] = '\u00ED',
        ["icircumflex"] = '\u00EE',
        ["idieresis"] = '\u00EF',
        ["eth"] = '\u00F0',
        ["ntilde"] = '\u00F1',
        ["ograve"] = '\u00F2',
        ["oacute"] = '\u00F3',
        ["ocircumflex"] = '\u00F4',
        ["otilde"] = '\u00F5',
        ["odieresis"] = '\u00F6',
        ["yacute"] = '\u00FD',
        ["thorn"] = '\u00FE',
        ["ydieresis"] = '\u00FF',
        ["Ydieresis"] = '\u0178',
    };

    /// <summary>Builds the table of a base encoding.</summary>
    /// <param name="name">The encoding name, without the leading slash, or <see langword="null"/>.</param>
    /// <param name="table">The table of 256 entries, when the encoding is one this library knows.</param>
    /// <returns>
    /// <see langword="true"/> when the encoding is known; <see langword="false"/> for one this library
    /// does not implement, which the caller reports instead of guessing at.
    /// </returns>
    /// <remarks>
    /// A font that states no encoding is read as <c>WinAnsiEncoding</c>. The specification's default is
    /// the font's built-in encoding, which for the standard fonts is <c>StandardEncoding</c>; the two
    /// agree on every code below 127, and for the codes above it a producer that states no encoding is
    /// almost always writing Windows text, so the practical reading is the one taken here.
    /// </remarks>
    public static bool TryGetBaseEncoding(string? name, out string?[] table)
    {
        switch (name)
        {
            case null or "" or "WinAnsiEncoding":
                table = BuildWinAnsi();
                return true;

            case "StandardEncoding":
                table = BuildStandard();
                return true;

            default:
                table = [];
                return false;
        }
    }

    /// <summary>Resolves a glyph name to the character it names.</summary>
    /// <param name="name">The glyph name, without the leading slash.</param>
    /// <returns>The character, or <see langword="null"/> when the name is not one this library knows.</returns>
    public static char? FromGlyphName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 1)
        {
            return name[0];
        }

        if (GlyphNames.TryGetValue(name, out var character))
        {
            return character;
        }

        if (name.StartsWith("uni", StringComparison.Ordinal)
            && name.Length >= 7
            && ushort.TryParse(
                name.AsSpan(3, 4),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var codePoint))
        {
            return (char)codePoint;
        }

        if (name.StartsWith('u') && name.Length is >= 5 and <= 7
            && ushort.TryParse(
                name.AsSpan(1),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var shortCodePoint))
        {
            return (char)shortCodePoint;
        }

        return null;
    }

    /// <summary>Builds the <c>WinAnsiEncoding</c> table, which is Windows-1252.</summary>
    /// <returns>The table of 256 entries.</returns>
    private static string?[] BuildWinAnsi()
    {
        var table = new string?[TableLength];

        for (var code = 32; code < 127; code++)
        {
            table[code] = ((char)code).ToString();
        }

        for (var offset = 0; offset < WinAnsiHigh.Length; offset++)
        {
            var character = WinAnsiHigh[offset];

            if (character != '\uFFFD')
            {
                table[0x80 + offset] = character.ToString();
            }
        }

        for (var code = 0xA0; code < TableLength; code++)
        {
            table[code] = ((char)code).ToString();
        }

        return table;
    }

    /// <summary>Builds the <c>StandardEncoding</c> table.</summary>
    /// <returns>The table of 256 entries.</returns>
    /// <remarks>
    /// Only the printable ASCII range is reproduced. The codes above it name typographic characters in
    /// an order no other encoding shares, and a font that needs them states <c>/Differences</c> or a
    /// <c>/ToUnicode</c> map, so a code without a table entry is reported rather than guessed.
    /// </remarks>
    private static string?[] BuildStandard()
    {
        var table = new string?[TableLength];

        for (var code = 32; code < 127; code++)
        {
            table[code] = ((char)code).ToString();
        }

        table[0x27] = "\u2019";
        table[0x60] = "\u2018";

        return table;
    }
}
