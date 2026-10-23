using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Splits a page recursively along the whitespace that separates two groups of words: the widest vertical
/// gutter first — which is what separates columns — and then the widest horizontal one, until a region admits
/// no split.
/// </summary>
/// <remarks>
/// <para>
/// The classic XY cut, read the way the strategy is documented: it finds columns and headings without
/// knowing what a column is, because a vertical gutter that runs the full height of a region is what a
/// column boundary looks like from the geometry alone. The horizontal cut then separates headings and
/// paragraphs from the blocks below them.
/// </para>
/// <para>
/// The cut is bounded in depth so a pathological page — words scattered at a thousand different positions —
/// cannot recurse without end, and it only splits where the gap is wider than
/// <see cref="TextGeometry.Gutter"/> of an em, which keeps the space between two words of a justified line
/// from becoming a block boundary.
/// </para>
/// </remarks>
internal static class RecursiveXyCutSegmenter
{
    /// <summary>How many times a region may be split before it is accepted as a block.</summary>
    private const int MaxDepth = 8;

    /// <summary>Splits the words of a page into the blocks its whitespace separates.</summary>
    /// <param name="words">The words of one page, in any order.</param>
    /// <returns>The blocks, in the order the cuts produced them.</returns>
    public static List<List<PdfWord>> Segment(IReadOnlyList<PdfWord> words)
    {
        var blocks = new List<List<PdfWord>>();

        if (words.Count == 0)
        {
            return blocks;
        }

        var gutter = Math.Max(
            TextGeometry.Median(words.Select(word => word.FontSize)) * TextGeometry.Gutter,
            1d);

        Cut([.. words], gutter, 0, blocks);

        return blocks;
    }

    /// <summary>Splits a region, or accepts it as a block when it admits no split.</summary>
    /// <param name="region">The words of the region.</param>
    /// <param name="gutter">The narrowest gap that splits the region.</param>
    /// <param name="depth">How many splits lie above this region.</param>
    /// <param name="blocks">The list that receives the blocks.</param>
    private static void Cut(List<PdfWord> region, double gutter, int depth, List<List<PdfWord>> blocks)
    {
        if (region.Count > 1 && depth < MaxDepth && Split(region, gutter) is { } halves)
        {
            Cut(halves.First, gutter, depth + 1, blocks);
            Cut(halves.Second, gutter, depth + 1, blocks);
            return;
        }

        blocks.Add(region);
    }

    /// <summary>Finds the widest gutter of a region and the words on either side of it.</summary>
    /// <param name="region">The words of the region.</param>
    /// <param name="gutter">The narrowest gap that splits the region.</param>
    /// <returns>The two halves, or <see langword="null"/> when the region admits no split.</returns>
    /// <remarks>
    /// A vertical gutter is tried first, because separating columns is the split that a page states most
    /// clearly and cutting horizontally first would put two columns into one block.
    /// </remarks>
    private static (List<PdfWord> First, List<PdfWord> Second)? Split(List<PdfWord> region, double gutter) =>
        VerticalSplit(region, gutter) ?? HorizontalSplit(region, gutter);

    /// <summary>
    /// Splits a region at a vertical gutter: the widest band of whitespace that runs between two groups of
    /// words side by side.
    /// </summary>
    /// <param name="region">The words of the region.</param>
    /// <param name="gutter">The narrowest gap that splits the region.</param>
    /// <returns>The left and right halves, or <see langword="null"/> when the region admits no split.</returns>
    private static (List<PdfWord> First, List<PdfWord> Second)? VerticalSplit(List<PdfWord> region, double gutter)
    {
        var ordered = region
            .OrderBy(word => word.BoundingBox.Left)
            .ThenByDescending(word => word.BaselineY)
            .ToList();

        var widest = 0d;
        var at = -1;
        var right = ordered[0].BoundingBox.Right;

        for (var index = 1; index < ordered.Count; index++)
        {
            var gap = ordered[index].BoundingBox.Left - right;

            if (gap > widest)
            {
                widest = gap;
                at = index;
            }

            right = Math.Max(right, ordered[index].BoundingBox.Right);
        }

        return at > 0 && widest > gutter
            ? (ordered.GetRange(0, at), ordered.GetRange(at, ordered.Count - at))
            : null;
    }

    /// <summary>
    /// Splits a region at a horizontal gutter: the widest band of whitespace that runs between two groups of
    /// words one above the other.
    /// </summary>
    /// <param name="region">The words of the region.</param>
    /// <param name="gutter">The narrowest gap that splits the region.</param>
    /// <returns>The upper and lower halves, or <see langword="null"/> when the region admits no split.</returns>
    private static (List<PdfWord> Upper, List<PdfWord> Lower)? HorizontalSplit(List<PdfWord> region, double gutter)
    {
        var ordered = region
            .OrderByDescending(word => word.BaselineY)
            .ThenBy(word => word.BoundingBox.Left)
            .ToList();

        var widest = 0d;
        var at = -1;
        var bottom = ordered[0].BoundingBox.Bottom;

        for (var index = 1; index < ordered.Count; index++)
        {
            var gap = bottom - ordered[index].BoundingBox.Top;

            if (gap > widest)
            {
                widest = gap;
                at = index;
            }

            bottom = Math.Min(bottom, ordered[index].BoundingBox.Bottom);
        }

        return at > 0 && widest > gutter
            ? (ordered.GetRange(0, at), ordered.GetRange(at, ordered.Count - at))
            : null;
    }
}
