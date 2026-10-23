using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Groups the lines of a page by the distance between them: lines that sit closer together than the page's
/// own leading become one block, and a gutter wider than that leading keeps two blocks apart.
/// </summary>
/// <remarks>
/// <para>
/// The Docstrum algorithm in the form a reader can use: the page states its own line spacing through the
/// lines it draws, so the threshold is measured from the document instead of assumed from the font size,
/// which is what lets a page of uneven leading group better than a fixed threshold can. The distance is the
/// median of the baseline drops between consecutive lines, bounded by a multiple of the em so that a page
/// whose only two lines sit far apart is still two blocks.
/// </para>
/// <para>
/// A line joins the block whose last line it sits under and shares a horizontal span with, which is what
/// keeps two columns apart even when they share baselines: the left column's line and the right column's
/// line do not overlap, so neither continues the other.
/// </para>
/// </remarks>
internal static class DocstrumSegmenter
{
    /// <summary>
    /// How much wider than the page's own leading a gap may be and still be read as a paragraph break, as a
    /// multiple of that leading.
    /// </summary>
    private const double Break = 1.5d;

    /// <summary>
    /// How far above the em the measured leading is ignored, as a multiple of the em. A page whose lines are
    /// drawn far apart states no usable leading, and the font size is the only measure left.
    /// </summary>
    private const double Ceiling = 3d;

    /// <summary>
    /// The multiple of the em a gap may reach and still join two lines, whatever the page measures, so that
    /// a page of two widely spaced lines does not become one block.
    /// </summary>
    private const double Floor = 0.8d;

    /// <summary>Groups words into blocks by the distance between the lines of the page.</summary>
    /// <param name="words">The words of one page, in any order.</param>
    /// <returns>The blocks, each the words of the lines it spans, in reading order.</returns>
    public static List<List<PdfWord>> Segment(IReadOnlyList<PdfWord> words)
    {
        var lines = LineGrouper.Group(words);
        var blocks = new List<List<PdfWord>>();
        var lastLines = new List<TextLine>();
        var spacing = Spacing(lines);

        foreach (var line in lines)
        {
            var index = lastLines.FindIndex(previous => Continues(previous, line, spacing));

            if (index < 0)
            {
                blocks.Add([.. line.Words]);
                lastLines.Add(line);
            }
            else
            {
                blocks[index].AddRange(line.Words);
                lastLines[index] = line;
            }
        }

        return blocks;
    }

    /// <summary>Measures the line spacing the page states.</summary>
    /// <param name="lines">The lines of the page, top to bottom.</param>
    /// <returns>The widest baseline drop that still counts as paragraph leading, in points.</returns>
    private static double Spacing(List<TextLine> lines)
    {
        var drops = new List<double>();

        for (var index = 1; index < lines.Count; index++)
        {
            var drop = lines[index - 1].Baseline - lines[index].Baseline;

            if (drop > 0d)
            {
                drops.Add(drop);
            }
        }

        var size = TextGeometry.Median(lines.Select(line => line.Size));
        var leading = TextGeometry.Median(drops);

        return Math.Max(Math.Min(leading * Break, size * Ceiling), size * Floor);
    }

    /// <summary>Decides whether a line continues the block its predecessor line belongs to.</summary>
    /// <param name="previous">The last line of the block.</param>
    /// <param name="line">The line being placed.</param>
    /// <param name="spacing">The widest baseline drop that still counts as paragraph leading.</param>
    /// <returns><see langword="true"/> when the line belongs to the same block.</returns>
    private static bool Continues(TextLine previous, TextLine line, double spacing)
    {
        var drop = previous.Baseline - line.Baseline;

        return drop <= spacing
            && drop >= -spacing
            && TextGeometry.Shares(previous.Bounds.Left, previous.Bounds.Right, line.Bounds);
    }
}
