using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the convenience overloads that read as if they belonged to <see cref="IPdfMerger"/>.
/// </summary>
/// <remarks>
/// The overloads exist so the three shapes a caller asks for most — merge to a file, to bytes, and a list
/// of sources to a stream — do not need a plan to be built by hand. Merging to a file is covered by the
/// walk a caller makes in <see cref="AddSharpCorePDFTests"/>; this class covers the other two, and the
/// guards that run before the merger is reached.
/// </remarks>
public sealed class PdfMergerExtensionsTests
{
    /// <summary>Merging to bytes returns a document that reads back, not a buffer that was never filled.</summary>
    [Fact]
    public async Task Merges_a_plan_to_bytes()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var merger = provider.GetRequiredService<IPdfMerger>();
        var inspector = provider.GetRequiredService<IPdfInspector>();

        var bytes = await merger.MergeToBytesAsync(
            MergePlan.Create().AddFile(TestAssets.Outlined),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5), StringComparison.Ordinal);

        var info = await inspector.InspectAsync(
            new PdfByteArraySource(bytes, "merged.pdf"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, info.PageCount);
        Assert.Equal("Outlined sample", info.Metadata.Title);
    }

    /// <summary>A list of sources becomes a plan that contributes every page of each, in order.</summary>
    [Fact]
    public async Task Merges_a_list_of_sources()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var merger = provider.GetRequiredService<IPdfMerger>();
        var inspector = provider.GetRequiredService<IPdfInspector>();

        IPdfContentSource[] sources = [new PdfFileSource(TestAssets.Outlined), new PdfFileSource(TestAssets.Modern)];
        using var destination = new MemoryStream();

        var result = await merger.MergeAsync(sources, destination, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.PageCount);
        Assert.Equal(destination.Length, result.OutputBytes);

        var info = await inspector.InspectAsync(
            new PdfByteArraySource(destination.ToArray(), "merged.pdf"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, info.PageCount);
        Assert.Equal(5, info.Pages.Count);
    }

    /// <summary>An overload that is handed nothing says so, rather than failing further down.</summary>
    [Fact]
    public async Task Refuses_a_missing_argument()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var merger = provider.GetRequiredService<IPdfMerger>();
        var plan = MergePlan.Create().AddFile(TestAssets.Modern);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeToBytesAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeToFileAsync(null!, "merged.pdf", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeAsync(
                (IEnumerable<IPdfContentSource>)null!,
                new MemoryStream(),
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => merger.MergeAsync([], null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => merger.MergeToFileAsync(plan, "   ", TestContext.Current.CancellationToken));
    }
}
