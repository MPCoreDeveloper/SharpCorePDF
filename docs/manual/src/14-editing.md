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

The operation objects are public values (`PdfRotatePageOperation`, `PdfDeletePageOperation`,
`PdfMovePageOperation`, `PdfDuplicatePageOperation`, `PdfCropPageOperation`,
`PdfResizePageOperation`, `PdfSetMetadataOperation`), but the methods on `PdfEditPlan` are the
intended construction route: they check the invariant that can be checked without a document, and
they keep the plan immutable.

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
result:

- **Page labels** are dropped when an operation changes the page order or count — delete, move or
  duplicate. A rotation alone does not change the labels, so they survive it. The result carries
  `PdfWarningKind.PageLabelsDropped`.
- **The XMP packet** is dropped when the information dictionary is overridden, whether by
  `SetMetadata` or by `PdfEditOutputOptions.Metadata`. Keeping an old packet would leave two
  descriptions of the same document that disagree. The result carries
  `PdfWarningKind.XmpPacketDropped`; the information dictionary holds the new values.

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
