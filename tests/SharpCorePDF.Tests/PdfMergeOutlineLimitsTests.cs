using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of how a merge copies an outline that is not a tree: nested deeper than it walks, or reaching a
/// bookmark twice. The first crash of this kind the <c>document</c> fuzz target found overflowed the stack.
/// </summary>
public sealed class PdfMergeOutlineLimitsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>An outline nested deeper than 256 levels is copied to that depth, and the merge says so.</summary>
    [Fact]
    public async Task Copies_an_outline_nested_deeper_than_it_walks_to_its_limit()
    {
        const int Levels = 300;
        var bookmarks = new List<string>();

        // Bookmark i is object 4 + i, its parent object 3 + i (object 3 is the outline root).
        for (var level = 0; level < Levels; level++)
        {
            var first = level + 1 < Levels ? $"/First {5 + level} 0 R /Last {5 + level} 0 R /Count 1" : string.Empty;
            bookmarks.Add($"<< /Title (level {level}) /Parent {3 + level} 0 R {first} >>");
        }

        var result = await MergeAsync(bookmarks);

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.BookmarksTruncated);
    }

    /// <summary>
    /// A bookmark two parents share is copied once, and the merge says so; it was walked again under every
    /// parent, which grows with the product of the levels, and a bookmark that points back up the tree never
    /// ended.
    /// </summary>
    [Fact]
    public async Task Copies_a_bookmark_reached_twice_once()
    {
        var result = await MergeAsync(
        [
            "<< /Title (a) /Parent 3 0 R /Next 6 0 R /First 5 0 R /Last 5 0 R /Count 1 >>",
            "<< /Title (shared) /Parent 4 0 R >>",
            "<< /Title (b) /Parent 3 0 R /Prev 4 0 R /First 5 0 R /Last 5 0 R /Count 1 >>",
        ]);

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.BookmarkSkipped);
    }

    /// <summary>A bookmark whose child is its own parent ends instead of recursing until the stack runs out.</summary>
    [Fact]
    public async Task Copies_a_bookmark_that_is_its_own_child_once()
    {
        var result = await MergeAsync(["<< /Title (loop) /Parent 3 0 R /First 4 0 R /Last 4 0 R /Count 1 >>"]);

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.BookmarkSkipped);
    }

    /// <summary>Merges a one page document whose outline root (object 3) holds the given bookmarks, objects 4 on.</summary>
    private static async Task<SharpCorePDF.Domain.ReadModels.MergeResult> MergeAsync(IReadOnlyList<string> bookmarks)
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var outline = builder.Reserve();

        foreach (var bookmark in bookmarks)
        {
            builder.Add(bookmark);
        }

        var page = builder.Add($"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] >>");

        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Outlines {outline} 0 R >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.Set(outline, "<< /Type /Outlines /First 4 0 R /Last 4 0 R >>");

        using var destination = new MemoryStream();

        return await new PdfMergerService(new PdfDocumentReader(), new PdfMergeEngine()).MergeAsync(
            MergePlan.Create().AddSource(PdfSource.Create(new PdfByteArraySource(builder.Build(catalog), "outline.pdf"))),
            destination,
            Token);
    }
}
