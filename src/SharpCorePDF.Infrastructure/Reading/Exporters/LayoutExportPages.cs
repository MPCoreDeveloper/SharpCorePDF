using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

namespace SharpCorePDF.Infrastructure.Reading.Exporters;

/// <summary>
/// One page an export writes: the size of the page, the blocks read from it and the lines those blocks are
/// made of, ready for a format that states them element by element.
/// </summary>
/// <param name="PageNumber">The one-based number of the page.</param>
/// <param name="Width">The width of the page, in points.</param>
/// <param name="Height">The height of the page, in points.</param>
/// <param name="Blocks">The blocks of the page, in reading order.</param>
internal sealed record LayoutExportPage(
    int PageNumber,
    double Width,
    double Height,
    IReadOnlyList<LayoutExportBlock> Blocks);

/// <summary>
/// One block of a page as an export sees it: the block the read reported and the lines it reads as.
/// </summary>
/// <param name="Block">The block, with the text, the area and the reading order of the read.</param>
/// <param name="Lines">The lines of the block, top to bottom, each with its words left to right.</param>
internal sealed record LayoutExportBlock(PdfTextBlock Block, IReadOnlyList<TextLine> Lines)
{
    /// <summary>Gets the area the block covers, in page space.</summary>
    public PdfRectangle Bounds => Block.BoundingBox;

    /// <summary>Gets the one-based position of the block in the reading order of its page.</summary>
    public int ReadingOrder => Block.ReadingOrder;
}

/// <summary>
/// Projects a block read into the pages an export writes, so that the four exporters share one answer to
/// the questions a format asks: which blocks belong to the page, in which order, and which words sit on
/// which line.
/// </summary>
/// <remarks>
/// The projection only regroups what the read already decided: a block keeps the reading order it was
/// given, and its words are grouped into lines by the same rule the read used to write its text, so the
/// export and the plain text of the block cannot disagree about where a line ends. A page of the range that
/// produced no block keeps its place, because an export states the page even when it draws nothing.
/// </remarks>
internal static class LayoutExportPages
{
    /// <summary>Groups the blocks of a read by the pages of the range.</summary>
    /// <param name="blocks">The blocks the read produced, in reading order.</param>
    /// <param name="pages">The boxes of the pages of the range, in ascending page order.</param>
    /// <returns>One entry per page of the range, in the order the pages were given.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static List<LayoutExportPage> Project(
        PdfReadResult<PdfTextBlock> blocks,
        IReadOnlyList<PdfPageBox> pages)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(pages);

        var byPage = new Dictionary<int, List<PdfTextBlock>>();

        foreach (var block in blocks.Items)
        {
            if (!byPage.TryGetValue(block.PageNumber, out var list))
            {
                byPage[block.PageNumber] = list = [];
            }

            list.Add(block);
        }

        return
        [
            .. pages.Select(page => new LayoutExportPage(
                page.PageNumber,
                page.Width,
                page.Height,
                byPage.TryGetValue(page.PageNumber, out var list) ? Project(list) : [])),
        ];
    }

    /// <summary>Orders the blocks of one page and groups the words of each into lines.</summary>
    /// <param name="blocks">The blocks of one page.</param>
    /// <returns>The blocks, in reading order, each with its lines.</returns>
    private static List<LayoutExportBlock> Project(List<PdfTextBlock> blocks) =>
    [
        .. blocks
            .OrderBy(block => block.ReadingOrder)
            .Select(block => new LayoutExportBlock(block, LineGrouper.Group(block.Words))),
    ];
}
