namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The character sets a simple font can be encoded in, and the glyph names a <c>/Differences</c> array
/// uses in place of them.
/// </summary>
/// <remarks>
/// <para>
/// A simple font draws one byte per glyph and states which character set the byte means. Three of the four
/// sets the specification names are reproduced here exactly — <c>WinAnsiEncoding</c>, which is what every
/// Latin producer uses, <c>StandardEncoding</c>, the specification's default, and <c>MacRomanEncoding</c>,
/// which a document written on a Mac states — and a font that states <c>MacExpertEncoding</c>, or a set no
/// table here knows, is reported as unreadable rather than mapped through a table that would be close but
/// wrong. Text that is guessed is worse than text that is missing: nothing downstream can tell the two
/// apart.
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

        // The mathematical and symbol names the Computer Modern fonts give their glyphs — the fonts a
        // LaTeX produced document embeds for its formulas. Without these a Type1 program's own encoding
        // maps a code to a name this library cannot turn into text, and the formula is read as nothing.
        ["angbracketleft"] = '\u27E8',
        ["angbracketright"] = '\u27E9',
        ["braceleftbig"] = '{',
        ["bracerightbig"] = '}',
        ["element"] = '\u2208',
        ["notelement"] = '\u2209',
        ["suchthat"] = '\u220B',
        ["greaterequal"] = '\u2265',
        ["lessequal"] = '\u2264',
        ["notequal"] = '\u2260',
        ["equivalence"] = '\u2261',
        ["approxequal"] = '\u2248',
        ["similar"] = '\u223C',
        ["proportional"] = '\u221D',
        ["asteriskmath"] = '\u2217',
        ["circleplus"] = '\u2295',
        ["circlemultiply"] = '\u2297',
        ["logicaland"] = '\u2227',
        ["logicalor"] = '\u2228',
        ["intersection"] = '\u2229',
        ["union"] = '\u222A',
        ["propersubset"] = '\u2282',
        ["reflexsubset"] = '\u2286',
        ["propersuperset"] = '\u2283',
        ["reflexsuperset"] = '\u2287',
        ["summation"] = '\u2211',
        ["summationdisplay"] = '\u2211',
        ["product"] = '\u220F',
        ["coproduct"] = '\u2210',
        ["integral"] = '\u222B',
        ["contourintegral"] = '\u222E',
        ["radical"] = '\u221A',
        ["infinity"] = '\u221E',
        ["partialdiff"] = '\u2202',
        ["gradient"] = '\u2207',
        ["universal"] = '\u2200',
        ["existential"] = '\u2203',
        ["emptyset"] = '\u2205',
        ["perpendicular"] = '\u22A5',
        ["angle"] = '\u2220',
        ["arrowleft"] = '\u2190',
        ["arrowright"] = '\u2192',
        ["arrowup"] = '\u2191',
        ["arrowdown"] = '\u2193',
        ["arrowboth"] = '\u2194',
        ["arrowdblleft"] = '\u21D0',
        ["arrowdblright"] = '\u21D2',
        ["arrowdblboth"] = '\u21D4',
        ["aleph"] = '\u2135',
        ["weierstrass"] = '\u2118',
        ["Ifraktur"] = '\u2111',
        ["Rfraktur"] = '\u211C',
        ["spade"] = '\u2660',
        ["club"] = '\u2663',
        ["heart"] = '\u2665',
        ["diamond"] = '\u2666',
        ["circlemultiplydisplay"] = '\u2297',
        ["circleplusdisplay"] = '\u2295',

        // The Greek names a math font gives its letters; a formula is mostly these.
        ["alpha"] = '\u03B1',
        ["beta"] = '\u03B2',
        ["gamma"] = '\u03B3',
        ["delta"] = '\u03B4',
        ["epsilon"] = '\u03B5',
        ["epsilon1"] = '\u03F5',
        ["zeta"] = '\u03B6',
        ["eta"] = '\u03B7',
        ["theta"] = '\u03B8',
        ["theta1"] = '\u03D1',
        ["iota"] = '\u03B9',
        ["kappa"] = '\u03BA',
        ["lambda"] = '\u03BB',
        ["nu"] = '\u03BD',
        ["xi"] = '\u03BE',
        ["pi"] = '\u03C0',
        ["pi1"] = '\u03D6',
        ["rho"] = '\u03C1',
        ["rho1"] = '\u03F1',
        ["sigma"] = '\u03C3',
        ["sigma1"] = '\u03C2',
        ["tau"] = '\u03C4',
        ["upsilon"] = '\u03C5',
        ["phi"] = '\u03C6',
        ["phi1"] = '\u03D5',
        ["chi"] = '\u03C7',
        ["psi"] = '\u03C8',
        ["omega"] = '\u03C9',
        ["Gamma"] = '\u0393',
        ["Delta"] = '\u0394',
        ["Theta"] = '\u0398',
        ["Lambda"] = '\u039B',
        ["Xi"] = '\u039E',
        ["Pi"] = '\u03A0',
        ["Sigma"] = '\u03A3',
        ["Upsilon"] = '\u03A5',
        ["Phi"] = '\u03A6',
        ["Psi"] = '\u03A8',
        ["Omega"] = '\u03A9',
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
                table = WinAnsi;
                return true;

            case "StandardEncoding":
                table = Standard;
                return true;

            case "MacRomanEncoding":
                table = MacRoman;
                return true;

            default:
                table = [];
                return false;
        }
    }

    /// <summary>
    /// The <c>WinAnsiEncoding</c> table, built once: every code it maps is a constant of the
    /// specification, so a second copy would say exactly what this one says.
    /// </summary>
    /// <remarks>
    /// The table is shared by every font that states this encoding and must not be changed. The one
    /// caller that needs to change it — an encoding dictionary's <c>/Differences</c> — copies it first.
    /// Building a 256-entry table per font decoded was the alternative, and it allocated a string per
    /// mapped code to repeat what the previous font had already said.
    /// </remarks>
    private static readonly string?[] WinAnsi = BuildWinAnsi();

    /// <summary>The <c>StandardEncoding</c> table, built once, for the same reason.</summary>
    private static readonly string?[] Standard = BuildStandard();

    /// <summary>The <c>MacRomanEncoding</c> table, built once, for the same reason.</summary>
    private static readonly string?[] MacRoman = BuildMacRoman();

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

    /// <summary>Builds the <c>MacRomanEncoding</c> table, the code page Mac OS wrote text in.</summary>
    /// <returns>The table of 256 entries.</returns>
    /// <remarks>
    /// The printable ASCII range is shared with every other encoding; the upper half is Mac OS Roman,
    /// including the typographic quotes and dashes a document written on a Mac uses. Code 0xF0 is the Apple
    /// logo, which has no Unicode character, so it is left unset and reported rather than mapped to a
    /// private-use code point — the same rule the other tables follow for a character they cannot state.
    /// </remarks>
    private static string?[] BuildMacRoman()
    {
        var table = new string?[TableLength];

        for (var code = 32; code < 127; code++)
        {
            table[code] = ((char)code).ToString();
        }

        char[] high =
        [
        '\u00C4', '\u00C5', '\u00C7', '\u00C9', '\u00D1', '\u00D6', '\u00DC', '\u00E1',
        '\u00E0', '\u00E2', '\u00E4', '\u00E3', '\u00E5', '\u00E7', '\u00E9', '\u00E8',
        '\u00EA', '\u00EB', '\u00ED', '\u00EC', '\u00EE', '\u00EF', '\u00F1', '\u00F3',
        '\u00F2', '\u00F4', '\u00F6', '\u00F5', '\u00FA', '\u00F9', '\u00FB', '\u00FC',
        '\u2020', '\u00B0', '\u00A2', '\u00A3', '\u00A7', '\u2022', '\u00B6', '\u00DF',
        '\u00AE', '\u00A9', '\u2122', '\u00B4', '\u00A8', '\u2260', '\u00C6', '\u00D8',
        '\u221E', '\u00B1', '\u2264', '\u2265', '\u00A5', '\u00B5', '\u2202', '\u2211',
        '\u220F', '\u03C0', '\u222B', '\u00AA', '\u00BA', '\u03A9', '\u00E6', '\u00F8',
        '\u00BF', '\u00A1', '\u00AC', '\u221A', '\u0192', '\u2248', '\u2206', '\u00AB',
        '\u00BB', '\u2026', '\u00A0', '\u00C0', '\u00C3', '\u00D5', '\u0152', '\u0153',
        '\u2013', '\u2014', '\u201C', '\u201D', '\u2018', '\u2019', '\u00F7', '\u25CA',
        '\u00FF', '\u0178', '\u2044', '\u20AC', '\u2039', '\u203A', '\uFB01', '\uFB02',
        '\u2021', '\u00B7', '\u201A', '\u201E', '\u2030', '\u00C2', '\u00CA', '\u00C1',
        '\u00CB', '\u00C8', '\u00CD', '\u00CE', '\u00CF', '\u00CC', '\u00D3', '\u00D4',
        '\0', '\u00D2', '\u00DA', '\u00DB', '\u00D9', '\u0131', '\u02C6', '\u02DC',
        '\u00AF', '\u02D8', '\u02D9', '\u02DA', '\u00B8', '\u02DD', '\u02DB', '\u02C7',
        ];

        for (var index = 0; index < high.Length; index++)
        {
            if (high[index] != '\0')
            {
                table[0x80 + index] = high[index].ToString();
            }
        }

        return table;
    }
}
