using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Serialises path construction and painting operators into a content stream.
/// </summary>
internal sealed partial class PathWriter
{
    private readonly ContentStreamBuilder builder;
    private PdfPoint currentPoint;
    private PdfPoint subpathStart;
    private bool hasCurrentPoint;
    private bool hasPath;

    /// <summary>Initializes a new instance of the <see cref="PathWriter"/> class.</summary>
    /// <param name="builder">The stream to write to.</param>
    public PathWriter(ContentStreamBuilder builder) => this.builder = builder;

    /// <summary>Starts a new subpath and writes <c>m</c>.</summary>
    /// <param name="point">The start point.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter MoveTo(PdfPoint point)
    {
        builder.WritePointOperator("m", point);
        currentPoint = point;
        subpathStart = point;
        hasCurrentPoint = true;
        hasPath = true;
        return this;
    }

    /// <summary>Draws a line and writes <c>l</c>.</summary>
    /// <param name="point">The end point.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter LineTo(PdfPoint point)
    {
        EnsureCurrentPoint();
        builder.WritePointOperator("l", point);
        currentPoint = point;
        return this;
    }

    /// <summary>Draws a cubic Bézier curve and writes <c>c</c>.</summary>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter CubicTo(PdfPoint control1, PdfPoint control2, PdfPoint end)
    {
        EnsureCurrentPoint();
        WritePoint(control1);
        builder.Write(" ");
        WritePoint(control2);
        builder.Write(" ");
        WritePoint(end);
        builder.Write(" ");
        builder.WriteOperator("c");
        currentPoint = end;
        return this;
    }

    /// <summary>Draws a cubic curve whose first control point is the current point and writes <c>v</c>.</summary>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The end point.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter CubicToV(PdfPoint control2, PdfPoint end)
    {
        EnsureCurrentPoint();
        WritePoint(control2);
        builder.Write(" ");
        WritePoint(end);
        builder.Write(" ");
        builder.WriteOperator("v");
        currentPoint = end;
        return this;
    }

    /// <summary>Draws a cubic curve that ends at the second control point and writes <c>y</c>.</summary>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The end point and second control point.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter CubicToY(PdfPoint control1, PdfPoint control2)
    {
        EnsureCurrentPoint();
        WritePoint(control1);
        builder.Write(" ");
        WritePoint(control2);
        builder.Write(" ");
        builder.WriteOperator("y");
        currentPoint = control2;
        return this;
    }

    /// <summary>Draws a quadratic Bézier curve as an equivalent cubic curve.</summary>
    /// <param name="control">The quadratic control point.</param>
    /// <param name="end">The end point.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter QuadraticTo(PdfPoint control, PdfPoint end)
    {
        EnsureCurrentPoint();
        var control1 = new PdfPoint(
            currentPoint.X + (2.0 / 3.0 * (control.X - currentPoint.X)),
            currentPoint.Y + (2.0 / 3.0 * (control.Y - currentPoint.Y)));
        var control2 = new PdfPoint(
            end.X + (2.0 / 3.0 * (control.X - end.X)),
            end.Y + (2.0 / 3.0 * (control.Y - end.Y)));

        return CubicTo(control1, control2, end);
    }

    /// <summary>Closes the current subpath and writes <c>h</c>.</summary>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter Close()
    {
        EnsureCurrentPoint();
        builder.WriteOperator("h");
        currentPoint = subpathStart;
        return this;
    }

    /// <summary>Adds a rectangle and writes <c>re</c>.</summary>
    /// <param name="left">The left edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>This writer, for chaining.</returns>
    public PathWriter Rectangle(double left, double bottom, double width, double height)
    {
        builder.WriteNumber(left);
        builder.Write(" ");
        builder.WriteNumber(bottom);
        builder.Write(" ");
        builder.WriteNumber(width);
        builder.Write(" ");
        builder.WriteNumber(height);
        builder.Write(" ");
        builder.WriteOperator("re");

        currentPoint = new PdfPoint(left, bottom);
        subpathStart = currentPoint;
        hasCurrentPoint = true;
        hasPath = true;
        return this;
    }

    private void WritePoint(PdfPoint point)
    {
        builder.WriteNumber(point.X);
        builder.Write(" ");
        builder.WriteNumber(point.Y);
    }

    private void EnsureCurrentPoint()
    {
        if (!hasCurrentPoint)
        {
            throw new InvalidOperationException("A path cannot continue before it has been started with MoveTo or Rectangle.");
        }
    }
}
