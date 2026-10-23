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
