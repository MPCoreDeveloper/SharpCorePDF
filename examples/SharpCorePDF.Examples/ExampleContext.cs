using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Ports;

namespace SharpCorePDF.Examples;

/// <summary>
/// What every example is handed: the documents it may merge, the ports it calls and somewhere to write.
/// </summary>
/// <remarks>
/// The context is the only place that knows about the container and about folders, so the examples
/// themselves read as the code a caller would write.
/// </remarks>
internal sealed class ExampleContext : IDisposable
{
    private ExampleContext(string root, ServiceProvider services, IReadOnlyList<string> documents)
    {
        this.services = services;
        Root = root;
        Documents = documents;
    }

    private readonly ServiceProvider services;

    /// <summary>Gets the folder the examples work in.</summary>
    public string Root { get; }

    /// <summary>Gets the folder that holds the sample documents, including the unusable ones.</summary>
    public string InputFolder => Path.Combine(Root, "input");

    /// <summary>Gets the folder the examples write their merged documents to.</summary>
    public string OutputFolder => Path.Combine(Root, "output");

    /// <summary>Gets the three sample documents that can be merged, in a sensible order.</summary>
    public IReadOnlyList<string> Documents { get; }

    /// <summary>Gets a two page portrait document.</summary>
    public string Summary => Documents[0];

    /// <summary>Gets a three page landscape document with an author, a different page size and a creation date.</summary>
    public string Appendix => Documents[1];

    /// <summary>Gets a one page portrait document.</summary>
    public string Annex => Documents[2];

    /// <summary>Gets the merger, exactly as an application would resolve it.</summary>
    public IPdfMerger Merger => services.GetRequiredService<IPdfMerger>();

    /// <summary>Gets the inspector, exactly as an application would resolve it.</summary>
    public IPdfInspector Inspector => services.GetRequiredService<IPdfInspector>();

    /// <summary>Creates the sample documents and the container the examples run against.</summary>
    /// <returns>The context; dispose it to release the container.</returns>
    public static ExampleContext Create()
    {
        // The engine options come from appsettings.json, bound to the "SharpCorePDF" section. VerifyOutput is
        // on there, so every example also proves that the document it wrote can be read back.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var services = new ServiceCollection()
            .AddSharpCorePDF(configuration)
            .BuildServiceProvider();

        var root = Path.Combine(Path.GetTempPath(), "SharpCorePDF.Examples");
        var input = Path.Combine(root, "input");
        Directory.CreateDirectory(Path.Combine(root, "output"));

        return new ExampleContext(root, services, SampleDocuments.CreateAll(input));
    }

    /// <summary>Returns the path of a file in the output folder, creating the folder when needed.</summary>
    /// <param name="fileName">The name of the file.</param>
    /// <returns>The full path of the file.</returns>
    public string OutputPath(string fileName)
    {
        Directory.CreateDirectory(OutputFolder);
        return Path.Combine(OutputFolder, fileName);
    }

    /// <summary>Releases the container.</summary>
    public void Dispose() => services.Dispose();
}

/// <summary>The output of the examples: a heading per step, and aligned facts below it.</summary>
internal static class Report
{
    /// <summary>Writes a heading.</summary>
    /// <param name="text">The heading.</param>
    public static void Heading(string text)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {text}");
    }

    /// <summary>Writes a label and a value on one aligned line.</summary>
    /// <param name="label">The label.</param>
    /// <param name="value">The value.</param>
    public static void Item(string label, object? value) => Console.WriteLine($"    {label,-28} {value}");

    /// <summary>Writes a line without a label.</summary>
    /// <param name="text">The line.</param>
    public static void Line(string text) => Console.WriteLine($"    {text}");

    /// <summary>Fingerprints content, so two runs can be compared without printing megabytes.</summary>
    /// <param name="content">The content to fingerprint.</param>
    /// <returns>The SHA-256 of the content as hexadecimal text.</returns>
    public static string Fingerprint(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}
