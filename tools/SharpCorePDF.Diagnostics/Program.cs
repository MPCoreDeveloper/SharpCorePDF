using System.Text.Json;
using System.Text.Json.Serialization;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Reading;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Diagnostics;

/// <summary>
/// Prints the one-call diagnostics report for a PDF: the document, its fonts, its signatures, an
/// optional archival pre-flight and the warnings the sub-reads made.
/// </summary>
/// <remarks>
/// <c>dotnet run --project tools/SharpCorePDF.Diagnostics -- &lt;file.pdf&gt; [--password &lt;p&gt;] [--profile 1b|2b|3b|2u|3u] [--json]</c>
/// </remarks>
internal static class Program
{
    // One instance: JsonSerializerOptions is immutable once used and expensive to build, so a per-call
    // instance would be thrown away on the one run this tool has anyway (CA1869).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string? path = null;
        string? password = null;
        PdfAProfile? profile = null;
        var json = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--password":
                    index++;
                    if (index >= args.Length)
                    {
                        Console.Error.WriteLine("--password needs a value.");
                        return 1;
                    }

                    password = args[index];
                    break;
                case "--profile":
                    index++;
                    if (index >= args.Length || !TryProfile(args[index], out var parsed))
                    {
                        Console.Error.WriteLine("--profile needs one of 1b, 2b, 3b, 2u, 3u or 4.");
                        return 1;
                    }

                    profile = parsed;
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    if (args[index].StartsWith('-'))
                    {
                        Console.Error.WriteLine("unknown argument: " + args[index]);
                        return 1;
                    }

                    path ??= args[index];
                    break;
            }
        }

        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine(path is null ? "no PDF was named." : $"{path} does not exist.");
            return 1;
        }

        try
        {
            var diagnoser = new PdfDiagnoserService(
                new PdfInspectorService(new PdfDocumentReader()),
                new PdfFontInspectorService(new PdfFontInspector()),
                new PdfSignatureValidatorService(new PdfDocumentSignatureValidator()),
                new PdfAWriterService(new PdfAWriter()));

            var report = await diagnoser
                .DiagnoseAsync(new PdfFileSource(path), password, profile, CancellationToken.None)
                .ConfigureAwait(false);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            }
            else
            {
                Print(report);
            }

            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            return 1;
        }
    }

    /// <summary>Prints the report as the lines a support engineer reads first.</summary>
    /// <param name="report">The report to print.</param>
    private static void Print(PdfDiagnosticsReport report)
    {
        var document = report.Document;

        Console.WriteLine($"document   : {document.SourceName}");
        Console.WriteLine($"version    : {document.Version}, {document.PageCount} page(s), readable {document.IsReadable}");
        Console.WriteLine($"fonts      : {report.Fonts.Count}");

        foreach (var font in report.Fonts)
        {
            Console.WriteLine($"  {font.ResourceName,-6} {font.BaseFont,-24} {font.Kind,-10} {font.Program}");
        }

        Console.WriteLine($"signatures : {report.Signatures.Count}");

        foreach (var signature in report.Signatures)
        {
            Console.WriteLine($"  {signature}");
        }

        Console.WriteLine(report.Archival is { } archival
            ? $"archival   : {archival}"
            : "archival   : not checked");

        Console.WriteLine($"findings   : {report.Warnings.Count}");

        foreach (var warning in report.Warnings)
        {
            Console.WriteLine($"  [{warning.Kind}] {warning.Message}");
        }
    }

    /// <summary>Maps the short profile names the command line accepts.</summary>
    /// <param name="text">The value after <c>--profile</c>.</param>
    /// <param name="profile">The profile, when the value names one.</param>
    /// <returns><see langword="true"/> when the value names a profile.</returns>
    private static bool TryProfile(string text, out PdfAProfile profile)
    {
        profile = text.ToLowerInvariant() switch
        {
            "1b" => PdfAProfile.PdfA1B,
            "2b" => PdfAProfile.PdfA2B,
            "3b" => PdfAProfile.PdfA3B,
            "2u" => PdfAProfile.PdfA2U,
            "3u" => PdfAProfile.PdfA3U,
            "4" => PdfAProfile.PdfA4,
            _ => (PdfAProfile)(-1),
        };

        return Enum.IsDefined(profile);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SharpCorePDF diagnostics");
        Console.WriteLine();
        Console.WriteLine("  SharpCorePDF.Diagnostics <file.pdf> [--password <p>] [--profile 1b|2b|3b|2u|3u|4] [--json]");
        Console.WriteLine();
        Console.WriteLine("  --password  the user or owner password, when the document is encrypted");
        Console.WriteLine("  --profile   also run the PDF/A pre-flight for that profile");
        Console.WriteLine("  --json      print the whole report as JSON instead of the text summary");
    }
}
