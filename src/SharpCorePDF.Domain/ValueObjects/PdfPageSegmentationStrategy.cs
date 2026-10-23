namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// How a layout read groups the words of a page into the blocks it reports.
/// </summary>
/// <remarks>
/// <para>
/// A strategy never changes the glyph data; it only groups it. The raw order is always available and is the
/// baseline a caller compares a segmentation against, because it follows the document exactly as it was
/// written; every other strategy is this library's reading of the page, and two of them can disagree about
/// a page without either being wrong.
/// </para>
/// <para>
/// <see cref="RecursiveXYCut"/> and <see cref="Docstrum"/> are the two classic page-segmentation
/// algorithms: the first splits a page along the whitespace that runs the full width or height of a region,
/// the second groups lines by the distance between them. The first is the default because it separates
/// columns and headings before it looks at line spacing, which is the order a page usually states.
/// </para>
/// <para>
/// The set is open. A later release may add a strategy, which is a source-compatible addition only for a
/// caller that keeps a <c>default</c> arm in its <c>switch</c>.
/// </para>
/// </remarks>
public enum PdfPageSegmentationStrategy
{
    /// <summary>
    /// Group the words in the order the content stream draws them, starting a block wherever the drawing
    /// stops following a line down the page. It is the only segmentation that reproduces a producer
    /// exactly, which makes it the baseline of a comparison rather than the reading of a page.
    /// </summary>
    Raw = 0,

    /// <summary>
    /// Group the words by their place on the page: lines top to bottom, paragraphs as the runs of lines
    /// that follow one another with the leading of a paragraph. This is the grouping the text read of the
    /// content surface uses.
    /// </summary>
    NearestNeighbour = 1,

    /// <summary>
    /// Split the page recursively along the whitespace that separates two groups of words: the widest
    /// vertical gutter first — which separates columns — and then the widest horizontal one, until a region
    /// admits no split. It finds columns and headings without knowing what a column is.
    /// </summary>
    RecursiveXYCut = 2,

    /// <summary>
    /// Group the lines of a page by the distance between them: lines that sit closer together than the
    /// page's own line spacing become one block, and a gutter wider than that spacing keeps two blocks
    /// apart. It reads a page of uneven leading better than a fixed threshold and a page of two columns
    /// only when the columns do not share lines.
    /// </summary>
    Docstrum = 3,
}
