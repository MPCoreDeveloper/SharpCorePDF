using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The face a page sets text in: one of the twelve standard PDF fonts, which needs no font program, or
/// a TrueType font program embedded in the document.
/// </summary>
/// <remarks>
/// <para>
/// A standard font is written with <c>/WinAnsiEncoding</c>. It keeps the document small and needs no
/// font file, but text must be expressible in Windows-1252 — Latin-1 plus the typographic characters
/// such as the curly apostrophe and the euro sign.
/// </para>
/// <para>
/// A TrueType typeface carries its font program in the document, subsetted to the glyphs the pages use,
/// with a <c>/ToUnicode</c> map so the text can be extracted again. Nothing is read from the machine the
/// code runs on: the bytes the caller supplies are the only source of the face. An instance is immutable
/// and can be shared by any number of builders.
/// </para>
/// </remarks>
public sealed class PdfTypeface
{
    private readonly TrueTypeFont? embedded;

    private PdfTypeface(StandardFont family) => Family = family;

    private PdfTypeface(TrueTypeFont font)
    {
        embedded = font;
        FontProgram = font.Data;
    }

    /// <summary>Gets the standard family, or <see langword="null"/> when this is an embedded font.</summary>
    public StandardFont? Family { get; }

    /// <summary>Gets a value indicating whether the face carries its own font program.</summary>
    public bool IsEmbedded => embedded is not null;

    /// <summary>Gets the name of the face, for diagnostics.</summary>
    public string Name => embedded?.Name ?? Family?.ToString() ?? "font";

    /// <summary>Gets the bytes of the embedded font program, or <see langword="null"/> for a standard font.</summary>
    public ReadOnlyMemory<byte>? FontProgram { get; }

    /// <summary>Gets the vertical metrics of the face, in font units.</summary>
    /// <remarks>
    /// A standard font states its metrics in units of 1/1000 em, which is the <c>UnitsPerEm</c> this
    /// property reports for it; an embedded program states its own. Nothing is read from the machine
    /// the code runs on: the numbers come from the font program or from the AFM data of the standard
    /// font, so the same text measures the same on every platform.
    /// </remarks>
    public PdfTextMetrics Metrics =>
        embedded is not null
            ? new PdfTextMetrics(
                embedded.Ascender,
                embedded.Descender,
                embedded.LineGap,
                embedded.CapHeight,
                embedded.XHeight,
                embedded.UnitsPerEm)
            : StandardMetrics(Family!.Value);

    /// <summary>Measures a line of text set in this face.</summary>
    /// <param name="text">The text to measure; it must not contain a line break.</param>
    /// <param name="fontSize">The size the text would be set at, in points.</param>
    /// <returns>The advance width of the text, in points.</returns>
    /// <remarks>
    /// The width is the sum of the advance widths of the characters, which is where a rendered line
    /// ends before any character spacing, word spacing or horizontal scaling is applied: those are
    /// text state a caller states once and the writer sets, not part of the face.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not finite or not greater than zero.</exception>
    /// <exception cref="ArgumentException">The face has no glyph or code for a character of the text.</exception>
    public double Measure(string text, double fontSize)
    {
        ValidateFontSize(fontSize);

        return MeasureInEms(text) * fontSize;
    }

    /// <summary>Measures a line of text, including the spacing it would be drawn with.</summary>
    /// <param name="text">The text to measure; it must not contain a line break.</param>
    /// <param name="fontSize">The size the text would be set at, in points.</param>
    /// <param name="characterSpacing">Extra space after every glyph, in points.</param>
    /// <param name="wordSpacing">Extra space after every space character, in points.</param>
    /// <returns>The advance width of the text, in points, as the writer sets it.</returns>
    /// <remarks>
    /// This is the width a reader sees, not the width of the glyphs: PDF adds the character spacing
    /// after every glyph and the word spacing after every space code, so a caller that sets either
    /// has to ask for the same numbers it draws with.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size or a spacing is not finite, or the size is not greater than zero.</exception>
    /// <exception cref="ArgumentException">The face has no glyph or code for a character of the text.</exception>
    public double Measure(string text, double fontSize, double characterSpacing, double wordSpacing)
    {
        ValidateFontSize(fontSize);

        if (!double.IsFinite(characterSpacing))
        {
            throw new ArgumentOutOfRangeException(nameof(characterSpacing), characterSpacing, "A character spacing must be finite.");
        }

        if (!double.IsFinite(wordSpacing))
        {
            throw new ArgumentOutOfRangeException(nameof(wordSpacing), wordSpacing, "A word spacing must be finite.");
        }

        var glyphs = 0;
        var spaces = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            glyphs++;
            spaces += rune.Value == ' ' ? 1 : 0;
        }

        return (MeasureInEms(text) * fontSize) + (glyphs * characterSpacing) + (spaces * wordSpacing);
    }

    /// <summary>Gets the parsed program of an embedded face.</summary>
    internal TrueTypeFont Embedded =>
        embedded ?? throw new InvalidOperationException("This typeface is a standard font and has no font program.");

    /// <summary>Creates a standard typeface.</summary>
    /// <param name="family">The standard font to use.</param>
    /// <returns>The typeface.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="family"/> is not a defined standard font.</exception>
    public static PdfTypeface Standard(StandardFont family)
    {
        if (!Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(nameof(family), family, "The standard font is not one the engine defines.");
        }

        return new PdfTypeface(family);
    }

    /// <summary>Creates a typeface from a TrueType font program.</summary>
    /// <param name="fontProgram">The bytes of the TrueType (or sfnt wrapped TrueType) file.</param>
    /// <returns>The typeface.</returns>
    /// <remarks>
    /// The bytes are copied and parsed immediately, so a caller may reuse or change its buffer, and a
    /// malformed program is reported where it was handed over rather than at the first page that uses
    /// it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="fontProgram"/> is empty or not a TrueType font.</exception>
    /// <exception cref="NotSupportedException">The program uses PostScript outlines.</exception>
    public static PdfTypeface FromTrueType(ReadOnlyMemory<byte> fontProgram)
    {
        if (fontProgram.IsEmpty)
        {
            throw new ArgumentException("A font program is needed to create an embedded typeface.", nameof(fontProgram));
        }

        var bytes = fontProgram.ToArray();

        return new PdfTypeface(TrueTypeFont.Parse(bytes));
    }

    /// <summary>Sums the advance widths of a line of text, as a fraction of the em.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The width in ems.</returns>
    /// <exception cref="ArgumentException">The face has no glyph or code for a character of the text.</exception>
    private double MeasureInEms(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return 0d;
        }

        if (embedded is not null)
        {
            long units = 0;

            foreach (var rune in text.EnumerateRunes())
            {
                if (!embedded.TryGetGlyph(rune.Value, out var glyph))
                {
                    throw new ArgumentException(
                        $"The font '{embedded.Name}' has no glyph for '{rune}' (U+{rune.Value:X4}). Supply a font that covers it, or use a different character.",
                        nameof(text));
                }

                units += embedded.GetAdvanceWidth(glyph);
            }

            return (double)units / embedded.UnitsPerEm;
        }

        var family = Family!.Value;
        var widths = StandardFontMetrics.Widths(family);
        long thousandths = 0;

        foreach (var character in text)
        {
            if (!WinAnsiEncoding.TryGetCode(character, out var code))
            {
                throw new ArgumentException(
                    $"The standard font {Name} is written with WinAnsiEncoding, which has no code for '{character}' (U+{(int)character:X4}). Use an embedded TrueType font for text outside that encoding.",
                    nameof(text));
            }

            thousandths += widths[code];
        }

        return thousandths / 1000d;
    }

    /// <summary>Builds the metrics of a standard font, whose em is one thousand units.</summary>
    /// <param name="family">The standard font.</param>
    /// <returns>The metrics, in units of 1/1000 em.</returns>
    private static PdfTextMetrics StandardMetrics(StandardFont family)
    {
        var (ascent, descent, capHeight, xHeight) = StandardFontMetrics.Get(family);

        return new PdfTextMetrics(ascent, descent, 0d, capHeight, xHeight, 1000);
    }

    /// <summary>Refuses a size that cannot describe a line of text.</summary>
    /// <param name="fontSize">The size in points.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size is not finite or not greater than zero.</exception>
    private static void ValidateFontSize(double fontSize)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "A font size must be finite and greater than zero.");
        }
    }

    /// <summary>Returns the name of the face.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
