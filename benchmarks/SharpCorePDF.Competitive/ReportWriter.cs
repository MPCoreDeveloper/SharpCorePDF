using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpCorePDF.Competitive;

internal static partial class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<IReadOnlyList<string>> WriteAsync(
        string outputFolder,
        BenchmarkReport report,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var jsonPath = Path.Combine(outputFolder, "results.json");
        var markdownPath = Path.Combine(outputFolder, "report.md");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false),
            cancellationToken);
        await File.WriteAllTextAsync(
            markdownPath,
            Markdown(report),
            new UTF8Encoding(false),
            cancellationToken);
        return [jsonPath, markdownPath];
    }

    private static string Markdown(BenchmarkReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# SharpCorePDF competitive evidence");
        text.AppendLine();
        text.AppendLine($"Generated (UTC): {report.GeneratedUtc:O}. This is a workload report, not an overall winner declaration.");
        text.AppendLine();
        text.AppendLine("## Environment and pinned engines");
        text.AppendLine();
        text.AppendLine($"- Runtime: {report.Environment.Runtime}; {report.Environment.OperatingSystem}; {report.Environment.Architecture}.");
        text.AppendLine($"- Logical processors: {report.Environment.LogicalProcessors}; server GC: {report.Environment.ServerGarbageCollection}.");
        text.AppendLine($"- Warm-ups / measured iterations per scenario: {report.Environment.Warmups} / {report.Environment.Iterations}.");
        text.AppendLine();
        text.AppendLine("| Engine | Version |");
        text.AppendLine("| --- | --- |");
        foreach (var engine in report.Engines)
        {
            text.AppendLine($"| {engine.Name} | `{engine.Version}` |");
        }

        Correctness(text, report.Correctness);
        Performance(text, report.Performance);
        Robustness(text, report.Robustness);
        text.AppendLine("## Method limits");
        text.AppendLine();
        text.AppendLine("- Text timing uses a deterministic repository-generated 200-page document. It is a regression shape, not a claim about the whole PDF ecosystem.");
        text.AppendLine("- Every scenario is executed once per engine before any warm-up is taken, so a row is steady state and excludes tiered compilation; the allocation column includes the harness's own per-word observation objects, which each engine pays alike.");
        text.AppendLine("- Inline-image timing uses a one-page document with a 1 MiB `BI`/`EI` image: it is the row that exercises the byte scan whose run is bulk.");
        text.AppendLine("- Correctness uses the checked-in human labels. Competitor output is never used as ground truth.");
        text.AppendLine("- Box agreement follows each label file's baseline-box convention. PdfPig's ink glyph rectangles will disagree with that convention even when character recall is perfect; see docs/competitive-scoreboard.md.");
        text.AppendLine("- Approximate managed peak is a 1 ms sampler of the whole process heap, not an exact per-operation high-water mark.");
        text.AppendLine("- Creation uses the same supplied TrueType bytes and text. Merge uses ten generated ten-page inputs. Output bytes are reported because producers legitimately differ.");
        text.AppendLine("- External-corpus rows exist only when `--robustness` is supplied. No external PDF is downloaded by this harness.");
        return text.ToString();
    }
}
