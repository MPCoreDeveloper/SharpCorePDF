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
/// from the same read instead of opening the document again. The walk groups on glyph geometry alone and a
/// glyph is read as a letter only once it becomes a member of a word — a skipped space, and the box of a
/// glyph the word never closes over, never pays for the record.
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

        var items = glyphs.Items;
        var words = new List<PdfWord>();

        if (items.Count == 0)
        {
            return new()
            {
                Items = words,
                Warnings = glyphs.Warnings,
            };
        }

        var pageStart = 0;
        var pageNumber = items[0].PageNumber;

        for (var index = 1; index < items.Count; index++)
        {
            if (items[index].PageNumber != pageNumber)
            {
                GroupPage(items, pageStart, index, options, words);
                pageStart = index;
                pageNumber = items[index].PageNumber;
            }
        }

        GroupPage(items, pageStart, items.Count, options, words);

        return new()
        {
            Items = words,
            Warnings = glyphs.Warnings,
        };
    }

    /// <summary>Groups the glyphs of one page into words.</summary>
    /// <param name="items">The glyphs of the read, in drawing order.</param>
    /// <param name="start">The inclusive start of the page in the read.</param>
    /// <param name="end">The exclusive end of the page in the read.</param>
    /// <param name="options">The order and the distances to group by.</param>
    /// <param name="words">The list that receives the words of the page.</param>
    /// <remarks>
    /// The raw strategy groups the page where it lies: no copy, and the caller's drawing order is what
    /// the words describe. The page's own order has to sort, and sorting the caller's list would
    /// reorder a result it already holds, so that path works on a copy of the page alone.
    /// </remarks>
    private static void GroupPage(
        IReadOnlyList<PdfGlyph> items,
        int start,
        int end,
        PdfWordExtractionOptions options,
        List<PdfWord> words)
    {
        if (end <= start)
        {
            return;
        }

        if (options.Strategy == PdfWordExtractionStrategy.Raw)
        {
            Group(items, start, end, options, words);
            return;
        }

        var page = new List<PdfGlyph>(end - start);

        for (var index = start; index < end; index++)
        {
            page.Add(items[index]);
        }

        InPageOrder(page, options);
        Group(page, 0, page.Count, options, words);
    }

    /// <summary>
    /// Takes the glyphs in the order of the page: the top line first, and inside a line the direction the
    /// line runs in.
    /// </summary>
    /// <param name="items">The glyphs of one page, in drawing order; reordered in place.</param>
    /// <param name="options">The rules, whose vertical gap decides where one line ends and the next begins.</param>
    /// <remarks>
    /// A run of glyphs that share a baseline is reversed when most of it runs right to left, so that a
    /// right-to-left word is read in its own direction rather than mirrored. The sort itself is by
    /// descending baseline and then ascending x, which is the page's order for the scripts that write the
    /// other way round as well.
    /// </remarks>
    private static void InPageOrder(List<PdfGlyph> items, PdfWordExtractionOptions options)
    {
        items.Sort(static (left, right) =>
        {
            var byY = right.Y.CompareTo(left.Y);

            return byY != 0
                ? byY
                : left.X.CompareTo(right.X);
        });

        for (var start = 0; start < items.Count;)
        {
            var end = start + 1;

            while (end < items.Count && SameLine(items[start], items[end], options))
            {
                end++;
            }

            if (RunsRightToLeft(items, start, end))
            {
                items.Reverse(start, end - start);
            }

            start = end;
        }
    }

    /// <summary>Decides whether two glyphs sit on the same line of the page.</summary>
    /// <param name="first">The first glyph of the line so far.</param>
    /// <param name="next">The glyph being considered.</param>
    /// <param name="options">The rules, whose vertical gap is the line tolerance.</param>
    /// <returns><see langword="true"/> when the glyphs share a baseline within the allowance.</returns>
    private static bool SameLine(PdfGlyph first, PdfGlyph next, PdfWordExtractionOptions options)
    {
        var size = Math.Max(first.FontSize, next.FontSize);

        return size > 0d
            && Math.Abs(next.Y - first.Y) <= size * options.MaxVerticalGap;
    }

    /// <summary>Decides whether a run of glyphs on one baseline mostly advances right to left.</summary>
    /// <param name="items">The glyphs of the page.</param>
    /// <param name="start">The inclusive start of the run.</param>
    /// <param name="end">The exclusive end of the run.</param>
    /// <returns><see langword="true"/> when more of the run advances to the left than to the right.</returns>
    private static bool RunsRightToLeft(List<PdfGlyph> items, int start, int end)
    {
        var left = 0;
        var right = 0;

        for (var index = start; index < end; index++)
        {
            // A letter runs right to left exactly when its end baseline point sits left of its start, which
            // for a glyph is a negative advance; see PdfLetter.TextDirection.
            if (items[index].Width < 0d)
            {
                left++;
            }
            else
            {
                right++;
            }
        }

        return left > right;
    }

    /// <summary>Groups a sequence of glyphs into words by the gap and baseline rules.</summary>
    /// <param name="items">The glyphs, in the order the strategy selected.</param>
    /// <param name="start">The inclusive start of the run to group.</param>
    /// <param name="end">The exclusive end of the run to group.</param>
    /// <param name="options">The rules to group by.</param>
    /// <param name="words">The list that receives the words, in the same order.</param>
    private static void Group(
        IReadOnlyList<PdfGlyph> items,
        int start,
        int end,
        PdfWordExtractionOptions options,
        List<PdfWord> words)
    {
        var runStart = start;

        for (var index = start; index < end; index++)
        {
            var glyph = items[index];

            if (glyph.IsWhitespace)
            {
                if (options.IncludeWhitespace && index > runStart)
                {
                    AddWord(items, runStart, index + 1, words);
                }
                else if (index > runStart)
                {
                    AddWord(items, runStart, index, words);
                }

                runStart = index + 1;
                continue;
            }

            if (index > runStart && !Continues(items[index - 1], glyph, options))
            {
                AddWord(items, runStart, index, words);
                runStart = index;
            }
        }

        AddWord(items, runStart, end, words);
    }

    /// <summary>Adds the glyphs of one run as a word.</summary>
    /// <param name="items">The glyphs of the page.</param>
    /// <param name="start">The inclusive start of the run; smaller than <paramref name="end"/> on entry.</param>
    /// <param name="end">The exclusive end of the run.</param>
    /// <param name="words">The list that receives the word.</param>
    /// <remarks>
    /// The text and the box are read straight off the glyphs: the box edges are the minimum and maximum of
    /// each glyph's two baseline points and the usual ascender and descender of the em, which is exactly
    /// what <see cref="PdfLetter.BoundingBox"/> says — one rectangle per word rather than one per glyph.
    /// </remarks>
    private static void AddWord(IReadOnlyList<PdfGlyph> items, int start, int end, List<PdfWord> words)
    {
        var count = end - start;

        if (count == 0)
        {
            return;
        }

        var letters = new PdfLetter[count];
        var glyphs = new PdfGlyph[count];
        var length = 0;
        var left = double.PositiveInfinity;
        var bottom = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.NegativeInfinity;

        for (var index = start; index < end; index++)
        {
            var glyph = items[index];

            letters[index - start] = LetterExtractor.ToLetter(glyph);
            glyphs[index - start] = glyph;
            length += glyph.Text.Length;

            var near = Math.Min(glyph.X, glyph.X + glyph.Width);
            var far = Math.Max(glyph.X, glyph.X + glyph.Width);

            left = Math.Min(left, near);
            right = Math.Max(right, far);
            bottom = Math.Min(bottom, glyph.Y - (glyph.FontSize * 0.2d));
            top = Math.Max(top, glyph.Y + (glyph.FontSize * 0.8d));
        }

        // The word's text is built once, at its final length: a StringBuilder per word was the same
        // characters with a buffer, a chunk list and a copy in front of them.
        var text = string.Create(
            length,
            (items, start, end),
            static (span, state) =>
            {
                var offset = 0;

                for (var index = state.start; index < state.end; index++)
                {
                    var value = state.items[index].Text;
                    value.AsSpan().CopyTo(span[offset..]);
                    offset += value.Length;
                }
            });

        words.Add(new PdfWord
        {
            Text = text,
            PageNumber = items[start].PageNumber,
            BoundingBox = PdfRectangle.FromCorners(left, bottom, right, top),
            Glyphs = glyphs,
            Letters = letters,
        });
    }

    /// <summary>Decides whether a glyph continues the word its predecessor began.</summary>
    /// <param name="previous">The last glyph of the word so far.</param>
    /// <param name="next">The glyph being placed.</param>
    /// <param name="options">The rules to group by.</param>
    /// <returns><see langword="true"/> when the glyph belongs to the same word.</returns>
    private static bool Continues(PdfGlyph previous, PdfGlyph next, PdfWordExtractionOptions options)
    {
        var size = Math.Max(previous.FontSize, next.FontSize);

        if (size <= 0d || Math.Abs(next.Y - previous.Y) > size * options.MaxVerticalGap)
        {
            return false;
        }

        var gap = next.X - (previous.X + previous.Width);
        var allowance = size * options.MaxHorizontalGap;

        // A gap wider than the allowance is a space. A glyph that starts before the previous one ends is
        // either a kerned pair, which overlaps by a hair, or the start of a new column on the same baseline,
        // which overlaps by most of a word; the overlap is bounded for that.
        return gap <= allowance && -gap <= Math.Abs(previous.Width) + allowance;
    }
}