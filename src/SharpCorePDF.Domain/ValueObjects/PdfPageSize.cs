namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The dimensions of a page expressed in PDF user space units (1 unit = 1/72 inch).
/// </summary>
/// <remarks>
/// A page size is always stored as it is stored in the document: the <c>/MediaBox</c> width and
/// height of the page, before any <see cref="PageRotation"/> is applied. Use
/// <see cref="Rotate"/> to obtain the size as a viewer displays it.
/// </remarks>
public readonly record struct PdfPageSize
{
    /// <summary>The number of PDF user space units in one inch.</summary>
    public const double PointsPerInch = 72d;

    /// <summary>The number of millimetres in one inch.</summary>
    public const double MillimetersPerInch = 25.4d;

    /// <summary>Initializes a new instance of the <see cref="PdfPageSize"/> struct.</summary>
    /// <param name="widthPoints">The width in PDF user space units; must be finite and greater than zero.</param>
    /// <param name="heightPoints">The height in PDF user space units; must be finite and greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not a finite, positive number.</exception>
    public PdfPageSize(double widthPoints, double heightPoints)
    {
        if (!double.IsFinite(widthPoints) || widthPoints <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(widthPoints),
                widthPoints,
                "A page width must be a finite number greater than zero.");
        }

        if (!double.IsFinite(heightPoints) || heightPoints <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heightPoints),
                heightPoints,
                "A page height must be a finite number greater than zero.");
        }

        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
    }

    /// <summary>Gets the width in PDF user space units.</summary>
    public double WidthPoints { get; }

    /// <summary>Gets the height in PDF user space units.</summary>
    public double HeightPoints { get; }

    /// <summary>Gets the A5 size (148 x 210 mm).</summary>
    public static PdfPageSize A5 { get; } = FromMillimeters(148d, 210d);

    /// <summary>Gets the A4 size (210 x 297 mm).</summary>
    public static PdfPageSize A4 { get; } = FromMillimeters(210d, 297d);

    /// <summary>Gets the A3 size (297 x 420 mm).</summary>
    public static PdfPageSize A3 { get; } = FromMillimeters(297d, 420d);

    /// <summary>Gets the US Letter size (8.5 x 11 inch).</summary>
    public static PdfPageSize Letter { get; } = FromInches(8.5d, 11d);

    /// <summary>Gets the US Legal size (8.5 x 14 inch).</summary>
    public static PdfPageSize Legal { get; } = FromInches(8.5d, 14d);

    /// <summary>Gets the width in millimetres.</summary>
    public double WidthMillimeters => WidthPoints / PointsPerInch * MillimetersPerInch;

    /// <summary>Gets the height in millimetres.</summary>
    public double HeightMillimeters => HeightPoints / PointsPerInch * MillimetersPerInch;

    /// <summary>Gets the width in inches.</summary>
    public double WidthInches => WidthPoints / PointsPerInch;

    /// <summary>Gets the height in inches.</summary>
    public double HeightInches => HeightPoints / PointsPerInch;

    /// <summary>Gets a value indicating whether the width exceeds the height.</summary>
    public bool IsLandscape => WidthPoints > HeightPoints;

    /// <summary>Gets a value indicating whether the height is greater than or equal to the width.</summary>
    public bool IsPortrait => HeightPoints >= WidthPoints;

    /// <summary>Creates a size from PDF user space units.</summary>
    /// <param name="widthPoints">The width in PDF user space units.</param>
    /// <param name="heightPoints">The height in PDF user space units.</param>
    /// <returns>The created size.</returns>
    public static PdfPageSize FromPoints(double widthPoints, double heightPoints) => new(widthPoints, heightPoints);

    /// <summary>Creates a size from millimetres.</summary>
    /// <param name="widthMillimeters">The width in millimetres.</param>
    /// <param name="heightMillimeters">The height in millimetres.</param>
    /// <returns>The created size.</returns>
    public static PdfPageSize FromMillimeters(double widthMillimeters, double heightMillimeters) =>
        new(widthMillimeters / MillimetersPerInch * PointsPerInch,
            heightMillimeters / MillimetersPerInch * PointsPerInch);

    /// <summary>Creates a size from inches.</summary>
    /// <param name="widthInches">The width in inches.</param>
    /// <param name="heightInches">The height in inches.</param>
    /// <returns>The created size.</returns>
    public static PdfPageSize FromInches(double widthInches, double heightInches) =>
        new(widthInches * PointsPerInch, heightInches * PointsPerInch);

    /// <summary>Swaps the width and the height.</summary>
    /// <returns>The transposed size.</returns>
    public PdfPageSize Transpose() => new(HeightPoints, WidthPoints);

    /// <summary>Gets the size as a viewer displays it once <paramref name="rotation"/> has been applied.</summary>
    /// <param name="rotation">The rotation declared by the page.</param>
    /// <returns>The displayed size, which is transposed for quarter turns.</returns>
    public PdfPageSize Rotate(PageRotation rotation) => rotation.SwapsPageDimensions ? Transpose() : this;

    /// <summary>Formats the size as "width x height pt".</summary>
    /// <returns>The formatted size.</returns>
    public override string ToString() => $"{WidthPoints:0.##} x {HeightPoints:0.##} pt";
}
