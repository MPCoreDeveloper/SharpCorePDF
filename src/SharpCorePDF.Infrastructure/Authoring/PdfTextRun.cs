using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>A contiguous piece of a text line that shares one position and width.</summary>
/// <param name="Text">The characters of the run.</param>
/// <param name="Start">The baseline start of the run.</param>
/// <param name="Width">The advance width of the run, in points.</param>
public sealed record PdfTextRun(string Text, PdfPoint Start, double Width)
{
    /// <summary>Gets the characters of the run.</summary>
    public string Text { get; } = Text ?? throw new ArgumentNullException(nameof(Text));

    /// <summary>Gets the baseline start of the run.</summary>
    public PdfPoint Start { get; } = Start;

    /// <summary>Gets the advance width of the run, in points.</summary>
    public double Width { get; } = ValidateWidth(Width);

    private static double ValidateWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A run width must be finite and non-negative.");
        }

        return width;
    }
}
