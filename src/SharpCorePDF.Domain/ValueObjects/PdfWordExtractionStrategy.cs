namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// How a read groups the letters of a page into words.
/// </summary>
/// <remarks>
/// <para>
/// A strategy never changes the glyph data; it only groups it. The raw order is always available and is
/// the baseline a caller compares a layout strategy against, because it is the only grouping that follows
/// the document exactly as it was written.
/// </para>
/// <para>
/// The set is open. A later release may add a strategy, which is a source-compatible addition only for a
/// caller that keeps a <c>default</c> arm in its <c>switch</c>.
/// </para>
/// </remarks>
public enum PdfWordExtractionStrategy
{
    /// <summary>
    /// Group the letters in the order the content stream draws them. A word is a run of letters that
    /// follow one another on one baseline, which is what a producer that writes text in reading order
    /// means; a page that draws its words out of order reads out of order too.
    /// </summary>
    Raw = 0,

    /// <summary>
    /// Group the letters by their place on the page: a line is read top to bottom and a word left to
    /// right inside it, whatever order the content stream used to draw them. This is the default,
    /// because a search wants the page's order and not the producer's.
    /// </summary>
    NearestNeighbour = 1,
}
