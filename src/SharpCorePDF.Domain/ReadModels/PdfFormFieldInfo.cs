using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One field of an interactive form, as the document stores it: its fully-qualified name, its type and
/// value, its flags, and the widgets that display it.
/// </summary>
/// <remarks>
/// <para>
/// The name is fully qualified, <c>parent.child</c>, because that is how a PDF identifies a field and
/// how any other tool will refer to it; two fields in different parents may share a partial name.
/// The field type is the document's own token — <c>Tx</c>, <c>Btn</c>, <c>Ch</c>, <c>Sig</c> — or
/// <see langword="null"/> for a field that inherits none.
/// </para>
/// <para>
/// A value that the document does not state is <see langword="null"/>, never an empty string, so
/// "absent" and "empty" stay distinguishable. A signature field is reported as present with its name
/// and rectangle; its contents are not interpreted, because signing and validation are a non-goal.
/// </para>
/// </remarks>
public sealed record PdfFormFieldInfo
{
    /// <summary>Gets the fully-qualified name of the field, with its ancestors joined by <c>.</c>.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the field type as the document spells it — <c>Tx</c>, <c>Btn</c>, <c>Ch</c>, <c>Sig</c> —
    /// or <see langword="null"/> when neither the field nor an ancestor states one.
    /// </summary>
    public string? FieldType { get; init; }

    /// <summary>
    /// Gets the value of the field, or <see langword="null"/> when it has none. A button reports the
    /// name of its on state; a text field reports its text; a signature field reports
    /// <see langword="null"/> because its value is a dictionary this library does not interpret.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>Gets the default value of the field, or <see langword="null"/> when it states none.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Gets the field flags as the document states them.</summary>
    public int Flags { get; init; }

    /// <summary>Gets a value indicating whether the field is read-only.</summary>
    public bool IsReadOnly => (Flags & 0x1) != 0;

    /// <summary>Gets a value indicating whether the field must be filled in.</summary>
    public bool IsRequired => (Flags & 0x2) != 0;

    /// <summary>Gets the widgets that display the field, one per place it appears.</summary>
    public IReadOnlyList<PdfWidgetInfo> Widgets { get; init; } = [];

    /// <summary>Formats the field for diagnostics.</summary>
    /// <returns>The name, the type and the value.</returns>
    public override string ToString() => $"{Name} ({FieldType ?? "?"}) = {Value ?? "(none)"}";
}

/// <summary>
/// One occurrence of a form field on a page: where it is drawn, and on which page.
/// </summary>
/// <remarks>
/// A field may have several widgets — a group of check boxes sharing one name, for example — so the
/// widgets are listed rather than collapsed into a single rectangle.
/// </remarks>
public sealed record PdfWidgetInfo
{
    /// <summary>
    /// Gets the one-based number of the page that carries the widget, or <see langword="null"/> when
    /// the widget is not referenced by any page of the document.
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>Gets the rectangle the widget occupies in page space, or <see langword="null"/>.</summary>
    public PdfRectangle? Rectangle { get; init; }

    /// <summary>Formats the widget for diagnostics.</summary>
    /// <returns>The page and the rectangle.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"widget on page {PageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}: {Rectangle?.ToString() ?? "no rectangle"}");
}
