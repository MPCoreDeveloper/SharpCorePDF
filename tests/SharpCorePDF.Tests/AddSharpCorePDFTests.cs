using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Reading;
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

        Assert.Same(provider.GetRequiredService<IPdfMerger>(), provider.GetRequiredService<IPdfMerger>());
        Assert.Same(provider.GetRequiredService<IPdfDocumentEditor>(), provider.GetRequiredService<IPdfDocumentEditor>());
        Assert.Same(provider.GetRequiredService<IPdfInspector>(), provider.GetRequiredService<IPdfInspector>());
        Assert.Same(provider.GetRequiredService<IPdfContentReader>(), provider.GetRequiredService<IPdfContentReader>());
        Assert.Same(provider.GetRequiredService<IPdfLayoutReader>(), provider.GetRequiredService<IPdfLayoutReader>());
        Assert.Same(provider.GetRequiredService<IPdfObjectInspector>(), provider.GetRequiredService<IPdfObjectInspector>());
        Assert.Same(provider.GetRequiredService<IPdfSigner>(), provider.GetRequiredService<IPdfSigner>());
        Assert.Same(provider.GetRequiredService<IPdfSignatureValidator>(), provider.GetRequiredService<IPdfSignatureValidator>());
        Assert.Same(provider.GetRequiredService<IPdfAWriter>(), provider.GetRequiredService<IPdfAWriter>());

        Assert.Same(provider.GetRequiredService<PdfDocumentReader>(), provider.GetRequiredService<IPdfDocumentReader>());
        Assert.Same(provider.GetRequiredService<PdfDocumentReader>(), provider.GetRequiredService<IPdfDocumentBytesReader>());
        Assert.Same(provider.GetRequiredService<PdfMergeEngine>(), provider.GetRequiredService<IPdfMergeEngine>());
        Assert.Same(provider.GetRequiredService<PdfEditEngine>(), provider.GetRequiredService<IPdfEditEngine>());
        Assert.Same(provider.GetRequiredService<PdfPageContentReader>(), provider.GetRequiredService<IPdfPageContentReader>());
        Assert.Same(provider.GetRequiredService<PdfLayoutReader>(), provider.GetRequiredService<IPdfPageLayoutReader>());
        Assert.Same(provider.GetRequiredService<PdfObjectInspector>(), provider.GetRequiredService<IPdfDocumentObjectInspector>());
        Assert.Same(provider.GetRequiredService<PdfDocumentSigner>(), provider.GetRequiredService<IPdfDocumentSigner>());
        Assert.Same(provider.GetRequiredService<PdfDocumentSignatureValidator>(), provider.GetRequiredService<IPdfDocumentSignatureValidator>());
        Assert.Same(provider.GetRequiredService<PdfAWriter>(), provider.GetRequiredService<IPdfDocumentAWriter>());
        Assert.Same(PdfEngineOptions.Default, provider.GetRequiredService<PdfEngineOptions>());
    }

    /// <summary>Null registration arguments are refused before any service is added.</summary>
    [Fact]
    public void Refuses_null_registration_arguments()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var options = PdfEngineOptions.Default;

        var nullServices = Assert.Throws<ArgumentNullException>(
            () => SharpCorePDFServiceCollectionExtensions.AddSharpCorePDF(null!));
        var nullOptions = Assert.Throws<ArgumentNullException>(
            () => services.AddSharpCorePDF((PdfEngineOptions)null!));
        var nullServicesWithOptions = Assert.Throws<ArgumentNullException>(
            () => SharpCorePDFServiceCollectionExtensions.AddSharpCorePDF(null!, options));
        var nullConfiguration = Assert.Throws<ArgumentNullException>(
            () => services.AddSharpCorePDF((IConfiguration)null!));
        var nullServicesWithConfiguration = Assert.Throws<ArgumentNullException>(
            () => SharpCorePDFServiceCollectionExtensions.AddSharpCorePDF(null!, configuration));
        var blankSection = Assert.Throws<ArgumentException>(
            () => services.AddSharpCorePDF(configuration, " "));

        Assert.Equal("services", nullServices.ParamName);
        Assert.Equal("engineOptions", nullOptions.ParamName);
        Assert.Equal("services", nullServicesWithOptions.ParamName);
        Assert.Equal("configuration", nullConfiguration.ParamName);
        Assert.Equal("services", nullServicesWithConfiguration.ParamName);
        Assert.Equal("sectionName", blankSection.ParamName);
        Assert.Empty(services);
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

    /// <summary>Engine options can come from a custom configuration section without runtime binding.</summary>
    [Fact]
    public void Binds_the_engine_options_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Custom:Pdf:MaxSourceBytes"] = "1048576",
                ["Custom:Pdf:MaxNestingDepth"] = "12",
                ["Custom:Pdf:MaxPagesPerSource"] = "123",
                ["Custom:Pdf:ReconstructCrossReferenceOnFailure"] = "false",
                ["Custom:Pdf:VerifyOutput"] = "true",
                ["Custom:Pdf:WriteBinaryMarker"] = "false",
                ["Custom:Pdf:MaxInlineContentBytes"] = "4096",
            })
            .Build();

        using var provider = new ServiceCollection().AddSharpCorePDF(configuration, "Custom:Pdf").BuildServiceProvider();
        var options = provider.GetRequiredService<PdfEngineOptions>();

        Assert.Equal(1_048_576, options.MaxSourceBytes);
        Assert.Equal(12, options.MaxNestingDepth);
        Assert.Equal(123, options.MaxPagesPerSource);
        Assert.False(options.ReconstructCrossReferenceOnFailure);
        Assert.True(options.VerifyOutput);
        Assert.False(options.WriteBinaryMarker);
        Assert.Equal(4096, options.MaxInlineContentBytes);
        Assert.Same(options, provider.GetRequiredService<IOptions<PdfEngineOptions>>().Value);
    }

    /// <summary>The configuration overload is part of the trim and Native AOT compatibility claim.</summary>
    [Fact]
    public void Configuration_registration_has_no_trim_or_aot_requirements()
    {
        var registration = typeof(SharpCorePDFServiceCollectionExtensions).GetMethod(
            nameof(SharpCorePDFServiceCollectionExtensions.AddSharpCorePDF),
            [typeof(IServiceCollection), typeof(IConfiguration), typeof(string)]);

        Assert.NotNull(registration);
        Assert.Empty(registration.GetCustomAttributes(typeof(RequiresUnreferencedCodeAttribute), inherit: false));
        Assert.Empty(registration.GetCustomAttributes(typeof(RequiresDynamicCodeAttribute), inherit: false));
    }

    /// <summary>An absent configuration section keeps the documented defaults.</summary>
    [Fact]
    public void Keeps_the_defaults_when_the_configuration_section_is_absent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Other:MaxNestingDepth"] = "3",
            })
            .Build();

        using var provider = new ServiceCollection().AddSharpCorePDF(configuration).BuildServiceProvider();

        Assert.Equal(PdfEngineOptions.Default, provider.GetRequiredService<PdfEngineOptions>());
    }

    /// <summary>A value the configuration cannot parse is left as the default for that member.</summary>
    [Fact]
    public void Leaves_an_unparsable_configuration_value_as_its_default()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharpCorePDF:MaxNestingDepth"] = "not-a-number",
                ["SharpCorePDF:VerifyOutput"] = "true",
            })
            .Build();

        using var provider = new ServiceCollection().AddSharpCorePDF(configuration).BuildServiceProvider();
        var options = provider.GetRequiredService<PdfEngineOptions>();

        Assert.Equal(PdfEngineOptions.Default.MaxNestingDepth, options.MaxNestingDepth);
        Assert.True(options.VerifyOutput);
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
