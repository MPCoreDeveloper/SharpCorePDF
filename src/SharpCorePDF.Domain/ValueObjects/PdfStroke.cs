namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The paint and line style used to outline a path or a glyph.
/// </summary>
/// <remarks>
/// The dash array is copied when the stroke is created. An empty array means a solid line; an array
/// whose values are all zero is refused because PDF would draw no line at all.
/// </remarks>
public sealed record PdfStroke
{
    private readonly double[] dashArray;

    /// <summary>Initializes a new instance of the <see cref="PdfStroke"/> record.</summary>
    /// <param name="paint">The paint the line is drawn with.</param>
    /// <param name="width">The line width in points.</param>
    /// <param name="cap">The shape at the ends of open subpaths.</param>
    /// <param name="join">The shape where segments meet.</param>
    /// <param name="miterLimit">The miter limit, which must be at least 1.</param>
    /// <param name="dashArray">The dash pattern; null or empty means a solid line.</param>
    /// <param name="dashPhase">The distance into the pattern at which the line starts.</param>
    /// <param name="strokeAdjustment">The optional viewer stroke adjustment.</param>
    /// <exception cref="ArgumentOutOfRangeException">A width, limit, phase or enum value is outside its range.</exception>
    /// <exception cref="ArgumentException">The dash array contains a negative or non-finite value, or every value is zero.</exception>
    public PdfStroke(
        PdfPaint paint,
        double width = 1.0,
        PdfLineCap cap = PdfLineCap.Butt,
        PdfLineJoin join = PdfLineJoin.Miter,
        double miterLimit = 10.0,
        IReadOnlyList<double>? dashArray = null,
        double dashPhase = 0.0,
        PdfStrokeAdjustment? strokeAdjustment = null)
    {
        ArgumentNullException.ThrowIfNull(paint);

        if (!double.IsFinite(width) || width <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A line width must be finite and greater than zero.");
        }

        if (!double.IsFinite(miterLimit) || miterLimit < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(miterLimit), miterLimit, "A miter limit must be finite and at least 1.");
        }

        if (!double.IsFinite(dashPhase))
        {
            throw new ArgumentOutOfRangeException(nameof(dashPhase), dashPhase, "A dash phase must be finite.");
        }

        if (!Enum.IsDefined(cap))
        {
            throw new ArgumentOutOfRangeException(nameof(cap), cap, "The line cap is not one the engine defines.");
        }

        if (!Enum.IsDefined(join))
        {
            throw new ArgumentOutOfRangeException(nameof(join), join, "The line join is not one the engine defines.");
        }

        if (strokeAdjustment is { } adjustment && !Enum.IsDefined(adjustment))
        {
            throw new ArgumentOutOfRangeException(
                nameof(strokeAdjustment),
                strokeAdjustment,
                "The stroke adjustment is not one the engine defines.");
        }

        Paint = paint;
        Width = width;
        Cap = cap;
        Join = join;
        MiterLimit = miterLimit;
        this.dashArray = CopyDashArray(dashArray);
        DashPhase = dashPhase;
        StrokeAdjustment = strokeAdjustment;
    }

    /// <summary>Gets the paint the line is drawn with.</summary>
    public PdfPaint Paint { get; }

    /// <summary>Gets the line width in points.</summary>
    public double Width { get; }

    /// <summary>Gets the shape at the ends of open subpaths.</summary>
    public PdfLineCap Cap { get; }

    /// <summary>Gets the shape where segments meet.</summary>
    public PdfLineJoin Join { get; }

    /// <summary>Gets the miter limit.</summary>
    public double MiterLimit { get; }

    /// <summary>Gets the dash pattern; empty means a solid line.</summary>
    public IReadOnlyList<double> DashArray => dashArray;

    /// <summary>Gets the distance into the pattern at which the line starts.</summary>
    public double DashPhase { get; }

    /// <summary>Gets the optional viewer stroke adjustment.</summary>
    public PdfStrokeAdjustment? StrokeAdjustment { get; }

    private static double[] CopyDashArray(IReadOnlyList<double>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var result = new double[values.Count];
        var anyNonZero = false;
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (!double.IsFinite(value) || value < 0.0)
            {
                throw new ArgumentException(
                    "Every dash length must be finite and non-negative.",
                    nameof(values));
            }

            anyNonZero |= value > 0.0;
            result[index] = value;
        }

        if (!anyNonZero)
        {
            throw new ArgumentException("A dash array cannot contain only zeroes.", nameof(values));
        }

        return result;
    }
}
