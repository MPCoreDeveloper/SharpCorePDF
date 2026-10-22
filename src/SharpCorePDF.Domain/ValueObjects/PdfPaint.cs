namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The colour, opacity and fill rule used to paint a path or a glyph.
/// </summary>
/// <remarks>
/// Alpha is composited through a PDF <c>/ExtGState</c>; it is not a fourth colour component. A paint
/// with an alpha of <c>1</c> therefore needs no graphics state and keeps the content stream smaller.
/// </remarks>
public sealed record PdfPaint
{
    /// <summary>Initializes a new instance of the <see cref="PdfPaint"/> record.</summary>
    /// <param name="color">The colour to paint with.</param>
    /// <param name="alpha">The opacity, from <c>0</c> to <c>1</c>.</param>
    /// <param name="fillRule">The rule used when the paint fills a path.</param>
    /// <param name="colorSpace">The device colour space the colour is written in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is outside 0–1 or not finite, or an enum value is not defined.</exception>
    public PdfPaint(
        PdfColor color,
        double alpha = 1.0,
        PdfFillRule fillRule = PdfFillRule.NonZero,
        PdfColorSpace colorSpace = PdfColorSpace.DeviceRGB)
    {
        if (!double.IsFinite(alpha) || alpha is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "An alpha must be finite and between 0 and 1.");
        }

        if (!Enum.IsDefined(fillRule))
        {
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not one the engine defines.");
        }

        if (!Enum.IsDefined(colorSpace))
        {
            throw new ArgumentOutOfRangeException(nameof(colorSpace), colorSpace, "The colour space is not one the engine writes.");
        }

        Color = color;
        Alpha = alpha;
        FillRule = fillRule;
        ColorSpace = colorSpace;
    }

    /// <summary>Gets opaque black, the paint used when a caller states none.</summary>
    public static PdfPaint Black { get; } = new(PdfColor.Black);

    /// <summary>Gets the colour to paint with.</summary>
    public PdfColor Color { get; }

    /// <summary>Gets the opacity, from <c>0</c> to <c>1</c>.</summary>
    public double Alpha { get; }

    /// <summary>Gets the rule used when the paint fills a path.</summary>
    public PdfFillRule FillRule { get; }

    /// <summary>Gets the device colour space the colour is written in.</summary>
    public PdfColorSpace ColorSpace { get; }
}
