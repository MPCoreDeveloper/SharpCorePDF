using System.Globalization;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A set of form fields to write into a document at once, with the style the fields that state none
/// are drawn in.
/// </summary>
/// <remarks>
/// <para>
/// A document has one form: its fields are the leaves of a single field tree, addressed by name. This
/// type is that set as a caller states it, so the names can be checked against one another before any
/// of them reaches a page — two fields cannot share a name, because a document would have to choose
/// which of them a value belongs to.
/// </para>
/// <para>
/// The fields keep the order they were stated in, and that is the order they are written in: a viewer
/// that walks the form shows them in that order, and tabbing follows it.
/// </para>
/// </remarks>
public sealed record PdfForm
{
    /// <summary>Gets a form with no fields, which removes every field of the document it is applied to.</summary>
    public static PdfForm Empty { get; } = new();

    /// <summary>Gets the fields of the form, in the order they are written.</summary>
    public IReadOnlyList<PdfFormField> Fields { get; init; } = [];

    /// <summary>Gets the style a field that states none of its own is drawn in.</summary>
    public PdfStandardTextStyle? DefaultTextStyle { get; init; }

    /// <summary>Creates a form from the fields to write.</summary>
    /// <param name="fields">The fields, in the order they are written.</param>
    /// <returns>The form.</returns>
    public static PdfForm Create(params PdfFormField[] fields) => new() { Fields = fields ?? [] };

    /// <summary>States the style a field that carries none of its own is drawn in.</summary>
    /// <param name="style">The style to use.</param>
    /// <returns>The new form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is <see langword="null"/>.</exception>
    public PdfForm WithDefaultStyle(PdfStandardTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return this with { DefaultTextStyle = style };
    }

    /// <summary>Validates every field of the form, and the names against one another.</summary>
    /// <returns>Every problem found, or an empty list when the form is writable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (DefaultTextStyle is { } style)
        {
            errors.AddRange(style.Validate());
        }

        var names = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < Fields.Count; index++)
        {
            var field = Fields[index];

            if (field is null)
            {
                errors.Add($"field {index} is null.");
                continue;
            }

            foreach (var problem in field.Validate())
            {
                errors.Add($"field {index} ('{field.Name}'): {problem}");
            }

            if (names.TryGetValue(field.Name, out var first))
            {
                errors.Add(
                    $"field {index} ('{field.Name}'): the name is already used by field {first}; a document addresses a field by its name.");
            }
            else
            {
                names[field.Name] = index;
            }
        }

        return errors;
    }

    /// <summary>Formats the form for diagnostics.</summary>
    /// <returns>The number of fields and their names.</returns>
    public override string ToString() =>
        Fields.Count == 0
            ? "form with no fields"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"form with {Fields.Count} field(s): {string.Join(", ", Fields.Select(field => field?.Name ?? "(null)"))}");
}
