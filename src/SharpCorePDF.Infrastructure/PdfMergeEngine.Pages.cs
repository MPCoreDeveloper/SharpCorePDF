using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The page half of <see cref="PdfMergeEngine"/>: which pages end up in the result, in which order.
/// </summary>
public sealed partial class PdfMergeEngine
{
    /// <summary>Reserves the object number of every page a source contributes and flattens its page tree.</summary>
    /// <param name="context">The merge context.</param>
    /// <param name="source">The source being prepared.</param>
    /// <exception cref="PdfSourceFormatException">The source has no page tree.</exception>
    /// <exception cref="MergePlanValidationException">The source contributes no page at all.</exception>
    private static void PreparePages(PdfMergeContext context, MergeSource source)
    {
        var pages = source.Reader.Pages;

        if (pages.Count == 0)
        {
            throw new PdfSourceFormatException(
                source.DisplayName,
                "the document has no page tree, so it cannot contribute pages to a merge.");
        }

        foreach (var pageNumber in source.Resolved.Pages)
        {
            if (pageNumber < 1 || pageNumber > pages.Count)
            {
                continue;
            }

            var page = pages[pageNumber - 1];
            var target = source.Copier.Reserve(page.ObjectNumber);

            source.PageTargets[pageNumber] = target;
            source.PagesInOrder.Add(target);
            source.Copier.Override(
                page.ObjectNumber,
                body => BuildMergedPage(context, source, body, page.Inherited));
        }

        if (source.PagesInOrder.Count == 0)
        {
            throw new MergePlanValidationException(
                $"'{source.DisplayName}' does not contribute any page to the merge.");
        }

        // Pages that were not selected are folded onto the first page the source contributes, so a
        // bookmark, a link or a form field that points at them still points at a real page.
        foreach (var page in pages.Where(page => !source.PageTargets.ContainsKey(page.Index + 1)))
        {
            source.Copier.Redirect(page.ObjectNumber, source.FirstPageTarget);
        }

        // The page tree of the source is replaced by the single page tree node of the merged document,
        // which is why inherited attributes were materialised onto the pages when they were read.
        foreach (var nodeNumber in source.Reader.PageTree.NodeObjectNumbers)
        {
            source.Copier.Redirect(nodeNumber, context.PagesNodeNumber);
        }
    }

    /// <summary>Turns a copied source page into a page of the merged document.</summary>
    /// <param name="context">The merge context.</param>
    /// <param name="source">The source the page came from.</param>
    /// <param name="body">The copied page dictionary.</param>
    /// <param name="inherited">The attributes the page inherited in its own document.</param>
    /// <returns>The page dictionary of the merged document.</returns>
    private static PdfDictionary BuildMergedPage(
        PdfMergeContext context,
        MergeSource source,
        PdfObject body,
        PdfDictionary inherited)
    {
        var copy = body as PdfDictionary ?? new PdfDictionary();
        var page = new PdfDictionary();

        // Attributes the page inherited from its ancestors travel with the page, because the merged
        // document has one flat page tree; the page's own entries always win.
        foreach (var entry in inherited.Entries.Where(entry => !copy.ContainsKey(entry.Key)))
        {
            page.Set(entry.Key, source.Copier.Clone(entry.Value));
        }

        foreach (var entry in copy.Entries)
        {
            page.Set(entry.Key, entry.Value);
        }

        page.Set("Type", new PdfName("Page"));
        page.Set("Parent", new PdfReference(context.PagesNodeNumber, 0));

        if (!context.Output.PreserveAnnotations)
        {
            page.Remove("Annots");
        }

        return page;
    }

    /// <summary>Writes the single page tree node of the merged document.</summary>
    /// <param name="context">The merge context.</param>
    private static void WritePagesNode(PdfMergeContext context)
    {
        var kids = new PdfArray();

        foreach (var source in context.Sources)
        {
            foreach (var target in source.PagesInOrder)
            {
                kids.Add(new PdfReference(target, 0));
            }
        }

        var node = new PdfDictionary();
        node.Set("Type", new PdfName("Pages"));
        node.Set("Kids", kids);
        node.SetInteger("Count", kids.Count);

        context.Writer.WriteObject(context.PagesNodeNumber, node);
    }
}
