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
                body => BuildMergedPage(context, source, body, page));
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
    /// <param name="sourcePage">The page as its own document states it, with the attributes it inherited there.</param>
    /// <returns>The page dictionary of the merged document.</returns>
    private static PdfDictionary BuildMergedPage(
        PdfMergeContext context,
        MergeSource source,
        PdfObject body,
        SourcePage sourcePage)
    {
        var inherited = sourcePage.Inherited;
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

        // Every page must have a media box (ISO 32000-1, table 30), on itself or inherited. A source page that
        // has neither is described as A4 by the inspection, and is written with that box, so the result is a
        // valid document whose page is the size the library reported instead of whatever each reader assumes.
        if (Pdf.PdfDocumentInspection.TryReadPageSize(source.Reader, sourcePage) is null)
        {
            var assumed = Domain.ValueObjects.PdfPageSize.A4;

            page.Set("MediaBox", new PdfArray(
            [
                new PdfInteger(0),
                new PdfInteger(0),
                new PdfReal(assumed.WidthPoints),
                new PdfReal(assumed.HeightPoints),
            ]));
            context.Warnings.Add(new(
                Domain.ValueObjects.PdfWarningKind.PageSizeAssumed,
                $"'{source.DisplayName}': page {sourcePage.Index + 1} has no readable /MediaBox; it was written with the A4 box it is described with, because every page of the result needs one."));
        }

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
