using System.Globalization;
using System.Text;

namespace SharpCorePDF.Competitive;

internal static partial class ReportWriter
{
    private static void Correctness(StringBuilder text, IReadOnlyList<CorrectnessResult> results)
    {
        text.AppendLine();
        text.AppendLine("## Labelled extraction correctness");
        text.AppendLine();
        if (results.Count == 0)
        {
            text.AppendLine("Not run.");
            return;
        }

        text.AppendLine("| Engine | Documents | Character recall | Word precision | Word recall | Word F1 | Mean box agreement (label convention) | Mean box overlap (IoU) | Mean reading order |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var group in results.GroupBy(result => result.Engine).OrderBy(group => group.Key))
        {
            var completed = group.Where(result => result.Status == "Completed").ToArray();
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {group.Key} | {completed.Length}/{group.Count()} | {Ratio(completed.Sum(item => item.MatchedCharacters), completed.Sum(item => item.ExpectedCharacters))} | {Ratio(completed.Sum(item => item.MatchedWords), completed.Sum(item => item.ActualWords))} | {Ratio(completed.Sum(item => item.MatchedWords), completed.Sum(item => item.ExpectedWords))} | {F1(completed)} | {Mean(completed.Select(item => item.BoxAgreement))} | {Mean(completed.Select(item => item.BoxOverlap))} | {Mean(completed.Select(item => item.ReadingOrderAgreement))} |"));
        }

        text.AppendLine();
        text.AppendLine("| Engine | Document | Characters | Words matched / actual / expected | Word F1 | Boxes (label) | Box overlap (IoU) | Reading order | Warnings | Error |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var result in results)
        {
            text.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {result.Engine} | {result.Document} | {result.MatchedCharacters}/{result.ExpectedCharacters} | {result.MatchedWords}/{result.ActualWords}/{result.ExpectedWords} | {Percent(result.WordF1)} | {Percent(result.BoxAgreement)} | {Percent(result.BoxOverlap)} | {Percent(result.ReadingOrderAgreement)} | {result.WarningCount} | {Escape(result.Error)} |"));
        }
    }

    private static string F1(IReadOnlyCollection<CorrectnessResult> completed)
    {
        var actual = completed.Sum(item => item.ActualWords);
        var expected = completed.Sum(item => item.ExpectedWords);
        var matched = completed.Sum(item => item.MatchedWords);
        var precision = actual == 0 ? 0d : (double)matched / actual;
        var recall = expected == 0 ? 0d : (double)matched / expected;
        return Percent(precision + recall == 0d ? 0d : (2d * precision * recall) / (precision + recall));
    }
}
