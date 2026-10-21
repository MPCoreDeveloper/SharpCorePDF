using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One glyph a page draws: the character it stands for and where the content stream puts it, in page
/// space.
/// </summary>
/// <remarks>
/// <para>
/// A glyph is the smallest unit a PDF paints text in, and the only one that survives every producer:
/// a word is a glyph run this library groups, and a block is a group of those, so a caller that needs
/// its own typography rules can work from the glyphs and ignore the grouping (NFR-7.9).
/// </para>
/// <para>
/// Positions are in page space — the space <see cref="PdfRectangle"/> documents, with the origin at the
/// lower left corner of the page before the rotation a viewer applies. <see cref="X"/> and
/// <see cref="Y"/> are the origin of the glyph on its baseline, not the corner of an inked box,
/// because a font carries no glyph outline here: the document is read, not rendered.
/// <see cref="Width"/> is the advance the document states, in points, and <see cref="Height"/> is the
/// size of the em box. <see cref="BoundingBox"/> combines the two with the usual ascender and
/// descender proportions, which is enough to group glyphs and to overlay a page; it is not a
/// typographic bounding box.
/// </para>
/// <para>
/// <see cref="Text"/> is the Unicode text the glyph maps to, which is usually one character and
/// occasionally several: a ligature or a code that a <c>/ToUnicode</c> map expands to a sequence. A
/// glyph the font cannot be mapped through is not reported at all — the read says so through a
/// <see cref="ValueObjects.PdfWarningKind.TextUnreadable"/> warning instead of inventing a character.
/// </para>
/// </remarks>
public sealed record PdfGlyph
{
    /// <summary>
    /// Gets the Unicode text of the glyph: one character, or a short sequence when the font expands a
    /// code to several.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>Gets the one-based number of the page the glyph is drawn on.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Gets the x coordinate of the glyph origin, in points.</summary>
    public required double X { get; init; }

    /// <summary>Gets the y coordinate of the glyph origin on its baseline, in points.</summary>
    public required double Y { get; init; }

    /// <summary>Gets the advance width of the glyph, in points, as the document states it.</summary>
    public required double Width { get; init; }

    /// <summary>Gets the size of the em box of the font the glyph is drawn with, in points.</summary>
    public required double FontSize { get; init; }

    /// <summary>
    /// Gets the name of the font resource the glyph was drawn with — <c>F1</c>, not <c>Helvetica</c> —
    /// or <see langword="null"/> when the content stream names none.
    /// </summary>
    public string? FontName { get; init; }

    /// <summary>
    /// Gets the base font the resource declares, for example <c>Helvetica-Bold</c>, or
    /// <see langword="null"/> when the document states none.
    /// </summary>
    public string? BaseFont { get; init; }

    /// <summary>Gets the size of the em box of the font, in points, which is also the glyph's height.</summary>
    public double Height => FontSize;

    /// <summary>
    /// Gets the area the glyph occupies, derived from the origin, the advance and the em box. The
    /// descender is taken as a fifth of the em and the ascender as four fifths, which is the proportion
    /// most Latin fonts share and is stated here rather than read from the font program.
    /// </summary>
    public PdfRectangle BoundingBox => new(
        X,
        Y - (FontSize * 0.2d),
        X + Math.Max(0d, Width),
        Y + (FontSize * 0.8d));

    /// <summary>Gets a value indicating whether the glyph paints no ink, such as a space or a tab.</summary>
    public bool IsWhitespace => string.IsNullOrWhiteSpace(Text);

    /// <summary>Formats the glyph for diagnostics.</summary>
    /// <returns>The text, the font and the origin.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"glyph '{Text}' at ({X}, {Y}) {FontSize}pt of {FontName ?? BaseFont ?? "an unnamed font"}");
}
