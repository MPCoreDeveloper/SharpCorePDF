using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of link reading: the clickable area of every link on a page, and the two shapes its target can
/// take — a URI outside the document or a destination inside it.
/// </summary>
public sealed partial class PdfLayoutReaderTests
{
    /// <summary>Only annotations of the link subtype are read, and a URI is kept exactly as written.</summary>
    [Fact]
    public async Task Reads_the_links_of_a_page_and_their_uris()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadHyperlinksAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(4, result.Count);

        var external = result.Items[0];

        Assert.Equal(1, external.PageNumber);
        Assert.Equal("https://example.com/a?b=c", external.Uri);
        Assert.Null(external.Destination);
        Assert.True(external.IsExternal);
        Assert.False(external.IsInternal);
        Assert.NotNull(external.Rectangle);
        Assert.Equal(10d, external.Rectangle!.Right, 0.001d);
    }

    /// <summary>A direct destination and a named one both resolve to the page and the area they name.</summary>
    [Fact]
    public async Task Resolves_a_direct_and_a_named_destination()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadHyperlinksAsync(source, PageRange.All, cancellationToken: Token);

        var direct = result.Items[1];

        Assert.Null(direct.Uri);
        Assert.True(direct.IsInternal);
        Assert.Equal(1, direct.Destination?.PageNumber);

        var named = result.Items[2];

        Assert.Equal("chapter1", named.Destination?.NamedDestination);
        Assert.Equal(1, named.Destination?.PageNumber);
        Assert.False(named.IsExternal);
    }

    /// <summary>
    /// A named destination that does not resolve keeps its name and produces the warning the annotation read
    /// made, so the link is neither dropped nor invented.
    /// </summary>
    [Fact]
    public async Task Keeps_an_unresolved_destination_and_says_so()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadHyperlinksAsync(source, PageRange.All, cancellationToken: Token);

        var unresolved = result.Items[3];

        Assert.Equal("missing", unresolved.Destination?.NamedDestination);
        Assert.Null(unresolved.Destination?.PageNumber);
        AssertWarning(result.Warnings, PdfWarningKind.NamedDestinationUnresolved);
    }

    /// <summary>A page that carries no annotation yields no link and no warning.</summary>
    [Fact]
    public async Task Reads_a_page_without_links_as_no_links()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var result = await Reader().ReadHyperlinksAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Empty(result.Items);
        Assert.Empty(result.Warnings);
    }
}
