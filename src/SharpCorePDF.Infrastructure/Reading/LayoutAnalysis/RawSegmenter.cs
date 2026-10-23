using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Groups the words of a page in the order the content stream draws them: a block is a run of words that
/// stay on one baseline.
/// </summary>
/// <remarks>
/// <para>
/// This is the only segmentation that reproduces a producer exactly, which is what makes it the baseline a
/// comparison against the producer needs — and what makes it a poor reading of a page, because a producer is
/// free to draw its footer before its title. A run ends where the drawing moves to another baseline, so
/// "World Hello" drawn as two runs on one line is one block and the line under it is the next.
/// </para>
/// <para>
/// The words keep the order they were drawn in; a block is never sorted, because sorting it would turn this
/// segmentation into one of the others.
/// </para>
/// </remarks>
internal static class RawSegmenter
{
    /// <summary>Groups words into the blocks the content stream draws, in drawing order.</summary>
    /// <param name="words">The words of one page, in the order the content stream draws them.</param>
    /// <returns>The blocks, each a run of words on one baseline.</returns>
    public static List<List<PdfWord>> Segment(IReadOnlyList<PdfWord> words)
    {
        var blocks = new List<List<PdfWord>>();
        var run = new List<PdfWord>();

        foreach (var word in words)
        {
            if (run.Count > 0 && !SharesBaseline(run[0], word))
            {
                blocks.Add(run);
                run = [];
            }

            run.Add(word);
        }

        if (run.Count > 0)
        {
            blocks.Add(run);
        }

        return blocks;
    }

    /// <summary>Decides whether a word continues the baseline the run is drawn on.</summary>
    /// <param name="first">The first word of the run.</param>
    /// <param name="next">The word being placed.</param>
    /// <returns><see langword="true"/> when the word belongs to the same line.</returns>
    private static bool SharesBaseline(PdfWord first, PdfWord next)
    {
        var size = Math.Max(first.FontSize, next.FontSize);
        var tolerance = Math.Max(size * TextGeometry.BaselineTolerance, 0.01d);

        return Math.Abs(first.BaselineY - next.BaselineY) <= tolerance;
    }
}
