using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// The measurements every block strategy shares: how far apart two baselines may sit, how far a word may
/// sit from a line, where the whitespace between two groups begins, and the rectangle that encloses them.
/// </summary>
/// <remarks>
/// <para>
/// The distances are stated in ems — fractions of the size of the text being grouped — so one rule serves a
/// 6pt footnote and a 30pt title, and a document that draws a line at half size needs no second set of
/// thresholds.
/// </para>
/// <para>
/// Nothing here decides what a block is; the strategies do. This is only the arithmetic they agree on, kept
/// in one place so that two strategies cannot disagree about what "the same line" means.
/// </para>
/// </remarks>
internal static class TextGeometry
{
    /// <summary>
    /// How far apart two baselines may sit and still belong to one line, as a fraction of the em box. A
    /// superscript rises by less than this and a line drawn with a small rise still shares its baseline.
    /// </summary>
    public const double BaselineTolerance = 0.25d;

    /// <summary>
    /// How far a word may sit from a line and still belong to it, in ems. The allowance covers the gap a
    /// wide word spacing leaves without letting two columns of a page become one line.
    /// </summary>
    public const double LineReach = 3d;

    /// <summary>
    /// How wide the whitespace between two groups of words must be before it counts as a gutter, in ems.
    /// A space is about a quarter of an em, so a gap this wide is a layout decision rather than a space.
    /// </summary>
    public const double Gutter = 0.6d;

    /// <summary>Finds the smallest rectangle that encloses the words given.</summary>
    /// <param name="words">The words to enclose; an empty collection yields an empty rectangle at the origin.</param>
    /// <returns>The enclosing rectangle.</returns>
    public static PdfRectangle Bounds(IEnumerable<PdfWord> words) =>
        Bounds(words.Select(word => word.BoundingBox));

    /// <summary>Finds the smallest rectangle that encloses the rectangles given.</summary>
    /// <param name="rectangles">The rectangles to enclose; an empty collection yields an empty rectangle at the origin.</param>
    /// <returns>The enclosing rectangle.</returns>
    public static PdfRectangle Bounds(IEnumerable<PdfRectangle> rectangles)
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

    /// <summary>Finds the middle value of a set of measurements.</summary>
    /// <param name="values">The measurements; an empty collection yields zero.</param>
    /// <returns>The median, which ignores the outlier a single tall or short line would contribute.</returns>
    public static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToList();

        return ordered.Count switch
        {
            0 => 0d,
            _ when ordered.Count % 2 == 1 => ordered[ordered.Count / 2],
            _ => (ordered[(ordered.Count / 2) - 1] + ordered[ordered.Count / 2]) / 2d,
        };
    }

    /// <summary>Finds how much two horizontal spans share.</summary>
    /// <param name="left">The left edge of the first span.</param>
    /// <param name="right">The right edge of the first span.</param>
    /// <param name="bounds">The span to compare with.</param>
    /// <returns>The width of the shared part, zero or negative when the spans do not meet.</returns>
    public static double Overlap(double left, double right, PdfRectangle bounds) =>
        Math.Min(right, bounds.Right) - Math.Max(left, bounds.Left);

    /// <summary>Decides whether two spans sit over one another rather than side by side.</summary>
    /// <param name="left">The left edge of the first span.</param>
    /// <param name="right">The right edge of the first span.</param>
    /// <param name="bounds">The span to compare with.</param>
    /// <returns><see langword="true"/> when more than half of the narrower span is shared.</returns>
    public static bool Shares(double left, double right, PdfRectangle bounds)
    {
        var narrower = Math.Min(Math.Max(right - left, 0.001d), Math.Max(bounds.Width, 0.001d));

        return Overlap(left, right, bounds) > narrower / 2d;
    }
}
