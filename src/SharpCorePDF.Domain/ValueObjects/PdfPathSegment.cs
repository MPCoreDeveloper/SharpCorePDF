namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// One segment of a path. The hierarchy is closed: the engine knows how to write each segment and a
/// caller cannot add a variant with an unknown operator.
/// </summary>
public abstract record PdfPathSegment
{
    private protected PdfPathSegment()
    {
    }

    /// <summary>Starts a new subpath at a point.</summary>
    /// <param name="Point">The point at which the subpath starts.</param>
    public sealed record MoveTo(PdfPoint Point) : PdfPathSegment;

    /// <summary>Adds a straight line from the current point.</summary>
    /// <param name="Point">The end point of the line.</param>
    public sealed record LineTo(PdfPoint Point) : PdfPathSegment;

    /// <summary>Adds a cubic Bézier curve from the current point.</summary>
    /// <param name="Control1">The first control point.</param>
    /// <param name="Control2">The second control point.</param>
    /// <param name="End">The end point of the curve.</param>
    public sealed record CubicTo(PdfPoint Control1, PdfPoint Control2, PdfPoint End) : PdfPathSegment;

    /// <summary>Adds a quadratic Bézier curve from the current point.</summary>
    /// <param name="Control">The control point.</param>
    /// <param name="End">The end point of the curve.</param>
    public sealed record QuadraticTo(PdfPoint Control, PdfPoint End) : PdfPathSegment;

    /// <summary>Closes the current subpath back to its start point.</summary>
    public sealed record Close : PdfPathSegment;
}
