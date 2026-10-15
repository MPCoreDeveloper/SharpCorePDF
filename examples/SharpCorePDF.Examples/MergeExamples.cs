using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Merging documents that are on disk, and choosing which pages of them end up in the result.
/// </summary>
internal static class MergeExamples
{
    /// <summary>Merges three documents into one file, contributing every page of two of them.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    public static async Task MergeFilesAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // The shortest useful merge: a document per line, one destination, and nothing else to configure.
        // The plan says which pages, from which documents, end up where; the merger does the work.
        var plan = MergePlan.Create()
            .AddFile(context.Summary)              // every page of a two page document
            .AddFile(context.Appendix)             // every page of a three page document
            .AddFile(context.Annex, pages: "1");   // the first page of a one page document

        var destination = context.OutputPath("combined.pdf");
        var result = await context.Merger.MergeToFileAsync(plan, destination, cancellationToken);

        Report.Item("wrote", destination);
        Report.Item("pages", result.PageCount);
        Report.Item("bytes", result.OutputBytes);
        Report.Item("took", $"{result.Duration.TotalMilliseconds:0} ms");
        Report.Item("version", result.OutputVersion);

        Report.Line("what each source contributed:");
        foreach (var source in result.Sources)
        {
            Report.Line($"  {source}");
        }

        // The merge result describes the output; read the file back to see what a viewer will see. The
        // document information is the primary source's, because the merged document can only have one.
        var info = await context.Inspector.InspectAsync(
            PdfFileSource.From(destination),
            cancellationToken: cancellationToken);

        Report.Item("read back", info);
        Report.Item("title", info.Metadata.Title);

        Report.Line("page sizes of the result (the appendix is landscape, and every page of it is narrower than the one before):");
        foreach (var page in info.Pages)
        {
            Report.Line($"  {page}");
        }
    }

    /// <summary>Takes selected pages of a document, in the order the selection gives them.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    public static async Task SelectPagesAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // A selection is written the way a person writes it: one-based, comma separated, with "5", "2-7",
        // "3-" and "-4" as its ranges. The order of the ranges is the order of the pages in the result.
        await ShowAsync(
            context,
            "Appendix, page 3 before page 1: \"3,1\"",
            "pages-3-1.pdf",
            MergePlan.Create().AddFile(context.Appendix, pages: "3,1"),
            cancellationToken);

        await ShowAsync(
            context,
            "Appendix from page 2 to the end: \"2-\"",
            "pages-2-.pdf",
            MergePlan.Create().AddFile(context.Appendix, pages: "2-"),
            cancellationToken);

        await ShowAsync(
            context,
            "Summary up to page 2, then the first appendix page: \"-2\" and \"1\"",
            "pages-two-sources.pdf",
            MergePlan.Create()
                .AddFile(context.Summary, pages: "-2")
                .AddFile(context.Appendix, pages: "1"),
            cancellationToken);

        await ShowAsync(
            context,
            "A page named twice is taken once: \"2,2,1\"",
            "pages-duplicates.pdf",
            MergePlan.Create().AddFile(context.Appendix, pages: "2,2,1"),
            cancellationToken);

        // The same selection can be built in code instead of being parsed from text, which is what a page
        // picker in a user interface would do. PdfSource.Create is the way to a plan without a file path.
        var source = PdfSource.Create(PdfFileSource.From(context.Appendix), pages: PageSelection.FromTo(2, 3));

        await ShowAsync(
            context,
            "Appendix pages 2 to 3, selected in code",
            "pages-in-code.pdf",
            MergePlan.Create().AddSource(source),
            cancellationToken);
    }

    /// <summary>Merges a plan and prints which pages ended up in the result, in which order.</summary>
    /// <param name="context">The context the merge runs in.</param>
    /// <param name="what">The sentence that describes the selection.</param>
    /// <param name="fileName">The name of the file to write.</param>
    /// <param name="plan">The plan to execute.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    private static async Task ShowAsync(
        ExampleContext context,
        string what,
        string fileName,
        MergePlan plan,
        CancellationToken cancellationToken)
    {
        var destination = context.OutputPath(fileName);
        var result = await context.Merger.MergeToFileAsync(plan, destination, cancellationToken);

        Report.Heading($"{what}  ->  {result.PageCount} page(s)");

        var info = await context.Inspector.InspectAsync(
            PdfFileSource.From(destination),
            cancellationToken: cancellationToken);

        foreach (var page in info.Pages)
        {
            Report.Line(page.ToString());
        }
    }
}
