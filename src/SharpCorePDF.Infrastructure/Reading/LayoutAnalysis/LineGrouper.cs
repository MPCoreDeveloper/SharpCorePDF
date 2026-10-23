using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// Gathers the words of a page into the lines a block strategy works with.
/// </summary>
/// <remarks>
/// A word joins the first line it sits on — within a quarter of an em of that line's baseline and inside the
/// reach of its span — so two runs that share a baseline but sit far apart stay two lines: a heading beside a
/// body, two cells of a table, two columns of a page. The result is ordered the way a page is read: lines top
/// to bottom, and the words of a line left to right.
/// </remarks>
internal static class LineGrouper
{
    /// <summary>Groups words into lines.</summary>
    /// <param name="words">The words of one page, in any order.</param>
    /// <returns>The lines of the page, top to bottom, each line's words left to right.</returns>
    public static List<TextLine> Group(IEnumerable<PdfWord> words)
    {
        var lines = new List<List<PdfWord>>();

        foreach (var word in words
            .OrderByDescending(word => word.BaselineY)
            .ThenBy(word => word.BoundingBox.Left))
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

        return [.. lines.Select(line => new TextLine { Words = line, Bounds = TextGeometry.Bounds(line) })];
    }

    /// <summary>Decides whether a word sits on the baseline of a line and near enough to belong to it.</summary>
    /// <param name="line">The words already gathered on the line.</param>
    /// <param name="word">The word being placed.</param>
    /// <returns><see langword="true"/> when the word belongs to the line.</returns>
    private static bool Shares(List<PdfWord> line, PdfWord word)
    {
        var size = Math.Max(line[0].FontSize, word.FontSize);

        if (size > 0d && Math.Abs(line[0].BaselineY - word.BaselineY) > size * TextGeometry.BaselineTolerance)
        {
            return false;
        }

        var left = line.Min(candidate => candidate.BoundingBox.Left);
        var right = line.Max(candidate => candidate.BoundingBox.Right);
        var reach = Math.Max(size, 0d) * TextGeometry.LineReach;

        return word.BoundingBox.Left <= right + reach
            && word.BoundingBox.Right >= left - reach;
    }
}
