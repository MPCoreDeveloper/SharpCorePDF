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
