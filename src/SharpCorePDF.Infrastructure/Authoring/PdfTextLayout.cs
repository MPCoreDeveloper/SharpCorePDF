using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// The immutable result of laying text out: the style it was laid out with, its lines and the box the
/// lines occupy.
/// </summary>
public sealed class PdfTextLayout
{
    private readonly PdfTextLine[] lines;

    /// <summary>Initializes a new instance of the <see cref="PdfTextLayout"/> class.</summary>
    /// <param name="style">The style the text was laid out with.</param>
    /// <param name="lines">The lines, in reading order.</param>
    /// <param name="bounds">The box the lines occupy.</param>
    /// <param name="lineHeight">The distance between baselines.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineHeight"/> is not finite or not greater than zero.</exception>
    public PdfTextLayout(
        PdfTextStyle style,
        IEnumerable<PdfTextLine> lines,
        PdfRectangle bounds,
        double lineHeight)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(bounds);

        if (!double.IsFinite(lineHeight) || lineHeight <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineHeight), lineHeight, "A line height must be finite and greater than zero.");
        }

        this.lines = lines.ToArray();
        if (Array.IndexOf(this.lines, null) >= 0)
        {
            throw new ArgumentException("A layout cannot contain a null line.", nameof(lines));
        }

        Style = style;
        Bounds = bounds;
        LineHeight = lineHeight;
    }

    /// <summary>Gets the style the text was laid out with.</summary>
    public PdfTextStyle Style { get; }

    /// <summary>Gets the lines, in reading order.</summary>
    public IReadOnlyList<PdfTextLine> Lines => lines;

    /// <summary>Gets the box the lines occupy.</summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Gets the y coordinate of the first line's baseline, or zero when there are no lines.</summary>
    public double Baseline => lines.Length == 0 ? 0.0 : lines[0].Baseline;

    /// <summary>Gets the number of lines.</summary>
    public int LineCount => lines.Length;

    /// <summary>Gets the distance between baselines.</summary>
    public double LineHeight { get; }
}
