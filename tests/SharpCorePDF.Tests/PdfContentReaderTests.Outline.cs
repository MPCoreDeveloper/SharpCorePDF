using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of outline reading: the tree in document order, the destinations resolved through direct and
/// named forms, and the entries that cannot be resolved being reported instead of dropped.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>The tree is returned with its titles, its nesting and its open or closed state.</summary>
    [Fact]
    public async Task Returns_the_tree_in_document_order()
    {
        var source = Source(PdfContentFixtures.Outline(), "outline.pdf");

        var result = await Reader().ReadOutlineAsync(source, cancellationToken: Token);

        Assert.Equal(4, result.Count);
        Assert.Equal(["Chapter 1", "Chapter 2", "Other file", "Website"], result.Items.Select(item => item.Title));

        var chapter1 = result.Items[0];

        Assert.True(chapter1.IsOpen);
        Assert.Equal(2, chapter1.Children.Count);
        Assert.Equal(["Section 1.1", "Section 1.2"], chapter1.Children.Select(item => item.Title));
        Assert.Single(chapter1.Children[0].Children);
        Assert.Equal("Topic 1.1.1", chapter1.Children[0].Children[0].Title);
    }

    /// <summary>A direct destination names a page and its coordinates.</summary>
    [Fact]
    public async Task Resolves_a_direct_destination()
    {
        var source = Source(PdfContentFixtures.Outline(), "outline.pdf");

        var result = await Reader().ReadOutlineAsync(source, cancellationToken: Token);
        var chapter1 = result.Items[0];
        var section11 = chapter1.Children[0];

        Assert.Equal(1, chapter1.Destination!.PageNumber);
        Assert.Equal(2, section11.Destination!.PageNumber);
        Assert.Equal(new PdfRectangle(0d, 700d, 0d, 700d), section11.Destination.Area);
    }

    /// <summary>A named destination is resolved to the page it names.</summary>
    [Fact]
    public async Task Resolves_a_named_destination()
    {
        var source = Source(PdfContentFixtures.Outline(), "outline.pdf");

        var result = await Reader().ReadOutlineAsync(source, cancellationToken: Token);
        var topic = result.Items[0].Children[0].Children[0];

        Assert.Equal("chapter2", topic.Destination!.NamedDestination);
        Assert.Equal(2, topic.Destination.PageNumber);
        Assert.True(topic.Destination.IsInternal);
    }

    /// <summary>A named destination that does not resolve is reported, not thrown.</summary>
    [Fact]
    public async Task Reports_a_bookmark_whose_named_destination_does_not_resolve()
    {
        var source = Source(PdfContentFixtures.Outline(), "outline.pdf");

        var result = await Reader().ReadOutlineAsync(source, cancellationToken: Token);
        var section12 = result.Items[0].Children[1];

        Assert.True(section12.Destination!.IsUnresolved);
        AssertWarning(result.Warnings, PdfWarningKind.NamedDestinationUnresolved);
    }

    /// <summary>A branch the document marks closed is reported closed, and still lists its children.</summary>
    [Fact]
    public async Task Keeps_a_closed_branch_closed()
    {
        var source = Source(PdfContentFixtures.Outline(), "outline.pdf");

        var result = await Reader().ReadOutlineAsync(source, cancellationToken: Token);
        var chapter2 = result.Items[1];

        Assert.False(chapter2.IsOpen);
        Assert.Equal(2, chapter2.Destination!.PageNumber);
        Assert.Single(chapter2.Children);
        Assert.Equal("Hidden", chapter2.Children[0].Title);
    }

    /// <summary>A bookmark that targets another file is external, and names the file.</summary>
    [Fact]
    public async Task Returns_an_external_bookmark_as_external()
    {
        var source = Source(PdfContentFixtures.Outline(), "outline.pdf");

        var result = await Reader().ReadOutlineAsync(source, cancellationToken: Token);

        Assert.Equal("other.pdf", result.Items[2].Destination!.FileName);
        Assert.True(result.Items[2].Destination!.IsExternal);
        Assert.Equal("https://example.com", result.Items[3].Destination!.Uri);
    }

    /// <summary>A document without an outline yields an empty result rather than an exception.</summary>
    [Fact]
    public async Task Returns_an_empty_result_for_a_document_without_an_outline()
    {
        var source = Source(PdfContentFixtures.WithoutMetadata(), "plain.pdf");

        var result = await Reader().ReadOutlineAsync(source, cancellationToken: Token);

        Assert.Empty(result.Items);
    }
}
