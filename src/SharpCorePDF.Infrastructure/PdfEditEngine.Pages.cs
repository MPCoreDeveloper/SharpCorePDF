using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;
using DomainMatrix = SharpCorePDF.Domain.ValueObjects.PdfMatrix;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The page half of <see cref="PdfEditEngine"/>: which pages survive, in which order, and with which
/// rotation.
/// </summary>
public sealed partial class PdfEditEngine
{
    /// <summary>Turns the page tree of the source into the ordered list of pages the plan produces.</summary>
    /// <param name="sources">The documents of the edit.</param>
    /// <param name="plan">The plan being applied.</param>
    /// <param name="form">The form state the operations of the plan fill in.</param>
    /// <param name="warnings">The list that receives normalisation remarks.</param>
    /// <param name="layoutChanged">Receives whether any operation changed the order or number of pages.</param>
    /// <returns>The pages in output order.</returns>
    /// <exception cref="PdfEditPlanValidationException">An operation names a page or position that does not exist at that point.</exception>
    private static List<EditedPage> BuildLayout(
        EditSources sources,
        PdfEditPlan plan,
        FormPlan form,
        List<PdfWarning> warnings,
        out bool layoutChanged)
    {
        var document = sources.Document;
        var layout = new List<EditedPage>(document.Pages.Count);

        foreach (var page in document.Pages)
        {
            layout.Add(CreatePage(document, page, warnings));
        }

        layoutChanged = false;

        for (var index = 0; index < plan.Operations.Count; index++)
        {
            var operation = plan.Operations[index];

            switch (operation)
            {
                case PdfRotatePageOperation rotate:
                    EnsurePage(layout, rotate.PageNumber, index, document.SourceName);

                    var rotated = layout[rotate.PageNumber - 1];
                    rotated.AddedRotation = PageRotationExtensions.Normalize(
                        rotated.AddedRotation.Degrees + rotate.Rotation.Degrees);
                    break;

                case PdfCropPageOperation crop:
                    EnsurePage(layout, crop.PageNumber, index, document.SourceName);
                    ApplyCrop(layout[crop.PageNumber - 1], crop, index, document.SourceName, warnings);
                    break;

                case PdfResizePageOperation resize:
                    EnsurePage(layout, resize.PageNumber, index, document.SourceName);
                    ApplyResize(layout[resize.PageNumber - 1], resize);
                    break;

                case PdfDeletePageOperation delete:
                    EnsurePage(layout, delete.PageNumber, index, document.SourceName);

                    if (layout.Count == 1)
                    {
                        throw new PdfEditPlanValidationException(
                            $"operation {index}: deleting page {delete.PageNumber} would leave '{document.SourceName}' without a page.");
                    }

                    layout.RemoveAt(delete.PageNumber - 1);
                    layoutChanged = true;
                    break;

                case PdfMovePageOperation move:
                    EnsurePage(layout, move.From, index, document.SourceName);

                    if (move.To > layout.Count)
                    {
                        throw new PdfEditPlanValidationException(
                            $"operation {index}: position {move.To} does not exist in '{document.SourceName}', which has {layout.Count} page(s) at that point.");
                    }

                    var moved = layout[move.From - 1];
                    layout.RemoveAt(move.From - 1);
                    layout.Insert(move.To - 1, moved);
                    layoutChanged = true;
                    break;

                case PdfDuplicatePageOperation duplicate:
                    EnsurePage(layout, duplicate.PageNumber, index, document.SourceName);

                    if (duplicate.InsertAt > layout.Count + 1)
                    {
                        throw new PdfEditPlanValidationException(
                            $"operation {index}: insert position {duplicate.InsertAt} is beyond the end of '{document.SourceName}', which has {layout.Count} page(s) at that point.");
                    }

                    var original = layout[duplicate.PageNumber - 1];
                    layout.Insert(duplicate.InsertAt - 1, original.Copy());
                    layoutChanged = true;
                    break;

                case PdfSetMetadataOperation:
                case PdfSetXmpOperation:
                case PdfSetOutlineOperation:
                case PdfSetPageLabelsOperation:
                case PdfSetViewerPreferencesOperation:
                    // These describe the document rather than its pages; they are written with the
                    // catalog, after the layout is known.
                    break;

                case PdfSetFormOperation setForm:
                {
                    form.Touched = true;

                    if (setForm.Form?.DefaultTextStyle is { } style)
                    {
                        form.DefaultStyle = style;
                    }

                    foreach (var field in setForm.Form?.Fields ?? [])
                    {
                        if (field is null)
                        {
                            continue;
                        }

                        EnsurePage(layout, field.PageNumber, index, document.SourceName);
                        form.Fields.Add((index, field));
                    }

                    break;
                }

                case PdfResetFormOperation:
                    form.Touched = true;
                    form.Reset = true;
                    break;

                case PdfFlattenFormOperation flatten:
                {
                    form.Touched = true;

                    if (flatten.PageNumber is { } pageNumber)
                    {
                        EnsurePage(layout, pageNumber, index, document.SourceName);
                        form.FlattenPages.Add(pageNumber);
                    }
                    else
                    {
                        form.FlattenAll = true;
                    }

                    break;
                }

                case PdfOverlayOperation overlay:
                {
                    EnsurePage(layout, overlay.PageNumber, index, document.SourceName);

                    // The operation names a document of exactly one page, so the layer is that page.
                    // It travels with the page it was applied to: a duplicate keeps it, a deletion
                    // removes it, and a move carries it along.
                    var layer = sources.SourceOf(overlay);
                    layout[overlay.PageNumber - 1].Overlays.Add(
                        new OverlayLayer(layer, layer.Pages[0], overlay.Options, overlay.Layer));
                    break;
                }

                case PdfAddAnnotationOperation add:
                    ApplyAnnotation(
                        layout,
                        add.PageNumber,
                        new AnnotationChange
                        {
                            Kind = AnnotationChangeKind.Add,
                            Annotation = add.Annotation,
                            Name = add.Annotation.Name ?? string.Create(CultureInfo.InvariantCulture, $"scpdf-{index + 1}"),
                        },
                        index,
                        document.SourceName);
                    break;

                case PdfUpdateAnnotationOperation update:
                    ApplyAnnotation(
                        layout,
                        update.PageNumber,
                        new AnnotationChange
                        {
                            Kind = AnnotationChangeKind.Update,
                            Annotation = update.Annotation,
                            Id = update.AnnotationId,
                            Name = update.AnnotationId,
                        },
                        index,
                        document.SourceName);
                    break;

                case PdfRemoveAnnotationOperation remove:
                    ApplyAnnotation(
                        layout,
                        remove.PageNumber,
                        new AnnotationChange
                        {
                            Kind = AnnotationChangeKind.Remove,
                            Id = remove.AnnotationId,
                            Name = remove.AnnotationId,
                        },
                        index,
                        document.SourceName);
                    break;


                case PdfImportPageOperation import:
                {
                    var source = sources.SourceOf(import);
                    var page = SelectPage(source, import.SourcePageNumber, index);
                    EnsurePosition(layout, import.InsertAt, index, document.SourceName);
                    layout.Insert(import.InsertAt - 1, CreatePage(source, page, warnings));
                    layoutChanged = true;
                    break;
                }

                case PdfInsertPageOperation insert:
                {
                    var generated = sources.SourceOf(insert);
                    EnsurePosition(layout, insert.InsertAt, index, document.SourceName);
                    layout.Insert(insert.InsertAt - 1, CreatePage(generated, generated.Pages[0], warnings));
                    layoutChanged = true;
                    break;
                }

                case PdfAppendOperation append:
                {
                    var appended = sources.SourceOf(append);
                    var count = appended.Pages.Count;

                    if (append.Pages.From > count)
                    {
                        throw new PdfEditPlanValidationException(
                            $"operation {index}: page range '{append.Pages}' starts at page {append.Pages.From} but '{appended.SourceName}' only has {count} page(s).");
                    }

                    // A range that reaches past the last page of the appended document simply stops
                    // there, which is the same rule an extraction follows inside the edited document.
                    var last = Math.Min(append.Pages.IsOpenEnded ? count : append.Pages.To, count);

                    for (var number = append.Pages.From; number <= last; number++)
                    {
                        layout.Add(CreatePage(appended, appended.Pages[number - 1], warnings));
                    }

                    layoutChanged = true;
                    break;
                }

                case PdfReorderPagesOperation reorder:
                    layout = Reorder(layout, reorder, index, document.SourceName);
                    layoutChanged = true;
                    break;

                case PdfExtractPagesOperation extract:
                    layout = Extract(layout, extract, index, document.SourceName);
                    layoutChanged = true;
                    break;

                case PdfSplitDocumentOperation:
                    // A split produces several documents, so the engine refuses such a plan in
                    // ApplyAsync; the operation itself changes nothing about the layout.
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
    /// <param name="layout">The pages of the result that come from this source, in output order.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <remarks>
    /// A bookmark, a link or a form field keeps pointing at a real page even when its page was
    /// deleted or duplicated. The first occurrence of a source page is the one such a reference
    /// follows. A page that did not survive is folded onto the first page this source contributes,
    /// which is the same rule a merge uses for pages that were not selected.
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
    /// <param name="sources">The documents of the edit.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="pagesNodeNumber">The object number of the page tree node of the result.</param>
    /// <param name="layout">The pages in output order.</param>
    /// <param name="annotations">The annotations of the edit.</param>
    /// <param name="warnings">The list that receives the remarks made while writing.</param>
    private static void WritePages(
        EditSources sources,
        PdfWriter writer,
        int pagesNodeNumber,
        List<EditedPage> layout,
        AnnotationContext annotations,
        List<PdfWarning> warnings)
    {
        for (var index = 0; index < layout.Count; index++)
        {
            var entry = layout[index];
            // A resized page keeps its original content stream untouched. Two tiny streams around it
            // open a graphics state, apply the scale or the offset, and close it again; a viewer
            // concatenates the array, so the original bytes are still exactly the bytes that were read.
            WriteContentFrame(writer, entry);

            // The form XObject of an overlay or underlay and the stream that draws it are written
            // before the page, because the page names both of them.
            WriteOverlayLayers(sources, writer, entry);

            // The annotations of the page are written before it for the same reason: its /Annots array
            // names the objects, and an appearance is named by the annotation that owns it.
            WritePageAnnotations(writer, entry, annotations, layout, index + 1, warnings);

            // The page object itself is written directly: a duplicated page needs its own object,
            // while the resources and annotations it shares with the original are reserved once by
            // the copier and shared by reference. Each page is read through the copier of its own
            // document, so a page copied from another file is written exactly like a native one.
            writer.WriteObject(
                entry.TargetNumber,
                BuildPage(sources.CopierOf(entry.Reader), writer, entry, pagesNodeNumber));
        }
    }

    /// <summary>Builds one page dictionary of the result.</summary>
    /// <param name="copier">The copier that writes the objects of the document the page comes from.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="entry">The page and the changes the plan applied to it.</param>
    /// <param name="pagesNodeNumber">The object number of the page tree node of the result.</param>
    /// <returns>The page dictionary.</returns>
    private static PdfDictionary BuildPage(
        PdfObjectCopier copier,
        PdfWriter writer,
        EditedPage entry,
        int pagesNodeNumber)
    {
        var source = entry.Source.Dictionary;
        var page = new PdfDictionary();
        var layered = entry.HasLayer;

        // Attributes the page inherited from its ancestors travel with the page, because the edited
        // document has one flat page tree; the page's own entries always win.
        foreach (var inherited in entry.Source.Inherited.Entries.Where(item =>
            !source.ContainsKey(item.Key) && !(layered && item.Key is "Resources")))
        {
            page.Set(inherited.Key, copier.Clone(inherited.Value));
        }

        foreach (var item in source.Entries)
        {
            if (item.Key is "Parent" or "Rotate" or "MediaBox" or "CropBox" or "Contents"
                || (layered && item.Key is "Resources")
                || (entry.AnnotationSlots is not null && item.Key is "Annots"))
            {
                continue;
            }

            page.Set(item.Key, copier.Clone(item.Value));
        }

        page.Set("Type", new PdfName("Page"));
        page.Set("Parent", new PdfReference(pagesNodeNumber, 0));
        page.Set("MediaBox", ToArray(entry.MediaBox));

        if (entry.CropBox is { } cropBox)
        {
            page.Set("CropBox", ToArray(cropBox));
        }

        var rotation = entry.EffectiveRotation;

        if (rotation != PageRotation.None)
        {
            page.SetInteger("Rotate", rotation.Degrees);
        }

        if (layered)
        {
            // An overlay or underlay is content, not an annotation: the streams the page already had
            // are kept byte for byte and wrapped in q/Q, so a reader that ignores the layer still
            // shows every byte the page carried before the edit.
            page.Set("Contents", LayeredContents(copier, writer, entry));
            page.Set("Resources", LayeredResources(copier, entry));
        }
        else if (entry.HasContentFrame)
        {
            page.Set("Contents", FramedContents(entry.Reader, copier, writer, entry, source.Get("Contents")));
        }
        else if (source.Get("Contents") is { } content)
        {
            page.Set("Contents", copier.Clone(content));
        }

        // The array is rebuilt from the slots the page kept, so a removed annotation is simply absent
        // and an added one names the object written for it.
        if (entry.AnnotationSlots is not null)
        {
            page.Set("Annots", BuildAnnots(copier, entry));
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

    /// <summary>Builds the edited page of one page of one of the documents of the edit.</summary>
    /// <param name="reader">The document the page comes from.</param>
    /// <param name="page">The page.</param>
    /// <param name="warnings">The list that receives the geometry remarks.</param>
    /// <returns>The page as the edit sees it.</returns>
    private static EditedPage CreatePage(PdfFileReader reader, SourcePage page, List<PdfWarning> warnings)
    {
        var mediaBox = ReadBox(reader, page, "MediaBox", out var mediaInverted);

        if (mediaBox is null)
        {
            warnings.Add(new(
                PdfWarningKind.PageSizeAssumed,
                $"'{reader.SourceName}': page {page.Index + 1} has no readable /MediaBox; it is treated as A4 while editing."));
            mediaBox = new PdfRectangle(0d, 0d, PdfPageSize.A4.WidthPoints, PdfPageSize.A4.HeightPoints);
        }
        else if (mediaInverted)
        {
            warnings.Add(new(
                PdfWarningKind.PageGeometryNormalized,
                $"'{reader.SourceName}': the /MediaBox of page {page.Index + 1} is inverted and was normalised to [{mediaBox.Left:0.##} {mediaBox.Bottom:0.##} {mediaBox.Right:0.##} {mediaBox.Top:0.##}]."));
        }

        var cropBox = ReadBox(reader, page, "CropBox", out var cropInverted);

        if (cropInverted)
        {
            warnings.Add(new(
                PdfWarningKind.PageGeometryNormalized,
                $"'{reader.SourceName}': the /CropBox of page {page.Index + 1} is inverted and was normalised to [{cropBox!.Left:0.##} {cropBox.Bottom:0.##} {cropBox.Right:0.##} {cropBox.Top:0.##}]."));
        }

        return new EditedPage(reader, page, mediaBox) { CropBox = cropBox };
    }

    /// <summary>Selects a page of another document of the edit.</summary>
    /// <param name="reader">The document to read from.</param>
    /// <param name="pageNumber">The one-based page number inside that document.</param>
    /// <param name="index">The position of the operation in the plan.</param>
    /// <returns>The page.</returns>
    /// <exception cref="PdfEditPlanValidationException">The document has no such page.</exception>
    private static SourcePage SelectPage(PdfFileReader reader, int pageNumber, int index)
    {
        if (pageNumber < 1 || pageNumber > reader.Pages.Count)
        {
            throw new PdfEditPlanValidationException(
                $"operation {index}: page {pageNumber} does not exist in '{reader.SourceName}', which has {reader.Pages.Count} page(s).");
        }

        return reader.Pages[pageNumber - 1];
    }

    /// <summary>Checks that a page can be inserted at a position of the pages produced so far.</summary>
    /// <param name="layout">The pages in output order so far.</param>
    /// <param name="position">The one-based position the new page takes.</param>
    /// <param name="index">The position of the operation in the plan.</param>
    /// <param name="sourceName">The name of the edited document, for diagnostics.</param>
    /// <exception cref="PdfEditPlanValidationException">The position is past the end.</exception>
    private static void EnsurePosition(List<EditedPage> layout, int position, int index, string sourceName)
    {
        if (position < 1 || position > layout.Count + 1)
        {
            throw new PdfEditPlanValidationException(
                $"operation {index}: insert position {position} is beyond the end of '{sourceName}', which has {layout.Count} page(s) at that point.");
        }
    }

    /// <summary>Rearranges the pages produced so far.</summary>
    /// <param name="layout">The pages in output order so far.</param>
    /// <param name="reorder">The operation.</param>
    /// <param name="index">The position of the operation in the plan.</param>
    /// <param name="sourceName">The name of the edited document, for diagnostics.</param>
    /// <returns>The pages in the requested order.</returns>
    /// <exception cref="PdfEditPlanValidationException">The order is not a permutation of the pages.</exception>
    private static List<EditedPage> Reorder(
        List<EditedPage> layout,
        PdfReorderPagesOperation reorder,
        int index,
        string sourceName)
    {
        var order = reorder.Order;

        if (order.Count != layout.Count)
        {
            throw new PdfEditPlanValidationException(
                $"operation {index}: the new page order lists {order.Count} page(s) but '{sourceName}' has {layout.Count} page(s) at that point.");
        }

        var seen = new HashSet<int>(layout.Count);

        foreach (var pageNumber in order)
        {
            if (pageNumber < 1 || pageNumber > layout.Count)
            {
                throw new PdfEditPlanValidationException(
                    $"operation {index}: page {pageNumber} of the new page order does not exist in '{sourceName}', which has {layout.Count} page(s) at that point.");
            }

            if (!seen.Add(pageNumber))
            {
                throw new PdfEditPlanValidationException(
                    $"operation {index}: page {pageNumber} is listed more than once in the new page order.");
            }
        }

        return [.. order.Select(pageNumber => layout[pageNumber - 1])];
    }

    /// <summary>Keeps only the pages an extraction selects.</summary>
    /// <param name="layout">The pages in output order so far.</param>
    /// <param name="extract">The operation.</param>
    /// <param name="index">The position of the operation in the plan.</param>
    /// <param name="sourceName">The name of the edited document, for diagnostics.</param>
    /// <returns>The selected pages, in their original order.</returns>
    /// <exception cref="PdfEditPlanValidationException">The range selects no page.</exception>
    private static List<EditedPage> Extract(
        List<EditedPage> layout,
        PdfExtractPagesOperation extract,
        int index,
        string sourceName)
    {
        var kept = new List<EditedPage>(layout.Count);

        for (var position = 0; position < layout.Count; position++)
        {
            if (extract.Pages.Contains(position + 1))
            {
                kept.Add(layout[position]);
            }
        }

        return kept.Count > 0
            ? kept
            : throw new PdfEditPlanValidationException(
                $"operation {index}: page range '{extract.Pages}' selects no page of '{sourceName}', which has {layout.Count} page(s) at that point.");
    }

    /// <summary>Reads an inherited page box, or <see langword="null"/> when it is missing or unreadable.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="page">The page whose box is read.</param>
    /// <param name="key">The name of the box, <c>MediaBox</c> or <c>CropBox</c>.</param>
    /// <param name="inverted">Receives whether the corners were written in the opposite order.</param>
    /// <returns>The box with its corners normalised, or <see langword="null"/>.</returns>
    private static PdfRectangle? ReadBox(
        PdfFileReader reader,
        SourcePage page,
        string key,
        out bool inverted)
    {
        inverted = false;

        var array = reader.ResolveArray(page.Dictionary.Get(key))
            ?? reader.ResolveArray(page.Inherited.Get(key));

        if (array is not { Count: >= 4 })
        {
            return null;
        }

        var numbers = new double[4];

        for (var index = 0; index < numbers.Length; index++)
        {
            if (ReadNumber(reader, array.Items[index]) is not { } number)
            {
                return null;
            }

            numbers[index] = number;
        }

        inverted = numbers[0] > numbers[2] || numbers[1] > numbers[3];
        return PdfRectangle.FromCorners(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>Reads one number, resolving an indirect reference.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="value">The value to read.</param>
    /// <returns>The number, or <see langword="null"/> when the value is not a number.</returns>
    private static double? ReadNumber(PdfFileReader reader, PdfObject? value) => reader.Resolve(value) switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => null,
    };

    /// <summary>Clamps a requested crop box to the media box and records what was changed.</summary>
    /// <param name="page">The page being cropped.</param>
    /// <param name="crop">The operation.</param>
    /// <param name="index">The position of the operation in the plan.</param>
    /// <param name="sourceName">The name of the source, for diagnostics.</param>
    /// <param name="warnings">The list that receives the normalisation remark.</param>
    /// <exception cref="PdfEditPlanValidationException">The crop box does not overlap the media box.</exception>
    private static void ApplyCrop(
        EditedPage page,
        PdfCropPageOperation crop,
        int index,
        string sourceName,
        List<PdfWarning> warnings)
    {
        var media = page.MediaBox;
        var requested = crop.CropBox;

        var left = Math.Max(media.Left, requested.Left);
        var bottom = Math.Max(media.Bottom, requested.Bottom);
        var right = Math.Min(media.Right, requested.Right);
        var top = Math.Min(media.Top, requested.Top);

        if (left >= right || bottom >= top)
        {
            throw new PdfEditPlanValidationException(
                $"operation {index}: the crop box of page {crop.PageNumber} does not overlap the media box of '{sourceName}'.");
        }

        var normalised = new PdfRectangle(left, bottom, right, top);

        if (normalised != requested)
        {
            warnings.Add(new(
                PdfWarningKind.PageGeometryNormalized,
                $"'{sourceName}': the crop box of page {crop.PageNumber} reaches outside the media box and was normalised to [{normalised.Left:0.##} {normalised.Bottom:0.##} {normalised.Right:0.##} {normalised.Top:0.##}]."));
        }

        page.CropBox = normalised;
    }

    /// <summary>Changes the page size and records the transform the original content needs.</summary>
    /// <param name="page">The page being resized.</param>
    /// <param name="resize">The operation.</param>
    private static void ApplyResize(EditedPage page, PdfResizePageOperation resize)
    {
        var media = page.MediaBox;
        var width = resize.Size.WidthPoints;
        var height = resize.Size.HeightPoints;

        DomainMatrix transform;

        if (resize.Mode == PdfPageResizeMode.Scale)
        {
            var scaleX = width / media.Width;
            var scaleY = height / media.Height;

            transform = new DomainMatrix(
                scaleX,
                0d,
                0d,
                scaleY,
                -media.Left * scaleX,
                -media.Bottom * scaleY);
        }
        else
        {
            transform = DomainMatrix.Translate(
                ((width - media.Width) / 2d) - media.Left,
                ((height - media.Height) / 2d) - media.Bottom);
        }

        if (page.CropBox is { } cropBox)
        {
            page.CropBox = TransformBox(cropBox, transform);
        }

        page.Transform = page.Transform.Multiply(transform);
        page.MediaBox = new PdfRectangle(0d, 0d, width, height);
    }

    /// <summary>Maps the corners of a box through a transform and normalises the result.</summary>
    /// <param name="box">The box to map.</param>
    /// <param name="transform">The transform to apply.</param>
    /// <returns>The mapped box.</returns>
    private static PdfRectangle TransformBox(PdfRectangle box, DomainMatrix transform)
    {
        var lowerLeft = transform.Transform(new PdfPoint(box.Left, box.Bottom));
        var upperRight = transform.Transform(new PdfPoint(box.Right, box.Top));

        return PdfRectangle.FromCorners(lowerLeft.X, lowerLeft.Y, upperRight.X, upperRight.Y);
    }

    /// <summary>Writes a page box as a PDF array.</summary>
    /// <param name="box">The box to write.</param>
    /// <returns>The array.</returns>
    private static PdfArray ToArray(PdfRectangle box) =>
        new(
        [
            new PdfReal(box.Left),
            new PdfReal(box.Bottom),
            new PdfReal(box.Right),
            new PdfReal(box.Top),
        ]);

    /// <summary>
    /// Writes the streams that frame the original content of a page: the state it starts in is opened
    /// before its own streams and restored after them.
    /// </summary>
    /// <remarks>
    /// A page whose transform is the identity still gets the pair, because what it opens and closes is
    /// the state itself: a stream a page leaves unbalanced — a transform stated outside a <c>q</c>, as a
    /// browser's print output does — must not reach what the plan draws after it.
    /// </remarks>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="page">The page whose content is framed.</param>
    private static void WriteContentFrame(PdfWriter writer, EditedPage page)
    {
        if (!page.HasContentFrame)
        {
            return;
        }

        var matrix = page.Transform;
        var prefix = new StringBuilder("q\n");

        if (matrix != DomainMatrix.Identity)
        {
            prefix
                .Append(PdfNumberFormat.Format(matrix.A)).Append(' ')
                .Append(PdfNumberFormat.Format(matrix.B)).Append(' ')
                .Append(PdfNumberFormat.Format(matrix.C)).Append(' ')
                .Append(PdfNumberFormat.Format(matrix.D)).Append(' ')
                .Append(PdfNumberFormat.Format(matrix.E)).Append(' ')
                .Append(PdfNumberFormat.Format(matrix.F)).Append(" cm\n");
        }

        page.PrefixNumber = writer.AllocateObject();
        writer.WriteObject(
            page.PrefixNumber,
            new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(prefix.ToString())));

        page.SuffixNumber = writer.AllocateObject();
        writer.WriteObject(
            page.SuffixNumber,
            new PdfStream(new PdfDictionary(), "Q\n"u8.ToArray()));
    }

    /// <summary>Builds the content array of a framed page: the prefix, the original streams, the suffix.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="page">The framed page.</param>
    /// <param name="contents">The original <c>/Contents</c> value.</param>
    /// <returns>The content array.</returns>
    private static PdfArray FramedContents(
        PdfFileReader reader,
        PdfObjectCopier copier,
        PdfWriter writer,
        EditedPage page,
        PdfObject? contents)
    {
        var framed = new PdfArray();
        framed.Add(new PdfReference(page.PrefixNumber, 0));
        AddContentItems(reader, copier, writer, contents, framed);
        framed.Add(new PdfReference(page.SuffixNumber, 0));
        return framed;
    }

    /// <summary>Flattens the original content streams into a content array.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="value">The value to add, which may be a stream, a reference or an array.</param>
    /// <param name="destination">The content array that receives the streams.</param>
    private static void AddContentItems(
        PdfFileReader reader,
        PdfObjectCopier copier,
        PdfWriter writer,
        PdfObject? value,
        PdfArray destination)
    {
        var resolved = reader.Resolve(value);

        if (resolved is PdfArray array)
        {
            foreach (var item in array.Items)
            {
                AddContentItems(reader, copier, writer, item, destination);
            }

            return;
        }

        if (resolved is not PdfStream stream)
        {
            return;
        }

        if (value is PdfReference reference)
        {
            destination.Add(copier.Clone(reference));
            return;
        }

        // A stream that was stored directly in the page dictionary still has to become an indirect
        // object of its own, because a stream may not be written as a direct object.
        var number = writer.AllocateObject();
        writer.WriteObject(number, copier.Clone(stream));
        destination.Add(new PdfReference(number, 0));
    }

    /// <summary>One page of the edited document while its layout is being resolved.</summary>
    private sealed class EditedPage(PdfFileReader reader, SourcePage source, PdfRectangle mediaBox)
    {
        /// <summary>Gets the document the page comes from.</summary>
        public PdfFileReader Reader { get; } = reader;

        public SourcePage Source { get; } = source;

        public PageRotation AddedRotation { get; set; } = PageRotation.None;

        public PdfRectangle MediaBox { get; set; } = mediaBox;

        public PdfRectangle? CropBox { get; set; }

        public DomainMatrix Transform { get; set; } = DomainMatrix.Identity;

        public List<OverlayLayer> Overlays { get; } = [];

        /// <summary>Gets the annotations the plan added, replaced or removed on this page.</summary>
        public List<AnnotationChange> AnnotationChanges { get; } = [];

        /// <summary>Gets or sets the annotations the page carries once its changes are applied.</summary>
        public List<AnnotationSlot>? AnnotationSlots { get; set; }

        /// <summary>
        /// Gets a value indicating whether the original content of the page has to be framed by a
        /// graphics state of its own.
        /// </summary>
        /// <remarks>
        /// A resized page needs the frame to be placed; a page that draws a layer needs it because the
        /// streams of one page reach a reader as a single stream, so a transform the page states outside
        /// a <c>q</c> would otherwise be still in force where the layer is drawn.
        /// </remarks>
        public bool HasContentFrame => Transform != DomainMatrix.Identity || HasLayer;

        /// <summary>
        /// Gets or sets the object number of the stream that draws the flattened fields of the page,
        /// or zero when no field of this page was flattened.
        /// </summary>
        public int FlattenStreamNumber { get; set; }

        /// <summary>Gets the appearances a flattened page draws, by the name its stream selects them by.</summary>
        public List<(string Name, int Number)> FlattenAppearances { get; } = [];

        /// <summary>Gets a value indicating whether the page draws anything on top of its own content.</summary>
        public bool HasLayer => Overlays.Count > 0 || FlattenStreamNumber != 0;

        public int PrefixNumber { get; set; }

        public int SuffixNumber { get; set; }

        public int TargetNumber { get; set; }

        public PageRotation EffectiveRotation =>
            PageRotationExtensions.Normalize(ReadRotation(Source).Degrees + AddedRotation.Degrees);

        public EditedPage Copy()
        {
            var copy = new EditedPage(Reader, Source, MediaBox)
            {
                AddedRotation = AddedRotation,
                CropBox = CropBox,
                Transform = Transform,
            };

            // A duplicate draws the same layers as the page it copies, so the layers themselves are
            // shared and the form of each is written once for both pages.
            copy.Overlays.AddRange(Overlays);

            // The annotations a page carried when it was copied travel with it the same way: a later
            // addition to the original page is not part of the copy the plan already made.
            copy.AnnotationChanges.AddRange(AnnotationChanges);
            return copy;
        }
    }
}
