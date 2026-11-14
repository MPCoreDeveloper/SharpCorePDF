using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Reading;

namespace SharpCorePDF.DependencyInjection;

/// <summary>
/// Registration of SharpCorePDF in a dependency injection container: one <c>AddSharpCorePDF()</c> call
/// wires the merger, the editor, the inspector, the signer, the signature validator and the PDF engines
/// behind their ports.
/// </summary>
/// <remarks>
/// Every service is stateless, so all of them are registered as singletons; a merge executes on its
/// own context and can therefore run concurrently with other merges.
/// </remarks>
public static class SharpCorePDFServiceCollectionExtensions // NOSONAR S101: the product spells PDF in capitals
{
    /// <summary>The configuration section read when no other name is given.</summary>
    public const string DefaultConfigurationSection = "SharpCorePDF";

    /// <summary>Registers SharpCorePDF with the default engine options.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddSharpCorePDF(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(PdfEngineOptions.Default);
        return services.AddSharpCorePDFCore();
    }

    /// <summary>Registers SharpCorePDF with explicit engine options.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="engineOptions">The options of the PDF engine.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddSharpCorePDF(this IServiceCollection services, PdfEngineOptions engineOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(engineOptions);

        services.Replace(ServiceDescriptor.Singleton(engineOptions));
        return services.AddSharpCorePDFCore();
    }

    /// <summary>Registers SharpCorePDF with engine options bound from configuration.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration to read from.</param>
    /// <param name="sectionName">The section to read; defaults to <see cref="DefaultConfigurationSection"/>.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// Binding is performed with ordinary property reads against the named section. That keeps this
    /// overload trim- and Native AOT-compatible: <see cref="PdfEngineOptions"/> is a sealed record with
    /// init-only members, so the framework binder would leave defaults in place and reflection-based
    /// converters would require the annotations this library no longer carries.
    /// </para>
    /// <para>
    /// Binding an absent section keeps the defaults. Present keys are applied one by one; a value the
    /// configuration cannot parse is left as the default for that member, matching the binder's ordinary
    /// behaviour for a scalar that does not convert.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddSharpCorePDF(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var bound = BindEngineOptions(configuration.GetSection(sectionName));
        services.Replace(ServiceDescriptor.Singleton(bound));
        services.Replace(ServiceDescriptor.Singleton<IOptions<PdfEngineOptions>>(
            new OptionsWrapper<PdfEngineOptions>(bound)));
        return services.AddSharpCorePDFCore();
    }

    /// <summary>Registers the reader, the engines, the merger, the editor, the inspector and the signer.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    private static IServiceCollection AddSharpCorePDFCore(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new PdfDocumentReader(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfMergeEngine(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfEditEngine(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfPageContentReader(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfLayoutReader(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfObjectInspector(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfFontInspector(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfDocumentSigner(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfDocumentSignatureValidator(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfAWriter(sp.GetRequiredService<PdfEngineOptions>()));

        services.TryAddSingleton<IPdfDocumentReader>(sp => sp.GetRequiredService<PdfDocumentReader>());
        services.TryAddSingleton<IPdfDocumentBytesReader>(sp => sp.GetRequiredService<PdfDocumentReader>());
        services.TryAddSingleton<IPdfMergeEngine>(sp => sp.GetRequiredService<PdfMergeEngine>());
        services.TryAddSingleton<IPdfEditEngine>(sp => sp.GetRequiredService<PdfEditEngine>());
        services.TryAddSingleton<IPdfPageContentReader>(sp => sp.GetRequiredService<PdfPageContentReader>());
        services.TryAddSingleton<IPdfPageLayoutReader>(sp => sp.GetRequiredService<PdfLayoutReader>());
        services.TryAddSingleton<IPdfDocumentObjectInspector>(sp => sp.GetRequiredService<PdfObjectInspector>());
        services.TryAddSingleton<IPdfDocumentFontInspector>(sp => sp.GetRequiredService<PdfFontInspector>());
        services.TryAddSingleton<IPdfDocumentSigner>(sp => sp.GetRequiredService<PdfDocumentSigner>());
        services.TryAddSingleton<IPdfDocumentSignatureValidator>(sp => sp.GetRequiredService<PdfDocumentSignatureValidator>());
        services.TryAddSingleton<IPdfDocumentAWriter>(sp => sp.GetRequiredService<PdfAWriter>());
        services.TryAddSingleton<IPdfMerger, PdfMergerService>();
        services.TryAddSingleton<IPdfDocumentEditor, PdfDocumentEditorService>();
        services.TryAddSingleton<IPdfInspector, PdfInspectorService>();
        services.TryAddSingleton<IPdfContentReader, PdfContentReaderService>();
        services.TryAddSingleton<IPdfLayoutReader, PdfLayoutReaderService>();
        services.TryAddSingleton<IPdfObjectInspector, PdfObjectInspectorService>();
        services.TryAddSingleton<IPdfFontInspector, PdfFontInspectorService>();
        services.TryAddSingleton<IPdfSigner, PdfSignerService>();
        services.TryAddSingleton<IPdfSignatureValidator, PdfSignatureValidatorService>();
        services.TryAddSingleton<IPdfAWriter, PdfAWriterService>();

        return services;
    }

    /// <summary>Builds engine options from one configuration section without reflection.</summary>
    /// <param name="section">The section whose keys are the option names.</param>
    /// <returns>The bound options, with defaults for every key that is absent or unparsable.</returns>
    private static PdfEngineOptions BindEngineOptions(IConfiguration section)
    {
        var defaults = PdfEngineOptions.Default;

        return defaults with
        {
            MaxSourceBytes = ReadInt64(section, nameof(PdfEngineOptions.MaxSourceBytes), defaults.MaxSourceBytes),
            MaxNestingDepth = ReadInt32(section, nameof(PdfEngineOptions.MaxNestingDepth), defaults.MaxNestingDepth),
            MaxPagesPerSource = ReadInt32(section, nameof(PdfEngineOptions.MaxPagesPerSource), defaults.MaxPagesPerSource),
            ReconstructCrossReferenceOnFailure = ReadBoolean(
                section,
                nameof(PdfEngineOptions.ReconstructCrossReferenceOnFailure),
                defaults.ReconstructCrossReferenceOnFailure),
            VerifyOutput = ReadBoolean(section, nameof(PdfEngineOptions.VerifyOutput), defaults.VerifyOutput),
            WriteBinaryMarker = ReadBoolean(section, nameof(PdfEngineOptions.WriteBinaryMarker), defaults.WriteBinaryMarker),
            MaxInlineContentBytes = ReadInt32(
                section,
                nameof(PdfEngineOptions.MaxInlineContentBytes),
                defaults.MaxInlineContentBytes),
        };
    }

    private static long ReadInt64(IConfiguration section, string key, long fallback) =>
        long.TryParse(
            section[key],
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static int ReadInt32(IConfiguration section, string key, int fallback) =>
        int.TryParse(
            section[key],
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static bool ReadBoolean(IConfiguration section, string key, bool fallback) =>
        bool.TryParse(section[key], out var value) ? value : fallback;
}
