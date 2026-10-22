using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

internal sealed partial class GraphicsState
{
    /// <summary>Sets the dash pattern and writes <c>d</c>.</summary>
    /// <param name="dashArray">The pattern; null or empty means a solid line.</param>
    /// <param name="phase">The phase.</param>
    public void SetDash(IReadOnlyList<double>? dashArray, double phase)
    {
        if (!double.IsFinite(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "A dash phase must be finite.");
        }

        if (dashArray is null || dashArray.Count == 0)
        {
            builder.Write("[ ] ");
            builder.WriteNumber(phase);
            builder.Write(" d\n");
            return;
        }

        var anyNonZero = false;
        builder.Write("[");
        for (var index = 0; index < dashArray.Count; index++)
        {
            var value = dashArray[index];
            if (!double.IsFinite(value) || value < 0.0)
            {
                throw new ArgumentException("Every dash length must be finite and non-negative.", nameof(dashArray));
            }

            anyNonZero |= value > 0.0;
            if (index > 0)
            {
                builder.Write(" ");
            }

            builder.WriteNumber(value);
        }

        if (!anyNonZero)
        {
            throw new ArgumentException("A dash array cannot contain only zeroes.", nameof(dashArray));
        }

        builder.Write("] ");
        builder.WriteNumber(phase);
        builder.Write(" d\n");
    }

    /// <summary>Sets both alpha values and writes an <c>/ExtGState</c> selection.</summary>
    /// <param name="alpha">The alpha, from 0 to 1.</param>
    /// <param name="blendMode">The blend mode.</param>
    public void SetAlpha(double alpha, PdfBlendMode blendMode = PdfBlendMode.Normal)
    {
        if (!double.IsFinite(alpha) || alpha is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "An alpha must be finite and between 0 and 1.");
        }

        if (!Enum.IsDefined(blendMode))
        {
            throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, "The blend mode is not one the engine defines.");
        }

        var changed = Differs(current.FillAlpha, alpha)
            || Differs(current.StrokeAlpha, alpha)
            || current.BlendMode != blendMode;

        current.FillAlpha = alpha;
        current.StrokeAlpha = alpha;
        current.BlendMode = blendMode;
        current.Public = ReplaceAlpha(current.Public, alpha, blendMode);

        if (changed)
        {
            WriteTransparency();
        }
    }

    /// <summary>Sets the clipping path and writes <c>W</c>/<c>W*</c> followed by <c>n</c>.</summary>
    /// <param name="path">The path.</param>
    /// <param name="fillRule">The rule that decides what is inside.</param>
    public void SetClip(PdfPath path, PdfFillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(path);

        new PathWriter(builder).Append(path).Clip(fillRule);
        current.Public = ReplaceClip(current.Public, path, fillRule);
    }

    /// <summary>Refuses a stream that still has a saved state.</summary>
    /// <exception cref="InvalidOperationException">At least one save has no matching restore.</exception>
    public void EnsureBalanced()
    {
        if (saved.Count != 0)
        {
            throw new InvalidOperationException(
                $"The content stream ends with {saved.Count} graphics state save(s) that were not restored.");
        }
    }

    private void WriteTransparency()
    {
        var name = builder.Resources.Register(
            current.FillAlpha,
            current.StrokeAlpha,
            current.BlendMode,
            current.StrokeAdjustment);
        builder.WriteNameOperator("gs", name);
    }

    private sealed class CurrentState
    {
        public CurrentState(
            PdfGraphicsState publicState,
            double fillAlpha,
            double strokeAlpha,
            PdfBlendMode blendMode,
            PdfStrokeAdjustment? strokeAdjustment)
        {
            Public = publicState;
            FillAlpha = fillAlpha;
            StrokeAlpha = strokeAlpha;
            BlendMode = blendMode;
            StrokeAdjustment = strokeAdjustment;
        }

        public PdfGraphicsState Public { get; set; }

        public double FillAlpha { get; set; }

        public double StrokeAlpha { get; set; }

        public PdfBlendMode BlendMode { get; set; }

        public PdfStrokeAdjustment? StrokeAdjustment { get; set; }

        public CurrentState Clone() => new(Public, FillAlpha, StrokeAlpha, BlendMode, StrokeAdjustment);
    }

    private sealed record SavedState(CurrentState State);
}
