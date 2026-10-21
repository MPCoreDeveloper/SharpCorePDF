using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of annotation reading: the geometry and text of every subtype, and the three shapes a link
/// target can take — an external URI, a direct destination and a named one.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>Every annotation is returned with its subtype, rectangle, flags and text.</summary>
    [Fact]
    public async Task Returns_every_annotation_with_its_geometry_and_text()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadAnnotationsAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(8, result.Count);
        Assert.All(result.Items, annotation => Assert.Equal(1, annotation.PageNumber));

        var note = Assert.Single(result.Items, annotation => annotation.Subtype == "Text");

        Assert.Equal(new PdfRectangle(10d, 10d, 20d, 20d), note.Rectangle);
        Assert.Equal("Note", note.Contents);
        Assert.Equal("Alice", note.Author);
        Assert.Equal(4, note.Flags);
        Assert.True(note.HasAppearance);
        Assert.Equal("note-1", note.Name);
        Assert.Equal(2024, note.ModificationDate?.Year);

        Assert.Single(result.Items, annotation => annotation.Subtype == "Highlight");
        Assert.Single(result.Items, annotation => annotation.Subtype == "Stamp");
    }

    /// <summary>A link to a URL carries the URI exactly as the document wrote it.</summary>
    [Fact]
    public async Task Resolves_an_external_link_to_its_uri()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadAnnotationsAsync(source, PageRange.All, cancellationToken: Token);
        var link = Assert.Single(result.Items, annotation => annotation.Target?.Uri is not null);

        Assert.Equal("https://example.com/a?b=c", link.Target!.Uri);
        Assert.True(link.Target.IsExternal);
        Assert.False(link.Target.IsInternal);
    }

    /// <summary>A direct destination and a named one both resolve to a page and its coordinates.</summary>
    [Fact]
    public async Task Resolves_direct_and_named_destinations()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadAnnotationsAsync(source, PageRange.All, cancellationToken: Token);

        var direct = Assert.Single(result.Items, annotation => annotation.Target is { NamedDestination: null, PageNumber: 1 });
        Assert.Equal(new PdfRectangle(5d, 6d, 5d, 6d), direct.Target!.Area);

        var named = Assert.Single(result.Items, annotation => annotation.Target?.NamedDestination == "chapter1");
        Assert.Equal(1, named.Target!.PageNumber);
        Assert.True(named.Target.IsInternal);
    }

    /// <summary>A named destination that does not resolve is reported, and the call does not throw.</summary>
    [Fact]
    public async Task Reports_a_named_destination_that_does_not_resolve()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadAnnotationsAsync(source, PageRange.All, cancellationToken: Token);
        var unresolved = Assert.Single(result.Items, annotation => annotation.Target?.IsUnresolved == true);

        Assert.Equal("missing", unresolved.Target!.NamedDestination);
        Assert.Null(unresolved.Target.PageNumber);
        AssertWarning(result.Warnings, PdfWarningKind.NamedDestinationUnresolved);
    }

    /// <summary>An annotation of a subtype this library does not model is still returned.</summary>
    [Fact]
    public async Task Keeps_an_unknown_subtype()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var result = await Reader().ReadAnnotationsAsync(source, PageRange.All, cancellationToken: Token);
        var unknown = Assert.Single(result.Items, annotation => annotation.Subtype == "FooBar");

        Assert.Equal(new PdfRectangle(1d, 2d, 3d, 4d), unknown.Rectangle);
    }
}
