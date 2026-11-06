using System.Globalization;
using System.Text;

namespace SharpCorePDF.Competitive;

internal static partial class ReportWriter
{
    private static void Robustness(StringBuilder text, IReadOnlyList<RobustnessResult> results)
    {
        text.AppendLine();
        text.AppendLine("## External-corpus robustness");
        text.AppendLine();
        if (results.Count == 0)
        {
            text.AppendLine("Not run. Pass `--robustness <folder>` to score open/extract completion on a local corpus.");
            return;
        }

        text.AppendLine("| Engine | Completed | Error | Timeout | Skipped | Median ms (completed) |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var group in results.GroupBy(result => result.Engine).OrderBy(group => group.Key))
        {
            var completed = group.Where(result => result.Status == "Completed").ToArray();
            var median = completed.Length == 0
                ? "—"
                : completed.Select(result => result.ElapsedMilliseconds).Order().ElementAt(completed.Length / 2)
                    .ToString("F2", CultureInfo.InvariantCulture);
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {group.Key} | {completed.Length} | {group.Count(result => result.Status == "Error")} | {group.Count(result => result.Status == "Timeout")} | {group.Count(result => result.Status == "Skipped")} | {median} |"));
        }

        var failures = results
            .Where(result => result.Status is "Error" or "Timeout")
            .Take(20)
            .ToArray();
        if (failures.Length == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("First failures:");
        text.AppendLine();
        text.AppendLine("| Engine | File | Status | Error |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var failure in failures)
        {
            text.AppendLine($"| {failure.Engine} | `{failure.File}` | {failure.Status} | {Escape(failure.Error)} |");
        }
    }
}
