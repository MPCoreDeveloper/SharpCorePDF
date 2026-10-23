using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of block-level reading: the words of a page grouped by the segmentation the caller selects and
/// reported in the reading order it selects, with the raw order of the content as the baseline.
/// </summary>
public sealed partial class PdfLayoutReaderTests
{
    /// <summary>
    /// The default segmentation groups the lines of a paragraph into one block, and the block carries the
    /// words it was grouped from, the area it covers and its position on the page.
    /// </summary>
    [Fact]
    public async Task Groups_a_page_into_blocks_by_the_default_segmentation()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var result = await Reader().ReadBlocksAsync(source, PageRange.All, cancellationToken: Token);

        var block = Assert.Single(result.Items);

        Assert.Empty(result.Warnings);
        Assert.Equal(1, block.PageNumber);
        Assert.Equal(1, block.ReadingOrder);
        Assert.Equal(2, block.LineCount);
        Assert.Equal(["Hello World", "Second line"], block.Text.Split('\n'));
        Assert.Equal(["Hello", "World", "Second", "line"], block.Words.Select(word => word.Text));
        Assert.Equal(20, block.Words.Sum(word => word.Letters.Count));
        Assert.Equal(72d, block.BoundingBox.Left, 0.001d);
        Assert.Equal(729.6d, block.BoundingBox.Top, 0.001d);
    }

    /// <summary>
    /// The segmentation and the reading order that produced the blocks travel with the result, so a caller
    /// comparing two reads does not have to recall the options it passed.
    /// </summary>
    [Fact]
    public async Task Reports_the_segmentation_and_the_reading_order_the_read_applied()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var defaults = await Reader().ReadBlocksAsync(source, PageRange.All, cancellationToken: Token);
        var raw = await Reader().ReadBlocksAsync(
            source,
            PageRange.All,
            new PdfLayoutOptions
            {
                Segmentation = PdfPageSegmentationStrategy.Raw,
                ReadingOrder = PdfReadingOrderStrategy.Raw,
            },
            cancellationToken: Token);

        Assert.Equal(PdfPageSegmentationStrategy.RecursiveXYCut, defaults.Segmentation);
        Assert.Equal(PdfReadingOrderStrategy.ColumnAware, defaults.ReadingOrderStrategy);
        Assert.Equal(PdfPageSegmentationStrategy.Raw, raw.Segmentation);
        Assert.Equal(PdfReadingOrderStrategy.Raw, raw.ReadingOrderStrategy);
    }

    /// <summary>
    /// A page of two columns is one block per column: the gutter between them is the whitespace the
    /// recursive XY cut splits on first.
    /// </summary>
    [Fact]
    public async Task Splits_a_two_column_page_into_one_block_per_column()
    {
        var source = Source(PdfContentFixtures.TextTwoColumns(), "columns.pdf");

        var result = await Reader().ReadBlocksAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(["Left one\nLeft two", "Right one\nRight two"], result.Items.Select(block => block.Text));
        Assert.Equal([1, 2], result.Items.Select(block => block.ReadingOrder));
        Assert.Equal([72d, 330d], result.Items.Select(block => block.BoundingBox.Left));
        Assert.All(result.Items, block => Assert.Equal(2, block.LineCount));
    }

    /// <summary>
    /// The reading order is the page's, not the content stream's: a page whose right column is drawn first
    /// is still read left to right.
    /// </summary>
    [Fact]
    public async Task Reads_a_page_of_two_columns_left_to_right_whatever_order_the_content_used()
    {
        var source = Source(PdfContentFixtures.TextTwoColumns(), "columns.pdf");

        var result = await Reader().ReadBlocksAsync(
            source,
            PageRange.All,
            new PdfLayoutOptions
            {
                Segmentation = PdfPageSegmentationStrategy.Raw,
                ReadingOrder = PdfReadingOrderStrategy.ColumnAware,
            },
            cancellationToken: Token);

        Assert.Equal(
            ["Left one", "Left two", "Right one", "Right two"],
            result.Items.Select(block => block.Text));
        Assert.Equal([1, 2, 3, 4], result.Items.Select(block => block.ReadingOrder));
    }

    /// <summary>
    /// The raw segmentation and the raw order reproduce the producer exactly, which is the baseline a
    /// comparison against the producer needs.
    /// </summary>
    [Fact]
    public async Task Keeps_the_content_order_when_the_segmentation_and_the_order_are_raw()
    {
        var source = Source(PdfContentFixtures.TextTwoColumns(), "columns.pdf");

        var result = await Reader().ReadBlocksAsync(
            source,
            PageRange.All,
            new PdfLayoutOptions
            {
                Segmentation = PdfPageSegmentationStrategy.Raw,
                ReadingOrder = PdfReadingOrderStrategy.Raw,
            },
            cancellationToken: Token);

        Assert.Equal(
            ["Right one", "Right two", "Left one", "Left two"],
            result.Items.Select(block => block.Text));
        Assert.All(result.Items, block => Assert.Equal(1, block.LineCount));
    }

    /// <summary>
    /// The nearest-neighbour segmentation groups the lines of a paragraph into one block: the second line
    /// follows the first with the leading of a paragraph.
    /// </summary>
    [Fact]
    public async Task Groups_the_lines_of_a_paragraph_with_the_nearest_neighbour()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var result = await Reader().ReadBlocksAsync(
            source,
            PageRange.All,
            new PdfLayoutOptions { Segmentation = PdfPageSegmentationStrategy.NearestNeighbour },
            cancellationToken: Token);

        var block = Assert.Single(result.Items);

        Assert.Equal(2, block.LineCount);
        Assert.Equal(4, block.Words.Count);
    }

    /// <summary>
    /// Two paragraphs a page's height apart stay two blocks: the gap is wider than the leading a paragraph
    /// leaves, whichever of the two line-based strategies groups the page.
    /// </summary>
    /// <param name="segmentation">The segmentation to group the page with.</param>
    [Theory]
    [InlineData(PdfPageSegmentationStrategy.NearestNeighbour)]
    [InlineData(PdfPageSegmentationStrategy.Docstrum)]
    public async Task Leaves_two_paragraphs_apart(PdfPageSegmentationStrategy segmentation)
    {
        var source = Source(PdfContentFixtures.TextBlocks(), "paragraphs.pdf");

        var result = await Reader().ReadBlocksAsync(
            source,
            PageRange.All,
            new PdfLayoutOptions { Segmentation = segmentation },
            cancellationToken: Token);

        Assert.Equal(["Earlier paragraph", "Later paragraph"], result.Items.Select(block => block.Text));
        Assert.Equal([1, 2], result.Items.Select(block => block.ReadingOrder));
        Assert.All(result.Items, block => Assert.Equal(1, block.LineCount));
    }

    /// <summary>
    /// Docstrum measures the leading from the page it is given, so a page of evenly spaced lines is one
    /// block and a page whose lines are drawn a paragraph apart is two.
    /// </summary>
    /// <param name="text">Whether the fixture is one paragraph or two.</param>
    /// <param name="blocks">The number of blocks the page must yield.</param>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task Groups_lines_by_the_leading_the_page_states_with_docstrum(bool text, int blocks)
    {
        var source = Source(
            text ? PdfContentFixtures.Text() : PdfContentFixtures.TextBlocks(),
            "leading.pdf");

        var result = await Reader().ReadBlocksAsync(
            source,
            PageRange.All,
            new PdfLayoutOptions { Segmentation = PdfPageSegmentationStrategy.Docstrum },
            cancellationToken: Token);

        Assert.Equal(blocks, result.Count);
    }

    /// <summary>
    /// A block that carries no letter and no digit is a rule, a bullet or a separator: it is reported by
    /// default and dropped when the caller asks for prose only.
    /// </summary>
    [Fact]
    public async Task Keeps_a_block_that_carries_no_prose_only_when_the_caller_asks_for_decorations()
    {
        var source = Source(PdfContentFixtures.TextDecoration(), "decoration.pdf");

        var kept = await Reader().ReadBlocksAsync(source, PageRange.All, cancellationToken: Token);
        var prose = await Reader().ReadBlocksAsync(
            source,
            PageRange.All,
            new PdfLayoutOptions { IncludeDecorations = false },
            cancellationToken: Token);

        Assert.Equal(["----", "Prose"], kept.Items.Select(block => block.Text));

        var block = Assert.Single(prose.Items);

        Assert.Equal("Prose", block.Text);
        Assert.Equal(1, block.ReadingOrder);
    }

    /// <summary>Blocks carry the page they were drawn on, and the reading order restarts on every page.</summary>
    [Fact]
    public async Task Reads_the_blocks_of_every_page_in_order_and_restarts_the_reading_order()
    {
        var source = Source(PdfContentFixtures.TextPages(), "pages.pdf");

        var result = await Reader().ReadBlocksAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(["Page one", "Page two"], result.Items.Select(block => block.Text));
        Assert.Equal([1, 2], result.Items.Select(block => block.PageNumber));
        Assert.Equal([1, 1], result.Items.Select(block => block.ReadingOrder));
    }

    /// <summary>
    /// A page whose font could not be read yields no block and keeps the warning the letter read made, so
    /// "no text" and "text that could not be read" stay different answers.
    /// </summary>
    [Fact]
    public async Task Yields_no_block_for_a_page_it_cannot_read_and_keeps_the_warning()
    {
        var source = Source(PdfContentFixtures.TextUnreadable(), "unreadable.pdf");

        var result = await Reader().ReadBlocksAsync(source, PageRange.All, cancellationToken: Token);

        AssertWarning(result.Warnings, PdfWarningKind.TextUnreadable);
        Assert.Equal([2], result.Items.Select(block => block.PageNumber));
        Assert.Equal(PdfPageSegmentationStrategy.RecursiveXYCut, result.Segmentation);
    }

    /// <summary>A run that advances towards the left is reported as a block read in its own direction.</summary>
    [Fact]
    public async Task Reads_a_right_to_left_run_as_one_block_in_its_own_direction()
    {
        var source = Source(PdfContentFixtures.TextRightToLeft(), "rtl.pdf");

        var result = await Reader().ReadBlocksAsync(source, PageRange.All, cancellationToken: Token);

        var block = Assert.Single(result.Items);

        Assert.Equal("AB", block.Text);
        Assert.Equal(188d, block.BoundingBox.Left, 0.001d);
        Assert.Equal(200d, block.BoundingBox.Right, 0.001d);
        Assert.Equal(PdfTextDirection.RightToLeft, block.Words[0].Letters[0].TextDirection);
    }

    /// <summary>A segmentation this release does not implement is refused when the options are created.</summary>
    [Fact]
    public void Refuses_a_segmentation_it_does_not_implement() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfLayoutOptions { Segmentation = (PdfPageSegmentationStrategy)42 });

    /// <summary>A reading order this release does not implement is refused when the options are created.</summary>
    [Fact]
    public void Refuses_a_reading_order_it_does_not_implement() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfLayoutOptions { ReadingOrder = (PdfReadingOrderStrategy)42 });
}
