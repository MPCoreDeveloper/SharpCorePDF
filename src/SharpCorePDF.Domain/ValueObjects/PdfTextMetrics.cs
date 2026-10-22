namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The vertical measurements of a font, expressed in font units. A typeface scales them by the
/// requested font size when it lays text out.
/// </summary>
/// <param name="Ascent">The distance from the baseline to the top of the font's ascent.</param>
/// <param name="Descent">The signed distance from the baseline to the bottom of the font's descent; normally negative.</param>
/// <param name="LineGap">The recommended extra space between lines.</param>
/// <param name="CapHeight">The height of capital letters above the baseline.</param>
/// <param name="XHeight">The height of lowercase letters above the baseline.</param>
/// <param name="UnitsPerEm">The number of font units in one em.</param>
public sealed record PdfTextMetrics(
    double Ascent,
    double Descent,
    double LineGap,
    double CapHeight,
    double XHeight,
    int UnitsPerEm)
{
    /// <summary>Gets the distance from the baseline to the top of the font's ascent.</summary>
    public double Ascent { get; } = Validate(Ascent, nameof(Ascent));

    /// <summary>Gets the signed distance from the baseline to the bottom of the font's descent.</summary>
    public double Descent { get; } = Validate(Descent, nameof(Descent));

    /// <summary>Gets the recommended extra space between lines.</summary>
    public double LineGap { get; } = Validate(LineGap, nameof(LineGap));

    /// <summary>Gets the height of capital letters above the baseline.</summary>
    public double CapHeight { get; } = Validate(CapHeight, nameof(CapHeight));

    /// <summary>Gets the height of lowercase letters above the baseline.</summary>
    public double XHeight { get; } = Validate(XHeight, nameof(XHeight));

    /// <summary>Gets the number of font units in one em.</summary>
    public int UnitsPerEm { get; } =
        UnitsPerEm > 0
            ? UnitsPerEm
            : throw new ArgumentOutOfRangeException(nameof(UnitsPerEm), UnitsPerEm, "A font must have at least one unit per em.");

    /// <summary>Gets the default line height at a font size: ascent plus descent plus line gap.</summary>
    /// <param name="fontSize">The font size in points.</param>
    /// <returns>The line height in points.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not finite or not greater than zero.</exception>
    public double LineHeight(double fontSize)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "A font size must be finite and greater than zero.");
        }

        return (Ascent - Descent + LineGap) * fontSize / UnitsPerEm;
    }

    private static double Validate(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "A font metric must be finite.");
        }

        return value;
    }
}
