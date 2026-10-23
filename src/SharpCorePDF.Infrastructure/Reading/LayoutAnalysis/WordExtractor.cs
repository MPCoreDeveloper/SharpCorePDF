using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Groups the letters of a page into the words a reader reads, by a rule the caller selects.
/// </summary>
/// <remarks>
/// <para>
/// The grouping is this library's reading of the page, not a claim the document makes: a producer may draw
/// a word one glyph at a time or a whole line in one string, with or without the spaces, in reading order
/// or not. Two strategies are offered — the raw order of the content stream, which is the baseline a
/// comparison against the producer needs, and the page's own order, which is what a search wants — and both
/// share the rule for where a word ends: a whitespace letter, a gap wider than the option allows, or a
/// baseline that has moved to another line.
/// </para>
/// <para>
/// Every word keeps the letters it was grouped from, so a caller that disagrees with the rule can regroup
/// from the same read instead of opening the document again.
/// </para>
/// </remarks>
internal static class WordExtractor
{
    /// <summary>Groups the glyphs a read produced into words.</summary>
    /// <param name="glyphs">The glyphs and the remarks of the read that produced them.</param>
    /// <param name="options">The order and the distances to group by.</param>
    /// <returns>The words of every page, in the order the strategy selects, with the same remarks.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static PdfReadResult<PdfWord> Extract(PdfReadResult<PdfGlyph> glyphs, PdfWordExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(options);

        var words = new List<PdfWord>();

        foreach (var page in glyphs.Items.GroupBy(glyph => glyph.PageNumber))
        {
            var items = page.Select(glyph => new Item(glyph, LetterExtractor.ToLetter(glyph))).ToList();

            words.AddRange(Group(
                options.Strategy == PdfWordExtractionStrategy.Raw ? items : InPageOrder(items, options),
                options));
        }

        return new()
        {
            Items = words,
            Warnings = glyphs.Warnings,
        };
    }

    /// <summary>
    /// Takes the letters in the order of the page: the top line first, and inside a line the direction the
    /// line runs in.
    /// </summary>
    /// <param name="items">The letters of one page, in drawing order.</param>
    /// <param name="options">The rules, whose vertical gap decides where one line ends and the next begins.</param>
    /// <returns>The letters of the page, ordered for reading.</returns>
    /// <remarks>
    /// A run of letters that share a baseline is reversed when most of it runs right to left, so that a
    /// right-to-left word is read in its own direction rather than mirrored. The sort itself is by
    /// descending baseline and then ascending x, which is the page's order for the scripts that write the
    /// other way round as well.
    /// </remarks>
    private static List<Item> InPageOrder(List<Item> items, PdfWordExtractionOptions options)
    {
        var ordered = items
            .OrderByDescending(item => item.Letter.StartBaseLine.Y)
            .ThenBy(item => item.Letter.StartBaseLine.X)
            .ToList();

        for (var start = 0; start < ordered.Count;)
        {
            var end = start + 1;
            var baseline = ordered[start].Letter.StartBaseLine.Y;
            var size = Math.Max(ordered[start].Letter.FontSize, 1d);

            while (end < ordered.Count
                && Math.Abs(ordered[end].Letter.StartBaseLine.Y - baseline) <= size * options.MaxVerticalGap)
            {
                end++;
            }

            if (RunsRightToLeft(ordered, start, end))
            {
                ordered.Reverse(start, end - start);
            }

            start = end;
        }

        return ordered;
    }

    /// <summary>Decides whether most of a run of letters on one baseline runs right to left.</summary>
    /// <param name="items">The letters of the page.</param>
    /// <param name="start">The index of the first letter of the run.</param>
    /// <param name="end">The index one past the last letter of the run.</param>
    /// <returns><see langword="true"/> when the run reads right to left.</returns>
    private static bool RunsRightToLeft(List<Item> items, int start, int end)
    {
        var rightToLeft = 0;
        var leftToRight = 0;

        for (var index = start; index < end; index++)
        {
            if (items[index].Letter.IsWhitespace)
            {
                continue;
            }

            if (items[index].Letter.TextDirection == PdfTextDirection.RightToLeft)
            {
                rightToLeft++;
            }
            else
            {
                leftToRight++;
            }
        }

        return rightToLeft > leftToRight;
    }

    /// <summary>Groups one page's letters into words.</summary>
    /// <param name="items">The letters of the page, in the order the strategy selected.</param>
    /// <param name="options">The rules to group by.</param>
    /// <returns>The words, in the same order.</returns>
    private static List<PdfWord> Group(List<Item> items, PdfWordExtractionOptions options)
    {
        var words = new List<PdfWord>();
        var run = new List<Item>();

        foreach (var item in items)
        {
            if (item.Letter.IsWhitespace)
            {
                // A white space ends the word it follows. When the caller asked for whitespace it stays in
                // that word, which is what makes concatenating the words of a page reproduce its text.
                if (options.IncludeWhitespace && run.Count > 0)
                {
                    run.Add(item);
                }

                AddWord(run, words);
                continue;
            }

            if (run.Count > 0 && !Continues(run[^1].Letter, item.Letter, options))
            {
                AddWord(run, words);
            }

            run.Add(item);
        }

        AddWord(run, words);

        return words;
    }

    /// <summary>Adds the letters collected so far as a word and clears the collection.</summary>
    /// <param name="run">The letters of the word.</param>
    /// <param name="words">The list that receives the word.</param>
    private static void AddWord(List<Item> run, List<PdfWord> words)
    {
        if (run.Count == 0)
        {
            return;
        }

        words.Add(new PdfWord
        {
            Text = string.Concat(run.Select(item => item.Letter.Value)),
            PageNumber = run[0].Letter.PageNumber,
            BoundingBox = Union(run.Select(item => item.Letter.BoundingBox)),
            Glyphs = [.. run.Select(item => item.Glyph)],
            Letters = [.. run.Select(item => item.Letter)],
        });

        run.Clear();
    }

    /// <summary>Decides whether a letter continues the word its predecessor began.</summary>
    /// <param name="previous">The last letter of the word so far.</param>
    /// <param name="next">The letter being placed.</param>
    /// <param name="options">The rules to group by.</param>
    /// <returns><see langword="true"/> when the letter belongs to the same word.</returns>
    private static bool Continues(PdfLetter previous, PdfLetter next, PdfWordExtractionOptions options)
    {
        var size = Math.Max(previous.FontSize, next.FontSize);

        if (size <= 0d || Math.Abs(next.StartBaseLine.Y - previous.StartBaseLine.Y) > size * options.MaxVerticalGap)
        {
            return false;
        }

        var gap = next.StartBaseLine.X - previous.EndBaseLine.X;
        var allowance = size * options.MaxHorizontalGap;

        // A gap wider than the allowance is a space. A letter that starts before the previous one ends is
        // either a kerned pair, which overlaps by a hair, or the start of a new column on the same baseline,
        // which overlaps by most of a word; the overlap is bounded for that.
        return gap <= allowance && -gap <= Math.Abs(previous.Width) + allowance;
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

    /// <summary>One glyph and the letter it was read as, kept together so a word can carry both.</summary>
    /// <param name="Glyph">The glyph of the content stream.</param>
    /// <param name="Letter">The letter the glyph was projected into.</param>
    private readonly record struct Item(PdfGlyph Glyph, PdfLetter Letter);
}
