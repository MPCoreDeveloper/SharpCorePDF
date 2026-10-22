namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A position in the coordinate system of a page, measured in points from the top-left corner with
/// <c>Y</c> increasing downwards.
/// </summary>
/// <remarks>
/// A point is a value: two points with the same coordinates are equal, and neither can be changed
/// after it has been created. The authoring surface uses top-left coordinates and converts to PDF user
/// space when a page is written.
/// </remarks>
public readonly record struct PdfPoint
{
    /// <summary>Initializes a new instance of the <see cref="PdfPoint"/> struct.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    public PdfPoint(double x, double y)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "A horizontal coordinate must be finite.");
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "A vertical coordinate must be finite.");
        }

        X = x;
        Y = y;
    }

    /// <summary>Gets the origin, the top-left corner of a page.</summary>
    public static PdfPoint Origin { get; } = new(0, 0);

    /// <summary>Gets the horizontal coordinate.</summary>
    public double X { get; }

    /// <summary>Gets the vertical coordinate.</summary>
    public double Y { get; }

    /// <summary>Formats the point as <c>(x, y)</c> using invariant culture.</summary>
    /// <returns>The formatted point.</returns>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X}, {Y})");
}
