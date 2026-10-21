# Data Model: PDF Content Inspection

**Feature**: `001-pdf-content-inspection` | **Plan**: [plan.md](./plan.md) | **Spec**: [spec.md](./spec.md)

Everything here is a public, immutable `sealed record` in `SharpCorePDF.Domain.ReadModels` (or a value
object in `SharpCorePDF.Domain.ValueObjects`) with `init`-only properties. There is no persistence, no
identity beyond the document's own object numbers, and no mutation: an entity is a value that describes
a document at the moment it was read. Collections are never `null`; "nothing found" is an empty list,
and the two questions "absent" and "unreadable" are answered by different fields rather than by a
shared `null`.

## Capability map

| Capability | Caller member | Result shape | Entity |
| --- | --- | --- | --- |
| Images a range paints | `ReadImagesAsync(source, range, …)` | `PdfReadResult<PdfImageInfo>` | `PdfImageInfo`, `PdfImageMask` |
| Text the pages draw | `ReadTextAsync(source, range, …)`, `ReadTextAsync(source, page, …)` | `PdfReadResult<PdfTextBlock>` | `PdfTextBlock`, `PdfWord`, `PdfGlyph` |
| One image's bytes | `CopyImageDataAsync`, `ReadImageDataAsync`, `SaveImageDataAsync` | `Stream` or `ReadOnlyMemory<byte>` | `PdfImageInfo` (the handle) |
| Annotations and links | `ReadAnnotationsAsync(source, range, …)` | `PdfReadResult<PdfAnnotationInfo>` | `PdfAnnotationInfo`, `PdfDestination` |
| Interactive form | `ReadFormFieldsAsync(source, …)` | `PdfReadResult<PdfFormFieldInfo>` | `PdfFormFieldInfo`, `PdfWidgetInfo` |
| XMP metadata | `ReadMetadataAsync(source, …)` | `PdfXmpResult` | `PdfXmpMetadata` |
| Embedded files | `ReadEmbeddedFilesAsync(source, …)` | `PdfReadResult<PdfEmbeddedFileInfo>` | `PdfEmbeddedFileInfo` |
| One attachment's bytes | `CopyEmbeddedFileDataAsync`, `ReadEmbeddedFileDataAsync`, `SaveEmbeddedFileAsync` | `Stream` or `ReadOnlyMemory<byte>` | `PdfEmbeddedFileInfo` (the handle) |
| Bookmarks | `ReadOutlineAsync(source, …)` | `PdfReadResult<PdfOutlineItem>` | `PdfOutlineItem` |
| Everything about one page | `ReadPageDetailsAsync(source, range, …)`, `ReadPageDetailAsync(source, page, …)` | `IAsyncEnumerable<PdfPageDetail>` / `PdfPageDetail?` | `PdfPageDetail` |

Every `Task`-returning member takes `(…, string? password = null, CancellationToken ct = default)`;
every `IAsyncEnumerable` member takes the range before the password. The source is always an
`IPdfContentSource`, so a path, a stream and a byte array are the same call.

## Entity catalogue

### `PdfReadResult<T>`

The shape of every read that returns a collection.

| Member | Type | Required | Meaning |
| --- | --- | --- | --- |
| `Items` | `IReadOnlyList<T>` | yes | What was read, in document order |
| `Warnings` | `IReadOnlyList<PdfWarning>` | no (defaults empty) | What did not stop the read |
| `Count` | `int` | derived | `Items.Count` |

"Nothing was found" is `Items` empty with `Warnings` empty. "Nothing could be read" is `Items` empty
with at least one warning naming the reason. That distinction is the reason every capability returns
this shape instead of a bare list.

### `PdfImageInfo`

One image object, de-duplicated across the pages of the range.

| Member | Type | Meaning |
| --- | --- | --- |
| `Id` | `string` (**required**) | `"12 0"` for an object, `"inline:3:2"` for the second inline image on page 3, `"resource:3:1"` for a reference-less resource value |
| `ObjectNumber` | `long?` | `null` for an inline image |
| `Generation` | `int?` | `null` for an inline image |
| `Pages` | `IReadOnlyList<int>` | One-based, distinct, ascending; only pages of the requested range |
| `Width`, `Height` | `int?` | In samples; `null` when the dictionary does not state them |
| `BitsPerComponent` | `int?` | `null` when not stated |
| `ColorSpace` | `string?` | As named by the document (`DeviceRGB`, `Indexed`, `ICCBased`, …) |
| `Filter` | `string?` | As named, a chain joined with a space in application order |
| `IsDecodable` | `bool` | `false` when the filter is not one the library decodes; raw bytes are returned |
| `ByteLength` | `long` | True payload length, buffered or not |
| `IsBuffered` | `bool` | Whether `Data` carries the payload |
| `Data` | `ReadOnlyMemory<byte>?` | Payload when at most `MaxInlineContentBytes`; `null` otherwise |
| `Mask` | `PdfImageMask?` | Soft, explicit or colour-key mask |
| `Position` | `PdfRectangle?` | Unit square transformed by the CTM of the first draw, in page space |
| `IsInline` | `bool` | Derived: `ObjectNumber is null` |

### `PdfImageMask`

| Member | Type | Meaning |
| --- | --- | --- |
| `Kind` | `PdfImageMaskKind` (**required**) | `SoftMask`, `ExplicitMask` or `ColorKey` |
| `ObjectNumber`, `Generation` | `long?`, `int?` | The mask image, when it is one |
| `Width`, `Height`, `BitsPerComponent` | `int?` | As stated by the mask stream |
| `ColorSpace`, `Filter` | `string?` | As stated by the mask stream |
| `IsDecodable` | `bool` | Same meaning as on the image |
| `ByteLength` | `long` | Decoded length; `0` for a colour key, which has no payload |
| `IsBuffered`, `Data` | `bool`, `ReadOnlyMemory<byte>?` | Same budget rule as the image |
| `ColorKey` | `IReadOnlyList<double>` | The sample ranges a colour key excludes, in document order; empty for an image mask |

### `PdfTextBlock`, `PdfWord` and `PdfGlyph`

Three nested records: a block is a paragraph, a word is a run of glyphs that touch on one baseline, and
a glyph is one decoded character as drawn. *(Added 2026-10-21.)*

| Member (block) | Type | Meaning |
| --- | --- | --- |
| `PageNumber` | `int` (**required**) | One-based page the block is drawn on |
| `Text` | `string` (**required**) | The block as plain text: its lines joined with a newline, each line's words with one space |
| `BoundingBox` | `PdfRectangle` (**required**) | Union of the words' rectangles, in page space |
| `ReadingOrder` | `int` (**required**) | One-based position on its page; blocks read down the page, words along the line |
| `Words` | `IReadOnlyList<PdfWord>` | The words, in reading order |
| `LineCount` | `int` (derived) | The newlines in `Text` plus one |

| Member (word) | Type | Meaning |
| --- | --- | --- |
| `Text` | `string` (**required**) | The word's characters, concatenated from its glyphs |
| `PageNumber` | `int` (**required**) | One-based page |
| `BoundingBox` | `PdfRectangle` (**required**) | Union of the glyphs' rectangles |
| `Glyphs` | `IReadOnlyList<PdfGlyph>` | The glyphs the word is made of |
| `FontName`, `FontSize`, `BaselineY` | derived | Taken from the first glyph; `null` or `0` when the word carries no glyph |

| Member (glyph) | Type | Meaning |
| --- | --- | --- |
| `Text` | `string` (**required**) | What the font says the code means — never the code byte |
| `PageNumber` | `int` (**required**) | One-based page |
| `X`, `Y`, `Width` | `double` (**required**) | Page-space origin and advance, as the text rendering matrix places them |
| `FontSize` | `double` (**required**) | Effective size in page space |
| `FontName`, `BaseFont` | `string?` | The resource name and the document's `/BaseFont`, when it states one |

### `PdfAnnotationInfo`

| Member | Type | Meaning |
| --- | --- | --- |
| `PageNumber` | `int` (**required**) | One-based page the annotation belongs to |
| `Subtype` | `string` (**required**) | As spelled by the document (`Text`, `Highlight`, `Stamp`, `Link`, …); not reduced to an enum |
| `Rectangle` | `PdfRectangle?` | Page-space rectangle, or `null` when none can be read |
| `Flags` | `int` | The annotation flags as stated |
| `Contents`, `Author` | `string?` | Text and author, `null` when absent |
| `ModificationDate` | `DateTimeOffset?` | `null` when absent or unreadable |
| `HasAppearance` | `bool` | Whether an appearance stream exists; nothing is rendered |
| `Name` | `string?` | The `/NM` identifier, `null` when the document states none |
| `Target` | `PdfDestination?` | Where a link or action points, `null` when it points nowhere |
| `IsLink` | `bool` | Derived: `Subtype == "Link"` |

### `PdfDestination`

| Member | Type | Meaning |
| --- | --- | --- |
| `PageNumber` | `int?` | One-based target page; `null` when external or unresolved |
| `Area` | `PdfRectangle?` | Named area or point; a corner names a rectangle of no size |
| `NamedDestination` | `string?` | The name the document used, whether or not it resolved |
| `Uri` | `string?` | External URL exactly as written |
| `FileName` | `string?` | Name of the other file an external target names |
| `IsInternal` | `bool` | Derived: `PageNumber is not null` |
| `IsExternal` | `bool` | Derived: `Uri` or `FileName` set |
| `IsUnresolved` | `bool` | Derived: a name was given, nothing resolved and it is not external |

A destination states exactly one of the internal, external-URL, external-file or unresolved shapes. It
is a record, so a resolved name and a direct destination with the same page and area are equal only if
the name is also equal — which is why `NamedDestination` is documented as provenance, not identity.

### `PdfFormFieldInfo` and `PdfWidgetInfo`

| Field member | Type | Meaning |
| --- | --- | --- |
| `Name` | `string` (**required**) | Fully-qualified `parent.child` name |
| `FieldType` | `string?` | `Tx`, `Btn`, `Ch`, `Sig`, or `null` when neither the field nor an ancestor states one |
| `Value` | `string?` | Text, a button's on-state name, `null` for a signature and for an absent value |
| `DefaultValue` | `string?` | The `/DV` value when stated |
| `Flags` | `int` | Field flags as stated |
| `Widgets` | `IReadOnlyList<PdfWidgetInfo>` | One entry per place the field appears |
| `IsReadOnly` | `bool` | Derived: flag bit 1 |
| `IsRequired` | `bool` | Derived: flag bit 2 |

| Widget member | Type | Meaning |
| --- | --- | --- |
| `PageNumber` | `int?` | Page that carries the widget, `null` when no page references it |
| `Rectangle` | `PdfRectangle?` | Page-space rectangle, `null` when none can be read |

A field with no widget is still a field; a widget with no page is still a widget. Neither is dropped,
because each absence is a fact about the document.

### `PdfEmbeddedFileInfo`

| Member | Type | Meaning |
| --- | --- | --- |
| `Id` | `string` (**required**) | The name, or the name with a numeric suffix when the tree holds two entries of that name |
| `Name` | `string` (**required**) | The name the document gives the file |
| `MimeType` | `string?` | The `/Subtype` of the embedded stream |
| `Relationship` | `string?` | `Source`, `Data`, `Alternative`, … |
| `ModificationDate` | `DateTimeOffset?` | `/ModDate`, `null` when absent or unreadable |
| `Filter` | `string?` | Filter chain of the embedded stream |
| `IsDecodable` | `bool` | `false` when the bytes are stored behind a filter the library does not decode |
| `ByteLength` | `long` | True length, buffered or not |
| `IsBuffered`, `Data` | `bool`, `ReadOnlyMemory<byte>?` | The payload rule, unchanged |

`Id` is distinct inside one read, which is what makes two same-named attachments addressable and what
the `EmbeddedFileNameCollision` warning is about.

### `PdfOutlineItem`

| Member | Type | Meaning |
| --- | --- | --- |
| `Title` | `string` (**required**) | As stated by the document |
| `Destination` | `PdfDestination?` | `null` when the bookmark points nowhere |
| `IsOpen` | `bool` | The open/closed state a viewer shows; defaults to `true` |
| `Children` | `IReadOnlyList<PdfOutlineItem>` | Siblings in document order; the tree is recursive |

Depth is bounded by the engine's nesting limit, so a cyclic outline cannot recurse forever. An
unreadable or cyclic branch is skipped with `BookmarkSkipped` and the rest of the tree is returned.

### `PdfPageDetail`

| Member | Type | Meaning |
| --- | --- | --- |
| `PageNumber` | `int` (**required**) | One-based |
| `Size` | `PdfPageSize` (**required**) | As stored, before rotation |
| `Rotation` | `PageRotation` | The rotation a viewer applies |
| `Images` | `IReadOnlyList<PdfImageInfo>` | In draw order (not de-duplicated the way `ReadImagesAsync` de-duplicates) |
| `Annotations` | `IReadOnlyList<PdfAnnotationInfo>` | Links included |
| `FormFields` | `IReadOnlyList<PdfFormFieldInfo>` | Fields with a widget on this page |
| `Warnings` | `IReadOnlyList<PdfWarning>` | This page's own problems |
| `Links` | `IReadOnlyList<PdfAnnotationInfo>` | Derived: annotations whose `IsLink` is set |
| `DisplaySize` | `PdfPageSize` | Derived: `Size.Rotate(Rotation)` |

The record is a *view*: it gathers the page-scoped readers rather than adding a second parse, so a
page's images here and the same page's images from `ReadImagesAsync` describe the same objects.

### `PdfXmpResult` and `PdfXmpMetadata`

| Result member | Type | Meaning |
| --- | --- | --- |
| `Metadata` | `PdfXmpMetadata?` | `null` when the document carries no `/Metadata` stream, or when that stream cannot be decoded (a warning says which) |
| `Warnings` | `IReadOnlyList<PdfWarning>` | Empty, or the reason the packet could not be parsed |

| Metadata member | Type | Meaning |
| --- | --- | --- |
| `RawPacket` | `ReadOnlyMemory<byte>` (**required**) | Stored bytes after decompression, byte-exact |
| `Text` | `string` (**required**) | The same packet decoded as UTF-8 |
| `Properties` | `IReadOnlyDictionary<string, string>` | Prefixed names (`dc:title`, `pdf:Producer`, `xmp:CreatorTool`, …) to values; `Ordinal` keys |
| `Title`, `Producer`, `CreatorTool` | `string?` | Derived convenience lookups into `Properties` |

`Metadata is null` and `Metadata is not null && Properties.Count == 0` are different answers: no packet
at all versus a packet that exists but could not be parsed. The second always comes with a
`XmpPacketMalformed` warning.

### `PdfWarning`

The vehicle for every non-fatal remark. Existing value object, unchanged by this feature.

| Member | Type | Meaning |
| --- | --- | --- |
| `Kind` | `PdfWarningKind` | Closed enum; this feature adds the twenty content kinds listed below |
| `Message` | `string` | Human-readable, names the source and the page or object; never empty; wording is not a contract |

Content-reading kinds added by this feature, numbered 26–35 in the enum:
`ContentStreamUnreadable`, `ImageFilterUnsupported`, `ImageDataUnreadable`, `XObjectResourceMissing`,
`NamedDestinationUnresolved`, `EmbeddedFileUnreadable`, `EmbeddedFileNameCollision`,
`XmpPacketMalformed`, `FormFieldUnreadable`, `AnnotationUnreadable`. The outline kinds
(`BookmarkSkipped`, `BookmarksTruncated`, `BookmarkDestinationDropped`) and the encryption kinds
(`EncryptionNotOpened`, `EncryptionOpened`, …) already existed; the readers reuse them unchanged rather
than minting variants.

A condition that merely reduces the payload — going over budget — raises no warning. Nothing is wrong
when the caller can still fetch every byte.

## Relationships

```text
PdfReadResult<T> 1 ──── *  T                        T ∈ { Image, Annotation, FormField, EmbeddedFile, Outline }
PdfReadResult<T> 1 ──── *  PdfWarning

PdfImageInfo     1 ──── 0..1 PdfImageMask
PdfImageInfo     * ──── *  page numbers (one-based ints, not entities)
PdfImageInfo     1 ──── 0..1 PdfRectangle (Position)

PdfAnnotationInfo * ──── 0..1 PdfDestination
PdfOutlineItem    1 ──── 0..1 PdfDestination
PdfOutlineItem    1 ──── *  PdfOutlineItem (Children, recursive)

PdfFormFieldInfo  1 ──── 1..* PdfWidgetInfo
PdfWidgetInfo     * ──── 0..1 page number

PdfPageDetail     1 ──── *  PdfImageInfo
PdfPageDetail     1 ──── *  PdfAnnotationInfo
PdfPageDetail     1 ──── *  PdfFormFieldInfo
PdfPageDetail     1 ──── *  PdfWarning

PdfXmpResult      1 ──── 0..1 PdfXmpMetadata
PdfXmpResult      1 ──── *  PdfWarning
```

There is no reference cycle between entities: `PdfOutlineItem` is the only recursive type, and it
recurses downward only. An image does not point at its pages, it names their numbers, so no entity
graph has to be walked to free it.

## Invariants

These hold for every read, on every document, and are what the contract tests assert.

| # | Invariant | Enforced by |
| --- | --- | --- |
| INV-1 | `Items` and `Warnings` are never `null`; empty means "none" | Collection initialisers on the records |
| INV-2 | A required member is always set; the compiler rejects a construction that omits it | `required` members |
| INV-3 | Every page number is one-based and lies in `1 … PageCount` | Engine resolves indices before constructing records |
| INV-4 | `PdfImageInfo.Pages` is distinct and ascending, and contains only pages of the requested range | Image gathering de-duplicates and sorts per range |
| INV-5 | Within one read, `PdfImageInfo.Id` and `PdfEmbeddedFileInfo.Id` are unique | Image keyed by object number (plus per-page ordinal for inline); file disambiguated with a suffix |
| INV-6 | `Data is not null` implies `IsBuffered` and `Data.Value.Length == ByteLength` | Single payload helper is the only constructor path |
| INV-7 | `Data is null` implies the payload was withheld, not absent: `ByteLength` is the true length, and the copy route emits exactly that many bytes for the same handle | Budget check happens after the true length is known |
| INV-8 | `IsDecodable` is `false` only when `Filter` names a filter the library does not decode, and the returned bytes are then the stored bytes | Filter table decides decode versus passthrough |
| INV-9 | A colour-key mask has no payload: `ByteLength == 0`, `Data is null`, `IsBuffered` is `false` | Mask reader handles the array form separately |
| INV-10 | Warning kinds are stable; messages are not. No behaviour depends on message text | `PdfWarning` documents this; tests assert on `Kind` |
| INV-11 | A read does not mutate, seek back, or dispose the caller's source or destination | Source wrapper opens its own view; copy route flushes and leaves the destination open |
| INV-12 | Two reads of the same bytes with the same arguments produce equal records (`record` value equality) | No ambient state, no timestamps, no random ids |
| INV-13 | `PdfPageDetail.Warnings` are the page's own remarks; document-level remarks appear on the document-scoped reads instead | Page detail gathers per-page warnings only |
| INV-14 | `PdfXmpResult.Metadata is null` exactly when the document has no `/Metadata` stream or that stream cannot be decoded; a malformed packet yields non-null `Metadata` with `Properties` empty and a warning | Metadata reader distinguishes absent from unparsable |
| INV-15 | Exactly one of a destination's shapes is populated; `IsUnresolved` implies `NamedDestination` is set and both external fields are null | One factory method per shape |
| INV-16 | `PdfFormFieldInfo.Value is null` and `Value == ""` are distinct: absent versus empty | Reader copies the raw string, or leaves it null |
| INV-17 | A returned block is never empty, and its words and lines appear in the order its `Text` reads | Blocks are built from words, and an empty run is never added |
| INV-18 | `PdfTextBlock.ReadingOrder` restarts at 1 on every page and increases by one | Blocks are built per page |
| INV-19 | A glyph's `Text` is the font's mapping of the code; a code with no mapping produces no glyph, and one `TextUnreadable` warning per font per page says so | The collector skips unmapped codes and de-duplicates the remark by font and page |

## Payload state transitions

The payload rule is one rule applied in three places (image, mask, embedded file). It has three states
and one forward transition between the first two.

```text
                      length ≤ MaxInlineContentBytes
   ┌──────────┐ ───────────────────────────────────────► ┌──────────┐
   │  absent  │                                          │ buffered │  Data ≠ null, IsBuffered = true
   │ (no      │                                          │          │  ByteLength = Data.Length
   │ payload) │                                          └──────────┘
   └──────────┘                                                 │
   colour-key mask only                                         │ length > MaxInlineContentBytes
                                                                ▼
                                                          ┌──────────┐
                                                          │ withheld │  Data = null, IsBuffered = false
                                                          │          │  ByteLength = true length
                                                          └──────────┘
                                                               │ copy route
                                                               ▼
                                                         bytes to caller's stream
```

- **buffered → withheld** is a consequence of size, not of failure. No warning is raised and
  `IsDecodable` is unaffected.
- **withheld → buffered** does not happen by re-reading: the caller either copies the bytes or raises
  `MaxInlineContentBytes`. There is no "fetch into the record" transition.
- **absent** exists only for a colour-key mask, whose mask is a pair of numbers rather than a stream.
- A zero-length payload is **buffered** with an empty `Data`, not absent.

## Other state models

**XMP packet**: `absent` (`Metadata is null`) → `parsed` (`Properties` populated) or `malformed`
(`RawPacket` intact, `Properties` empty, `XmpPacketMalformed` warning). A malformed packet never
becomes absent.

**Form field value**: `absent` (`Value is null`, no `/V`) / `empty` (`Value == ""`) / `present`.
`IsReadOnly` and `IsRequired` are pure functions of `Flags`, so they never disagree with it.

**Document openability**: `open` → results with items; `open but damaged` → results with items *and*
warnings, or fewer items plus warnings; `not openable` (encrypted, wrong or missing password) → empty
results plus the encryption warning the inspector already raises, except in page details, where the
page tree is still readable: those pages are yielded with `ContentStreamUnreadable` in place of their
content. A document that is not a PDF at all raises `PdfSourceFormatException` instead of producing any
of these states.

## Validation rules

| Rule | Where | Failure |
| --- | --- | --- |
| `source` is not null | Every port member | `ArgumentNullException` |
| `destination` is writable | `Copy*Async` | `ArgumentException` |
| `destinationPath` is not null, empty or whitespace | `Save*Async` | `ArgumentException` |
| `page` is at least one | `ReadPageDetailAsync` | `ArgumentOutOfRangeException` |
| `range` is well formed | `PageRange` construction | `ArgumentOutOfRangeException` from the value object |
| A range starting past the last page | Read | Empty result, no warning — not an error |
| A range ending past the last page | Read | Clamped to the last page, no warning |
| `MaxInlineContentBytes` is at least zero | `PdfEngineOptions.Validate()` | Error reported at configuration time |
| The content is a readable PDF | Read | `PdfSourceFormatException` |

Argument validation happens before the document is opened, so a bad call fails fast and never leaves a
half-read stream behind. Everything about the *document* is reported, not thrown.
