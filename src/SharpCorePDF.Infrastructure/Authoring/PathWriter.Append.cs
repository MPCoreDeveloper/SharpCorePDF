using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

internal sealed partial class PathWriter
{
    /// <summary>Gets the stream this writer appends to.</summary>
    public ContentStreamBuilder Builder => builder;

    /// <summary>Appends an existing path.</summary>
    /// <param name="path">The path.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter Append(PdfPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        foreach (var segment in path.Segments)
        {
            switch (segment)
            {
                case PdfPathSegment.MoveTo move:
                    MoveTo(move.Point);
                    break;

                case PdfPathSegment.LineTo line:
                    LineTo(line.Point);
                    break;

                case PdfPathSegment.CubicTo cubic:
                    CubicTo(cubic.Control1, cubic.Control2, cubic.End);
                    break;

                case PdfPathSegment.QuadraticTo quadratic:
                    QuadraticTo(quadratic.Control, quadratic.End);
                    break;

                case PdfPathSegment.Close:
                    Close();
                    break;

                default:
                    throw new ArgumentException(
                        $"The path segment {segment.GetType().Name} is not one the engine writes.",
                        nameof(path));
            }
        }

        if (path.IsClosed && path.Segments[^1] is not PdfPathSegment.Close)
        {
            Close();
        }

        return this;
    }

    /// <summary>Paints the accumulated path and clears it.</summary>
    /// <param name="fill">The fill, or null for no fill.</param>
    /// <param name="stroke">The stroke, or null for no stroke.</param>
    /// <returns>This writer, for chaining.</returns>
    /// <exception cref="ArgumentException">Neither a fill nor a stroke is supplied.</exception>
    public PathWriter Paint(PdfPaint? fill, PdfStroke? stroke)
    {
        if (fill is null && stroke is null)
        {
            throw new ArgumentException("A path needs a fill, a stroke, or both before it can be painted.");
        }

        if (!hasPath)
        {
            throw new InvalidOperationException("A path cannot be painted before it has been constructed.");
        }

        if (fill is not null)
        {
            builder.GraphicsState.SetFill(fill);
        }

        if (stroke is not null)
        {
            builder.GraphicsState.SetStroke(stroke);
        }

        var evenOdd = fill?.FillRule == PdfFillRule.EvenOdd;
        var operatorName = (fill, stroke) switch
        {
            (not null, not null) => evenOdd ? "B*" : "B",
            (not null, null) => evenOdd ? "f*" : "f",
            (null, not null) => "S",
            _ => throw new InvalidOperationException("Unreachable paint combination."),
        };

        builder.WriteOperator(operatorName);
        Reset();
        return this;
    }

    /// <summary>Fills the accumulated path and clears it.</summary>
    /// <param name="fill">The fill.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter Fill(PdfPaint fill) => Paint(fill, null);

    /// <summary>Strokes the accumulated path and clears it.</summary>
    /// <param name="stroke">The stroke.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter Stroke(PdfStroke stroke) => Paint(null, stroke);

    /// <summary>Uses the accumulated path as a clip and clears it.</summary>
    /// <param name="fillRule">The rule that decides what is inside.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter Clip(PdfFillRule fillRule = PdfFillRule.NonZero)
    {
        if (!hasPath)
        {
            throw new InvalidOperationException("A path cannot be clipped before it has been constructed.");
        }

        if (!Enum.IsDefined(fillRule))
        {
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not one the engine defines.");
        }

        builder.WriteOperator(fillRule == PdfFillRule.EvenOdd ? "W*" : "W");
        builder.WriteOperator("n");
        Reset();
        return this;
    }

    /// <summary>Ends the path without painting it and writes <c>n</c>.</summary>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter EndPath()
    {
        builder.WriteOperator("n");
        Reset();
        return this;
    }

    private void Reset()
    {
        hasCurrentPoint = false;
        hasPath = false;
    }
}
