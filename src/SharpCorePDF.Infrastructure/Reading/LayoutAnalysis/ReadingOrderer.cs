using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Puts the blocks a segmentation produced into the order a reader takes them in.
/// </summary>
/// <remarks>
/// <para>
/// Segmentation decides what a block is; this decides which block is read first. The two are separate
/// because a page can be segmented correctly and still be listed in the wrong order — a two-column page
/// whose left column is drawn after its right one is the usual case — and because a caller comparing two
/// segmentations needs one order fixed while it varies the other.
/// </para>
/// <para>
/// The column-aware order gathers blocks into columns by the horizontal span they share, walking the page
/// from the top: a block that spans the gutter — a heading over two columns, a footer under them — joins the
/// column it starts in, which is what makes a heading read before the columns it spans. The columns are then
/// reported left to right and the blocks of a column top to bottom.
/// </para>
/// </remarks>
internal static class ReadingOrderer
{
    /// <summary>Orders blocks the way the strategy selects.</summary>
    /// <param name="blocks">The blocks a segmentation produced.</param>
    /// <param name="strategy">The order to report them in.</param>
    /// <returns>The blocks, ordered.</returns>
    public static List<List<PdfWord>> Order(
        IReadOnlyList<List<PdfWord>> blocks,
        PdfReadingOrderStrategy strategy) =>
        strategy == PdfReadingOrderStrategy.Raw ? [.. blocks] : Columns(blocks);

    /// <summary>Orders blocks as the columns of the page, left to right.</summary>
    /// <param name="blocks">The blocks a segmentation produced.</param>
    /// <returns>The blocks, ordered.</returns>
    private static List<List<PdfWord>> Columns(IReadOnlyList<List<PdfWord>> blocks)
    {
        var columns = new List<Column>();

        foreach (var block in blocks
            .Where(block => block.Count > 0)
            .Select(block => (Block: block, Bounds: TextGeometry.Bounds(block)))
            .OrderByDescending(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left))
        {
            var index = columns.FindIndex(column => column.Holds(block.Bounds));

            if (index < 0)
            {
                columns.Add(new Column(block.Bounds, block.Block));
            }
            else
            {
                columns[index].Add(block.Bounds, block.Block);
            }
        }

        return
        [
            .. columns
                .OrderBy(column => column.Left)
                .SelectMany(column => column.Blocks
                    .Select(block => (Block: block, Bounds: TextGeometry.Bounds(block)))
                    .OrderByDescending(item => item.Bounds.Top)
                    .ThenBy(item => item.Bounds.Left)
                    .Select(item => item.Block)),
        ];
    }

    /// <summary>One column of a page: the horizontal span its blocks share and the blocks themselves.</summary>
    private sealed class Column
    {
        /// <summary>Initializes a new instance of the <see cref="Column"/> class.</summary>
        /// <param name="bounds">The area of the first block of the column.</param>
        /// <param name="block">The first block of the column.</param>
        public Column(PdfRectangle bounds, List<PdfWord> block)
        {
            Left = bounds.Left;
            Right = bounds.Right;
            Blocks = [block];
        }

        /// <summary>Gets the left edge of the column.</summary>
        public double Left { get; private set; }

        /// <summary>Gets the right edge of the column.</summary>
        public double Right { get; private set; }

        /// <summary>Gets the blocks of the column, in the order they were placed in it.</summary>
        public List<List<PdfWord>> Blocks { get; }

        /// <summary>Decides whether a block belongs to this column.</summary>
        /// <param name="bounds">The area of the block.</param>
        /// <returns><see langword="true"/> when the block shares the column's horizontal span.</returns>
        public bool Holds(PdfRectangle bounds) => TextGeometry.Shares(Left, Right, bounds);

        /// <summary>Places a block in the column and widens the column to cover it.</summary>
        /// <param name="bounds">The area of the block.</param>
        /// <param name="block">The block.</param>
        public void Add(PdfRectangle bounds, List<PdfWord> block)
        {
            Left = Math.Min(Left, bounds.Left);
            Right = Math.Max(Right, bounds.Right);
            Blocks.Add(block);
        }
    }
}
