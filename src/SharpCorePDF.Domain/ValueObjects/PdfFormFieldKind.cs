namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Identifies the kind of an AcroForm field the editor writes: what it looks like and what a caller
/// can put in it.
/// </summary>
/// <remarks>
/// The kinds are the four a form in the wild is built from, plus the one that carries no value. A
/// signature field is deliberately absent: signing is a separate surface, and a field this editor
/// writes never claims to be signed.
/// </remarks>
public enum PdfFormFieldKind
{
    /// <summary>A text field: one line, or several when it is multiline.</summary>
    Text,

    /// <summary>A check box: on or off, drawn as a tick in a square.</summary>
    CheckBox,

    /// <summary>One button of a radio group: on or off, drawn as a dot in a circle.</summary>
    RadioButton,

    /// <summary>A choice field: a list box, or a combo box when it is stated as one.</summary>
    Choice,

    /// <summary>A push button: a caption and a border, holding no value.</summary>
    PushButton,
}

/// <summary>
/// The horizontal alignment of the value inside a field.
/// </summary>
/// <remarks>
/// The alignment is written into the field's default appearance by the <c>Tj</c> position it computes,
/// because a standard font has no kerning table the engine could rely on to right-align by advance.
/// </remarks>
public enum PdfFormFieldAlignment
{
    /// <summary>The value starts at the left edge of the field, inset by its border.</summary>
    Left,

    /// <summary>The value is centred in the field.</summary>
    Center,

    /// <summary>The value ends at the right edge of the field, inset by its border.</summary>
    Right,
}
