using System.Globalization;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>How the border of an annotation is drawn.</summary>
public enum PdfBorderStyle
{
    /// <summary>One solid line.</summary>
    Solid = 0,

    /// <summary>A dashed line, with the pattern of <see cref="PdfBorder.Dash"/>.</summary>
    Dashed = 1,

    /// <summary>A line that looks engraved into the page.</summary>
    Beveled = 2,

    /// <summary>A line that looks pressed into the page.</summary>
    Inset = 3,

    /// <summary>A single line under the annotation, as a hyperlink is drawn.</summary>
    Underline = 4,
}

/// <summary>
/// The border of an annotation: its width, its style and, for a dashed border, the pattern.
/// </summary>
/// <remarks>
/// <para>
/// The border is written twice: once as the older <c>/Border</c> array, because that is what every
/// reader reads, and once as <c>/BS</c> when the style or the pattern is more than a solid line, because
/// the array cannot express them. A reader that knows only one of the two still draws the border.
/// </para>
/// <para>
/// A width of zero is legal and means no border is drawn, which is how a link annotation is written.
/// </para>
/// </remarks>
public sealed record PdfBorder
{
    /// <summary>Gets the border a caller gets when none is stated: one point wide, solid.</summary>
    public static PdfBorder Default { get; } = new();

    /// <summary>Gets the width of the border in points.</summary>
    public double Width { get; init; } = 1.0;

    /// <summary>Gets how the border is drawn.</summary>
    public PdfBorderStyle Style { get; init; } = PdfBorderStyle.Solid;

    /// <summary>
    /// Gets the dash pattern, in points: alternating lengths of ink and of gap. It is only written for
    /// <see cref="PdfBorderStyle.Dashed"/>, and an empty pattern means the reader's own default.
    /// </summary>
    public IReadOnlyList<double> Dash { get; init; } = [];

    /// <summary>Validates the border.</summary>
    /// <returns>Every problem found, or an empty list when the border is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!double.IsFinite(Width) || Width < 0.0)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the border width {Width} is not zero or a positive number of points."));
        }

        if (!Enum.IsDefined(Style))
        {
            errors.Add($"border style '{(int)Style}' is not one the engine writes.");
        }

        if ((Dash ?? []).Any(length => !double.IsFinite(length) || length <= 0.0))
        {
            errors.Add("the dash pattern has a length that is not a positive number of points.");
        }

        return errors;
    }

    /// <summary>Formats the border for diagnostics.</summary>
    /// <returns>The width and the style.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Width}pt {Style}");
}
