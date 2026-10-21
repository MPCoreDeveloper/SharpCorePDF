using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The page half of <see cref="PdfEditEngine"/>: which pages survive, in which order, and with which
/// rotation.
/// </summary>
public sealed partial class PdfEditEngine
{
    /// <summary>Turns the page tree of the source into the ordered list of pages the plan produces.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="plan">The plan being applied.</param>
    /// <param name="layoutChanged">Receives whether any operation changed the order or number of pages.</param>
    /// <returns>The pages in output order.</returns>
    /// <exception cref="PdfEditPlanValidationException">An operation names a page or position that does not exist at that point.</exception>
    private static List<EditedPage> BuildLayout(PdfFileReader reader, PdfEditPlan plan, out bool layoutChanged)
    {
        var layout = new List<EditedPage>(reader.Pages.Count);

        foreach (var page in reader.Pages)
        {
            layout.Add(new EditedPage(page));
        }

        layoutChanged = false;

        for (var index = 0; index < plan.Operations.Count; index++)
        {
            var operation = plan.Operations[index];

            switch (operation)
            {
                case PdfRotatePageOperation rotate:
                    EnsurePage(layout, rotate.PageNumber, index, reader.SourceName);

                    var rotated = layout[rotate.PageNumber - 1];
                    rotated.AddedRotation = PageRotationExtensions.Normalize(
                        rotated.AddedRotation.Degrees + rotate.Rotation.Degrees);
                    break;

                case PdfDeletePageOperation delete:
                    EnsurePage(layout, delete.PageNumber, index, reader.SourceName);

                    if (layout.Count == 1)
                    {
                        throw new PdfEditPlanValidationException(
                            $"operation {index}: deleting page {delete.PageNumber} would leave '{reader.SourceName}' without a page.");
                    }

                    layout.RemoveAt(delete.PageNumber - 1);
                    layoutChanged = true;
                    break;

                case PdfMovePageOperation move:
                    EnsurePage(layout, move.From, index, reader.SourceName);

                    if (move.To > layout.Count)
                    {
                        throw new PdfEditPlanValidationException(
                            $"operation {index}: position {move.To} does not exist in '{reader.SourceName}', which has {layout.Count} page(s) at that point.");
                    }

                    var moved = layout[move.From - 1];
                    layout.RemoveAt(move.From - 1);
                    layout.Insert(move.To - 1, moved);
                    layoutChanged = true;
                    break;

                case PdfDuplicatePageOperation duplicate:
                    EnsurePage(layout, duplicate.PageNumber, index, reader.SourceName);

                    if (duplicate.InsertAt > layout.Count + 1)
                    {
                        throw new PdfEditPlanValidationException(
                            $"operation {index}: insert position {duplicate.InsertAt} is beyond the end of '{reader.SourceName}', which has {layout.Count} page(s) at that point.");
                    }

                    var original = layout[duplicate.PageNumber - 1];
                    layout.Insert(duplicate.InsertAt - 1, original.Copy());
                    layoutChanged = true;
                    break;

                case PdfSetMetadataOperation:
                    break;

                default:
                    throw new PdfEditPlanValidationException(
                        $"operation {index}: operation kind '{operation.Kind}' is not implemented by this engine.");
            }
        }

        return layout;
    }

    /// <summary>Reserves the object number of every page that is written.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="layout">The pages in output order.</param>
    private static void AllocatePageTargets(PdfWriter writer, List<EditedPage> layout)
    {
        foreach (var page in layout)
        {
            page.TargetNumber = writer.AllocateObject();
        }
    }

    /// <summary>Points every reference to a source page at the first page it survives as.</summary>
    /// <param name="reader">The source document, whose complete page list is redirected.</param>
    /// <param name="layout">The pages in output order.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <remarks>
    /// A bookmark, a link or a form field keeps pointing at a real page even when its page was
    /// deleted or duplicated. The first occurrence of a source page is the one such a reference
    /// follows. A page that did not survive is folded onto the first page of the result, which is the
    /// same rule a merge uses for pages that were not selected.
    /// </remarks>
    private static void RedirectSourcePages(
        PdfFileReader reader,
        List<EditedPage> layout,
        PdfObjectCopier copier)
    {
        var firstTarget = layout[0].TargetNumber;
        var targetsBySource = new Dictionary<long, int>();

        foreach (var page in layout)
        {
            targetsBySource.TryAdd(page.Source.ObjectNumber, page.TargetNumber);
        }

        foreach (var objectNumber in reader.Pages.Select(page => page.ObjectNumber))
        {
            copier.Redirect(
                objectNumber,
                targetsBySource.TryGetValue(objectNumber, out var target) ? target : firstTarget);
        }
    }

    /// <summary>Redirects the source page tree nodes to the single page tree node of the result.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="pagesNodeNumber">The object number of the page tree node of the result.</param>
    private static void RedirectPageTree(PdfFileReader reader, PdfObjectCopier copier, int pagesNodeNumber)
    {
        foreach (var nodeNumber in reader.PageTree.NodeObjectNumbers)
        {
            copier.Redirect(nodeNumber, pagesNodeNumber);
        }
    }

    /// <summary>Builds and writes the dictionary of every page.</summary>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="pagesNodeNumber">The object number of the page tree node of the result.</param>
    /// <param name="layout">The pages in output order.</param>
    private static void WritePages(
        PdfObjectCopier copier,
        PdfWriter writer,
        int pagesNodeNumber,
        List<EditedPage> layout)
    {
        foreach (var entry in layout)
        {
            // The page object itself is written directly: a duplicated page needs its own object,
            // while the resources and annotations it shares with the original are reserved once by
            // the copier and shared by reference.
            writer.WriteObject(entry.TargetNumber, BuildPage(copier, entry, pagesNodeNumber));
        }
    }

    /// <summary>Builds one page dictionary of the result.</summary>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="entry">The page and the rotation the plan added to it.</param>
    /// <param name="pagesNodeNumber">The object number of the page tree node of the result.</param>
    /// <returns>The page dictionary.</returns>
    private static PdfDictionary BuildPage(
        PdfObjectCopier copier,
        EditedPage entry,
        int pagesNodeNumber)
    {
        var source = entry.Source.Dictionary;
        var page = new PdfDictionary();

        // Attributes the page inherited from its ancestors travel with the page, because the edited
        // document has one flat page tree; the page's own entries always win.
        foreach (var inherited in entry.Source.Inherited.Entries.Where(item => !source.ContainsKey(item.Key)))
        {
            page.Set(inherited.Key, copier.Clone(inherited.Value));
        }

        foreach (var item in source.Entries)
        {
            if (item.Key is "Parent" or "Rotate")
            {
                continue;
            }

            page.Set(item.Key, copier.Clone(item.Value));
        }

        page.Set("Type", new PdfName("Page"));
        page.Set("Parent", new PdfReference(pagesNodeNumber, 0));

        var rotation = entry.EffectiveRotation;

        if (rotation != PageRotation.None)
        {
            page.SetInteger("Rotate", rotation.Degrees);
        }

        return page;
    }

    /// <summary>Writes the single page tree node of the edited document.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="pagesNodeNumber">The object number of the page tree node.</param>
    /// <param name="layout">The pages in output order.</param>
    private static void WritePagesNode(PdfWriter writer, int pagesNodeNumber, List<EditedPage> layout)
    {
        var kids = new PdfArray();

        foreach (var page in layout)
        {
            kids.Add(new PdfReference(page.TargetNumber, 0));
        }

        var node = new PdfDictionary();
        node.Set("Type", new PdfName("Pages"));
        node.Set("Kids", kids);
        node.SetInteger("Count", kids.Count);

        writer.WriteObject(pagesNodeNumber, node);
    }

    /// <summary>Reads the rotation a page declares, before any edit is applied.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The normalised rotation.</returns>
    private static PageRotation ReadRotation(SourcePage page)
    {
        var declared = page.Dictionary.GetInteger("Rotate")
            ?? page.Inherited.GetInteger("Rotate")
            ?? 0;

        return PageRotationExtensions.Normalize((int)declared);
    }

    private static void EnsurePage(List<EditedPage> layout, int pageNumber, int index, string sourceName)
    {
        if (pageNumber > layout.Count)
        {
            throw new PdfEditPlanValidationException(
                $"operation {index}: page {pageNumber} does not exist in '{sourceName}', which has {layout.Count} page(s) at that point.");
        }
    }

    /// <summary>One page of the edited document while its layout is being resolved.</summary>
    private sealed class EditedPage(SourcePage source)
    {
        public SourcePage Source { get; } = source;

        public PageRotation AddedRotation { get; set; } = PageRotation.None;

        public int TargetNumber { get; set; }

        public PageRotation EffectiveRotation =>
            PageRotationExtensions.Normalize(ReadRotation(Source).Degrees + AddedRotation.Degrees);

        public EditedPage Copy() => new(Source) { AddedRotation = AddedRotation };
    }
}
