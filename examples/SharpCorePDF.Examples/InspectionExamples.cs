using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Describing documents before merging them, which is how a caller finds out what a merge would run into.
/// </summary>
internal static class InspectionExamples
{
    /// <summary>Describes every .pdf in the input folder and merges the ones that can be merged.</summary>
    /// <param name="context">The documents to inspect and the ports to inspect them with.</param>
    /// <param name="cancellationToken">A token to cancel the inspection.</param>
    public static async Task InspectFirstAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // The sample folder holds six files that behave differently: three that merge cleanly, one that merges
        // but has something to report, one that is not a PDF at all, and one whose trailer declares encryption.
        var mergeable = new List<string>();

        Report.Heading($"every .pdf in {context.InputFolder}");

        foreach (var path in Directory.GetFiles(context.InputFolder, "*.pdf").OrderBy(entry => entry, StringComparer.Ordinal))
        {
            var source = PdfFileSource.From(path);

            try
            {
                var info = await context.Inspector.InspectAsync(source, cancellationToken: cancellationToken);

                // Inspection reports encryption instead of throwing, so a caller can explain the problem
                // before a merge refuses the document.
                if (info.IsEncrypted)
                {
                    Report.Line($"{source.DisplayName,-14} encrypted: it cannot be merged");
                    continue;
                }

                Report.Line($"{source.DisplayName,-14} {info}");
                Report.Line($"{string.Empty,-14} first page {info.FirstPageSize?.ToString() ?? "(none)"}, "
                    + $"uniform page size {info.HasUniformPageSize}, bookmarks {info.OutlineItemCount}, "
                    + $"form {info.HasAcroForm}");

                if (info.Metadata.Title is { } title)
                {
                    Report.Line($"{string.Empty,-14} title \"{title}\"");
                }

                foreach (var warning in info.Warnings)
                {
                    Report.Line($"{string.Empty,-14} warning: {warning}");
                }

                mergeable.Add(path);
            }
            catch (PdfSourceFormatException exception)
            {
                Report.Line($"{source.DisplayName,-14} not a readable PDF: {exception.Detail}");
            }
        }

        Report.Line(
            "(HasUniformPageSize is False wherever the pages of a document differ in size, which the samples do on purpose)");

        // A plan is immutable, so building one in a loop is a reassignment and not a mutation. That is also
        // why a plan can be built once and executed from several threads.
        var plan = MergePlan.Create();

        foreach (var path in mergeable)
        {
            plan = plan.AddFile(path);
        }

        var problems = plan.Validate();

        Report.Heading($"the {mergeable.Count} document(s) that passed inspection, in one plan");
        Report.Item("plan problems", problems.Count == 0 ? "none" : string.Join("; ", problems));
        Report.Item("sources", plan.SourceCount);

        var result = await context.Merger.MergeToFileAsync(plan, context.OutputPath("inspected.pdf"), cancellationToken);

        Report.Item("pages", result.PageCount);
        Report.Item("bytes", result.OutputBytes);
    }
}
