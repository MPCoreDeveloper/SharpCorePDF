using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the merge service that decides what a plan means before an engine writes anything.
/// </summary>
/// <remarks>
/// The order matters: a destination that cannot be written is refused before a source is opened, so a
/// caller who passes a read-only stream gets an argument error rather than a half-read document.
/// </remarks>
public sealed class PdfMergerServiceTests
{
    /// <summary>A destination that cannot be written is refused, and the stream is named.</summary>
    [Fact]
    public async Task Refuses_a_destination_that_cannot_be_written()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var merger = provider.GetRequiredService<IPdfMerger>();
        var plan = MergePlan.Create().AddFile(TestAssets.Modern);

        using var readOnly = new MemoryStream([], writable: false);
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => merger.MergeAsync(plan, readOnly, TestContext.Current.CancellationToken));

        Assert.Contains("must be writable", exception.Message, StringComparison.Ordinal);
        Assert.Equal("destination", exception.ParamName);
    }
}
