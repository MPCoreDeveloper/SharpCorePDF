using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of form reading: fully-qualified names, inherited types, several widgets for one field, and the
/// difference between a value that is absent and one that is empty.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>Nested fields are returned with their fully-qualified names and their values.</summary>
    [Fact]
    public async Task Returns_the_field_tree_with_qualified_names()
    {
        var source = Source(PdfContentFixtures.Form(), "form.pdf");

        var result = await Reader().ReadFormFieldsAsync(source, cancellationToken: Token);

        Assert.Equal(4, result.Items.Count);
        Assert.Equal(["person.name", "group", "sig", "empty"], result.Items.Select(field => field.Name));

        var name = Assert.Single(result.Items, field => field.Name == "person.name");

        Assert.Equal("Tx", name.FieldType);
        Assert.Equal("Alice", name.Value);
        Assert.Equal("Bob", name.DefaultValue);
        Assert.True(name.IsReadOnly);
        Assert.False(name.IsRequired);
        Assert.Equal(1, name.Widgets[0].PageNumber);
    }

    /// <summary>A field with three widgets reports all three, each with its own rectangle.</summary>
    [Fact]
    public async Task Returns_every_widget_of_a_field()
    {
        var source = Source(PdfContentFixtures.Form(), "form.pdf");

        var result = await Reader().ReadFormFieldsAsync(source, cancellationToken: Token);
        var group = Assert.Single(result.Items, field => field.Name == "group");

        Assert.Equal("Btn", group.FieldType);
        Assert.Equal("Yes", group.Value);
        Assert.True(group.IsRequired);
        Assert.Equal(3, group.Widgets.Count);
        Assert.All(group.Widgets, widget => Assert.Equal(1, widget.PageNumber));
        Assert.Equal(0d, group.Widgets[0].Rectangle?.Left);
        Assert.Equal(20d, group.Widgets[1].Rectangle?.Left);
        Assert.Equal(40d, group.Widgets[2].Rectangle?.Left);
    }

    /// <summary>A signature field is reported as present, and its contents are not interpreted.</summary>
    [Fact]
    public async Task Reports_a_signature_field_without_interpreting_it()
    {
        var source = Source(PdfContentFixtures.Form(), "form.pdf");

        var result = await Reader().ReadFormFieldsAsync(source, cancellationToken: Token);
        var signature = Assert.Single(result.Items, field => field.Name == "sig");

        Assert.Equal("Sig", signature.FieldType);
        Assert.Null(signature.Value);
        Assert.Single(signature.Widgets);
        Assert.NotNull(signature.Widgets[0].Rectangle);
    }

    /// <summary>A field with no value reports <see langword="null"/>, not an empty string.</summary>
    [Fact]
    public async Task Leaves_an_absent_value_null()
    {
        var source = Source(PdfContentFixtures.Form(), "form.pdf");

        var result = await Reader().ReadFormFieldsAsync(source, cancellationToken: Token);
        var empty = Assert.Single(result.Items, field => field.Name == "empty");

        Assert.Null(empty.Value);
        Assert.Null(empty.DefaultValue);
        Assert.Empty(empty.Widgets);
    }

    /// <summary>A document without a form yields an empty collection rather than an exception.</summary>
    [Fact]
    public async Task Returns_an_empty_result_for_a_document_without_a_form()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadFormFieldsAsync(source, cancellationToken: Token);

        Assert.Empty(result.Items);
    }
}
