using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
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
        Assert.NotNull(provider.GetRequiredService<IPdfPageLayoutReader>());
        Assert.NotNull(provider.GetRequiredService<IPdfLayoutReader>());
        Assert.NotNull(provider.GetRequiredService<IPdfObjectInspector>());
        Assert.NotNull(provider.GetRequiredService<IPdfDocumentObjectInspector>());
        Assert.NotNull(provider.GetRequiredService<IPdfMergeEngine>());

        Assert.Same(provider.GetRequiredService<IPdfMerger>(), provider.GetRequiredService<IPdfMerger>());
        Assert.Same(provider.GetRequiredService<IPdfContentReader>(), provider.GetRequiredService<IPdfContentReader>());
        Assert.Same(provider.GetRequiredService<IPdfLayoutReader>(), provider.GetRequiredService<IPdfLayoutReader>());
        Assert.Same(provider.GetRequiredService<IPdfObjectInspector>(), provider.GetRequiredService<IPdfObjectInspector>());
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

    /// <summary>
    /// The signing pair a caller resolves from the container works end to end: the signer opens the source,
    /// writes a revision and leaves the destination open, and the validator reads that revision back as
    /// intact and, against the signer's own certificate, trusted.
    /// </summary>
    [Fact]
    public async Task Signs_through_the_container_and_validates_the_result()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        using var destination = new MemoryStream();

        var signed = await provider.GetRequiredService<IPdfSigner>().SignAsync(
            new PdfByteArraySource(SigningFixtures.BuildDocument(), "sign.pdf"),
            destination,
            SigningFixtures.Options(SigningFixtures.Certificate),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Signature1", signed.FieldName);
        Assert.True(signed.CoversWholeDocument);

        // The source is revision 1 and the signature is written into the appended revision, so the
        // revision it covers is the second one.
        Assert.Equal(2, signed.CoveredRevision);
        Assert.True(destination.Length > 0);
        Assert.True(destination.CanWrite);

        var validation = Assert.Single(await provider.GetRequiredService<IPdfSignatureValidator>().ValidateAsync(
            new PdfByteArraySource(destination.ToArray(), "signed.pdf"),
            new PdfTrustOptions { ExtraRoots = [SigningFixtures.Certificate] },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Equal(PdfTrustResult.Trusted, validation.Trust);
        Assert.Empty(validation.Problems);
    }

    /// <summary>
    /// The arguments a caller can get wrong are refused before the source is opened, so a call that cannot
    /// produce a signature never reads a document or writes to the destination.
    /// </summary>
    [Fact]
    public async Task Refuses_a_signature_it_cannot_produce()
    {
        using var provider = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
        var signer = provider.GetRequiredService<IPdfSigner>();
        using var destination = new MemoryStream();
        using var readOnly = new MemoryStream([], writable: false);
        using var publicOnly = X509CertificateLoader.LoadCertificate(
            SigningFixtures.Certificate.Export(X509ContentType.Cert));
        var source = new PdfByteArraySource(SigningFixtures.BuildDocument(), "sign.pdf");

        var noCertificate = await Assert.ThrowsAsync<ArgumentException>(() => signer.SignAsync(
            source,
            destination,
            SigningFixtures.Options(SigningFixtures.Certificate) with { Certificate = null! },
            cancellationToken: TestContext.Current.CancellationToken));

        var noKey = await Assert.ThrowsAsync<ArgumentException>(() => signer.SignAsync(
            source,
            destination,
            SigningFixtures.Options(publicOnly),
            cancellationToken: TestContext.Current.CancellationToken));

        var noWrite = await Assert.ThrowsAsync<ArgumentException>(() => signer.SignAsync(
            source,
            readOnly,
            SigningFixtures.Options(SigningFixtures.Certificate),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("options", noCertificate.ParamName);
        Assert.Contains("no private key", noKey.Message, StringComparison.Ordinal);
        Assert.Equal("destination", noWrite.ParamName);
        Assert.Equal(0, destination.Length);
    }
}
