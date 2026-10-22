namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// An immutable snapshot of the graphics state: transform, paints, opacity, blend mode and clipping.
/// </summary>
/// <remarks>
/// The canvas keeps a stack of these snapshots. A restore of the initial state is refused, so a page
/// can never be left with a transform or clip that its caller did not intend.
/// </remarks>
public sealed record PdfGraphicsState
{
    /// <summary>Initializes a new instance of the <see cref="PdfGraphicsState"/> record.</summary>
    /// <param name="transform">The current transform; null means the identity.</param>
    /// <param name="fill">The current fill paint; null means opaque black.</param>
    /// <param name="stroke">The current stroke; null means no stroke.</param>
    /// <param name="alpha">The opacity of both fills and strokes, from 0 to 1.</param>
    /// <param name="blendMode">The blend mode used when painting.</param>
    /// <param name="clipPath">The current clipping path, or null for no clip.</param>
    /// <param name="clipFillRule">The rule used by the current clipping path.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is outside 0–1 or an enum value is not defined.</exception>
    public PdfGraphicsState(
        PdfMatrix? transform = null,
        PdfPaint? fill = null,
        PdfStroke? stroke = null,
        double alpha = 1.0,
        PdfBlendMode blendMode = PdfBlendMode.Normal,
        PdfPath? clipPath = null,
        PdfFillRule clipFillRule = PdfFillRule.NonZero)
    {
        if (!double.IsFinite(alpha) || alpha is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "An alpha must be finite and between 0 and 1.");
        }

        if (!Enum.IsDefined(blendMode))
        {
            throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, "The blend mode is not one the engine defines.");
        }

        if (!Enum.IsDefined(clipFillRule))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clipFillRule),
                clipFillRule,
                "The fill rule is not one the engine defines.");
        }

        Transform = transform ?? PdfMatrix.Identity;
        Fill = fill ?? PdfPaint.Black;
        Stroke = stroke;
        Alpha = alpha;
        BlendMode = blendMode;
        ClipPath = clipPath;
        ClipFillRule = clipFillRule;
    }

    /// <summary>Gets the initial state: identity transform, opaque black fill and no stroke or clip.</summary>
    public static PdfGraphicsState Default { get; } = new();

    /// <summary>Gets the current transform.</summary>
    public PdfMatrix Transform { get; }

    /// <summary>Gets the current fill paint.</summary>
    public PdfPaint Fill { get; }

    /// <summary>Gets the current stroke, or null when no stroke is painted.</summary>
    public PdfStroke? Stroke { get; }

    /// <summary>Gets the opacity of both fills and strokes.</summary>
    public double Alpha { get; }

    /// <summary>Gets the blend mode used when painting.</summary>
    public PdfBlendMode BlendMode { get; }

    /// <summary>Gets the current clipping path, or null when nothing is clipped.</summary>
    public PdfPath? ClipPath { get; }

    /// <summary>Gets the rule used by the current clipping path.</summary>
    public PdfFillRule ClipFillRule { get; }
}
