using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.DependencyInjection;

/// <summary>
/// Registration of SharpCorePDF in a dependency injection container: one <c>AddSharpCorePDF()</c> call
/// wires the merger, the inspector and the PDF engine behind their ports.
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
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddSharpCorePDF(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services
            .AddOptions<PdfEngineOptions>()
            .Configure(engineOptions => configuration.GetSection(sectionName).Bind(engineOptions));

        services.Replace(ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<PdfEngineOptions>>().Value));
        return services.AddSharpCorePDFCore();
    }

    /// <summary>Registers the reader, the engine, the merger and the inspector.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    private static IServiceCollection AddSharpCorePDFCore(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new PdfDocumentReader(sp.GetRequiredService<PdfEngineOptions>()));
        services.TryAddSingleton(sp => new PdfMergeEngine(sp.GetRequiredService<PdfEngineOptions>()));

        services.TryAddSingleton<IPdfDocumentReader>(sp => sp.GetRequiredService<PdfDocumentReader>());
        services.TryAddSingleton<IPdfMergeEngine>(sp => sp.GetRequiredService<PdfMergeEngine>());
        services.TryAddSingleton<IPdfMerger, PdfMergerService>();
        services.TryAddSingleton<IPdfInspector, PdfInspectorService>();

        return services;
    }
}
