namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A rectangle in page space: the space a page is described in, with the origin at the lower left
/// corner of the page and one unit equal to one point (1/72 inch).
/// </summary>
/// <remarks>
/// <para>
/// A rectangle is stored as its four edges rather than as an origin and a size, because that is how a
/// PDF writes one — an annotation's <c>/Rect</c>, a destination's <c>/FitR</c> — and normalising the
/// corners on the way in means a caller never has to ask whether the first pair is the lower left
/// one. Document order is not guaranteed to be that way round: a producer is free to write
/// <c>[100 700 20 500]</c>, and <see cref="FromCorners"/> turns it into the same rectangle a viewer
/// draws.
/// </para>
/// <para>
/// The rotation a page declares is not applied. A rectangle is what the document says about the page,
/// in the same way <see cref="ReadModels.PdfPageInfo.Size"/> is the size before rotation; a caller
/// that displays a rotated page maps the rectangle the way the viewer does.
/// </para>
/// </remarks>
public sealed record PdfRectangle
{
    /// <summary>Initializes a new instance of the <see cref="PdfRectangle"/> class.</summary>
    /// <param name="left">The x coordinate of the left edge.</param>
    /// <param name="bottom">The y coordinate of the bottom edge.</param>
    /// <param name="right">The x coordinate of the right edge; not smaller than <paramref name="left"/>.</param>
    /// <param name="top">The y coordinate of the top edge; not smaller than <paramref name="bottom"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">An edge is not a finite number, or a right or top edge is smaller than its opposite.</exception>
    public PdfRectangle(double left, double bottom, double right, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(bottom) || !double.IsFinite(right) || !double.IsFinite(top))
        {
            throw new ArgumentOutOfRangeException(nameof(left), "Every edge of a rectangle must be a finite number.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(left, right);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bottom, top);

        Left = left;
        Bottom = bottom;
        Right = right;
        Top = top;
    }

    /// <summary>Gets the x coordinate of the left edge.</summary>
    public double Left { get; }

    /// <summary>Gets the y coordinate of the bottom edge.</summary>
    public double Bottom { get; }

    /// <summary>Gets the x coordinate of the right edge.</summary>
    public double Right { get; }

    /// <summary>Gets the y coordinate of the top edge.</summary>
    public double Top { get; }

    /// <summary>Gets the width of the rectangle.</summary>
    public double Width => Right - Left;

    /// <summary>Gets the height of the rectangle.</summary>
    public double Height => Top - Bottom;

    /// <summary>Gets a value indicating whether the rectangle encloses no area.</summary>
    public bool IsEmpty => Width <= 0d || Height <= 0d;

    /// <summary>
    /// Creates a rectangle from two opposite corners in either order, normalising them the way a
    /// viewer does.
    /// </summary>
    /// <param name="x1">The x coordinate of one corner.</param>
    /// <param name="y1">The y coordinate of the same corner.</param>
    /// <param name="x2">The x coordinate of the opposite corner.</param>
    /// <param name="y2">The y coordinate of the same opposite corner.</param>
    /// <returns>The normalised rectangle.</returns>
    public static PdfRectangle FromCorners(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

    /// <summary>Formats the rectangle for diagnostics.</summary>
    /// <returns>The four edges, in points.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"[{Left} {Bottom} {Right} {Top}]");
}
