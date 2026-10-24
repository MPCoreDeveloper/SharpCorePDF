using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// The reverse table of <c>WinAnsiEncoding</c>: the code a character is written with.
/// </summary>
/// <remarks>
/// One table serves both halves of the engine that need it — the writer, which turns a character
/// into a code, and <see cref="PdfTypeface.Measure(string, double)"/>, which turns a character into a width — so a
/// character cannot be measured as one glyph and drawn as another.
/// </remarks>
internal static class WinAnsiEncoding
{
    private static readonly Lazy<Dictionary<char, byte>> Codes = new(Build);

    /// <summary>Finds the code a character is written with.</summary>
    /// <param name="character">The character.</param>
    /// <param name="code">The code, when the encoding has one.</param>
    /// <returns><see langword="true"/> when the character is expressible in the encoding.</returns>
    public static bool TryGetCode(char character, out byte code) => Codes.Value.TryGetValue(character, out code);

    /// <summary>Builds the reverse table from the base encoding the engine states.</summary>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidOperationException">The engine cannot build the table it claims to know.</exception>
    private static Dictionary<char, byte> Build()
    {
        if (!Pdf.PdfGlyphEncodings.TryGetBaseEncoding("WinAnsiEncoding", out var table))
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
