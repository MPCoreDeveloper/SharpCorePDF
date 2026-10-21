using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The text half of <see cref="PdfPageContentReader"/>: the glyphs a page draws, the words those glyphs
/// make and the blocks those words make, in reading order.
/// </summary>
/// <remarks>
/// <para>
/// Three steps, each of which a caller can stop at: <see cref="PdfTextCollector"/> walks the content and
/// yields the glyphs the document draws; this file groups them into words by the geometry the page
/// states; and it groups the words into blocks by baselines and gaps. None of the three reads the font
/// program or the page's appearance, so a document whose text cannot be decoded yields fewer glyphs and
/// a warning rather than invented text (NFR-7.8).
/// </para>
/// <para>
/// The thresholds below are stated in ems, so they scale with the text: a line of a 6pt footnote is
/// grouped by the same rule as a line of a 30pt title, and neither needs a constant of its own. They are
/// a reading of the page, not a claim about its structure — a table cell and a paragraph are both
/// "lines that follow one another" to this code.
/// </para>
/// </remarks>
public sealed partial class PdfPageContentReader
{
    /// <summary>
    /// How far apart two baselines may sit and still belong to one line, as a fraction of the em box.
    /// </summary>
    private const double BaselineTolerance = 0.25d;

    /// <summary>
    /// How wide a gap between two glyphs may be and still be a gap inside a word rather than between
    /// two words, as a fraction of the em box. A space is usually a quarter of an em, so a gap this
    /// narrow is a producer placing glyphs one by one rather than one that meant a space.
    /// </summary>
    private const double WordGap = 0.2d;

    /// <summary>
    /// How far apart two lines may sit and still be lines of one block, in ems. A line spacing of 1.8
    /// clears ordinary leading and still breaks at the blank space between paragraphs in most
    /// documents.
    /// </summary>
    private const double LineGap = 1.8d;

    /// <summary>
    /// How far a line may stray from the left edge of the block above it and still continue it, in ems.
    /// </summary>
    private const double IndentTolerance = 2d;

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfTextBlock>> ReadTextAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();
        var blocks = new List<PdfTextBlock>();

        NoteClosedEncryption(reader, warnings);

        foreach (var page in SelectPages(reader, range))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageNumber = page.Index + 1;
            var pageContent = ReadPageContent(reader, page, warnings, pageNumber);

            if (pageContent is null)
            {
                continue;
            }

            var collector = new PdfTextCollector(reader, pageNumber, warnings);
            var glyphs = collector.Collect(pageContent, PageResources(reader, page));

            blocks.AddRange(BuildBlocks(glyphs, pageNumber));
        }

        return Result(reader, blocks, warnings);
    }

    /// <summary>Groups the glyphs of a page into words, the words into lines and the lines into blocks.</summary>
    /// <param name="glyphs">The glyphs of the page, in drawing order.</param>
    /// <param name="pageNumber">The one-based number of the page.</param>
    /// <returns>The blocks, in reading order.</returns>
    private static List<PdfTextBlock> BuildBlocks(List<PdfGlyph> glyphs, int pageNumber)
    {
        var lines = BuildLines(BuildWords(glyphs, pageNumber));
        var blocks = new List<PdfTextBlock>();

        foreach (var paragraph in BuildParagraphs(lines))
        {
            blocks.Add(new PdfTextBlock
            {
                PageNumber = pageNumber,
                Text = string.Join(
                    '\n',
                    paragraph.Select(line => string.Join(' ', line.Select(word => word.Text)))),
                BoundingBox = Union(paragraph.SelectMany(line => line).Select(word => word.BoundingBox)),
                ReadingOrder = blocks.Count + 1,
                Words = [.. paragraph.SelectMany(line => line)],
            });
        }

        return blocks;
    }

    /// <summary>Groups glyphs into words: runs that share a baseline and touch or nearly touch.</summary>
    /// <param name="glyphs">The glyphs of the page, in drawing order.</param>
    /// <param name="pageNumber">The one-based number of the page.</param>
    /// <returns>The words, in drawing order.</returns>
    /// <remarks>
    /// Whitespace glyphs are what the gaps between words measure, so they end the run they interrupt and
    /// are part of no word. A glyph that continues a run neither moves the baseline nor leaves a gap
    /// wide enough to be a space; anything else starts a new word, which is why a line drawn one
    /// character at a time still yields words.
    /// </remarks>
    private static List<PdfWord> BuildWords(List<PdfGlyph> glyphs, int pageNumber)
    {
        var words = new List<PdfWord>();
        var run = new List<PdfGlyph>();

        foreach (var glyph in glyphs)
        {
            if (glyph.IsWhitespace)
            {
                AddWord(run, words, pageNumber);
                continue;
            }

            if (run.Count > 0 && !Continues(run[^1], glyph))
            {
                AddWord(run, words, pageNumber);
            }

            run.Add(glyph);
        }

        AddWord(run, words, pageNumber);

        return words;
    }

    /// <summary>Adds the glyphs collected so far as a word and clears the collection.</summary>
    /// <param name="run">The glyphs of the word.</param>
    /// <param name="words">The list that receives the word.</param>
    /// <param name="pageNumber">The one-based number of the page.</param>
    private static void AddWord(List<PdfGlyph> run, List<PdfWord> words, int pageNumber)
    {
        if (run.Count == 0)
        {
            return;
        }

        words.Add(new PdfWord
        {
            Text = string.Concat(run.Select(glyph => glyph.Text)),
            PageNumber = pageNumber,
            BoundingBox = Union(run.Select(glyph => glyph.BoundingBox)),
            Glyphs = [.. run],
        });

        run.Clear();
    }

    /// <summary>Decides whether a glyph continues the word its predecessor began.</summary>
    /// <param name="previous">The last glyph of the word so far.</param>
    /// <param name="next">The glyph being placed.</param>
    /// <returns><see langword="true"/> when the glyph belongs to the same word.</returns>
    private static bool Continues(PdfGlyph previous, PdfGlyph next)
    {
        var size = Math.Max(previous.FontSize, next.FontSize);

        if (size <= 0d || Math.Abs(next.Y - previous.Y) > size * BaselineTolerance)
        {
            return false;
        }

        var gap = next.X - (previous.X + previous.Width);

        // A gap wider than a fraction of an em is a space. A glyph that sits before the end of its
        // predecessor is either a kerned pair, which overlaps by a hair, or the start of a new column
        // on the same baseline, which overlaps by most of a word; the overlap is bounded for that.
        return gap <= size * WordGap && -gap <= (previous.Width + size);
    }

    /// <summary>Gathers the words of a page into lines.</summary>
    /// <param name="words">The words of the page, in drawing order.</param>
    /// <returns>The lines, top to bottom, each line's words left to right.</returns>
    /// <remarks>
    /// A word joins the first line it sits on, so two runs that share a baseline but sit far apart stay
    /// two lines: a heading beside a body, two cells of a table, two columns of a page.
    /// </remarks>
    private static List<List<PdfWord>> BuildLines(List<PdfWord> words)
    {
        var lines = new List<List<PdfWord>>();

        foreach (var word in words.OrderByDescending(word => word.BaselineY).ThenBy(word => word.BoundingBox.Left))
        {
            var line = lines.Find(candidate => Shares(candidate, word));

            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        foreach (var line in lines)
        {
            line.Sort((left, right) => left.BoundingBox.Left.CompareTo(right.BoundingBox.Left));
        }

        lines.Sort((left, right) => right[0].BaselineY.CompareTo(left[0].BaselineY));

        return lines;
    }

    /// <summary>Decides whether a word sits on the baseline of a line and near enough to belong to it.</summary>
    /// <param name="line">The words already gathered on the line.</param>
    /// <param name="word">The word being placed.</param>
    /// <returns><see langword="true"/> when the word belongs to the line.</returns>
    private static bool Shares(List<PdfWord> line, PdfWord word)
    {
        var size = Math.Max(line[0].FontSize, word.FontSize);

        if (size <= 0d || Math.Abs(line[0].BaselineY - word.BaselineY) > size * BaselineTolerance)
        {
            return false;
        }

        var left = line.Min(candidate => candidate.BoundingBox.Left);
        var right = line.Max(candidate => candidate.BoundingBox.Right);

        return word.BoundingBox.Left <= right + (size * 3d)
            && word.BoundingBox.Right >= left - (size * 3d);
    }

    /// <summary>Groups lines into blocks: lines that follow one another down the page.</summary>
    /// <param name="lines">The lines of the page, top to bottom.</param>
    /// <returns>The blocks, each the lines it spans, in reading order.</returns>
    private static List<List<List<PdfWord>>> BuildParagraphs(List<List<PdfWord>> lines)
    {
        var paragraphs = new List<List<List<PdfWord>>>();

        foreach (var line in lines)
        {
            if (paragraphs.Count == 0 || !ParagraphContinues(paragraphs[^1][^1], line))
            {
                paragraphs.Add([]);
            }

            paragraphs[^1].Add(line);
        }

        return paragraphs;
    }

    /// <summary>Decides whether a line continues the block above it.</summary>
    /// <param name="previous">The line above.</param>
    /// <param name="line">The line being placed.</param>
    /// <returns><see langword="true"/> when the line belongs to the same block.</returns>
    /// <remarks>
    /// A line continues when it sits below the previous one by no more than the leading a paragraph
    /// leaves and starts where the previous one started, within the indent a paragraph may add. A gap
    /// wider than that is the blank space between two blocks; a start further to the right is a column
    /// or a table cell of its own.
    /// </remarks>
    private static bool ParagraphContinues(List<PdfWord> previous, List<PdfWord> line)
    {
        var size = Math.Max(previous.Average(word => word.FontSize), line.Average(word => word.FontSize));

        if (size <= 0d)
        {
            return false;
        }

        var drop = previous[0].BaselineY - line[0].BaselineY;

        if (drop < -(size * BaselineTolerance) || drop > size * LineGap)
        {
            return false;
        }

        var start = line[0].BoundingBox.Left;
        var left = previous[0].BoundingBox.Left;

        return start >= left - (size * IndentTolerance)
            && start <= previous[^1].BoundingBox.Right + (size * IndentTolerance);
    }

    /// <summary>Finds the smallest rectangle that encloses the ones given.</summary>
    /// <param name="rectangles">The rectangles to enclose; never empty in the calls made here.</param>
    /// <returns>The enclosing rectangle, or an empty one at the origin when nothing was given.</returns>
    private static PdfRectangle Union(IEnumerable<PdfRectangle> rectangles)
    {
        var left = double.PositiveInfinity;
        var bottom = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.NegativeInfinity;

        foreach (var rectangle in rectangles)
        {
            left = Math.Min(left, rectangle.Left);
            bottom = Math.Min(bottom, rectangle.Bottom);
            right = Math.Max(right, rectangle.Right);
            top = Math.Max(top, rectangle.Top);
        }

        return double.IsFinite(left)
            ? PdfRectangle.FromCorners(left, bottom, right, top)
            : new PdfRectangle(0d, 0d, 0d, 0d);
    }
}
