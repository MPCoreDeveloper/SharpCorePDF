using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the merge engine through the public shape a caller uses: a plan, a destination, a result.
/// </summary>
/// <remarks>
/// The engine options enable <see cref="PdfEngineOptions.VerifyOutput"/>, so every merge in this class
/// also proves that the document it wrote can be read back and has the pages that were written.
/// </remarks>
public sealed partial class PdfMergeEngineTests
{
    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default with { VerifyOutput = true };
    private static readonly PdfMergerService Merger = new(new PdfDocumentReader(Options), new PdfMergeEngine(Options));
    private static readonly PdfDocumentReader Reader = new(Options);

    /// <summary>Merging every page of two documents yields the sum of their pages.</summary>
    [Fact]
    public async Task Merges_every_page_of_both_sources()
    {
        var merged = await MergeAsync(
            MergePlan.Create()
                .AddFile(TestAssets.SafeWebCore)
                .AddFile(TestAssets.Modern));

        Assert.Equal(8, merged.Result.PageCount);
        Assert.Equal(8, merged.Document.PageCount);
        Assert.Equal(2, merged.Result.SourceCount);
        Assert.Equal(PdfVersion.V1_7, merged.Result.OutputVersion);
        Assert.True(merged.Result.OutputBytes > 0);
        Assert.Equal(2, merged.Result.Sources.Count);
        Assert.Equal(6, merged.Result.Sources[0].PagesContributed);
        Assert.Equal(6, merged.Result.Sources[0].SourcePageCount);
        Assert.Equal(2, merged.Result.Sources[1].PagesContributed);
        Assert.True(merged.Result.Sources[0].IsComplete);
        Assert.EndsWith("%%EOF\n", System.Text.Encoding.Latin1.GetString(merged.Raw), StringComparison.Ordinal);
    }

    /// <summary>Three sources of three kinds merge into one document, in the order the plan lists them.</summary>
    [Fact]
    public async Task Merges_three_sources_of_three_kinds_in_plan_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var outlined = await File.ReadAllBytesAsync(TestAssets.Outlined, cancellationToken);
        var modern = await File.ReadAllBytesAsync(TestAssets.Modern, cancellationToken);

        // One source is a file, one is caller-supplied bytes and one is a stream: where the content comes from
        // makes no difference to the merge, only the order in the plan does.
        var merged = await MergeAsync(
            MergePlan.Create()
                .AddFile(TestAssets.SafeWebCore, pages: "1")
                .AddBytes(outlined, "outlined", pages: "3")
                .AddStream(new MemoryStream(modern, writable: false), "modern", pages: "2"));

        Assert.Equal(3, merged.Result.SourceCount);
        Assert.Equal(3, merged.Result.PageCount);
        Assert.Equal(3, merged.Document.PageCount);
        Assert.Equal(
            ["SafeWebCore.pdf", "outlined", "modern"],
            merged.Result.Sources.Select(source => source.SourceName).ToArray());
        Assert.Equal([1, 1, 1], merged.Result.Sources.Select(source => source.PagesContributed).ToArray());
        Assert.Equal([6, 3, 2], merged.Result.Sources.Select(source => source.SourcePageCount).ToArray());
        Assert.False(merged.Result.Sources[0].IsComplete);

        // Each page of the result is told apart by the page it came from: US Letter as stored, US Letter
        // rotated a half turn, then A4 rotated a quarter turn. That sequence is the order of the plan.
        Assert.Equal(PdfPageSize.Letter, merged.Document.Pages[0].Size);
        Assert.Equal(PageRotation.None, merged.Document.Pages[0].Rotation);
        Assert.Equal(PdfPageSize.Letter, merged.Document.Pages[1].Size);
        Assert.Equal(PageRotation.Clockwise180, merged.Document.Pages[1].Rotation);
        Assert.Equal(595.276d, merged.Document.Pages[2].Size.WidthPoints, 0.01d);
        Assert.Equal(841.89d, merged.Document.Pages[2].Size.HeightPoints, 0.01d);
        Assert.Equal(PageRotation.Clockwise90, merged.Document.Pages[2].Rotation);
    }

    /// <summary>A page selection decides both which pages are taken and in which order they appear.</summary>
    [Fact]
    public async Task Merges_the_selected_pages_in_the_selected_order()
    {
        var merged = await MergeAsync(
            MergePlan.Create()
                .AddFile(TestAssets.Outlined, pages: "3,1")
                .AddFile(TestAssets.Modern, pages: "2"));

        Assert.Equal(3, merged.Result.PageCount);
        Assert.Equal(PageRotation.Clockwise180, merged.Document.Pages[0].Rotation);
        Assert.Equal(PageRotation.None, merged.Document.Pages[1].Rotation);
        Assert.Equal(PageRotation.Clockwise90, merged.Document.Pages[2].Rotation);
    }

    /// <summary>The merged document carries a classic cross-reference table, as intended.</summary>
    [Fact]
    public async Task Writes_a_classic_cross_reference_table()
    {
        var merged = await MergeAsync(MergePlan.Create().AddFile(TestAssets.Outlined));
        var text = System.Text.Encoding.Latin1.GetString(merged.Raw);

        Assert.StartsWith("%PDF-1.7", text, StringComparison.Ordinal);
        Assert.Contains("\nxref\n", text, StringComparison.Ordinal);
        Assert.Contains("trailer\n", text, StringComparison.Ordinal);
        Assert.Contains("startxref\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Type /XRef", text, StringComparison.Ordinal);
    }

    /// <summary>Every page of the result hangs under one and the same page tree node.</summary>
    [Fact]
    public async Task Uses_one_flat_page_tree_node()
    {
        var merged = await MergeAsync(
            MergePlan.Create()
                .AddFile(TestAssets.SafeWebCore, pages: "1-2")
                .AddFile(TestAssets.Outlined));

        var document = new PdfFileReader(merged.Raw, "merged", Options);
        var parents = document.Pages.Select(page => page.Dictionary.GetReference("Parent")).ToList();

        Assert.Equal(5, document.Pages.Count);
        Assert.Single(parents.Select(parent => parent?.ObjectNumber).Distinct());

        // Inherited attributes were materialised onto the pages, and every page keeps its content.
        Assert.All(
            document.Pages,
            page =>
            {
                Assert.NotNull(page.Dictionary.Get("MediaBox") ?? page.Inherited.Get("MediaBox"));
                Assert.NotNull(page.Dictionary.Get("Contents"));
            });
    }
}
