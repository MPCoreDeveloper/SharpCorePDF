using System.Globalization;
using System.Text;

namespace SharpCorePDF.Competitive;

internal static partial class ReportWriter
{
    private static void Performance(StringBuilder text, IReadOnlyList<PerformanceSummary> results)
    {
        text.AppendLine();
        text.AppendLine("## Performance");
        text.AppendLine();
        if (results.Count == 0)
        {
            text.AppendLine("Not run.");
            return;
        }

        text.AppendLine("| Engine | Scenario | N | Median ms | P95 ms | Min–max ms | Allocated KiB | Approx. peak MiB | Retained MiB | Work / error |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var result in results)
        {
            var detail = result.Error is null ? result.Work : result.Error;
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {result.Engine} | {result.Scenario} | {result.SuccessfulSamples} | {result.MedianMilliseconds:F2} | {result.P95Milliseconds:F2} | {result.MinimumMilliseconds:F2}–{result.MaximumMilliseconds:F2} | {result.MedianAllocatedBytes / 1024d:F0} | {result.MedianApproximatePeakManagedBytes / 1048576d:F1} | {result.MedianRetainedManagedBytes / 1048576d:F1} | {Escape(detail)} |"));
        }
    }

    private static string Ratio(int numerator, int denominator) =>
        Percent(denominator == 0 ? 0d : (double)numerator / denominator);

    private static string Mean(IEnumerable<double?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? "—" : Percent(present.Average());
    }

    private static string Percent(double? value) =>
        value is { } number ? number.ToString("P2", CultureInfo.InvariantCulture) : "—";

    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value) ? "—" : value.Replace("|", "\\|").Replace('\n', ' ').Replace('\r', ' ');
}
