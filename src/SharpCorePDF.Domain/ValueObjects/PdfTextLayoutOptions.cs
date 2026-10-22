namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The width, alignment and overflow policy used to lay a string out into lines.
/// </summary>
public sealed record PdfTextLayoutOptions
{
    /// <summary>Initializes a new instance of the <see cref="PdfTextLayoutOptions"/> record.</summary>
    /// <param name="maxWidth">The maximum line width in points.</param>
    /// <param name="alignment">The alignment of each line within the maximum width.</param>
    /// <param name="lineHeight">The distance between baselines, or null to use the font's line height.</param>
    /// <param name="breakWords">Whether a word wider than the line may be split.</param>
    /// <param name="overflow">What to do when the text cannot fit.</param>
    /// <param name="direction">The reading direction.</param>
    /// <exception cref="ArgumentOutOfRangeException">A width is not finite or not greater than zero, or an enum value is not defined.</exception>
    public PdfTextLayoutOptions(
        double maxWidth,
        PdfTextAlignment alignment = PdfTextAlignment.Left,
        double? lineHeight = null,
        bool breakWords = false,
        PdfTextOverflow overflow = PdfTextOverflow.Refuse,
        PdfTextDirection direction = PdfTextDirection.LeftToRight)
    {
        if (!double.IsFinite(maxWidth) || maxWidth <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "A maximum width must be finite and greater than zero.");
        }

        if (lineHeight is { } value && (!double.IsFinite(value) || value <= 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(lineHeight), lineHeight, "A line height must be finite and greater than zero.");
        }

        if (!Enum.IsDefined(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "The alignment is not one the engine defines.");
        }

        if (!Enum.IsDefined(overflow))
        {
            throw new ArgumentOutOfRangeException(nameof(overflow), overflow, "The overflow policy is not one the engine defines.");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The text direction is not one the engine defines.");
        }

        MaxWidth = maxWidth;
        Alignment = alignment;
        LineHeight = lineHeight;
        BreakWords = breakWords;
        Overflow = overflow;
        Direction = direction;
    }

    /// <summary>Gets the maximum line width in points.</summary>
    public double MaxWidth { get; }

    /// <summary>Gets the alignment of each line within the maximum width.</summary>
    public PdfTextAlignment Alignment { get; }

    /// <summary>Gets the distance between baselines, or null to use the font's line height.</summary>
    public double? LineHeight { get; }

    /// <summary>Gets a value indicating whether a word wider than the line may be split.</summary>
    public bool BreakWords { get; }

    /// <summary>Gets what to do when the text cannot fit.</summary>
    public PdfTextOverflow Overflow { get; }

    /// <summary>Gets the reading direction.</summary>
    public PdfTextDirection Direction { get; }
}
