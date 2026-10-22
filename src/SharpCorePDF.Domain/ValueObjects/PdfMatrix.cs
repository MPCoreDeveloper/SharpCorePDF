namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A two-dimensional affine transform in the order PDF uses: <c>[a b c d e f]</c>, where a point
/// <c>(x, y)</c> maps to <c>(a·x + c·y + e, b·x + d·y + f)</c>.
/// </summary>
/// <remarks>
/// The default value is not the identity; use <see cref="Identity"/> when a transform is optional.
/// Multiplication follows PDF's row-vector convention: <c>left.Multiply(right)</c> applies
/// <c>left</c> first and then <c>right</c>.
/// </remarks>
public readonly record struct PdfMatrix
{
    /// <summary>Initializes a new instance of the <see cref="PdfMatrix"/> struct.</summary>
    /// <param name="a">The first row, first column.</param>
    /// <param name="b">The first row, second column.</param>
    /// <param name="c">The second row, first column.</param>
    /// <param name="d">The second row, second column.</param>
    /// <param name="e">The horizontal translation.</param>
    /// <param name="f">The vertical translation.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component is not finite.</exception>
    public PdfMatrix(double a, double b, double c, double d, double e, double f)
    {
        Validate(a, nameof(a));
        Validate(b, nameof(b));
        Validate(c, nameof(c));
        Validate(d, nameof(d));
        Validate(e, nameof(e));
        Validate(f, nameof(f));

        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    /// <summary>Gets the identity transform.</summary>
    public static PdfMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>Gets the first row, first column.</summary>
    public double A { get; }

    /// <summary>Gets the first row, second column.</summary>
    public double B { get; }

    /// <summary>Gets the second row, first column.</summary>
    public double C { get; }

    /// <summary>Gets the second row, second column.</summary>
    public double D { get; }

    /// <summary>Gets the horizontal translation.</summary>
    public double E { get; }

    /// <summary>Gets the vertical translation.</summary>
    public double F { get; }

    /// <summary>Creates a translation.</summary>
    /// <param name="x">The horizontal distance.</param>
    /// <param name="y">The vertical distance.</param>
    /// <returns>The translation.</returns>
    public static PdfMatrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>Creates a uniform scale.</summary>
    /// <param name="factor">The scale factor on both axes.</param>
    /// <returns>The scale.</returns>
    public static PdfMatrix Scale(double factor) => Scale(factor, factor);

    /// <summary>Creates a scale with independent axes.</summary>
    /// <param name="x">The horizontal factor.</param>
    /// <param name="y">The vertical factor.</param>
    /// <returns>The scale.</returns>
    public static PdfMatrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>Creates a counter-clockwise rotation.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The rotation.</returns>
    public static PdfMatrix Rotate(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "An angle must be finite.");
        }

        var radians = degrees * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new PdfMatrix(cosine, sine, -sine, cosine, 0, 0);
    }

    /// <summary>Multiplies this transform by another, applying this transform first.</summary>
    /// <param name="other">The transform to apply after this one.</param>
    /// <returns>The composed transform.</returns>
    public PdfMatrix Multiply(PdfMatrix other) =>
        new(
            (A * other.A) + (B * other.C),
            (A * other.B) + (B * other.D),
            (C * other.A) + (D * other.C),
            (C * other.B) + (D * other.D),
            (E * other.A) + (F * other.C) + other.E,
            (E * other.B) + (F * other.D) + other.F);

    /// <summary>Applies the transform to a point.</summary>
    /// <param name="point">The point to transform.</param>
    /// <returns>The transformed point.</returns>
    public PdfPoint Transform(PdfPoint point) =>
        new((A * point.X) + (C * point.Y) + E, (B * point.X) + (D * point.Y) + F);

    /// <summary>Formats the six components in PDF order using invariant culture.</summary>
    /// <returns>The formatted transform.</returns>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[{A} {B} {C} {D} {E} {F}]");

    private static void Validate(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "A matrix component must be finite.");
        }
    }
}
