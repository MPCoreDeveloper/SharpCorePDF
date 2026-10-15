namespace SharpCorePDF.Domain;

/// <summary>Identifies the assembly producing a merged document.</summary>
internal static class ProductInfo
{
    /// <summary>The product name used in the default producer string.</summary>
    internal const string Name = "SharpCorePDF";

    /// <summary>Gets the informational version of the domain assembly.</summary>
    internal static string Version { get; } =
        typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Gets the producer string written when the caller does not supply one.</summary>
    internal static string DefaultProducer { get; } = $"{Name} {Version}";
}
