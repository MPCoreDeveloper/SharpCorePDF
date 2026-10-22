using SharpCorePDF.Domain.ValueObjects;
using PdfAffine = SharpCorePDF.Domain.ValueObjects.PdfMatrix;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Tracks the graphics state of a content stream and writes the operators that change it.
/// </summary>
/// <remarks>
/// <c>q</c> pushes the complete state and <c>Q</c> pops it. A restore without a matching save is
/// refused at the call site, and <see cref="ContentStreamBuilder.Build"/> refuses a stream that still
/// has a saved state, so a caller cannot leave a clip or a transform behind by accident.
/// </remarks>
internal sealed partial class GraphicsState
{
    private readonly ContentStreamBuilder builder;
    private readonly Stack<SavedState> saved = new();
    private CurrentState current = new(PdfGraphicsState.Default, 1.0, 1.0, PdfBlendMode.Normal, null);

    /// <summary>Initializes a new instance of the <see cref="GraphicsState"/> class.</summary>
    /// <param name="builder">The stream the state belongs to.</param>
    public GraphicsState(ContentStreamBuilder builder) => this.builder = builder;

    /// <summary>Gets the state that is currently in effect.</summary>
    public PdfGraphicsState Current => current.Public;

    /// <summary>Gets the number of saved states that have not been restored.</summary>
    public int Depth => saved.Count;

    /// <summary>Saves the state and writes <c>q</c>.</summary>
    public void Save()
    {
        saved.Push(new SavedState(current.Clone()));
        builder.WriteOperator("q");
    }

    /// <summary>Restores the most recent saved state and writes <c>Q</c>.</summary>
    /// <exception cref="InvalidOperationException">There is no saved state.</exception>
    public void Restore()
    {
        if (saved.Count == 0)
        {
            throw new InvalidOperationException(
                "The graphics state cannot be restored because no matching save was made.");
        }

        builder.WriteOperator("Q");
        current = saved.Pop().State;
    }

    /// <summary>Concatenates a transform and writes <c>cm</c>.</summary>
    /// <param name="matrix">The transform.</param>
    public void Transform(PdfAffine matrix)
    {
        builder.WriteMatrixOperator("cm", matrix);
        current.Public = ReplaceTransform(current.Public, matrix);
    }

    /// <summary>Sets the fill paint and writes the colour operator.</summary>
    /// <param name="paint">The paint.</param>
    public void SetFill(PdfPaint paint)
    {
        ArgumentNullException.ThrowIfNull(paint);
        WriteColor(paint, fill: true);
        current.Public = ReplaceFill(current.Public, paint);

        if (Differs(current.FillAlpha, paint.Alpha))
        {
            current.FillAlpha = paint.Alpha;
            WriteTransparency();
        }
    }

    /// <summary>Sets the stroke style and its paint.</summary>
    /// <param name="stroke">The stroke.</param>
    public void SetStroke(PdfStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);

        SetLineWidth(stroke.Width);
        SetLineCap(stroke.Cap);
        SetLineJoin(stroke.Join);
        SetMiterLimit(stroke.MiterLimit);
        SetDash(stroke.DashArray, stroke.DashPhase);
        WriteColor(stroke.Paint, fill: false);

        var adjustment = stroke.StrokeAdjustment == PdfStrokeAdjustment.Default ? null : stroke.StrokeAdjustment;
        var transparencyChanged = Differs(current.StrokeAlpha, stroke.Paint.Alpha)
            || current.StrokeAdjustment != adjustment;
        current.Public = ReplaceStroke(current.Public, stroke);
        current.StrokeAlpha = stroke.Paint.Alpha;
        current.StrokeAdjustment = adjustment;

        if (transparencyChanged)
        {
            WriteTransparency();
        }
    }

    private static bool Differs(double left, double right) => Math.Abs(left - right) > 1e-9;

    /// <summary>Sets the line width and writes <c>w</c>.</summary>
    /// <param name="width">The width in points.</param>
    public void SetLineWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A line width must be finite and greater than zero.");
        }

        builder.WriteNumberOperator("w", width);
    }

    /// <summary>Sets the line cap and writes <c>J</c>.</summary>
    /// <param name="cap">The cap.</param>
    public void SetLineCap(PdfLineCap cap)
    {
        if (!Enum.IsDefined(cap))
        {
            throw new ArgumentOutOfRangeException(nameof(cap), cap, "The line cap is not one the engine defines.");
        }

        builder.WriteNumberOperator("J", (int)cap);
    }

    /// <summary>Sets the line join and writes <c>j</c>.</summary>
    /// <param name="join">The join.</param>
    public void SetLineJoin(PdfLineJoin join)
    {
        if (!Enum.IsDefined(join))
        {
            throw new ArgumentOutOfRangeException(nameof(join), join, "The line join is not one the engine defines.");
        }

        builder.WriteNumberOperator("j", (int)join);
    }

    /// <summary>Sets the miter limit and writes <c>M</c>.</summary>
    /// <param name="limit">The limit, at least 1.</param>
    public void SetMiterLimit(double limit)
    {
        if (!double.IsFinite(limit) || limit < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "A miter limit must be finite and at least 1.");
        }

        builder.WriteNumberOperator("M", limit);
    }
}
