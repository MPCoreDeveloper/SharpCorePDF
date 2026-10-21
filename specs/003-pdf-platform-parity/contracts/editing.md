# Contract: Editing

**Feature**: `003-pdf-platform-parity` | **Type**: public library API

The editor is an immutable plan applied to a source the caller owns. It never writes to the source and
never exposes a mutable object graph. Existing merge plans are unaffected.

## `IPdfDocumentEditor`

```csharp
public interface IPdfDocumentEditor
{
    Task<PdfEditResult> ApplyAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<byte[]> ApplyToArrayAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        string? password = null,
        CancellationToken cancellationToken = default);
}
```

`PdfEditResult` carries `PageCount`, `BytesWritten`, `Elapsed`, `Warnings` and the applied operation
summaries. `ApplyAsync` leaves the destination untouched when validation or opening fails.

## `PdfEditPlan`

```csharp
public sealed record PdfEditPlan
{
    public static PdfEditPlan Create();
    public static PdfEditPlan For(PdfEditOutputOptions output);

    public IReadOnlyList<PdfEditOperation> Operations { get; }
    public PdfEditOutputOptions Output { get; }

    public PdfEditPlan RotatePage(int pageNumber, PageRotation rotation);
    public PdfEditPlan CropPage(int pageNumber, PdfRectangle cropBox);
    public PdfEditPlan ResizePage(int pageNumber, PdfPageSize size,
        PdfPageResizeMode mode = PdfPageResizeMode.Scale);
    public PdfEditPlan DeletePage(int pageNumber);
    public PdfEditPlan DuplicatePage(int pageNumber, int insertAt);
    public PdfEditPlan MovePage(int from, int to);
    public PdfEditPlan InsertPage(int at, PdfPageSize size, Action<PdfPageCanvas> draw);
    public PdfEditPlan ImportPage(IPdfContentSource source, int pageNumber, int at);
    public PdfEditPlan Append(IPdfContentSource source, PageRange? pages = null);

    public PdfEditPlan Overlay(IPdfContentSource overlay, int pageNumber,
        PdfOverlayOptions? options = null);
    public PdfEditPlan Underlay(IPdfContentSource underlay, int pageNumber,
        PdfOverlayOptions? options = null);

    public PdfEditPlan SetMetadata(PdfMetadata metadata);
    public PdfEditPlan SetXmp(ReadOnlyMemory<byte> xmp);
    public PdfEditPlan SetOutline(PdfOutline outline);
    public PdfEditPlan SetPageLabels(PageLabelPolicy policy);
    public PdfEditPlan SetViewerPreferences(PdfViewerPreferences preferences);

    public IReadOnlyList<string> Validate();
}
```

**Guarantees**

- Page numbers are one-based and refer to the document state after all preceding operations.
- `Validate` reports every error it can see without opening the source, including an out-of-range page,
  an unsupported resize mode, a duplicate field name and an encryption/PDF/A conflict.
- Every `With…`/operation method returns a new plan; the old plan is unchanged.
- An imported page is copied, not referenced; the source can be disposed after `ApplyAsync` returns.
- A destination stream that is not writable is refused before any work.

## Page Geometry

- `RotatePage` adds to the existing `/Rotate` modulo 360.
- `CropPage` sets an explicit `/CropBox` inside the `/MediaBox`; an inverted or out-of-bounds box is
  normalised and reported.
- `ResizePage` supports `Scale` (content transformed) and `Preserve` (content placed at its original
  size with an offset); the mode is explicit.
- Inherited `/MediaBox`, `/CropBox`, `/Rotate` and `/Resources` are materialised on every page the plan
  keeps, so a partial selection or reorder does not depend on a removed parent.

## Overlay and Underlay

```csharp
public sealed record PdfOverlayOptions(
    PdfMatrix Transform,
    double Opacity = 1,
    bool RepeatOnEveryPage = false,
    PdfRectangle? Clip = null);
```

- Overlay writes the new content after the existing content; Underlay writes before it.
- The existing content is wrapped in `q`/`Q`; it is not rewritten in place.
- A watermark is an overlay with a caller-supplied page builder; a stamp is an overlay with a fixed
  appearance. Both are content, not annotations.
- Opacity is applied through an ExtGState; unsupported blend modes are refused.

## Output and Incremental Updates

```csharp
public sealed record PdfEditOutputOptions
{
    public PdfVersion TargetVersion { get; init; } = PdfVersion.V1_7;
    public PdfMetadata? Metadata { get; init; }
    public MetadataPolicy MetadataPolicy { get; init; } = MetadataPolicy.FromPrimarySource;
    public OutlinePolicy OutlinePolicy { get; init; } = OutlinePolicy.Preserve;
    public PageLabelPolicy PageLabelPolicy { get; init; } = PageLabelPolicy.Drop;
    public PdfEncryptionOptions? Encryption { get; init; }
    public PdfAProfile? PdfAProfile { get; init; }
    public DateTimeOffset? ModificationDate { get; init; }
    public string? DocumentId { get; init; }
    public bool UseIncrementalUpdate { get; init; }
    public IReadOnlyList<string> Validate();
}
```

- `UseIncrementalUpdate = true` requires the source to be a seekable, unchanged byte sequence; the
  editor appends a revision and does not rewrite the prior bytes.
- A signature that covers an earlier revision remains valid after a later incremental update; the
  result reports the prior signature and the new revision.
- `UseIncrementalUpdate = false` performs a complete rewrite and warns that prior signatures are
  invalid.
- `PdfAProfile` and `Encryption` cannot both be set; `PdfAProfile` also forbids JavaScript actions and
  requires every font to be embedded.
- A fixed `ModificationDate` and `DocumentId` make repeated edits byte-identical.

## Document-Level Edits

- Metadata: title, author, subject, keywords, creator, producer, creation/modification dates and custom
  entries; null means “leave unchanged”, empty means “remove”.
- XMP: the caller supplies a complete packet; the editor validates well-formed XML and updates the
  catalog’s `/Metadata`. A packet that describes a different document is the caller’s responsibility;
  the writer does not rewrite arbitrary XMP properties.
- Outlines: add, update, remove and reorder items; a destination to a deleted page is removed and
  reported, never left dangling.
- Page labels: drop, preserve, or a caller-supplied label range; labels are recomputed after reorder.
- Viewer preferences: hide toolbar/menubar, page layout, page mode, direction and print scaling.

## Guarantees and Diagnostics

- The source file is never modified; no temporary file is created beside it.
- A reference to a deleted page is removed, redirected or reported, never left broken.
- A page’s `/Annots` array is rewritten only when its annotations change; untouched pages keep their
  objects.
- An overlay/underlay does not change the bytes of the original content stream.
- A field, annotation, outline or destination that cannot be preserved is reported through
  `PdfWarning` with a stable `Kind`; the operation does not silently drop it.
- A failure after the destination has been partially written is reported and does not claim success.

## Out of Contract

- In-place editing of an existing content stream’s operators.
- Replacing text, removing an image from existing content, or changing a font already used by a page.
- Page reflow, content re-layout or automatic pagination.
- Redaction that removes content from the underlying stream; a redaction annotation is a visual mark,
  not a guarantee of data removal, and is not offered as redaction in 1.1.
- Repairing every malformed document; the reader’s existing recovery and warnings still apply.

