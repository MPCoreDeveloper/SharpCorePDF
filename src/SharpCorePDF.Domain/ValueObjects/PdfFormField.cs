using System.Globalization;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// One field of an interactive form, as a caller states it: its name, its kind, where it is drawn, and
/// the value or state it holds.
/// </summary>
/// <remarks>
/// <para>
/// The name is what a document addresses a field by, so it is the field's identity in the plan: writing
/// a field whose name the document already carries fills that field, and writing a name it does not
/// carry creates one. A dotted name, <c>customer.address.city</c>, is a fully-qualified name and is
/// written as the leaf of a field tree whose ancestors carry the parts before it; a name without a dot
/// is a root field. The empty segment a leading, trailing or doubled dot would produce is refused,
/// because no reader can address the field it would describe.
/// </para>
/// <para>
/// The field carries no appearance of its own unless a caller supplies one: the writer generates a
/// widget appearance from the kind, the value and the style, so a filled field is visible in a viewer
/// that does not synthesise appearances and in a print. A caller that needs a different face — a
/// company logo on a push button, a signature image — supplies <see cref="Appearance"/> and the writer
/// uses it instead.
/// </para>
/// <para>
/// The flags a document stores are derived from the properties, never set as bits: a caller says
/// <see cref="IsMultiline"/> and the writer sets the flag that means it. That keeps a flag the editor
/// cannot honour — a JavaScript action, a rich-text value — out of the document.
/// </para>
/// </remarks>
public sealed record PdfFormField
{
    /// <summary>
    /// Gets the name of the field, either a root name or a fully-qualified one whose parts are joined
    /// by <c>.</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Gets the kind of the field.</summary>
    public required PdfFormFieldKind Kind { get; init; }

    /// <summary>Gets the rectangle the widget occupies, in page space.</summary>
    public required PdfRectangle Rectangle { get; init; }

    /// <summary>Gets the one-based number of the page the widget is drawn on.</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Gets the value of the field: the text of a text field, the selected option of a choice field, or
    /// the export name of the state a button is in. A push button holds none.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>Gets the caption drawn on a push button; the name of the field when it states none.</summary>
    public string? Caption { get; init; }

    /// <summary>Gets a value indicating whether a check box or a radio button is on.</summary>
    public bool IsChecked { get; init; }

    /// <summary>
    /// Gets the export name of the on state of a check box or a radio button, which is what
    /// <see cref="Value"/> becomes when the button is on.
    /// </summary>
    public string OnState { get; init; } = "Yes";

    /// <summary>Gets the options a choice field offers, in the order they are shown.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Gets the font, size and colour the value is drawn in, or <see langword="null"/> for the form's default.</summary>
    public PdfStandardTextStyle? TextStyle { get; init; }

    /// <summary>Gets the alignment of the value inside the field.</summary>
    public PdfFormFieldAlignment Alignment { get; init; } = PdfFormFieldAlignment.Left;

    /// <summary>Gets the greatest number of characters a text field accepts, or <see langword="null"/> for no limit.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Gets a value indicating whether the user is prevented from changing the field.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Gets a value indicating whether the field must be filled in.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Gets a value indicating whether a text field accepts more than one line.</summary>
    public bool IsMultiline { get; init; }

    /// <summary>Gets a value indicating whether a text field echoes asterisks instead of its characters.</summary>
    public bool IsPassword { get; init; }

    /// <summary>Gets a value indicating whether a choice field is a drop-down list rather than a list box.</summary>
    public bool IsCombo { get; init; }

    /// <summary>Gets a value indicating whether a combo box accepts a value that is not one of its options.</summary>
    public bool IsEditable { get; init; }

    /// <summary>Gets a value indicating whether a choice field accepts more than one selection.</summary>
    public bool AllowMultiSelect { get; init; }

    /// <summary>Gets a value indicating whether the widget is hidden rather than shown.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Gets the text a viewer shows when the pointer rests on the field.</summary>
    public string? ToolTip { get; init; }

    /// <summary>Gets the colour of the border, or <see langword="null"/> for no border.</summary>
    public PdfColor? BorderColor { get; init; }

    /// <summary>Gets the colour behind the value, or <see langword="null"/> for none.</summary>
    public PdfColor? FillColor { get; init; }

    /// <summary>Gets the width of the border in points.</summary>
    public double BorderWidth { get; init; } = 1.0;

    /// <summary>Gets the appearance to draw instead of the one the writer generates.</summary>
    public PdfAnnotationAppearance? Appearance { get; init; }

    /// <summary>
    /// Gets the field flags the writer stores for this field, as the PDF specification numbers them,
    /// derived from the properties above.
    /// </summary>
    /// <remarks>
    /// The number is computed rather than stated by the caller, so a flag the editor cannot honour — a
    /// JavaScript action, a rich-text value, a flag that belongs to another kind of field — cannot be
    /// asked for. The bits are <c>1</c> read-only, <c>2</c> required, <c>4096</c> multiline,
    /// <c>8192</c> password, <c>32768</c> radio, <c>65536</c> push button, <c>131072</c> combo,
    /// <c>262144</c> editable and <c>2097152</c> multi-select.
    /// </remarks>
    public int Flags
    {
        get
        {
            var flags = 0;

            if (IsReadOnly)
            {
                flags |= ReadOnlyFlag;
            }

            if (IsRequired)
            {
                flags |= RequiredFlag;
            }

            switch (Kind)
            {
                case PdfFormFieldKind.Text:
                    if (IsMultiline)
                    {
                        flags |= MultilineFlag;
                    }

                    if (IsPassword)
                    {
                        flags |= PasswordFlag;
                    }

                    break;

                case PdfFormFieldKind.RadioButton:
                    flags |= RadioFlag;
                    break;

                case PdfFormFieldKind.PushButton:
                    flags |= PushButtonFlag;
                    break;

                case PdfFormFieldKind.Choice:
                    if (IsCombo)
                    {
                        flags |= ComboFlag;
                    }

                    if (IsEditable)
                    {
                        flags |= EditFlag;
                    }

                    if (AllowMultiSelect)
                    {
                        flags |= MultiSelectFlag;
                    }

                    break;
            }

            return flags;
        }
    }

    /// <summary>Validates the field.</summary>
    /// <returns>Every problem found, or an empty list when the field is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        errors.AddRange(ValidateName());

        if (Rectangle is null || Rectangle.IsEmpty)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the field rectangle '{Rectangle}' encloses no area."));
        }

        if (PageNumber < 1)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the page number {PageNumber} is not one-based."));
        }

        if (!double.IsFinite(BorderWidth) || BorderWidth < 0.0)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the border width {BorderWidth} is not a number of points greater than or equal to zero."));
        }

        if (TextStyle is { } style)
        {
            errors.AddRange(style.Validate());
        }

        if (Appearance is { } appearance)
        {
            errors.AddRange(appearance.Validate());
        }

        errors.AddRange(ValidateKind());
        return errors;
    }

    /// <summary>Formats the field for diagnostics.</summary>
    /// <returns>The kind, the name and the value.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Kind} field '{Name}' on page {PageNumber} = {DescribeValue()}");

    /// <summary>The flag that stops the user from changing the field.</summary>
    private const int ReadOnlyFlag = 1;

    /// <summary>The flag that marks the field as one the form requires.</summary>
    private const int RequiredFlag = 2;

    /// <summary>The flag that lets a text field hold more than one line.</summary>
    private const int MultilineFlag = 1 << 12;

    /// <summary>The flag that makes a text field echo asterisks.</summary>
    private const int PasswordFlag = 1 << 13;

    /// <summary>The flag that makes the buttons of a group exclude one another.</summary>
    private const int RadioFlag = 1 << 15;

    /// <summary>The flag that marks a button as one that is pressed rather than toggled.</summary>
    private const int PushButtonFlag = 1 << 16;

    /// <summary>The flag that shows a choice field as a drop-down list.</summary>
    private const int ComboFlag = 1 << 17;

    /// <summary>The flag that lets a combo box hold text that is not one of its options.</summary>
    private const int EditFlag = 1 << 18;

    /// <summary>The flag that lets a choice field hold more than one selection.</summary>
    private const int MultiSelectFlag = 1 << 21;

    /// <summary>Checks the name, which is what identifies the field in the document.</summary>
    /// <returns>Every problem found, or an empty sequence.</returns>
    private IEnumerable<string> ValidateName()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return "a form field needs a name: it is what the document addresses it by.";
            yield break;
        }

        if (Name.Contains('\0', StringComparison.Ordinal))
        {
            yield return "the field name contains a null character.";
        }

        if (Name.Any(char.IsControl))
        {
            yield return $"the field name '{Name}' contains a control character.";
        }

        if (Name[0] == '.' || Name[^1] == '.')
        {
            yield return $"the field name '{Name}' starts or ends with a separator, so one of its parts is empty.";
            yield break;
        }

        if (Name.Contains("..", StringComparison.Ordinal))
        {
            yield return $"the field name '{Name}' has an empty part between two separators.";
        }
    }

    /// <summary>Checks the properties the field's kind requires, and the ones it makes no sense with.</summary>
    /// <returns>Every problem found, or an empty sequence.</returns>
    private IEnumerable<string> ValidateKind()
    {
        if (Options.Count > 0 && Kind is not PdfFormFieldKind.Choice)
        {
            yield return $"a {Kind} field has no options; only a choice field offers them.";
        }

        if (Options.Any(string.IsNullOrEmpty))
        {
            yield return "a choice field has an option that is empty.";
        }

        if (Options.Distinct(StringComparer.Ordinal).Count() != Options.Count)
        {
            yield return "a choice field repeats an option; a reader could not tell the two apart.";
        }

        if (IsChecked && Kind is not (PdfFormFieldKind.CheckBox or PdfFormFieldKind.RadioButton))
        {
            yield return $"a {Kind} field is not a button, so it cannot be checked.";
        }

        if (Kind is PdfFormFieldKind.CheckBox or PdfFormFieldKind.RadioButton)
        {
            if (string.IsNullOrWhiteSpace(OnState))
            {
                yield return "a button needs the name of its on state.";
            }

            if (Value is not null)
            {
                yield return "a button states its value through IsChecked and OnState, so Value must be left unset.";
            }
        }

        if (Kind is not (PdfFormFieldKind.Text or PdfFormFieldKind.Choice) && MaxLength is not null)
        {
            yield return $"a {Kind} field has no maximum length; only a text or choice field holds text.";
        }

        if (Kind == PdfFormFieldKind.Text && (IsCombo || IsEditable || AllowMultiSelect))
        {
            yield return "a text field is not a choice field, so it cannot be a combo box or accept several selections.";
        }

        switch (Kind)
        {
            case PdfFormFieldKind.Text:
                if (MaxLength is < 0)
                {
                    yield return "a text field's maximum length cannot be negative.";
                }

                if (IsMultiline && IsPassword)
                {
                    yield return "a text field cannot be multiline and a password field at the same time.";
                }

                if (MaxLength is { } limit && Value is { } text && text.Length > limit)
                {
                    yield return string.Create(
                        CultureInfo.InvariantCulture,
                        $"the value of the field is {text.Length} characters long, longer than its maximum length of {limit}.");
                }

                break;

            case PdfFormFieldKind.Choice:
                if (Options.Count == 0)
                {
                    yield return "a choice field needs at least one option.";
                }

                if (AllowMultiSelect && IsCombo)
                {
                    yield return "a combo box shows one value, so it cannot accept several selections.";
                }

                if (IsEditable && !IsCombo)
                {
                    yield return "only a combo box can be edited; a list box states one of its options.";
                }

                if (Value is { } selected && !Options.Contains(selected, StringComparer.Ordinal) && !IsEditable)
                {
                    yield return $"the value '{selected}' is not one of the options of the choice field.";
                }

                if (MaxLength is not null)
                {
                    yield return "a choice field takes its length from its options, so it states no maximum length.";
                }

                break;

            case PdfFormFieldKind.PushButton when Value is not null:
                yield return "a push button holds no value; it states a caption instead.";
                break;
        }
    }

    /// <summary>Formats the value for diagnostics.</summary>
    /// <returns>The value, the checked state, or the fact that there is none.</returns>
    private string DescribeValue()
    {
        if (Kind is PdfFormFieldKind.CheckBox or PdfFormFieldKind.RadioButton)
        {
            return IsChecked ? OnState : "off";
        }

        return Value ?? (Kind == PdfFormFieldKind.PushButton ? Caption ?? "(no caption)" : "(no value)");
    }
}
