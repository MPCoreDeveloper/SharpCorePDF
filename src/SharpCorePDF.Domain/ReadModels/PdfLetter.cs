using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One glyph a page draws, read as a letter: the character it stands for, the font and size it is drawn
/// with, the box it covers and the baseline it runs along.
/// </summary>
/// <remarks>
/// <para>
/// A letter is the read at its most literal — one entry per glyph, in the order the content stream draws
/// them — and the level at which a caller keeps its own typography: a search index that must not lose a
/// hyphen, a form of analysis that groups by a rule this library does not implement, a viewer that
/// overlays the text of a page. <see cref="PdfGlyph"/> carries the same glyph for the grouping the reader
/// performs; a letter is that glyph shaped for a consumer that wants the baseline and the direction of
/// the run rather than the em box alone.
/// </para>
/// <para>
/// <see cref="BoundingBox"/> and the two baseline points are in page space — the space
/// <see cref="PdfRectangle"/> documents, with the origin at the lower left corner of the page before the
/// rotation a viewer applies — so a caller maps them the way it maps any other page coordinate. They are
/// not the top-left coordinates the authoring surface uses; the exports translate between the two.
/// </para>
/// <para>
/// A glyph the font cannot be mapped through is not reported at all and adds a
/// <see cref="PdfWarningKind.TextUnreadable"/> warning to the result, so a page whose font is unreadable
/// reads as no letters plus that warning rather than as a page of invented characters (NFR-7.8).
/// </para>
/// </remarks>
public sealed record PdfLetter
{
    /// <summary>
    /// Gets the Unicode text of the letter: one character, or a short sequence when the font expands a
    /// code to several, as a ligature does.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>Gets the one-based number of the page the letter is drawn on.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Gets the size of the em box of the font the letter is drawn with, in points.</summary>
    public required double FontSize { get; init; }

    /// <summary>
    /// Gets the name of the font the letter is drawn with, as the document declares it — for example
    /// <c>Helvetica</c> — or the resource name when it declares none, or <see langword="null"/> when the
    /// letter was drawn without a named font.
    /// </summary>
    public string? FontName { get; init; }

    /// <summary>Gets the area the letter covers, in page space.</summary>
    public required PdfRectangle BoundingBox { get; init; }

    /// <summary>
    /// Gets the point where the letter starts on its baseline, in page space: the glyph origin, which is
    /// where a viewer begins drawing it.
    /// </summary>
    public required PdfPoint StartBaseLine { get; init; }

    /// <summary>
    /// Gets the point where the letter ends on its baseline, in page space: its origin advanced by the
    /// width the document states, which is also where the next letter begins.
    /// </summary>
    public required PdfPoint EndBaseLine { get; init; }

    /// <summary>
    /// Gets the direction the letter runs in, derived from its baseline: a letter whose end sits to the
    /// left of its start runs right to left, and every other letter left to right.
    /// </summary>
    /// <remarks>
    /// Only the horizontal sense is derived. A letter drawn along a vertical or rotated baseline keeps
    /// the direction of its horizontal component, because the glyph placement is the part of the read
    /// that describes such a page and not this value.
    /// </remarks>
    public PdfTextDirection TextDirection =>
        EndBaseLine.X >= StartBaseLine.X ? PdfTextDirection.LeftToRight : PdfTextDirection.RightToLeft;

    /// <summary>Gets a value indicating whether the letter paints no ink, such as a space or a tab.</summary>
    public bool IsWhitespace => string.IsNullOrWhiteSpace(Value);

    /// <summary>Gets the advance width of the letter, in points, as the distance between its two baseline points.</summary>
    public double Width => EndBaseLine.X - StartBaseLine.X;

    /// <summary>Formats the letter for diagnostics.</summary>
    /// <returns>The text, the font, the origin and the size.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"letter '{Value}' at {StartBaseLine} {FontSize}pt of {FontName ?? "an unnamed font"} on page {PageNumber}");
}
