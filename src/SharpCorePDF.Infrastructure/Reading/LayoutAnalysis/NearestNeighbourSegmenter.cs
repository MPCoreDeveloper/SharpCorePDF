using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Groups the words of a page by their place on it: lines top to bottom, paragraphs as the runs of lines
/// that follow one another with the leading of a paragraph.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the one the text read of the content surface uses — a line continues the block above it when
/// it sits below it by no more than the leading a paragraph leaves and starts within the reach of the line
/// above — restated over the words the word read grouped. Restating it here rather than reaching into the
/// content reader keeps the two surfaces able to disagree about a page without one of them breaking.
/// </para>
/// <para>
/// The threshold is stated in ems, so a footnote and a title are grouped by the same rule. What it cannot do
/// is separate two columns that share lines, which is what the recursive XY cut is for.
/// </para>
/// </remarks>
internal static class NearestNeighbourSegmenter
{
    /// <summary>
    /// How far apart two baselines may sit and still be lines of one block, in ems. A line spacing of 1.8
    /// clears ordinary leading and still breaks at the blank space between paragraphs in most documents.
    /// </summary>
    private const double Leading = 1.8d;

    /// <summary>Groups words into blocks: lines that follow one another down the page.</summary>
    /// <param name="words">The words of one page, in any order.</param>
    /// <returns>The blocks, each the words of the lines it spans, in reading order.</returns>
    public static List<List<PdfWord>> Segment(IReadOnlyList<PdfWord> words)
    {
        var blocks = new List<List<PdfWord>>();
        TextLine? previous = null;

        foreach (var line in LineGrouper.Group(words))
        {
            if (previous is null || !Continues(previous, line))
            {
                blocks.Add([]);
            }

            blocks[^1].AddRange(line.Words);
            previous = line;
        }

        return blocks;
    }

    /// <summary>Decides whether a line continues the block the line above it belongs to.</summary>
    /// <param name="previous">The line above.</param>
    /// <param name="line">The line being placed.</param>
    /// <returns><see langword="true"/> when the line belongs to the same block.</returns>
    private static bool Continues(TextLine previous, TextLine line)
    {
        var size = Math.Max(previous.Size, line.Size);

        if (size <= 0d)
        {
            return false;
        }

        var drop = previous.Baseline - line.Baseline;

        if (drop < -(size * TextGeometry.BaselineTolerance) || drop > size * Leading)
        {
            return false;
        }

        var reach = size * TextGeometry.LineReach;

        return line.Bounds.Left <= previous.Bounds.Right + reach
            && line.Bounds.Right >= previous.Bounds.Left - reach;
    }
}
