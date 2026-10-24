using System.Globalization;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// An appearance stream a caller supplies for an annotation the engine would otherwise generate.
/// </summary>
/// <remarks>
/// <para>
/// The content is the operator stream itself, written in the appearance's own coordinate system: the
/// lower-left corner of <see cref="BoundingBox"/> is the origin, and the box is mapped onto the
/// annotation rectangle when the page is drawn. Only the operators matter here — the engine wraps them
/// in the form XObject and supplies the <c>/BBox</c>, the <c>/Resources</c> and the compression.
/// </para>
/// <para>
/// A font the content draws text with is registered as <c>/Helv</c>, which means the content says
/// <c>/Helv 12 Tf</c> for a twelve-point face. The standard-font restriction of
/// <see cref="PdfStandardTextStyle"/> applies for the same reason: the appearance must not carry a font
/// program.
/// </para>
/// </remarks>
/// <param name="Content">The appearance operators, as ASCII text.</param>
/// <param name="BoundingBox">
/// The box the operators are drawn in, or <see langword="null"/> for a box the size of the annotation
/// rectangle with its origin at the lower-left corner.
/// </param>
/// <param name="Font">The standard font the content selects as <c>/Helv</c>, or <see langword="null"/> when it draws no text.</param>
public sealed record PdfAnnotationAppearance(
    string Content,
    PdfRectangle? BoundingBox = null,
    StandardFont? Font = null)
{
    /// <summary>Validates the appearance.</summary>
    /// <returns>Every problem found, or an empty list when the appearance is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Content))
        {
            errors.Add("the appearance content is empty, so the annotation would draw nothing.");
        }

        if (BoundingBox is { } box && box.IsEmpty)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the appearance bounding box {box} encloses no area."));
        }

        if (Font is { } font && !Enum.IsDefined(font))
        {
            errors.Add($"the appearance font '{(int)font}' is not one the engine writes.");
        }

        return errors;
    }

    /// <summary>Formats the appearance for diagnostics.</summary>
    /// <returns>The number of bytes and the bounding box.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"appearance of {Content.Length} character(s){(BoundingBox is { } box ? $" in {box}" : string.Empty)}");
}
