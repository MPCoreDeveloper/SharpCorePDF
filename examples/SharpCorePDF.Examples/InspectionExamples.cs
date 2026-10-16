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
        // but has something to report, one that is not a PDF at all, and one that is encrypted with the
        // standard security handler and needs its password before it can be read.
        var mergeable = new List<string>();

        Report.Heading($"every .pdf in {context.InputFolder}");

        foreach (var path in Directory.GetFiles(context.InputFolder, "*.pdf").OrderBy(entry => entry, StringComparer.Ordinal))
        {
            var source = PdfFileSource.From(path);

            try
            {
                var info = await context.Inspector.InspectAsync(source, cancellationToken: cancellationToken);

                // Inspection reports a document it cannot read instead of throwing, so a caller can explain
                // the problem before a merge refuses the document. A document whose encryption stays closed
                // says whether a password is what stands in the way.
                if (!info.IsReadable)
                {
                    Report.Line($"{source.DisplayName,-14} unreadable: {info}");
                    Report.Line($"{string.Empty,-14} a password is required: {info.RequiresPassword}");
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

        // The same document, inspected with the password it was encrypted with: the page tree, the metadata
        // and the warnings are all there, exactly as they are for a document that was never encrypted.
        var encryptedPath = Path.Combine(context.InputFolder, "Encrypted.pdf");
        var locked = PdfFileSource.From(encryptedPath);
        var opened = await context.Inspector.InspectAsync(
            locked,
            password: SampleDocuments.EncryptedPassword,
            cancellationToken: cancellationToken);

        Report.Heading("the encrypted sample, inspected with its password");
        Report.Line($"{locked.DisplayName,-14} {opened}");
        Report.Line($"{string.Empty,-14} readable {opened.IsReadable}, password required {opened.RequiresPassword}, "
            + $"title \"{opened.Metadata.Title}\"");

        // A plan is immutable, so building one in a loop is a reassignment and not a mutation. That is also
        // why a plan can be built once and executed from several threads.
        var plan = MergePlan.Create();

        foreach (var path in mergeable)
        {
            plan = plan.AddFile(path);
        }

        // The encrypted sample joins the plan with its password, and every string and stream of it is read in
        // the clear, so the merged document holds a page that nothing else in this folder has.
        plan = plan.AddFile(encryptedPath, password: SampleDocuments.EncryptedPassword);

        var problems = plan.Validate();

        Report.Heading($"the {mergeable.Count} document(s) that passed inspection, plus the encrypted one, in one plan");
        Report.Item("plan problems", problems.Count == 0 ? "none" : string.Join("; ", problems));
        Report.Item("sources", plan.SourceCount);

        var result = await context.Merger.MergeToFileAsync(plan, context.OutputPath("inspected.pdf"), cancellationToken);

        Report.Item("pages", result.PageCount);
        Report.Item("bytes", result.OutputBytes);
    }
}
