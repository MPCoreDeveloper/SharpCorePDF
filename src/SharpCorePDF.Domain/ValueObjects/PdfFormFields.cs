namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Creates the form fields the editor writes, so a caller never has to know which property a kind
/// requires or which defaults make it valid.
/// </summary>
/// <remarks>
/// <para>
/// Every factory produces a field that passes <see cref="PdfFormField.Validate"/> for its kind. The
/// page comes first, because a field is placed before it is described: the same order the edit plan
/// uses for every other page operation.
/// </para>
/// <para>
/// The factories state nothing about colour or border, so a filled field is drawn the way a viewer
/// draws an empty one: a thin dark border, no fill. A caller that wants a different face sets the
/// properties with a <c>with</c> expression.
/// </para>
/// </remarks>
public static class PdfFormFields
{
    /// <summary>Creates a text field, optionally filled with a value.</summary>
    /// <param name="pageNumber">The one-based number of the page the widget is drawn on.</param>
    /// <param name="name">The name of the field, or its fully-qualified name.</param>
    /// <param name="rectangle">The area the widget occupies, in page space.</param>
    /// <param name="value">The text the field holds, or <see langword="null"/> for an empty field.</param>
    /// <param name="style">The style the value is drawn in, or <see langword="null"/> for the form's default.</param>
    /// <returns>The text field.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PdfFormField Text(
        int pageNumber,
        string name,
        PdfRectangle rectangle,
        string? value = null,
        PdfStandardTextStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new PdfFormField
        {
            Kind = PdfFormFieldKind.Text,
            Name = name,
            Rectangle = rectangle,
            PageNumber = pageNumber,
            Value = value,
            TextStyle = style,
        };
    }

    /// <summary>Creates a check box, on or off.</summary>
    /// <param name="pageNumber">The one-based number of the page the widget is drawn on.</param>
    /// <param name="name">The name of the field, or its fully-qualified name.</param>
    /// <param name="rectangle">The area the widget occupies, in page space.</param>
    /// <param name="isChecked">Whether the box is ticked.</param>
    /// <param name="onState">The export name of the ticked state; <c>Yes</c> when none is stated.</param>
    /// <returns>The check box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PdfFormField CheckBox(
        int pageNumber,
        string name,
        PdfRectangle rectangle,
        bool isChecked = false,
        string onState = "Yes")
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new PdfFormField
        {
            Kind = PdfFormFieldKind.CheckBox,
            Name = name,
            Rectangle = rectangle,
            PageNumber = pageNumber,
            IsChecked = isChecked,
            OnState = onState,
        };
    }

    /// <summary>Creates one button of a radio group.</summary>
    /// <param name="pageNumber">The one-based number of the page the widget is drawn on.</param>
    /// <param name="name">The name of the group the button belongs to, or its fully-qualified name.</param>
    /// <param name="rectangle">The area the widget occupies, in page space.</param>
    /// <param name="isChecked">Whether this button is the selected one of its group.</param>
    /// <param name="onState">The export name of this button's state: the option it stands for.</param>
    /// <returns>The radio button.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PdfFormField RadioButton(
        int pageNumber,
        string name,
        PdfRectangle rectangle,
        bool isChecked = false,
        string onState = "Yes")
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new PdfFormField
        {
            Kind = PdfFormFieldKind.RadioButton,
            Name = name,
            Rectangle = rectangle,
            PageNumber = pageNumber,
            IsChecked = isChecked,
            OnState = onState,
        };
    }

    /// <summary>Creates a choice field, as a list box or a drop-down list.</summary>
    /// <param name="pageNumber">The one-based number of the page the widget is drawn on.</param>
    /// <param name="name">The name of the field, or its fully-qualified name.</param>
    /// <param name="rectangle">The area the widget occupies, in page space.</param>
    /// <param name="options">The options to offer, in the order they are shown.</param>
    /// <param name="value">The selected option, or <see langword="null"/> for no selection.</param>
    /// <param name="isCombo">Whether the field is a drop-down list rather than a list box.</param>
    /// <returns>The choice field.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PdfFormField Choice(
        int pageNumber,
        string name,
        PdfRectangle rectangle,
        IEnumerable<string> options,
        string? value = null,
        bool isCombo = false)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new PdfFormField
        {
            Kind = PdfFormFieldKind.Choice,
            Name = name,
            Rectangle = rectangle,
            PageNumber = pageNumber,
            Options = [.. options],
            Value = value,
            IsCombo = isCombo,
        };
    }

    /// <summary>Creates a push button, which holds no value.</summary>
    /// <param name="pageNumber">The one-based number of the page the widget is drawn on.</param>
    /// <param name="name">The name of the field, or its fully-qualified name.</param>
    /// <param name="rectangle">The area the widget occupies, in page space.</param>
    /// <param name="caption">The text drawn on the button, or <see langword="null"/> for its name.</param>
    /// <returns>The push button.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public static PdfFormField PushButton(
        int pageNumber,
        string name,
        PdfRectangle rectangle,
        string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new PdfFormField
        {
            Kind = PdfFormFieldKind.PushButton,
            Name = name,
            Rectangle = rectangle,
            PageNumber = pageNumber,
            Caption = caption,
        };
    }
}
