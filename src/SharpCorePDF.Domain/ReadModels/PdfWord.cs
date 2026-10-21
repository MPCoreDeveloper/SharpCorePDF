using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// A run of glyphs a page draws as one word: glyphs that share a baseline and follow one another
/// without a gap a space would explain.
/// </summary>
/// <remarks>
/// <para>
/// The grouping is a rule of this library, not of the document: a producer is free to draw a word one
/// glyph at a time or a whole paragraph in one string, so a word is what the read made of
/// the glyphs. <see cref="Glyphs"/> keeps the original run, so a caller with its own rule — a
/// different gap threshold, hyphenation, a language that does not separate words with spaces — can
/// regroup without reading the document again.
/// </para>
/// <para>
/// A word is never empty and never whitespace: the spaces between words are what was measured to find
/// the boundaries, and they are not part of any word.
/// </para>
/// </remarks>
public sealed record PdfWord
{
    /// <summary>Gets the text of the word, in the order the glyphs are drawn.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the one-based number of the page the word is drawn on.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Gets the area the word covers, the union of the areas of its glyphs.</summary>
    public required PdfRectangle BoundingBox { get; init; }

    /// <summary>Gets the glyphs the word is made of, in drawing order. Never empty.</summary>
    public IReadOnlyList<PdfGlyph> Glyphs { get; init; } = [];

    /// <summary>Gets the name of the font resource the word is drawn with, or <see langword="null"/>.</summary>
    public string? FontName => Glyphs.Count == 0 ? null : Glyphs[0].FontName;

    /// <summary>Gets the size of the em box of the font the word is drawn with, in points.</summary>
    public double FontSize => Glyphs.Count == 0 ? 0d : Glyphs[0].FontSize;

    /// <summary>
    /// Gets the baseline the word sits on, in points. It is the baseline of the first glyph, because a
    /// word is a run that shares one baseline by definition.
    /// </summary>
    public double BaselineY => Glyphs.Count == 0 ? BoundingBox.Bottom : Glyphs[0].Y;

    /// <summary>Formats the word for diagnostics.</summary>
    /// <returns>The text and the baseline it sits on.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"word '{Text}' at y {BaselineY} on page {PageNumber}");
}
