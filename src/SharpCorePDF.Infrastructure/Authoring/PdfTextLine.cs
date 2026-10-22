using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>One laid-out line of text: its characters, runs, start point, width and baseline.</summary>
public sealed record PdfTextLine
{
    private readonly PdfTextRun[] runs;

    /// <summary>Initializes a new instance of the <see cref="PdfTextLine"/> record.</summary>
    /// <param name="text">The characters of the line.</param>
    /// <param name="start">The baseline start of the line.</param>
    /// <param name="width">The advance width of the line, in points.</param>
    /// <param name="baseline">The y coordinate of the baseline.</param>
    /// <param name="runs">The runs of the line, or null to create one run spanning the line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width is negative or not finite, or the baseline is not finite.</exception>
    public PdfTextLine(
        string text,
        PdfPoint start,
        double width,
        double baseline,
        IEnumerable<PdfTextRun>? runs = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateWidth(width, nameof(width));

        if (!double.IsFinite(baseline))
        {
            throw new ArgumentOutOfRangeException(nameof(baseline), baseline, "A baseline must be finite.");
        }

        Text = text;
        Start = start;
        Width = width;
        Baseline = baseline;
        this.runs = runs?.ToArray() ?? [new PdfTextRun(text, start, width)];
        if (Array.IndexOf(this.runs, null) >= 0)
        {
            throw new ArgumentException("A line cannot contain a null run.", nameof(runs));
        }
    }

    /// <summary>Gets the characters of the line.</summary>
    public string Text { get; }

    /// <summary>Gets the runs of the line.</summary>
    public IReadOnlyList<PdfTextRun> Runs => runs;

    /// <summary>Gets the baseline start of the line.</summary>
    public PdfPoint Start { get; }

    /// <summary>Gets the advance width of the line, in points.</summary>
    public double Width { get; }

    /// <summary>Gets the y coordinate of the baseline.</summary>
    public double Baseline { get; }

    private static void ValidateWidth(double width, string name)
    {
        if (!double.IsFinite(width) || width < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, width, "A line width must be finite and non-negative.");
        }
    }
}
