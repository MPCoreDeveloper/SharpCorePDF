# 14. Editing an Existing Document

A merge builds one document from several. An edit changes one source document and writes the result as
a new document. The source is only read: an edit never modifies it, needs no write access to it and
leaves no temporary file beside it.

Editing here means changing the **assembly** of the document — which pages it has, in what order, with
what rotation — and its document information. It does not mean changing what a page draws; Chapter 2
states that boundary.

## One plan, applied in order

```csharp
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

var plan = PdfEditPlan.Create()
    .RotatePage(1, PageRotation.Clockwise90)
    .DeletePage(4)
    .MovePage(3, 1)
    .DuplicatePage(2, 4)
    .SetMetadata(new PdfMetadata { Title = "Revised statement" });

await using var output = File.Create("statement-edited.pdf");
var result = await editor.ApplyAsync(
    new PdfFileSource("statement.pdf"), plan, output);
```

`editor` is an `IPdfDocumentEditor`, registered by `AddSharpCorePDF()` and constructible by hand as
`new PdfEditEngine()` (Chapter 10). The plan above is read against a four page source like this:

| Operation | Pages after it |
| --- | --- |
| source | `1 2 3 4` |
| `RotatePage(1, Clockwise90)` | `1 2 3 4`, page 1 rotated |
| `DeletePage(4)` | `1 2 3` |
| `MovePage(3, 1)` | `3 1 2` |
| `DuplicatePage(2, 4)` | `3 1 2 1` — a copy of page 2 appended |
| `SetMetadata(...)` | `3 1 2 1`, information overridden |

Page numbers are one-based and refer to the state produced by every preceding operation. That is the
whole ordering rule: a plan reads from top to bottom, and changing one operation changes what the
next one addresses.

Every method returns a new plan. A plan is immutable, so it can be built once, validated once, shared
between requests and applied concurrently without a lock.

## The operations

| Method | What it does |
| --- | --- |
| `RotatePage(pageNumber, rotation)` | Adds a quarter turn to the page's declared rotation. Rotation is additive and normalised: two `Clockwise90` operations make `Clockwise180`. `Clockwise270` is allowed. |
| `DeletePage(pageNumber)` | Removes one page. An edit that would leave the document with no page is refused. |
| `MovePage(from, to)` | Moves a page so that it ends up at position `to` in the state that exists before the move. |
| `DuplicatePage(pageNumber, insertAt)` | Copies a page and inserts the copy before `insertAt`. `insertAt` may be one past the current count, which appends. Both copies draw the same objects; the page entries are distinct. |
| `CropPage(pageNumber, cropBox)` | Sets the visible area of a page inside its media box. A box that reaches outside the media box is clamped to the part that overlaps it and reported as `PdfWarningKind.PageGeometryNormalized`; a box with no overlap is refused. |
| `ResizePage(pageNumber, size, mode)` | Changes the page size and makes the content follow it. `PdfPageResizeMode.Scale` scales the content to the new size; `PdfPageResizeMode.Preserve` keeps it at its original size and centres it. The original content streams are wrapped, not rewritten. |
| `SetMetadata(metadata)` | Merges document information into the source values. Fields left `null` keep their source value; fields set are replaced. Several metadata operations merge in plan order. |
| `SetXmp(xmp)` | Replaces the XMP packet with the bytes given. The packet must be well-formed XML; nothing in it is rewritten. |
| `SetOutline(outline)` | Replaces the bookmarks. Page numbers are the ones the edit leaves behind; `PdfOutline.Empty` removes the panel. |
| `SetPageLabels(labels)` | Replaces the page labels with explicit ranges; `PdfPageLabeling.None` removes them. |
| `SetViewerPreferences(preferences)` | Replaces what a viewer shows when it opens the document: window chrome, panel, page layout, binding side and print scaling. |
| `Overlay(overlay, pageNumber, options)` | Records one page of another document over a page. |
| `Underlay(underlay, pageNumber, options)` | Records one page of another document under a page. |
| `AddAnnotation(pageNumber, annotation)` | Draws a comment, a stamp or a link over a page without touching its content (chapter 18). |
| `UpdateAnnotation(pageNumber, id, annotation)` | Replaces the annotation that carries an identifier. |
| `RemoveAnnotation(pageNumber, id)` | Removes the annotation that carries an identifier. |
| `SetForm(form)` / `SetFormField(...)` | Writes a form, or fills the fields of the one the document carries, by name (chapter 19). |
| `ResetForm()` | Puts every field back to the value the document states as its default. |
| `FlattenForm()` / `FlattenForm(pageNumber)` | Draws the widgets into the page and removes the form. |

The operation objects are public values (`PdfRotatePageOperation`, `PdfDeletePageOperation`,
`PdfMovePageOperation`, `PdfDuplicatePageOperation`, `PdfCropPageOperation`,
`PdfResizePageOperation`, `PdfSetMetadataOperation`, `PdfOverlayOperation`, `PdfSetXmpOperation`,
`PdfSetOutlineOperation`, `PdfSetPageLabelsOperation`, `PdfSetViewerPreferencesOperation`,
`PdfAddAnnotationOperation`, `PdfUpdateAnnotationOperation`, `PdfRemoveAnnotationOperation`,
`PdfSetFormOperation`, `PdfResetFormOperation`, `PdfFlattenFormOperation`), but the
methods on `PdfEditPlan` are the intended construction route: they check the invariant that can be
checked without a document, and they keep the plan immutable.

The two surfaces that write over a page — annotations and forms — have chapters of their own, because each
carries a model a caller has to understand before the plan makes sense: chapter 18 for the annotation kinds
and their appearance streams, chapter 19 for the field kinds, the merge by name and the flatten.

## Overlays and underlays

An overlay or underlay records one page of another document on a page of the one being edited: a
stamp, a watermark, a letterhead.

```csharp
var plan = PdfEditPlan.Create()
    .Underlay(new PdfFileSource("letterhead.pdf"), 1)
    .Overlay(new PdfFileSource("approved.pdf"), 2, new PdfOverlayOptions
    {
        Opacity = 0.5,
        Placement = PdfOverlayPlacement.Fit,
        BlendMode = PdfOverlayBlendMode.Multiply,
    });
```

The layer is content, not an annotation. It is written as a form XObject that keeps the resources of
the document it came from, so a name it uses for a font or an image can never collide with a name the
page already means something else by. The page's own streams are copied unchanged: an overlay appends
a stream that draws the form, an underlay prepends one, and the bytes the page carried stay in
between, wrapped in the `q`/`Q` pair the contract states.

| Option | Effect |
| --- | --- |
| `Placement` | `Fit` scales the layer until it fits the visible area and centres it, keeping the aspect ratio. `Stretch` fills the area in both directions, so the aspect ratio may change. `OriginalSize` scales nothing and anchors the layer at the lower-left corner. |
| `Opacity` | From `0` (invisible) to `1` (opaque). It is written through an `ExtGState` of the layer's own, so it applies to the layer alone. |
| `BlendMode` | `Normal`, `Multiply`, `Screen`, `Overlay`, `Darken` or `Lighten`; also written through that `ExtGState`. |
| `Clip` | Clips the layer to the visible area of the page. Defaults to `true`; an overlay that reaches past the page would otherwise paint outside it. |

The document a layer comes from must hold exactly one page. The visible area is the page's `CropBox`
when it has one and its `MediaBox` otherwise, after any crop or resize that preceded the layer. The
layer travels with the page it was applied to: `MovePage` carries it, `DeletePage` removes it with the
page, and `DuplicatePage` makes both copies draw the same form.

`PDF/A-1b` is built on a version of PDF that has no transparency, so a plan written for it refuses a
layer with an opacity below `1` or a blend mode other than `Normal` rather than writing the layer
opaque without saying so. The validation happens when the plan is checked, before the source is read.

## Document-level edits

Four operations state a value of the document rather than of a page. Each replaces what the source
carried, a later operation of the same kind replaces an earlier one, and none of them changes the
pages:

- `SetOutline` writes the bookmark tree as stated. Page numbers are one-based in the order the edit
  leaves the pages in; a bookmark that names a page the result does not have is refused with
  `PdfEditPlanValidationException` before anything is written. `PdfOutline.Empty` writes no
  `/Outlines` entry at all, which removes the panel the source carried.
- `SetPageLabels` writes one range per numbering style — `Decimal`, `UpperRoman`, `LowerRoman`,
  `UpperLetters`, `LowerLetters` or `NoNumber` — each with an optional prefix and a start number. A
  range that starts past the last page of the result is refused. `PdfPageLabeling.None` removes the
  labels, so the edit reports no `PageLabelsDropped` warning for a change the caller made itself.
- `SetViewerPreferences` writes `HideToolbar`, `HideMenubar`, `HideWindowUi`, `FitWindow`,
  `CenterWindow`, `DisplayDocTitle`, `PageLayout`, `PageMode`, `ReadingDirection` and `PrintScaling`.
  The page layout and the page mode are catalog entries; the rest is written to
  `/ViewerPreferences`.
- `SetXmp` writes the packet as given, as UTF-8 XML, and never rewrites it. The plan refuses a packet
  that is not well-formed, and one set on a document written for a PDF/A profile, whose own packet
  declares conformance.

## Output options

`PdfEditOutputOptions` controls the document that is written:

| Property | Effect |
| --- | --- |
| `TargetVersion` | The version in the `%PDF-` header. PDF 1.4 to 2.0; the default is 1.7. |
| `Metadata` | Information values applied after every `SetMetadata` operation. It is the last override, not a replacement for the source values. |
| `ModificationDate` | The value written as `ModDate`. When unset, the moment the edit ran is used. |
| `Producer` | The value written as `Producer`. It defaults to the library's producer string; the source value is never kept. |
| `DocumentId` | An optional identity for the result. It participates in the trailer identifier. |

`Producer` and `ModDate` always describe the document that was just written, never the document it was
made from. The other information fields follow the source values and the overrides.

Supply the options when the plan is created, or replace them later:

```csharp
var plan = PdfEditPlan.Create()
    .RotatePage(2, PageRotation.Clockwise180)
    .WithOutput(PdfEditOutputOptions.Default with
    {
        TargetVersion = PdfVersion.V2_0,
        ModificationDate = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
        DocumentId = "statement-2026-04",
    });
```

A fixed `ModificationDate` and `DocumentId` make repeated edits of the same source byte-identical.
The trailer identifier is derived from the output version, the modification date, the page count, the
document identifier and the operations that were applied.

## What survives an edit

Every surviving page keeps its content streams, resources, fonts, images and annotations. Stream bytes
are copied verbatim; nothing is re-compressed and no page is re-rendered. The page tree is flattened,
so inherited `MediaBox`, `CropBox`, `Resources` and `Rotate` are materialised onto each page.

Document-level features are carried over from the source. Two of them cannot be carried over
truthfully after some edits, so they are dropped and reported rather than left to contradict the
result — unless the plan states them itself, in which case the stated value is written:

- **Page labels** are dropped when an operation changes the page order or count — delete, move or
  duplicate. A rotation alone does not change the labels, so they survive it. The result carries
  `PdfWarningKind.PageLabelsDropped`; `SetPageLabels` states them instead, and `PdfPageLabeling.None`
  removes them without the warning.
- **The XMP packet** is dropped when the information dictionary is overridden, whether by
  `SetMetadata` or by `PdfEditOutputOptions.Metadata`. Keeping an old packet would leave two
  descriptions of the same document that disagree. The result carries
  `PdfWarningKind.XmpPacketDropped`; the information dictionary holds the new values, and `SetXmp`
  replaces the packet instead of dropping it.

Bookmarks, named destinations and form widgets that name a page are copied with their references
redirected to the first surviving occurrence of that page, or to the first page of the result when the
page did not survive. A bookmark whose page was deleted therefore still opens a real page, and a
duplicate does not produce a second target for the same reference.

## Validation and failure

A plan checks what it can without opening a document: page numbers must be positive, a rotation must
be a quarter turn, a crop box must enclose an area, a resize mode must be `Scale` or `Preserve`, and
the output options must name a supported version and non-empty strings where a value is set.
`Validate()` returns every problem; `EnsureValid()` throws a single
`PdfEditPlanValidationException` that lists them.

The rest is checked when the source has been read, because only then is its page count known. A page
number beyond the last page at that point, an insert position past the end, or an operation that would
delete the last page fails with the same exception type. These failures happen before the destination
is written, so a failed edit does not leave a half document behind.

The source itself can still fail for the reasons any read can fail: a malformed file raises
`PdfSourceFormatException`, and an encrypted file that cannot be opened with the supplied password
raises `PdfEncryptionException`. Both are described in Chapter 9.

## What an edit cannot change

An edit copies pages; it does not re-render them. There is no operation that replaces text or an
image, removes or edits an annotation, fills a form field, redacts content, signs the result or adds
a digital signature. `CropPage` and `ResizePage` change the geometry a viewer uses, but they do not
change the bytes of the content streams they show. The source can be encrypted and is opened with
the `password` argument of `ApplyAsync`; the result is written in the clear, because output encryption
is not part of this editing release.

The writer produces a complete new document rather than an incremental update. That is what keeps
earlier revisions and signatures out of a document whose pages have changed; it also means a copied
signature is not a signature of the edited document.

## Reading the result

`ApplyAsync` returns a `PdfEditResult`:

```text
edited into 4 page(s), 18,204 bytes, PDF 1.7 in 6 ms
```

`PageCount`, `BytesWritten`, `OutputVersion` and `Elapsed` describe the write. `Operations` lists the
operations in plan order, formatted for diagnostics, and `Warnings` holds the remarks described above.
`ApplyToArrayAsync` performs the same edit and returns the bytes; use it only when the result fits in
memory, and use the stream overload for a large document.
