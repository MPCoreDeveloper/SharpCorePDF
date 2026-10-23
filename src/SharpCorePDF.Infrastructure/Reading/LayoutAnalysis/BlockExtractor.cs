using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Turns the glyphs a read produced into the text blocks a layout read reports: the words the strategy
/// groups, the order it puts them in and the text each block reads as.
/// </summary>
/// <remarks>
/// <para>
/// The steps are the ones the reading contract states, in that order: the glyphs are grouped into words
/// once, by the word read the layout surface already offers — the raw order of the content when the
/// segmentation is raw, the order of the page otherwise — and only then are those words grouped into blocks.
/// A strategy never changes what a letter says; it only decides which letters sit in which block.
/// </para>
/// <para>
/// The strategy that produced the blocks and the order they are in travel with the result, so a caller
/// comparing two reads does not have to recall the options it passed, and an empty page still names the rule
/// that found nothing on it.
/// </para>
/// </remarks>
internal static class BlockExtractor
{
    /// <summary>Groups the glyphs of a read into blocks.</summary>
    /// <param name="glyphs">The glyphs and the remarks of the read that produced them.</param>
    /// <param name="options">The segmentation, the reading order and the kinds of block to keep.</param>
    /// <returns>The blocks of every page, in reading order, with the same remarks.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static PdfReadResult<PdfTextBlock> Extract(PdfReadResult<PdfGlyph> glyphs, PdfLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(options);

        var words = WordExtractor.Extract(glyphs, new PdfWordExtractionOptions
        {
            Strategy = options.Segmentation == PdfPageSegmentationStrategy.Raw
                ? PdfWordExtractionStrategy.Raw
                : PdfWordExtractionStrategy.NearestNeighbour,
        });

        var blocks = new List<PdfTextBlock>();

        foreach (var page in words.Items.GroupBy(word => word.PageNumber))
        {
            var pageBlocks = Segment([.. page], options);

            for (var index = 0; index < pageBlocks.Count; index++)
            {
                blocks.Add(ToBlock(pageBlocks[index], page.Key, index + 1, options.Segmentation));
            }
        }

        return new()
        {
            Items = blocks,
            Warnings = words.Warnings,
            Segmentation = options.Segmentation,
            ReadingOrderStrategy = options.ReadingOrder,
        };
    }

    /// <summary>Segments the words of one page and orders the blocks.</summary>
    /// <param name="words">The words of one page, in the order the word read placed them.</param>
    /// <param name="options">The segmentation and the reading order.</param>
    /// <returns>The blocks of the page, in reading order.</returns>
    private static List<List<PdfWord>> Segment(List<PdfWord> words, PdfLayoutOptions options)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var blocks = options.Segmentation switch
        {
            PdfPageSegmentationStrategy.Raw => RawSegmenter.Segment(words),
            PdfPageSegmentationStrategy.NearestNeighbour => NearestNeighbourSegmenter.Segment(words),
            PdfPageSegmentationStrategy.Docstrum => DocstrumSegmenter.Segment(words),

            // The recursive XY cut is the default and the arm a strategy added by a later release takes
            // until it has an implementation of its own.
            _ => RecursiveXyCutSegmenter.Segment(words),
        };

        var ordered = ReadingOrderer.Order(blocks, options.ReadingOrder);

        return options.IncludeDecorations
            ? ordered
            : [.. ordered.Where(block => block.Exists(word => word.Text.Any(char.IsLetterOrDigit)))];
    }

    /// <summary>Reports one block as the text read model.</summary>
    /// <param name="words">The words of the block, in reading order.</param>
    /// <param name="pageNumber">The one-based number of the page the block is drawn on.</param>
    /// <param name="readingOrder">The one-based position of the block on its page.</param>
    /// <param name="strategy">The segmentation that produced the block.</param>
    /// <returns>The block.</returns>
    /// <remarks>
    /// A raw block is one run of one baseline, so its text is its words in the order the producer drew them;
    /// every other strategy reports the block as lines, because the lines are what the strategy found.
    /// </remarks>
    private static PdfTextBlock ToBlock(
        List<PdfWord> words,
        int pageNumber,
        int readingOrder,
        PdfPageSegmentationStrategy strategy) => new()
        {
            PageNumber = pageNumber,
            Text = strategy == PdfPageSegmentationStrategy.Raw
                ? string.Join(' ', words.Select(word => word.Text))
                : string.Join(
                    '\n',
                    LineGrouper.Group(words).Select(line => string.Join(' ', line.Words.Select(word => word.Text)))),
            BoundingBox = TextGeometry.Bounds(words),
            ReadingOrder = readingOrder,
            Words = [.. words],
        };
}
