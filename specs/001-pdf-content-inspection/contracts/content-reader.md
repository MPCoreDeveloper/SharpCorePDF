# Contract: `IPdfContentReader`

**Assembly**: `SharpCorePDF.Domain` | **Namespace**: `SharpCorePDF.Domain.Ports` | **File**: `src/SharpCorePDF.Domain/Ports/IPdfContentReader.cs`

The domain-facing read port. One member per capability, each answering one question about a document,
each returning either `PdfReadResult<T>`, `PdfXmpResult` or an `IAsyncEnumerable<PdfPageDetail>`. This
document is the contract a caller may rely on and a mock may be written against; it is not a
description of how the engine is organised.

## Surface

```csharp
public interface IPdfContentReader
{
    Task<PdfReadResult<PdfImageInfo>>        ReadImagesAsync(IPdfContentSource source, PageRange range, string? password = null, CancellationToken cancellationToken = default);
    Task<PdfReadResult<PdfTextBlock>>        ReadTextAsync(IPdfContentSource source, PageRange range, string? password = null, CancellationToken cancellationToken = default);
    Task                                     CopyImageDataAsync(IPdfContentSource source, PdfImageInfo image, Stream destination, string? password = null, CancellationToken cancellationToken = default);
    Task                                     CopyImageMaskDataAsync(IPdfContentSource source, PdfImageMask mask, Stream destination, string? password = null, CancellationToken cancellationToken = default);
    Task<PdfReadResult<PdfAnnotationInfo>>   ReadAnnotationsAsync(IPdfContentSource source, PageRange range, string? password = null, CancellationToken cancellationToken = default);
    Task<PdfReadResult<PdfFormFieldInfo>>    ReadFormFieldsAsync(IPdfContentSource source, string? password = null, CancellationToken cancellationToken = default);
    Task<PdfXmpResult>                       ReadMetadataAsync(IPdfContentSource source, string? password = null, CancellationToken cancellationToken = default);
    Task<PdfReadResult<PdfEmbeddedFileInfo>> ReadEmbeddedFilesAsync(IPdfContentSource source, string? password = null, CancellationToken cancellationToken = default);
    Task                                     CopyEmbeddedFileDataAsync(IPdfContentSource source, PdfEmbeddedFileInfo file, Stream destination, string? password = null, CancellationToken cancellationToken = default);
    Task<PdfReadResult<PdfOutlineItem>>      ReadOutlineAsync(IPdfContentSource source, string? password = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<PdfPageDetail>          ReadPageDetailsAsync(IPdfContentSource source, PageRange range, string? password = null, CancellationToken cancellationToken = default);
}
```

Eleven members, no overloads, no properties: the conveniences (single-page reads, a materialised
payload, a file destination) live in `PdfContentReaderExtensions`, so an implementer or a mock has the
smallest possible surface to supply.

## Contract common to every member

| Aspect | Guarantee |
| --- | --- |
| Null source | `ArgumentNullException` before the document is touched |
| Ordering | Document order: images by first draw, annotations by page then document order, fields in `/Fields` order, files in filename-tree order, bookmarks in sibling order, pages ascending |
| Page numbers | One-based, always inside `1 … PageCount` |
| Damaged document | Reported through `Warnings`; items that could be read are still returned |
| Unopenable document (encryption) | The collection reads return an empty result plus `EncryptionNotOpened`; `ReadPageDetailsAsync` still yields the pages it can find, each with `ContentStreamUnreadable` |
| Not a PDF | `PdfSourceFormatException` |
| Cancellation | Observed between pages and between objects; a cancelled call throws `OperationCanceledException` |
| Source ownership | The source is opened per call and left unmodified. No member disposes a source it did not create |
| Destination ownership | `Copy*` flush the stream and leave it open; they never dispose it |
| Writes | No member writes a file or opens a destination the caller did not hand in |
| Determinism | Same bytes, same arguments produce equal records. No clocks, no random ids, no ambient culture |
| Concurrency | One instance may serve concurrent reads of different sources; one source may not be read concurrently by two calls |

Argument validation always precedes I/O, so an invalid call fails fast rather than half-opening a
document. The `password` argument is a user or owner password for an encrypted document and is ignored
for a plaintext one; the caller never chooses the encryption handler.

## `ReadImagesAsync(source, range, …)`

**Returns** `PdfReadResult<PdfImageInfo>`: one entry per distinct image *object* the range draws, in
the order the document draws them first, each carrying every page of the range that draws it.

- An image painted twice on one page is one entry whose `Pages` lists that page once.
- An image shared by pages 2 and 5 of a requested 2–6 is one entry with `Pages = [2, 5]`; the same
  document read for range 3–6 reports `Pages = [5]`.
- An inline image is one entry per draw, identified `inline:<page>:<ordinal>`, with
  `ObjectNumber == null`.
- `Pages` is ascending and distinct; it never contains a page outside the requested range.
- A filter the library does not decode yields `IsDecodable == false` with the stored bytes, not a
  skipped entry and not an exception.
- Geometry that cannot be read yields `Width`/`Height`/`Position` as `null` plus a warning; the
  payload is still returned.
- A range past the last page returns an empty result with no warnings.

## `ReadTextAsync(source, range, …)`

**Returns** one `PdfTextBlock` per paragraph the pages of the range draw, in reading order: blocks down
the page, words along the line.

- Ordering is geometric, not the order of the content stream, so a header painted last still comes first.
  `ReadingOrder` is one-based and restarts on every page.
- A block is never empty. Its `Text` joins its lines with a newline and each line's words with one space;
  its `Words` and their `Glyphs` keep the geometry.
- A word ends where the document draws a space or a `TJ` adjustment stands in for one. Lines and blocks
  are grouped by baseline tolerance, word gap, leading and indent, all stated in ems, so the rule scales
  with the font size and never consults a dictionary, a font program or a rendering.
- Characters come from the font: the base encoding and `/Differences` glyph names for a simple font and
  the `/ToUnicode` map for a composite font. A code the font does not map is skipped, never guessed; each
  font skipped this way contributes exactly one `TextUnreadable` warning per page, naming the font, and
  nothing to the page's text. Blocks from the fonts that did read are still returned.
- A document that draws no text yields an empty result and no warning, so "no text" and "text that could
  not be read" stay distinguishable. Repair warnings from the source are still reported beside them.
- The read does not reflow, hyphenate or reconstruct columns: two columns come back as blocks that
  alternate by line, and layout decisions beyond the reported geometry belong to the caller.

## `CopyImageDataAsync(source, image, destination, …)`

**Returns** when the payload has been written and flushed.

- The bytes are exactly what `ReadImagesAsync` would have put in `Data`: the decoded bytes for a
  supported filter, the stored bytes for an unsupported one.
- The copy route is the only way to obtain a payload above `MaxInlineContentBytes`; it works for a
  buffered image too, so callers do not need two paths.
- `image` is a handle: it must be one returned by `ReadImagesAsync` for the same document. The document
  is re-walked to find the same `Id`, and a handle from a different document raises
  `ArgumentException` naming the parameter — it is diagnosed, not undefined.
- The working set is the copy buffer, not the payload.
- A handle whose image or mask has been damaged still writes whatever could be recovered; the
  corresponding warning appeared on the read that produced the handle.
- A damaged stream that yields no bytes at all writes what the filter recovered; an image the walk can
  no longer find raises `ArgumentException` rather than silently writing an empty file.

## `ReadAnnotationsAsync(source, range, …)`

**Returns** every annotation on every page of the range, links included, page by page.

- `Subtype` is the document's own token without the slash; an unmodelled subtype is still returned.
- `Target` is non-null for a link and for a widget with an action; a named destination resolves here,
  so the caller never resolves a name.
- A named destination that does not resolve keeps its name, has `PageNumber == null`, and is reported
  with `NamedDestinationUnresolved`.
- `HasAppearance` reports presence only; no appearance stream is rendered or returned.
- An annotation whose dictionary cannot be read is skipped with `AnnotationUnreadable`; the rest of the
  page's annotations are still returned.

## `ReadFormFieldsAsync(source, …)`

**Returns** every field of the interactive form, document-wide (a form is a document structure, not a
page's), each with all of its widgets.

- `Name` is fully qualified with `.`; two fields under different parents may share a partial name.
- `Widgets[i].PageNumber` is the page that carries the widget, or `null` when no page references it.
- `Value` is `null` when absent and `""` when the document states an empty string.
- Signature fields are reported with name, rectangle and type; their contents are not interpreted.
- A document without a form returns an empty result with no warnings. A field that cannot be read is
  skipped with `FormFieldUnreadable`.

## `ReadMetadataAsync(source, …)`

**Returns** `PdfXmpResult` — not a `PdfReadResult`, because there is at most one packet.

- `Metadata is null` exactly when the document has no `/Metadata` stream; no warning is raised for
  that, because nothing is wrong.
- A packet that exists is returned byte-exact after decompression, with `Text` as UTF-8.
- A packet that cannot be parsed is still returned, `Properties` empty, with `XmpPacketMalformed`.
- `Properties` keys are prefixed names (`dc:title`, `pdf:Producer`, `xmp:CreatorTool`, …); repeated
  values join with `", "` and a language alternative prefers `x-default`.

## `ReadEmbeddedFilesAsync(source, …)`

**Returns** every file of the embedded-file name tree, in tree order.

- `Id` is distinct within the result even when two entries share a name; the collision raises
  `EmbeddedFileNameCollision`.
- The payload follows the inline budget rule, exactly as an image's does.
- A file whose stream cannot be read is skipped with `EmbeddedFileUnreadable`; a file behind an
  unsupported filter is returned with `IsDecodable == false` and its stored bytes.

## `CopyEmbeddedFileDataAsync(source, file, destination, …)`

Identical in shape and guarantees to `CopyImageDataAsync`, with a `PdfEmbeddedFileInfo` handle: the
payload is copied through a bounded buffer, the destination is flushed and left open, and the bytes
match what the read would have buffered.

## `ReadOutlineAsync(source, …)`

**Returns** the top-level `PdfOutlineItem`s, each with its subtree.

- Sibling order is the document's order; `IsOpen` is the state a viewer shows.
- A bookmark whose destination does not resolve is kept, naming the destination that failed.
- A bookmark that points nowhere keeps `Destination == null`.
- A cyclic or unreadable branch is cut with `BookmarkSkipped`; depth is bounded by the engine's
  nesting limit, so the call terminates.
- A document without an outline returns an empty result with no warnings.

## `ReadPageDetailsAsync(source, range, …)`

**Returns** one `PdfPageDetail` per page of the range, ascending, yielded lazily: a page is read when
it is asked for, and the memory a caller commits is one page's worth.

- A blank page is still yielded, with empty collections, so page numbers never shift.
- A damaged page is still yielded, with what could be read and its own `Warnings`.
- An encrypted document that could not be opened still yields the pages it can find, because the page
  tree itself is not encrypted: each page carries empty collections and a `ContentStreamUnreadable`
  warning. `EncryptionNotOpened` belongs to the collection reads, so `ReadMetadataAsync` or the
  inspector is where the reason is named.
- Enumerating the same range twice reads the document twice. The enumerable is not cached and may not
  be enumerated concurrently.
- The enumeration must be disposed (`await foreach` does this); disposing it releases the document.

## Extension conveniences (`PdfContentReaderExtensions`)

Not part of the interface, so not something a mock must implement.

| Extension | Equivalent to |
| --- | --- |
| `ReadImagesAsync(source, int page, …)` | `ReadImagesAsync(source, PageRange.Single(page), …)` |
| `ReadTextAsync(source, int page, …)` | `ReadTextAsync(source, PageRange.Single(page), …)` |
| `ReadAnnotationsAsync(source, int page, …)` | `ReadAnnotationsAsync(source, PageRange.Single(page), …)` |
| `ReadImageDataAsync(source, image, …)` | `CopyImageDataAsync` into a `MemoryStream`, returned as `ReadOnlyMemory<byte>` |
| `SaveImageDataAsync(source, image, path, …)` | `CopyImageDataAsync` into a created or overwritten file |
| `ReadEmbeddedFileDataAsync(source, file, …)` | `CopyEmbeddedFileDataAsync` into memory |
| `SaveEmbeddedFileAsync(source, file, path, …)` | `CopyEmbeddedFileDataAsync` into a file |
| `ReadPageDetailAsync(source, int page, …)` | The first page of `ReadPageDetailsAsync(PageRange.Single(page))`, or `null` |

`ReadImageDataAsync` and `ReadEmbeddedFileDataAsync` materialise the payload and are documented as
unsuitable for large payloads; the copy route is the bounded alternative. `Save*` create the directory
when it is missing and overwrite an existing file.

## Conformance checklist

An implementation conforms when all of the following hold. Each item is observable from the public
surface, which is what makes them testable.

1. Returning a result never throws for a damaged, empty or unusual document.
2. Every item that could be read is returned even when a sibling could not be.
3. Page numbers are one-based and `Pages` never leaves the requested range.
4. `Id` is unique inside one result, and stable across reads of the same document.
5. A payload is either fully buffered with a truthful `ByteLength`, or withheld with a truthful
   `ByteLength` — never truncated, never silently zero.
6. `IsDecodable == false` is accompanied by a `Filter` that explains it.
7. Warnings carry a stable `Kind`; their text is never asserted on.
8. A cancelled call throws `OperationCanceledException` and disposes what it opened.
9. No call mutates the source; no call disposes a stream the caller supplied.
10. Two identical calls produce equal records.
11. A block's words appear in reading order, every word appears exactly once in the text of its block,
    and a code no font maps yields a `TextUnreadable` warning rather than a character.
