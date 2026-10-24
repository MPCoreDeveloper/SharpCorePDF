using System.Collections.Immutable;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Aggregates;

/// <summary>
/// The aggregate root of an edit: an ordered list of operations applied to one source document, plus
/// the options that control the result.
/// </summary>
/// <remarks>
/// <para>
/// A plan is immutable. Every operation returns a new instance, so a plan can be built once, shared,
/// and applied concurrently without locks:
/// </para>
/// <code>
/// var plan = PdfEditPlan.Create()
///     .RotatePage(1, PageRotation.Clockwise90)
///     .DeletePage(4)
///     .MovePage(3, 1);
/// </code>
/// <para>
/// Page numbers are one-based and refer to the document state produced by every preceding operation.
/// The invariants a plan can check on its own are enforced by <see cref="Validate"/> and
/// <see cref="EnsureValid"/>; an operation that reaches beyond the last page is refused when the plan
/// is executed, because only then is the source document read.
/// </para>
/// </remarks>
public sealed record PdfEditPlan
{
    private readonly ImmutableArray<PdfEditOperation> operations;

    private PdfEditPlan(ImmutableArray<PdfEditOperation> operations, PdfEditOutputOptions output)
    {
        this.operations = operations;
        Output = output;
    }

    /// <summary>Gets the operations in the order they are applied.</summary>
    public IReadOnlyList<PdfEditOperation> Operations => operations;

    /// <summary>Gets the options that control the edited document.</summary>
    public PdfEditOutputOptions Output { get; }

    /// <summary>Gets the number of operations in the plan.</summary>
    public int OperationCount => operations.Length;

    /// <summary>Gets a value indicating whether the plan changes nothing.</summary>
    public bool IsEmpty => operations.Length == 0;

    /// <summary>Creates an empty plan.</summary>
    /// <param name="output">The options for the edited document; defaults to <see cref="PdfEditOutputOptions.Default"/>.</param>
    /// <returns>The new plan.</returns>
    public static PdfEditPlan Create(PdfEditOutputOptions? output = null) =>
        new([], output ?? PdfEditOutputOptions.Default);

    /// <summary>Creates an empty plan that writes the result with the supplied options.</summary>
    /// <param name="output">The options for the edited document.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static PdfEditPlan For(PdfEditOutputOptions output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new([], output);
    }

    /// <summary>Adds a rotation to one page.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="rotation">The clockwise rotation to add to the page's existing rotation.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rotation"/> is not a quarter turn.</exception>
    public PdfEditPlan RotatePage(int pageNumber, PageRotation rotation)
    {
        if (!Enum.IsDefined(rotation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotation),
                rotation,
                "A page rotation must be a quarter turn.");
        }

        return Add(new PdfRotatePageOperation(pageNumber, rotation));
    }

    /// <summary>Sets the visible area of one page.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="cropBox">The crop box in page space; it is clamped to the media box when the plan is applied.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cropBox"/> is <see langword="null"/>.</exception>
    public PdfEditPlan CropPage(int pageNumber, PdfRectangle cropBox)
    {
        ArgumentNullException.ThrowIfNull(cropBox);
        return Add(new PdfCropPageOperation(pageNumber, cropBox));
    }

    /// <summary>Changes the page size of one page and makes the content follow it.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="size">The new page size.</param>
    /// <param name="mode">How the content follows the new size; defaults to <see cref="PdfPageResizeMode.Scale"/>.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined resize mode.</exception>
    public PdfEditPlan ResizePage(
        int pageNumber,
        PdfPageSize size,
        PdfPageResizeMode mode = PdfPageResizeMode.Scale)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "A page resize mode must be Scale or Preserve.");
        }

        return Add(new PdfResizePageOperation(pageNumber, size, mode));
    }

    /// <summary>Removes one page.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan DeletePage(int pageNumber) => Add(new PdfDeletePageOperation(pageNumber));

    /// <summary>Moves one page to another position.</summary>
    /// <param name="from">The one-based page number to move.</param>
    /// <param name="to">The one-based position the page ends up at.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan MovePage(int from, int to) => Add(new PdfMovePageOperation(from, to));

    /// <summary>Copies one page to another position.</summary>
    /// <param name="pageNumber">The one-based page number to copy.</param>
    /// <param name="insertAt">The one-based position the copy is inserted before; the current page count plus one appends it.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan DuplicatePage(int pageNumber, int insertAt) =>
        Add(new PdfDuplicatePageOperation(pageNumber, insertAt));

    /// <summary>Copies one page of another document into this one.</summary>
    /// <param name="source">The document to copy the page from; it is never modified.</param>
    /// <param name="sourcePageNumber">The one-based page number in <paramref name="source"/>.</param>
    /// <param name="insertAt">The one-based position the copied page is inserted before; the current page count plus one appends it.</param>
    /// <param name="password">The user or owner password of <paramref name="source"/>, when it is encrypted.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public PdfEditPlan ImportPage(
        IPdfContentSource source,
        int sourcePageNumber,
        int insertAt,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Add(new PdfImportPageOperation(source, sourcePageNumber, insertAt, password));
    }

    /// <summary>Inserts a generated page.</summary>
    /// <param name="insertAt">The one-based position the page is inserted before.</param>
    /// <param name="size">The page size the generated page was drawn at.</param>
    /// <param name="page">A document that holds exactly one page: the one to insert.</param>
    /// <returns>The new plan.</returns>
    /// <remarks>
    /// This overload takes a finished one-page document. Infrastructure offers
    /// <c>InsertPage(int, PdfPageSize, Action&lt;PdfPageCanvas&gt;)</c>, which builds that document from
    /// a drawing callback, which is the shape the editing contract describes.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    public PdfEditPlan InsertPage(int insertAt, PdfPageSize size, IPdfContentSource page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Add(new PdfInsertPageOperation(insertAt, size, page));
    }

    /// <summary>Appends pages of another document.</summary>
    /// <param name="source">The document to copy pages from; it is never modified.</param>
    /// <param name="pages">The pages to append; every page when omitted.</param>
    /// <param name="password">The user or owner password of <paramref name="source"/>, when it is encrypted.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public PdfEditPlan Append(
        IPdfContentSource source,
        PageRange? pages = null,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Add(new PdfAppendOperation(source, pages ?? PageRange.All, password));
    }

    /// <summary>Puts every page in an explicit order.</summary>
    /// <param name="order">The one-based page numbers in their new order: a permutation of the page count at that point.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan ReorderPages(params int[] order) => ReorderPages((IEnumerable<int>)order);

    /// <summary>Puts every page in an explicit order.</summary>
    /// <param name="order">The one-based page numbers in their new order: a permutation of the page count at that point.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is <see langword="null"/>.</exception>
    public PdfEditPlan ReorderPages(IEnumerable<int> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return Add(new PdfReorderPagesOperation([.. order]));
    }

    /// <summary>Keeps only a range of pages and removes the rest.</summary>
    /// <param name="pages">The pages to keep, in their existing order.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan ExtractPages(PageRange pages) => Add(new PdfExtractPagesOperation(pages));

    /// <summary>Writes the document as several documents, each holding a run of pages.</summary>
    /// <param name="pagesPerDocument">The number of pages each part receives; defaults to one page per part.</param>
    /// <returns>The new plan.</returns>
    /// <remarks>
    /// The parts are cut from the document as the plan leaves it, so any other operation runs first.
    /// Apply such a plan with <c>IPdfDocumentEditor.SplitAsync</c>, which writes the parts; a single
    /// destination cannot receive several documents.
    /// </remarks>
    public PdfEditPlan SplitDocument(int pagesPerDocument = 1) =>
        Add(new PdfSplitDocumentOperation(pagesPerDocument));

    /// <summary>Adds an operation that the typed helpers do not cover.</summary>
    /// <param name="operation">The operation to append.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    public PdfEditPlan AddOperation(PdfEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Add(operation);
    }

    /// <summary>Overrides part or all of the document information.</summary>
    /// <param name="metadata">The values to apply; fields left unset keep their source value.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetMetadata(PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Add(new PdfSetMetadataOperation(metadata));
    }

    /// <summary>Records one page of another document over one page of this document.</summary>
    /// <param name="overlay">A document that holds exactly one page: the one to record.</param>
    /// <param name="pageNumber">The one-based number of the page the overlay is applied to.</param>
    /// <param name="options">How the overlay page is placed and composited; defaults to <see cref="PdfOverlayOptions.Default"/>.</param>
    /// <param name="password">The user or owner password of <paramref name="overlay"/>, when it is encrypted.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="overlay"/> is <see langword="null"/>.</exception>
    public PdfEditPlan Overlay(
        IPdfContentSource overlay,
        int pageNumber,
        PdfOverlayOptions? options = null,
        string? password = null) =>
        AddOverlay(overlay, pageNumber, options, PdfOverlayLayer.Over, password);

    /// <summary>Records one page of another document under one page of this document.</summary>
    /// <param name="underlay">A document that holds exactly one page: the one to record.</param>
    /// <param name="pageNumber">The one-based number of the page the underlay is applied to.</param>
    /// <param name="options">How the underlay page is placed and composited; defaults to <see cref="PdfOverlayOptions.Default"/>.</param>
    /// <param name="password">The user or owner password of <paramref name="underlay"/>, when it is encrypted.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="underlay"/> is <see langword="null"/>.</exception>
    public PdfEditPlan Underlay(
        IPdfContentSource underlay,
        int pageNumber,
        PdfOverlayOptions? options = null,
        string? password = null) =>
        AddOverlay(underlay, pageNumber, options, PdfOverlayLayer.Under, password);

    /// <summary>Replaces the XMP metadata packet of the document.</summary>
    /// <param name="xmp">The packet as UTF-8 XML; it must be well formed.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan SetXmp(ReadOnlyMemory<byte> xmp) => Add(new PdfSetXmpOperation(xmp));

    /// <summary>Replaces the bookmarks of the document.</summary>
    /// <param name="outline">The bookmarks to write, numbered in the order the plan leaves the pages in.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="outline"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetOutline(PdfOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        return Add(new PdfSetOutlineOperation(outline));
    }

    /// <summary>Replaces the page labels of the document.</summary>
    /// <param name="labels">The labels to write, or <see cref="PdfPageLabeling.None"/> to write none.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="labels"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetPageLabels(PdfPageLabeling labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return Add(new PdfSetPageLabelsOperation(labels));
    }

    /// <summary>Replaces what a viewer shows when it opens the document.</summary>
    /// <param name="preferences">The window, panel, binding and printing preferences to write.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="preferences"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetViewerPreferences(PdfViewerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return Add(new PdfSetViewerPreferencesOperation(preferences));
    }

    /// <summary>Writes one annotation on a page.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="annotation">The annotation to write; use <see cref="PdfAnnotations"/> to create one.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="annotation"/> is <see langword="null"/>.</exception>
    public PdfEditPlan AddAnnotation(int pageNumber, PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return Add(new PdfAddAnnotationOperation(pageNumber, annotation));
    }

    /// <summary>Replaces one annotation of a page with another.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="annotationId">The identifier the annotation was written with.</param>
    /// <param name="annotation">The annotation to write in its place.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="annotation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="annotationId"/> is blank.</exception>
    public PdfEditPlan UpdateAnnotation(int pageNumber, string annotationId, PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationId);
        return Add(new PdfUpdateAnnotationOperation(pageNumber, annotationId, annotation));
    }

    /// <summary>Removes one annotation from a page.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="annotationId">The identifier the annotation was written with.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentException"><paramref name="annotationId"/> is blank.</exception>
    public PdfEditPlan RemoveAnnotation(int pageNumber, string annotationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationId);
        return Add(new PdfRemoveAnnotationOperation(pageNumber, annotationId));
    }

    /// <summary>Writes form fields into the form of the document.</summary>
    /// <param name="form">The fields to write, each with the page its widget is drawn on.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="form"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetForm(PdfForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Add(new PdfSetFormOperation(form));
    }

    /// <summary>Writes one field into the form of the document.</summary>
    /// <param name="field">The field to write; the page it states is the page its widget is drawn on.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetFormField(PdfFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Add(new PdfSetFormOperation(PdfForm.Create(field)));
    }

    /// <summary>Writes one field on a page the call states.</summary>
    /// <param name="pageNumber">The one-based number of the page the widget is drawn on.</param>
    /// <param name="field">The field to write; the page of the call wins over the one the field states.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is <see langword="null"/>.</exception>
    public PdfEditPlan SetFormField(int pageNumber, PdfFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return SetFormField(field with { PageNumber = pageNumber });
    }

    /// <summary>Puts every field of the form back to the value the document states as its default.</summary>
    /// <returns>The new plan.</returns>
    public PdfEditPlan ResetForm() => Add(new PdfResetFormOperation());

    /// <summary>Draws every field of the form into its page and removes the form.</summary>
    /// <returns>The new plan.</returns>
    public PdfEditPlan FlattenForm() => Add(new PdfFlattenFormOperation());

    /// <summary>Draws the fields of one page into it and removes them from the form.</summary>
    /// <param name="pageNumber">The one-based number of the page to flatten.</param>
    /// <returns>The new plan.</returns>
    public PdfEditPlan FlattenForm(int pageNumber) => Add(new PdfFlattenFormOperation(pageNumber));

    /// <summary>Replaces the options of the plan.</summary>
    /// <param name="output">The new options.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public PdfEditPlan WithOutput(PdfEditOutputOptions output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new(operations, output);
    }

    /// <summary>Adjusts the options of the plan.</summary>
    /// <param name="mutate">A function that produces the new options, typically a <c>with</c> expression.</param>
    /// <returns>The new plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is <see langword="null"/>.</exception>
    public PdfEditPlan WithOutput(Func<PdfEditOutputOptions, PdfEditOutputOptions> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        return WithOutput(mutate(Output));
    }

    /// <summary>Checks every invariant that can be verified without reading the source document.</summary>
    /// <returns>Every problem found, or an empty list when the plan is executable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        errors.AddRange(Output.Validate());

        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];

            if (operation is null)
            {
                errors.Add($"operation {index} is null.");
                continue;
            }

            errors.AddRange(ValidateOperation(operation, index));
        }

        // A PDF/A document carries exactly one XMP packet, and the one the profile writes is the one
        // that declares conformance. A caller's packet would either replace that declaration or be
        // replaced by it, so the combination is refused instead of resolved silently.
        if (Output.PdfAProfile is { } profile && operations.Any(operation => operation is PdfSetXmpOperation))
        {
            errors.Add(
                $"the XMP packet cannot be set on a document written for {profile}: the profile writes its own packet.");
        }

        // PDF/A-1b is built on a version of PDF that has no transparency, so an overlay that asks for
        // one would state a graphics state the profile forbids. It is refused rather than written
        // opaque, because a caller who asked for a translucent stamp would not get one.
        if (Output.PdfAProfile == PdfAProfile.PdfA1B)
        {
            for (var index = 0; index < operations.Length; index++)
            {
                if (operations[index] is PdfOverlayOperation overlay
                    && (overlay.Options.Opacity < 1d || overlay.Options.BlendMode != PdfOverlayBlendMode.Normal))
                {
                    errors.Add(
                        $"operation {index}: a layer with an opacity below 1 or a blend mode other than Normal cannot be written for PDF/A-1b, which has no transparency.");
                }

                if (operations[index] is PdfAddAnnotationOperation { Annotation.Opacity: < 1d }
                    || operations[index] is PdfUpdateAnnotationOperation { Annotation.Opacity: < 1d })
                {
                    errors.Add(
                        $"operation {index}: an annotation with an opacity below 1 cannot be written for PDF/A-1b, which has no transparency.");
                }
            }
        }

        // An identifier names one annotation of the result, so two annotations cannot share one: a
        // later update or removal would have to choose between them, and the document would report two
        // annotations under the same name.
        var names = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < operations.Length; index++)
        {
            if (operations[index] is not PdfAddAnnotationOperation { Annotation.Name: { Length: > 0 } name })
            {
                continue;
            }

            if (names.TryGetValue(name, out var first))
            {
                errors.Add(
                    $"operation {index}: the annotation name '{name}' is already used by operation {first}.");
            }
            else
            {
                names[name] = index;
            }
        }

        // A field is addressed by its name, so two operations cannot write the same name: the second
        // would replace what the first wrote, and a caller who asked for two values would get one.
        var fields = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < operations.Length; index++)
        {
            if (operations[index] is not PdfSetFormOperation { Form.Fields: { } formFields })
            {
                continue;
            }

            foreach (var field in formFields)
            {
                if (field is null || string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                if (fields.TryGetValue(field.Name, out var first))
                {
                    errors.Add(
                        $"operation {index}: the form field '{field.Name}' is already set by operation {first}.");
                }
                else
                {
                    fields[field.Name] = index;
                }
            }
        }

        return errors;
    }

    /// <summary>Throws when the plan violates an invariant.</summary>
    /// <exception cref="PdfEditPlanValidationException">The plan is not executable.</exception>
    public void EnsureValid()
    {
        var errors = Validate();

        if (errors.Count > 0)
        {
            throw new PdfEditPlanValidationException(errors);
        }
    }

    /// <summary>Formats the plan for diagnostics.</summary>
    /// <returns>The formatted plan.</returns>
    public override string ToString() =>
        operations.Length == 0
            ? "edit plan with no operations"
            : $"edit plan: {string.Join(", ", operations.Select(operation => operation.ToString()))}";

    private PdfEditPlan Add(PdfEditOperation operation) =>
        new(operations.Add(operation), Output);

    private PdfEditPlan AddOverlay(
        IPdfContentSource source,
        int pageNumber,
        PdfOverlayOptions? options,
        PdfOverlayLayer layer,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Add(new PdfOverlayOperation(source, pageNumber, options ?? PdfOverlayOptions.Default, layer, password));
    }

    private static IEnumerable<string> ValidateOperation(PdfEditOperation operation, int index)
    {
        switch (operation)
        {
            case PdfRotatePageOperation rotate:
                if (rotate.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {rotate.PageNumber} is not one-based.";
                }

                if (!Enum.IsDefined(rotate.Rotation))
                {
                    yield return $"operation {index}: rotation '{(int)rotate.Rotation}' is not a quarter turn.";
                }

                break;

            case PdfDeletePageOperation delete when delete.PageNumber < 1:
                yield return $"operation {index}: page number {delete.PageNumber} is not one-based.";
                break;

            case PdfSetFormOperation setForm:
                if (setForm.Form is null)
                {
                    yield return $"operation {index}: the form to write is null.";
                    break;
                }

                foreach (var problem in setForm.Form.Validate())
                {
                    yield return $"operation {index}: {problem}";
                }

                break;

            case PdfFlattenFormOperation flatten when flatten.PageNumber is < 1:
                yield return $"operation {index}: page number {flatten.PageNumber} is not one-based.";
                break;

            case PdfMovePageOperation move:
                if (move.From < 1)
                {
                    yield return $"operation {index}: page number {move.From} is not one-based.";
                }

                if (move.To < 1)
                {
                    yield return $"operation {index}: move position {move.To} is not one-based.";
                }

                break;

            case PdfDuplicatePageOperation duplicate:
                if (duplicate.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {duplicate.PageNumber} is not one-based.";
                }

                if (duplicate.InsertAt < 1)
                {
                    yield return $"operation {index}: insert position {duplicate.InsertAt} is not one-based.";
                }

                break;

            case PdfCropPageOperation crop:
                if (crop.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {crop.PageNumber} is not one-based.";
                }

                if (crop.CropBox is null)
                {
                    yield return $"operation {index}: the crop box is null.";
                }
                else if (crop.CropBox.IsEmpty)
                {
                    yield return $"operation {index}: the crop box encloses no area.";
                }

                break;

            case PdfResizePageOperation resize:
                if (resize.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {resize.PageNumber} is not one-based.";
                }

                if (!Enum.IsDefined(resize.Mode))
                {
                    yield return $"operation {index}: resize mode '{(int)resize.Mode}' is not defined.";
                }

                break;

            case PdfImportPageOperation import:
                if (import.Source is null)
                {
                    yield return $"operation {index}: the document to import from is null.";
                }

                if (import.SourcePageNumber < 1)
                {
                    yield return $"operation {index}: source page number {import.SourcePageNumber} is not one-based.";
                }

                if (import.InsertAt < 1)
                {
                    yield return $"operation {index}: insert position {import.InsertAt} is not one-based.";
                }

                break;

            case PdfInsertPageOperation insert:
                if (insert.Page is null)
                {
                    yield return $"operation {index}: the page to insert is null.";
                }

                if (insert.InsertAt < 1)
                {
                    yield return $"operation {index}: insert position {insert.InsertAt} is not one-based.";
                }

                break;

            case PdfAppendOperation append:
                if (append.Source is null)
                {
                    yield return $"operation {index}: the document to append is null.";
                }

                foreach (var problem in ValidateRange(append.Pages, index))
                {
                    yield return problem;
                }

                break;

            case PdfReorderPagesOperation reorder:
                if (reorder.Order is not { Count: > 0 })
                {
                    yield return $"operation {index}: the new page order is empty.";
                    break;
                }

                var seen = new HashSet<int>();

                for (var position = 0; position < reorder.Order.Count; position++)
                {
                    var pageNumber = reorder.Order[position];

                    if (pageNumber < 1)
                    {
                        yield return $"operation {index}: page number {pageNumber} is not one-based.";
                    }
                    else if (!seen.Add(pageNumber))
                    {
                        yield return $"operation {index}: page {pageNumber} appears twice in the new order.";
                    }
                }

                break;

            case PdfExtractPagesOperation extract:
                foreach (var problem in ValidateRange(extract.Pages, index))
                {
                    yield return problem;
                }

                break;

            case PdfSplitDocumentOperation split when split.PagesPerDocument < 1:
                yield return $"operation {index}: a split must give each part at least one page, not {split.PagesPerDocument}.";
                break;

            case PdfSetMetadataOperation metadata when metadata.Metadata is null:
                yield return $"operation {index}: the metadata to set is null.";
                break;

            case PdfOverlayOperation overlay:
                if (overlay.Overlay is null)
                {
                    yield return $"operation {index}: the document to overlay is null.";
                }

                if (overlay.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {overlay.PageNumber} is not one-based.";
                }

                if (!Enum.IsDefined(overlay.Layer))
                {
                    yield return $"operation {index}: overlay layer '{(int)overlay.Layer}' is not one the engine writes.";
                }

                if (overlay.Options is not { } overlayOptions)
                {
                    yield return $"operation {index}: the overlay options are null.";
                }
                else
                {
                    foreach (var problem in overlayOptions.Validate())
                    {
                        yield return $"operation {index}: {problem}";
                    }
                }

                break;

            case PdfSetXmpOperation xmp:
                if (xmp.Xmp.IsEmpty)
                {
                    yield return $"operation {index}: the XMP packet is empty.";
                }
                else if (!IsWellFormedXml(xmp.Xmp))
                {
                    yield return $"operation {index}: the XMP packet is not well-formed XML.";
                }

                break;

            case PdfSetOutlineOperation outline:
                if (outline.Outline is not { } tree)
                {
                    yield return $"operation {index}: the outline to set is null.";
                }
                else
                {
                    foreach (var problem in tree.Validate())
                    {
                        yield return $"operation {index}: {problem}";
                    }
                }

                break;

            case PdfSetPageLabelsOperation labels:
                if (labels.Labels is not { } labeling)
                {
                    yield return $"operation {index}: the page labels to set are null.";
                }
                else
                {
                    foreach (var problem in labeling.Validate())
                    {
                        yield return $"operation {index}: {problem}";
                    }
                }

                break;

            case PdfSetViewerPreferencesOperation preferences:
                if (preferences.Preferences is not { } viewer)
                {
                    yield return $"operation {index}: the viewer preferences to set are null.";
                }
                else
                {
                    foreach (var problem in viewer.Validate())
                    {
                        yield return $"operation {index}: {problem}";
                    }
                }

                break;

            case PdfAddAnnotationOperation add:
                if (add.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {add.PageNumber} is not one-based.";
                }

                if (add.Annotation is not { } added)
                {
                    yield return $"operation {index}: the annotation to add is null.";
                    break;
                }

                foreach (var problem in added.Validate())
                {
                    yield return $"operation {index}: {problem}";
                }

                break;

            case PdfUpdateAnnotationOperation update:
                if (update.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {update.PageNumber} is not one-based.";
                }

                if (string.IsNullOrWhiteSpace(update.AnnotationId))
                {
                    yield return $"operation {index}: the identifier of the annotation to update is blank.";
                }

                if (update.Annotation is not { } replacement)
                {
                    yield return $"operation {index}: the annotation to write is null.";
                    break;
                }

                foreach (var problem in replacement.Validate())
                {
                    yield return $"operation {index}: {problem}";
                }

                break;

            case PdfRemoveAnnotationOperation remove:
                if (remove.PageNumber < 1)
                {
                    yield return $"operation {index}: page number {remove.PageNumber} is not one-based.";
                }

                if (string.IsNullOrWhiteSpace(remove.AnnotationId))
                {
                    yield return $"operation {index}: the identifier of the annotation to remove is blank.";
                }

                break;
        }
    }

    /// <summary>
    /// Reads an XMP packet to its end to prove it is well-formed XML.
    /// </summary>
    /// <param name="xmp">The packet, as UTF-8 bytes.</param>
    /// <returns><see langword="true"/> when the packet parses; <see langword="false"/> when it does not.</returns>
    /// <remarks>
    /// A packet that does not parse is refused before the document is opened, because it would be
    /// written into the result exactly as handed over and every reader that trusts <c>/Metadata</c>
    /// would report a damaged document. A DTD is prohibited: an XMP packet never needs one, and one
    /// that has it is asking the engine to fetch something.
    /// </remarks>
    private static bool IsWellFormedXml(ReadOnlyMemory<byte> xmp)
    {
        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        try
        {
            using var stream = new MemoryStream(xmp.ToArray(), writable: false);
            using var reader = System.Xml.XmlReader.Create(stream, settings);

            while (reader.Read())
            {
                // Reading to the end is the check: the reader throws at the first malformed byte.
            }

            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ValidateRange(PageRange pages, int index)
    {
        if (pages.From < 1)
        {
            yield return $"operation {index}: page range '{pages}' does not start at a one-based page.";
        }
        else if (!pages.IsOpenEnded && pages.To < pages.From)
        {
            yield return $"operation {index}: page range '{pages}' ends before it starts.";
        }
    }
}
