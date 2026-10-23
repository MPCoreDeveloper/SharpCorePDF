using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// Turns the glyphs a page draws into the letters a consumer of the layout reads: one letter per glyph,
/// with the baseline and the direction the glyph alone does not state.
/// </summary>
/// <remarks>
/// <para>
/// The decoding is not repeated here. <see cref="Pdf.PdfTextCollector"/> has already walked the content,
/// resolved the fonts through <see cref="Pdf.PdfFontDecoder"/> and mapped every code through the font's
/// encoding or its <c>/ToUnicode</c> map; this class is a projection of that result, which is what keeps
/// the letter surface and the text surface telling the same story about the same document.
/// </para>
/// <para>
/// A letter's baseline runs from the glyph origin to that origin advanced by the width the document
/// states, and its direction follows from the two: a glyph whose advance is negative runs right to left.
/// Nothing is inferred from the character itself, so a right-to-left script in a left-to-right placement
/// is reported as the page draws it.
/// </para>
/// </remarks>
internal static class LetterExtractor
{
    /// <summary>Projects a glyph read into a letter read, keeping the order and the warnings.</summary>
    /// <param name="glyphs">The glyphs and the remarks of the read that produced them.</param>
    /// <returns>The letters of the same pages, in the same order, with the same remarks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glyphs"/> is <see langword="null"/>.</exception>
    public static PdfReadResult<PdfLetter> Extract(PdfReadResult<PdfGlyph> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        return new()
        {
            Items = [.. glyphs.Items.Select(ToLetter)],
            Warnings = glyphs.Warnings,
        };
    }

    /// <summary>Projects one glyph into one letter.</summary>
    /// <param name="glyph">The glyph to project.</param>
    /// <returns>The letter.</returns>
    /// <remarks>
    /// A glyph's own box assumes the advance runs to the right, which leaves a right-to-left glyph with an
    /// empty box. The letter takes the area between its two baseline points instead, so a box always says
    /// how much of the page the letter covers.
    /// </remarks>
    public static PdfLetter ToLetter(PdfGlyph glyph) => new()
    {
        Value = glyph.Text,
        PageNumber = glyph.PageNumber,
        FontSize = glyph.FontSize,

        // The base font is the name a reader recognises — Helvetica, not F1 — and the resource name is
        // what remains when the document declares no base font at all.
        FontName = glyph.BaseFont ?? glyph.FontName,
        BoundingBox = glyph.Width >= 0d
            ? glyph.BoundingBox
            : new PdfRectangle(
                glyph.X + glyph.Width,
                glyph.BoundingBox.Bottom,
                glyph.X,
                glyph.BoundingBox.Top),
        StartBaseLine = new PdfPoint(glyph.X, glyph.Y),
        EndBaseLine = new PdfPoint(glyph.X + glyph.Width, glyph.Y),
    };
}
