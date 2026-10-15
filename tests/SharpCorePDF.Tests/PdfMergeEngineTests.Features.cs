using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// The feature half of the merge tests: bookmarks, annotations, metadata and policies.
/// </summary>
public sealed partial class PdfMergeEngineTests
{
    /// <summary>Bookmarks of every source survive, in source order.</summary>
    [Fact]
    public async Task Merges_the_bookmarks_of_every_source()
    {
        var merged = await MergeAsync(
            MergePlan.Create()
                .AddFile(TestAssets.Outlined)
                .AddFile(TestAssets.Outlined));

        Assert.Equal(6, merged.Document.PageCount);
        Assert.True(merged.Document.HasOutlines);
        Assert.Equal(6, merged.Document.OutlineItemCount);

        var document = new PdfFileReader(merged.Raw, "merged", Options);
        var outline = document.ResolveDictionary(document.Catalog!.Get("Outlines"));
        var first = document.ResolveDictionary(outline!.Get("First"));
        var second = document.ResolveDictionary(first!.Get("Next"));

        Assert.Equal(6, outline.GetInteger("Count"));
        Assert.Equal("First", first.GetText("Title"));
        Assert.Equal("Second", second!.GetText("Title"));

        // Destinations follow the pages they pointed at in their own document …
        Assert.Equal([1], ReadDestinationPages(document, first));
        Assert.Equal([3], ReadDestinationPages(document, second));
        Assert.Equal([2], ReadDestinationPages(document, document.ResolveDictionary(second.Get("First"))!));

        // … and the top level items of the second source continue the chain of the first.
        var secondReference = first.GetReference("Next");
        var secondSourceItem = document.ResolveDictionary(second.GetReference("Next"));

        Assert.Equal("First", secondSourceItem!.GetText("Title"));
        Assert.Equal([4], ReadDestinationPages(document, secondSourceItem));
        Assert.Equal(secondReference!.ObjectNumber, secondSourceItem.GetReference("Prev")!.ObjectNumber);
    }

    /// <summary>Annotations are kept by default and dropped when the caller asks for it.</summary>
    [Fact]
    public async Task Honours_the_annotation_policy()
    {
        var kept = await MergeAsync(MergePlan.Create().AddFile(TestAssets.Outlined));
        var dropped = await MergeAsync(
            MergePlan.Create().AddFile(TestAssets.Outlined),
            MergeOutputOptions.Default with { PreserveAnnotations = false });

        Assert.Equal(1, kept.Document.Pages[0].AnnotationCount);
        Assert.Equal(0, dropped.Document.Pages[0].AnnotationCount);
    }

    /// <summary>The document information of the primary source leads the result.</summary>
    [Fact]
    public async Task Keeps_the_metadata_of_the_primary_source()
    {
        var merged = await MergeAsync(
            MergePlan.Create()
                .AddFile(TestAssets.Outlined)
                .AddFile(TestAssets.SafeWebCore));

        Assert.Equal("Outlined sample", merged.Document.Metadata.Title);
        Assert.Equal(MergeOutputOptions.Default.Producer, merged.Document.Metadata.Producer);
        Assert.Equal(9, merged.Document.PageCount);
    }

    /// <summary>A policy change and an override both reach the document information of the result.</summary>
    [Fact]
    public async Task Applies_the_metadata_policy_and_an_override()
    {
        var merged = await MergeAsync(
            MergePlan.Create().AddFile(TestAssets.SafeWebCore),
            MergeOutputOptions.Default with
            {
                MetadataPolicy = MetadataPolicy.None,
                Producer = "SharpCorePDF tests",
                MetadataOverride = new PdfMetadata { Title = "Combined", Author = "The tests" },
            });

        Assert.Equal("Combined", merged.Document.Metadata.Title);
        Assert.Equal("The tests", merged.Document.Metadata.Author);
        Assert.Equal("SharpCorePDF tests", merged.Document.Metadata.Producer);
    }
}
