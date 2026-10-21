using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the registration, through the same container a caller uses.
/// </summary>
public sealed class AddSharpCorePDFTests
{
    /// <summary>Every port is registered, and the services are singletons.</summary>
    [Fact]
    public void Registers_the_merger_the_inspector_and_the_engine()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IPdfMerger>());
        Assert.NotNull(provider.GetRequiredService<IPdfInspector>());
        Assert.NotNull(provider.GetRequiredService<IPdfContentReader>());
        Assert.NotNull(provider.GetRequiredService<IPdfDocumentReader>());
        Assert.NotNull(provider.GetRequiredService<IPdfPageContentReader>());
        Assert.NotNull(provider.GetRequiredService<IPdfMergeEngine>());

        Assert.Same(provider.GetRequiredService<IPdfMerger>(), provider.GetRequiredService<IPdfMerger>());
        Assert.Same(provider.GetRequiredService<IPdfContentReader>(), provider.GetRequiredService<IPdfContentReader>());
        Assert.Same(PdfEngineOptions.Default, provider.GetRequiredService<PdfEngineOptions>());
    }

    /// <summary>The whole path a caller walks: build a plan, merge to a file, inspect the result.</summary>
    [Fact]
    public async Task Merges_to_a_file_and_reads_the_result_back()
    {
        using var provider = new ServiceCollection()
            .AddSharpCorePDF(PdfEngineOptions.Default with { VerifyOutput = true })
            .BuildServiceProvider();

        using var folder = TestAssets.NewTemporaryFolder("merge-to-file");
        var destination = folder.File("merged.pdf");

        var plan = MergePlan.Create()
            .AddFile(TestAssets.Outlined)
            .AddFile(TestAssets.SafeWebCore, pages: "1,2");

        var merger = provider.GetRequiredService<IPdfMerger>();
        var result = await merger.MergeToFileAsync(plan, destination, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.PageCount);
        Assert.Equal(new FileInfo(destination).Length, result.OutputBytes);

        var inspector = provider.GetRequiredService<IPdfInspector>();
        var info = await inspector.InspectAsync(
            PdfFileSource.From(destination),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, info.PageCount);
        Assert.Equal("Outlined sample", info.Metadata.Title);
    }

    /// <summary>Engine options can come from configuration.</summary>
    [Fact]
    public void Binds_the_engine_options_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharpCorePDF:MaxNestingDepth"] = "12",
                ["SharpCorePDF:VerifyOutput"] = "true",
                ["SharpCorePDF:WriteBinaryMarker"] = "false",
            })
            .Build();

        using var provider = new ServiceCollection().AddSharpCorePDF(configuration).BuildServiceProvider();
        var options = provider.GetRequiredService<PdfEngineOptions>();

        Assert.Equal(12, options.MaxNestingDepth);
        Assert.True(options.VerifyOutput);
        Assert.False(options.WriteBinaryMarker);
    }

    /// <summary>Options that cannot work are reported instead of producing a broken document.</summary>
    [Fact]
    public async Task Refuses_unusable_engine_options()
    {
        using var provider = new ServiceCollection()
            .AddSharpCorePDF(PdfEngineOptions.Default with { MaxPagesPerSource = 0 })
            .BuildServiceProvider();

        var merger = provider.GetRequiredService<IPdfMerger>();
        var plan = MergePlan.Create().AddFile(TestAssets.Modern);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeToBytesAsync(plan, TestContext.Current.CancellationToken));
    }

    /// <summary>The most recent registration of the engine options wins.</summary>
    [Fact]
    public void Uses_the_last_registered_options()
    {
        using var provider = new ServiceCollection()
            .AddSharpCorePDF()
            .AddSharpCorePDF(PdfEngineOptions.Default with { MaxPagesPerSource = 7 })
            .BuildServiceProvider();

        Assert.Equal(7, provider.GetRequiredService<PdfEngineOptions>().MaxPagesPerSource);
    }
}
