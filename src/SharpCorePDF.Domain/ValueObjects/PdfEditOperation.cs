using SharpCorePDF.Domain.Ports;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>Identifies the kind of a <see cref="PdfEditOperation"/>.</summary>
public enum PdfEditOperationKind
{
    /// <summary>The rotation of a page is changed.</summary>
    RotatePage,

    /// <summary>A page is removed.</summary>
    DeletePage,

    /// <summary>A page is moved to another position.</summary>
    MovePage,

    /// <summary>A page is copied to another position.</summary>
    DuplicatePage,

    /// <summary>The document information is changed.</summary>
    SetMetadata,

    /// <summary>The visible area of a page is narrowed.</summary>
    CropPage,

    /// <summary>The page size is changed and the content follows it.</summary>
    ResizePage,

    /// <summary>A page of another document is copied into this one.</summary>
    ImportPage,

    /// <summary>A generated page is inserted.</summary>
    InsertPage,

    /// <summary>Pages of another document are appended.</summary>
    AppendPages,

    /// <summary>The pages are put in a different order.</summary>
    ReorderPages,

    /// <summary>The document is written as several documents.</summary>
    SplitDocument,

    /// <summary>Only a range of the pages is kept.</summary>
    ExtractPages,

    /// <summary>A page of another document is recorded over a page of this one.</summary>
    OverlayPage,

    /// <summary>A page of another document is recorded under a page of this one.</summary>
    UnderlayPage,

    /// <summary>The XMP metadata packet is replaced.</summary>
    SetXmp,

    /// <summary>The bookmarks are replaced.</summary>
    SetOutline,

    /// <summary>The page labels are replaced.</summary>
    SetPageLabels,

    /// <summary>What a viewer shows when it opens the document is replaced.</summary>
    SetViewerPreferences,

    /// <summary>An annotation is written on a page.</summary>
    AddAnnotation,

    /// <summary>An annotation on a page is replaced.</summary>
    UpdateAnnotation,

    /// <summary>An annotation is removed from a page.</summary>
    RemoveAnnotation,

    /// <summary>Form fields are written into the document's form.</summary>
    SetFormField,

    /// <summary>Every field of the document's form is put back to its default value.</summary>
    ResetForm,

    /// <summary>The form is drawn into the pages and removed, so the document keeps no form.</summary>
    FlattenForm,
}

/// <summary>Determines whether a page of another document is recorded under or over a page of this one.</summary>
public enum PdfOverlayLayer
{
    /// <summary>The page is drawn first, so the content of the page it is applied to covers it.</summary>
    Under = 0,

    /// <summary>The page is drawn last, so it covers the content of the page it is applied to.</summary>
    Over = 1,
}

/// <summary>
/// One edit of a <see cref="Aggregates.PdfEditPlan"/>.
/// </summary>
/// <remarks>
/// Operations are values: they name what to change, never the document they are applied to. Page
/// numbers are one-based and are resolved against the document state produced by every preceding
/// operation of the plan.
/// </remarks>
public abstract record PdfEditOperation // NOSONAR S1694: a closed hierarchy of edit values, not a capability
{
    /// <summary>Gets the kind of this operation.</summary>
    public abstract PdfEditOperationKind Kind { get; }
}

/// <summary>Adds to the rotation of one page, modulo a full turn.</summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="Rotation">The clockwise rotation to add.</param>
public sealed record PdfRotatePageOperation(int PageNumber, PageRotation Rotation) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.RotatePage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => $"rotate page {PageNumber} by {Rotation.Degrees} degrees";
}

/// <summary>Removes one page.</summary>
/// <param name="PageNumber">The one-based page number.</param>
public sealed record PdfDeletePageOperation(int PageNumber) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.DeletePage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => $"delete page {PageNumber}";
}

/// <summary>Moves one page to another position.</summary>
/// <param name="From">The one-based page number to move.</param>
/// <param name="To">The one-based position the page ends up at.</param>
public sealed record PdfMovePageOperation(int From, int To) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.MovePage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => $"move page {From} to position {To}";
}

/// <summary>Copies one page to another position.</summary>
/// <param name="PageNumber">The one-based page number to copy.</param>
/// <param name="InsertAt">The one-based position the copy is inserted before.</param>
public sealed record PdfDuplicatePageOperation(int PageNumber, int InsertAt) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.DuplicatePage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => $"duplicate page {PageNumber} at position {InsertAt}";
}

/// <summary>Sets the visible area of one page.</summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="CropBox">The requested crop box, in page space.</param>
public sealed record PdfCropPageOperation(int PageNumber, PdfRectangle CropBox) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.CropPage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"crop page {PageNumber} to [{CropBox.Left:0.##} {CropBox.Bottom:0.##} {CropBox.Right:0.##} {CropBox.Top:0.##}]";
}

/// <summary>Changes the page size of one page and makes the content follow it.</summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="Size">The new page size.</param>
/// <param name="Mode">How the content follows the new size.</param>
public sealed record PdfResizePageOperation(
    int PageNumber,
    PdfPageSize Size,
    PdfPageResizeMode Mode) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.ResizePage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"resize page {PageNumber} to {Size} using {Mode}";
}

/// <summary>Overrides part or all of the document information.</summary>
/// <param name="Metadata">The values to apply; fields left unset keep their source value.</param>
public sealed record PdfSetMetadataOperation(PdfMetadata Metadata) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.SetMetadata;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => "set document metadata";
}

/// <summary>Copies one page of another document into this one.</summary>
/// <param name="Source">The document to copy the page from; it is never modified.</param>
/// <param name="SourcePageNumber">The one-based page number in <paramref name="Source"/>.</param>
/// <param name="InsertAt">The one-based position the copied page is inserted before; the current page count plus one appends it.</param>
/// <param name="Password">The user or owner password of <paramref name="Source"/>, when it is encrypted.</param>
public sealed record PdfImportPageOperation(
    IPdfContentSource Source,
    int SourcePageNumber,
    int InsertAt,
    string? Password = null) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.ImportPage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"import page {SourcePageNumber} of '{Source?.DisplayName}' at position {InsertAt}";
}

/// <summary>Inserts a generated page, typically one an extension drew into a one-page document.</summary>
/// <param name="InsertAt">The one-based position the page is inserted before.</param>
/// <param name="Size">The page size the generated page was drawn at.</param>
/// <param name="Page">A document that holds exactly one page: the one to insert.</param>
public sealed record PdfInsertPageOperation(
    int InsertAt,
    PdfPageSize Size,
    IPdfContentSource Page) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.InsertPage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"insert a {Size.WidthPoints:0.##}x{Size.HeightPoints:0.##} point page at position {InsertAt}";
}

/// <summary>Appends pages of another document.</summary>
/// <param name="Source">The document to copy pages from; it is never modified.</param>
/// <param name="Pages">The pages to append, in ascending order.</param>
/// <param name="Password">The user or owner password of <paramref name="Source"/>, when it is encrypted.</param>
public sealed record PdfAppendOperation(
    IPdfContentSource Source,
    PageRange Pages,
    string? Password = null) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.AppendPages;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"append pages '{Pages}' of '{Source?.DisplayName}'";
}

/// <summary>Puts every page of the document in an explicit order.</summary>
/// <param name="Order">
/// The one-based page numbers in their new order; every page must appear exactly once, so the
/// collection is a permutation of <c>1..n</c> at the point the operation is applied.
/// </param>
public sealed record PdfReorderPagesOperation(IReadOnlyList<int> Order) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.ReorderPages;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"reorder pages to {string.Join(", ", Order ?? [])}";
}

/// <summary>Writes the document as several documents, each holding a run of pages.</summary>
/// <param name="PagesPerDocument">The number of pages each part receives, except the last part, which receives the remainder.</param>
/// <remarks>
/// A split produces more than one file, so a plan that contains it is applied with
/// <see cref="Ports.IPdfDocumentEditor.SplitAsync"/> rather than with
/// <see cref="Ports.IPdfDocumentEditor.ApplyAsync"/>. The split runs after every other operation, so
/// the parts are cut from the edited document, in plan order.
/// </remarks>
public sealed record PdfSplitDocumentOperation(int PagesPerDocument) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.SplitDocument;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => $"split into parts of {PagesPerDocument} page(s)";
}

/// <summary>Keeps only a range of pages and removes the rest.</summary>
/// <param name="Pages">The pages to keep, in their existing order.</param>
public sealed record PdfExtractPagesOperation(PageRange Pages) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.ExtractPages;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => $"extract pages '{Pages}'";
}

/// <summary>Records one page of another document over or under one page of this document.</summary>
/// <param name="Overlay">A document that holds exactly one page: the one to record.</param>
/// <param name="PageNumber">The one-based number of the page the overlay is applied to.</param>
/// <param name="Options">How the overlay page is placed and composited.</param>
/// <param name="Layer">Whether the overlay goes under or over the page content.</param>
/// <param name="Password">The user or owner password of <paramref name="Overlay"/>, when it is encrypted.</param>
/// <remarks>
/// The overlay is recorded in a form XObject, so the content stream of the page it is applied to keeps
/// its bytes and the resources of the overlay never collide with the resources of the page.
/// </remarks>
public sealed record PdfOverlayOperation(
    IPdfContentSource Overlay,
    int PageNumber,
    PdfOverlayOptions Options,
    PdfOverlayLayer Layer,
    string? Password = null) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind =>
        Layer == PdfOverlayLayer.Under ? PdfEditOperationKind.UnderlayPage : PdfEditOperationKind.OverlayPage;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"record '{Overlay?.DisplayName}' {(Layer == PdfOverlayLayer.Under ? "under" : "over")} page {PageNumber} at opacity {Options?.Opacity ?? 1.0:0.##}");
}

/// <summary>Replaces the XMP metadata packet of the document.</summary>
/// <param name="Xmp">The packet, as UTF-8 XML; it must be well formed.</param>
/// <remarks>
/// The packet replaces the one the source carried rather than adding to it, because a document has
/// exactly one XMP packet at <c>/Metadata</c>. Setting a packet on a document that is written for a
/// PDF/A profile is refused by the engine: the profile writes its own packet, and two packets would
/// describe the same document in two ways.
/// </remarks>
public sealed record PdfSetXmpOperation(ReadOnlyMemory<byte> Xmp) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.SetXmp;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"set the XMP packet ({Xmp.Length} bytes)");
}

/// <summary>Replaces the bookmarks of the document.</summary>
/// <param name="Outline">The bookmarks to write; page numbers are the ones the edit leaves behind.</param>
public sealed record PdfSetOutlineOperation(PdfOutline Outline) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.SetOutline;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"set the outline ({Outline?.Items.Count ?? 0} bookmark(s))";
}

/// <summary>Replaces the page labels of the document.</summary>
/// <param name="Labels">The labels to write, or <see cref="PdfPageLabeling.None"/> to write none.</param>
public sealed record PdfSetPageLabelsOperation(PdfPageLabeling Labels) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.SetPageLabels;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() =>
        $"set page labels ({Labels?.Ranges.Count ?? 0} range(s))";
}

/// <summary>Replaces what a viewer shows when it opens the document.</summary>
/// <param name="Preferences">The window, panel, binding and printing preferences to write.</param>
public sealed record PdfSetViewerPreferencesOperation(PdfViewerPreferences Preferences) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.SetViewerPreferences;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"set viewer preferences ({Preferences?.PageLayout}, {Preferences?.PageMode})");
}

/// <summary>Writes one annotation on a page.</summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="Annotation">The annotation to write, in page space.</param>
/// <remarks>
/// The page's <c>/Annots</c> array keeps every annotation it already had: the new one is appended to it,
/// and the content of the page is never rewritten. An annotation that states no identifier is named
/// <c>scpdf-N</c>, so a later operation of the same plan can update or remove it.
/// </remarks>
public sealed record PdfAddAnnotationOperation(int PageNumber, PdfAnnotation Annotation) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.AddAnnotation;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"add {Annotation?.Kind} annotation '{Annotation?.Name ?? "(unnamed)"}' to page {PageNumber}");
}

/// <summary>Replaces one annotation of a page with another.</summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="AnnotationId">The identifier the annotation was written with, its <c>/NM</c> entry.</param>
/// <param name="Annotation">The annotation to write in its place.</param>
/// <remarks>
/// The replacement keeps the rectangle of the annotation it replaces unless the new annotation states
/// one, and it is written as a new object: the annotation the source held is not modified, so a page
/// that shared it with another page keeps the old one there.
/// </remarks>
public sealed record PdfUpdateAnnotationOperation(
    int PageNumber,
    string AnnotationId,
    PdfAnnotation Annotation) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.UpdateAnnotation;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"update {Annotation?.Kind} annotation '{AnnotationId}' on page {PageNumber}");
}

/// <summary>Removes one annotation from a page.</summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="AnnotationId">The identifier the annotation was written with, its <c>/NM</c> entry.</param>
/// <remarks>
/// The annotation leaves the page's <c>/Annots</c> array, and an appearance or an action only it
/// referenced is not copied at all, because the objects the result keeps are the ones its pages
/// reach. An annotation without an identifier cannot be named, so it cannot be removed by this
/// operation.
/// </remarks>
public sealed record PdfRemoveAnnotationOperation(int PageNumber, string AnnotationId) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.RemoveAnnotation;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The formatted operation.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"remove annotation '{AnnotationId}' from page {PageNumber}");
}

/// <summary>Writes form fields into the form of the document.</summary>
/// <param name="Form">The fields to write, each with the page its widget is drawn on.</param>
/// <remarks>
/// <para>
/// A field is merged by name: a name the document's form already carries is filled in place, keeping the
/// ancestors and the widgets the document had, and a name it does not carry is created. Nothing else in
/// the form is touched, so a caller can fill ten fields in one operation and leave the rest alone.
/// </para>
/// <para>
/// The form is created when the document has none, and a document that carried an XFA form loses it:
/// the XFA packet is a form of its own, and a viewer that honoured it would show the XFA fields instead
/// of the ones this operation writes. The removal is reported as a warning rather than performed
/// silently.
/// </para>
/// </remarks>
public sealed record PdfSetFormOperation(PdfForm Form) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.SetFormField;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The number of fields and their names.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"set {Form?.Fields.Count ?? 0} form field(s): {string.Join(", ", Form?.Fields.Select(field => field?.Name ?? "(null)") ?? [])}");
}

/// <summary>Puts every field of the form back to the value the document states as its default.</summary>
/// <remarks>
/// The default is the field's <c>/DV</c> entry, which the document wrote when it was designed; a field
/// that states none is emptied. A read-only field is reset as well: the reset restores what the
/// document said, and refusing it would leave a filled field that a caller asked to empty.
/// </remarks>
public sealed record PdfResetFormOperation : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.ResetForm;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>A fixed description of the operation.</returns>
    public override string ToString() => "reset the form to its default values";
}

/// <summary>Draws the form into the pages and removes it, leaving no interactive field behind.</summary>
/// <param name="PageNumber">The one-based page to flatten, or <see langword="null"/> for every page.</param>
/// <remarks>
/// <para>
/// Flattening writes each widget's appearance into the content of the page that carries it, so the
/// value is part of the page and a viewer that has no form support — and a printer — still shows it.
/// The widgets leave the page's annotation array, and the form is removed from the document once its
/// last widget is gone.
/// </para>
/// <para>
/// The appearance is placed by the widget's rectangle, not by its default appearance: what the reader
/// saw before the flatten is what the page shows after it.
/// </para>
/// </remarks>
public sealed record PdfFlattenFormOperation(int? PageNumber = null) : PdfEditOperation
{
    /// <inheritdoc />
    public override PdfEditOperationKind Kind => PdfEditOperationKind.FlattenForm;

    /// <summary>Formats the operation for diagnostics.</summary>
    /// <returns>The page, or the whole form.</returns>
    public override string ToString() =>
        PageNumber is { } page
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"flatten the form on page {page}")
            : "flatten the form";
}
