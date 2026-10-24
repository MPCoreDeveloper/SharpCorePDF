using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the form model: what a field factory produces, what a field refuses, and what the plan
/// refuses before the source is opened.
/// </summary>
/// <remarks>
/// The write half of these tests — filling a document and reading the result back, then flattening it —
/// lives beside the engine facts, because it needs a document to fill. These facts hold whatever the
/// engine does: they are the contract a caller sees before any byte is written.
/// </remarks>
public sealed class PdfFormTests
{
    private static readonly PdfRectangle Box = new(72, 500, 272, 520);

    /// <summary>A text field the factory creates states its name, its page and its value, and passes validation.</summary>
    [Fact]
    public void A_text_field_factory_produces_a_valid_field()
    {
        var field = PdfFormFields.Text(2, "customer.name", Box, "Ada Lovelace");

        Assert.Equal(PdfFormFieldKind.Text, field.Kind);
        Assert.Equal("customer.name", field.Name);
        Assert.Equal(2, field.PageNumber);
        Assert.Equal("Ada Lovelace", field.Value);
        Assert.Empty(field.Validate());
        Assert.Contains("customer.name", field.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A name with an empty part is refused: no reader could address the field it describes.</summary>
    /// <param name="name">The name to refuse.</param>
    [Theory]
    [InlineData(".leading")]
    [InlineData("trailing.")]
    [InlineData("doubled..part")]
    public void A_name_with_an_empty_part_is_refused(string name)
    {
        var field = PdfFormFields.Text(1, name, Box);
        var errors = field.Validate();

        Assert.Contains(errors, error => error.Contains("empty", StringComparison.Ordinal));
    }

    /// <summary>Both pages and rectangles are checked before anything is written.</summary>
    [Fact]
    public void A_field_outside_the_first_page_or_with_no_area_is_refused()
    {
        var offPage = PdfFormFields.Text(0, "name", Box);
        var noArea = PdfFormFields.Text(1, "name", new PdfRectangle(72, 500, 72, 520));

        Assert.Contains(offPage.Validate(), error => error.Contains("one-based", StringComparison.Ordinal));
        Assert.Contains(noArea.Validate(), error => error.Contains("encloses no area", StringComparison.Ordinal));
    }

    /// <summary>The flags a document stores are derived from the properties, not stated as bits.</summary>
    [Fact]
    public void The_flags_follow_the_properties_of_the_field()
    {
        var text = PdfFormFields.Text(1, "note", Box) with { IsReadOnly = true, IsRequired = true, IsMultiline = true };
        var radio = PdfFormFields.RadioButton(1, "size", Box, isChecked: true, onState: "Large");
        var push = PdfFormFields.PushButton(1, "submit", Box, "Send");
        var combo = PdfFormFields.Choice(1, "country", Box, ["AT", "CH"], "AT", isCombo: true);
        var list = PdfFormFields.Choice(1, "tags", Box, ["a", "b"]) with { AllowMultiSelect = true };

        Assert.Equal(1 | 2 | 4096, text.Flags);
        Assert.Equal(32768, radio.Flags);
        Assert.Equal(65536, push.Flags);
        Assert.Equal(131072, combo.Flags);
        Assert.Equal(2097152, list.Flags);
    }

    /// <summary>A choice field states one of its options, or the writer would store a value the form does not offer.</summary>
    [Fact]
    public void A_choice_value_that_is_not_an_option_is_refused()
    {
        var field = PdfFormFields.Choice(1, "country", Box, ["AT", "CH"], "DE");
        var errors = field.Validate();

        Assert.Contains(errors, error => error.Contains("'DE'", StringComparison.Ordinal));
    }

    /// <summary>An editable combo box accepts a value of its own, because that is what <c>/Edit</c> means.</summary>
    [Fact]
    public void An_editable_combo_box_accepts_a_value_of_its_own()
    {
        var field = PdfFormFields.Choice(1, "city", Box, ["Vienna"], "Linz", isCombo: true) with { IsEditable = true };

        Assert.Empty(field.Validate());
        Assert.Equal(131072 | 262144, field.Flags);
    }

    /// <summary>Two options a reader cannot tell apart are refused; so is an empty one.</summary>
    [Fact]
    public void A_choice_field_refuses_repeated_and_empty_options()
    {
        var repeated = PdfFormFields.Choice(1, "country", Box, ["AT", "AT"]);
        var blank = PdfFormFields.Choice(1, "country", Box, ["AT", string.Empty]);

        Assert.Contains(repeated.Validate(), error => error.Contains("repeats an option", StringComparison.Ordinal));
        Assert.Contains(blank.Validate(), error => error.Contains("option that is empty", StringComparison.Ordinal));
    }

    /// <summary>A button states its state through IsChecked and OnState, so a value on one is refused.</summary>
    [Fact]
    public void A_button_with_a_value_is_refused()
    {
        var checkBox = PdfFormFields.CheckBox(1, "agree", Box) with { Value = "Yes" };
        var push = PdfFormFields.PushButton(1, "submit", Box, "Send") with { Value = "Send" };

        Assert.Contains(checkBox.Validate(), error => error.Contains("IsChecked", StringComparison.Ordinal));
        Assert.Contains(push.Validate(), error => error.Contains("holds no value", StringComparison.Ordinal));
    }

    /// <summary>A password field shows one line by definition, so the two flags cannot both be set.</summary>
    [Fact]
    public void A_text_field_cannot_be_multiline_and_a_password_field()
    {
        var field = PdfFormFields.Text(1, "secret", Box) with { IsMultiline = true, IsPassword = true };

        Assert.Contains(field.Validate(), error => error.Contains("at the same time", StringComparison.Ordinal));
    }

    /// <summary>A value longer than the stated maximum is refused rather than truncated by the writer.</summary>
    [Fact]
    public void A_value_longer_than_the_maximum_length_is_refused()
    {
        var field = PdfFormFields.Text(1, "code", Box, "ABCDEF") with { MaxLength = 4 };
        var errors = field.Validate();

        Assert.Contains(errors, error => error.Contains("longer than its maximum length", StringComparison.Ordinal));
    }

    /// <summary>Only a choice field offers options, and only a text or choice field states a length.</summary>
    [Fact]
    public void Options_and_lengths_belong_to_the_kinds_that_hold_text()
    {
        var button = PdfFormFields.CheckBox(1, "agree", Box) with { Options = ["Yes"] };
        var buttonLength = PdfFormFields.CheckBox(1, "agree", Box) with { MaxLength = 3 };
        var textAsChoice = PdfFormFields.Text(1, "note", Box) with { IsCombo = true };

        Assert.Contains(button.Validate(), error => error.Contains("no options", StringComparison.Ordinal));
        Assert.Contains(buttonLength.Validate(), error => error.Contains("no maximum length", StringComparison.Ordinal));
        Assert.Contains(textAsChoice.Validate(), error => error.Contains("not a choice field", StringComparison.Ordinal));
    }

    /// <summary>A form keeps the order it was given and refuses two fields with one name.</summary>
    [Fact]
    public void A_form_refuses_two_fields_with_one_name()
    {
        var form = PdfForm.Create(
            PdfFormFields.Text(1, "name", Box),
            PdfFormFields.CheckBox(1, "name", Box),
            PdfFormFields.Text(1, "other", Box));

        var errors = form.Validate();

        Assert.Equal(3, form.Fields.Count);
        Assert.Single(errors);
        Assert.Contains(errors, error => error.Contains("already used by field 0", StringComparison.Ordinal));
    }

    /// <summary>A form field that cannot be written is reported with its position and its name.</summary>
    [Fact]
    public void A_form_reports_which_field_is_wrong()
    {
        var form = PdfForm.Create(PdfFormFields.Text(1, "ok", Box), PdfFormFields.Choice(1, "country", Box, []));
        var errors = form.Validate();

        Assert.Contains(errors, error => error.Contains("field 1 ('country')", StringComparison.Ordinal));
    }

    /// <summary>A plan refuses two operations that write one field: the second would replace the first.</summary>
    [Fact]
    public void A_plan_refuses_two_operations_that_write_one_field()
    {
        var plan = PdfEditPlan.Create()
            .SetFormField(PdfFormFields.Text(1, "name", Box, "Ada"))
            .SetFormField(PdfFormFields.Text(1, "name", Box, "Grace"));

        var errors = plan.Validate();

        Assert.Single(errors);
        Assert.Contains(errors, error => error.Contains("already set by operation 0", StringComparison.Ordinal));
    }

    /// <summary>A plan that fills two different fields, resets the form and flattens it is executable.</summary>
    [Fact]
    public void A_plan_that_fills_resets_and_flattens_is_valid()
    {
        var plan = PdfEditPlan.Create()
            .SetForm(PdfForm.Create(
                PdfFormFields.Text(1, "name", Box, "Ada"),
                PdfFormFields.CheckBox(1, "agree", Box, isChecked: true)))
            .ResetForm()
            .FlattenForm();

        Assert.Empty(plan.Validate());
        Assert.Equal(3, plan.Operations.Count);
        Assert.Equal(PdfEditOperationKind.SetFormField, plan.Operations[0].Kind);
        Assert.Equal(PdfEditOperationKind.ResetForm, plan.Operations[1].Kind);
        Assert.Equal(PdfEditOperationKind.FlattenForm, plan.Operations[2].Kind);
    }

    /// <summary>The page a call states wins over the page the field carries, so a factory result can be reused.</summary>
    [Fact]
    public void The_page_of_the_call_replaces_the_page_of_the_field()
    {
        var field = PdfFormFields.Text(1, "name", Box, "Ada");
        var plan = PdfEditPlan.Create().SetFormField(3, field);
        var written = Assert.IsType<PdfSetFormOperation>(plan.Operations[0]).Form.Fields[0];

        Assert.Equal(3, written.PageNumber);
        Assert.Empty(plan.Validate());
    }

    /// <summary>Flattening a page that cannot exist is refused before the source is opened.</summary>
    [Fact]
    public void Flattening_page_zero_is_refused()
    {
        var plan = PdfEditPlan.Create().FlattenForm(0);

        Assert.Contains(plan.Validate(), error => error.Contains("one-based", StringComparison.Ordinal));
    }

    /// <summary>A field that states a bad style is reported through the plan, with the operation that carries it.</summary>
    [Fact]
    public void A_plan_reports_the_operation_that_carries_a_bad_field()
    {
        var plan = PdfEditPlan.Create()
            .SetFormField(PdfFormFields.Choice(1, "country", Box, ["AT", "CH"], "DE"));

        var errors = plan.Validate();

        Assert.Single(errors);
        Assert.Contains(errors, error => error.StartsWith("operation 0:", StringComparison.Ordinal));
    }
}
