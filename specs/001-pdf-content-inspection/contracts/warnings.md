# Contract: Warnings

**Type**: `PdfWarning` (`SharpCorePDF.Domain.ValueObjects`) | **Shape**: `Kind`, `Message` — and nothing else

A read never throws because a document is damaged. Every page, image, bookmark, field, annotation,
embedded file and XMP packet is either returned, or its absence is explained by a warning. The warning is
the whole error-handling story of this feature, so what it does and does not promise is worth being
precise about.

## What a warning is

| Member | Type | Contract |
| --- | --- | --- |
| `Kind` | `PdfWarningKind` | **Stable.** New kinds may appear; existing kinds never change meaning |
| `Message` | `string` | **Not** a contract. Names the source and the object; may be reworded in any release |

There is no page number property, no severity, no inner exception and no object reference on a warning.
The page a remark is about can be read from the message, and — more reliably — from *where the warning
appears*: `PdfPageDetail.Warnings` holds the remarks about that page, `PdfReadResult.Warnings` and the
document-level results hold the remarks about the document. Callers that need to branch must switch on
`Kind`; callers that need to know which page must read the collection, not the text.

`Message` is never null, empty or whitespace; the constructor enforces it.

## The rule that makes warnings safe

**A warning never replaces a readable item.** The two are independent outcomes:

- Damage that removes something produces a warning and one fewer item.
- Damage that degrades something produces a warning and an item that is present and honest about the
  degradation — `ByteLength` without `Data`, raw bytes instead of decoded ones, `null` geometry.
- A normal condition that merely limits the payload (the inline budget) produces **no** warning at all.

The practical consequence: a caller can render a complete summary from an empty warning list, and a
non-empty warning list never means "the rest is untrustworthy". It means "these specific things are
missing or degraded, and the rest of the result is exactly what the document says".

The other half of the rule: **a warning is never emitted for something the caller can still obtain.**
Going over the inline budget is the canonical example — the payload is withheld from the model, but the
copy route still yields it, so nothing is wrong.

## One warning per condition

- A damaged object produces one warning where it is discovered, not one per consumer.
- In `ReadPageDetailsAsync` a damaged image drawn on two pages yields two entries and two warnings, one per
  page. In `ReadImagesAsync` the same image is one entry listing both pages and one warning, because the
  object was described once. Scoping follows the read, not the object.
- Order is not a contract. A caller that needs determinism groups by `Kind`.
- The collections are never `null`; they default to empty.

## The ten kinds this feature adds

| Kind | Raised when | What is still returned | Caller's move |
| --- | --- | --- | --- |
| `ContentStreamUnreadable` | A page's content stream, or a form XObject's stream, cannot be decoded | The page is present with empty `Content`; a form that failed contributes no images | Treat the page as having no visible content; do not infer the document is broken |
| `ImageFilterUnsupported` | An image or mask uses a filter the library does not decode | The entry, fully described, with the **stored** bytes when within budget and `IsDecodable == false` | Hand the bytes to a decoder that knows the filter; the filter name is on the entry |
| `ImageDataUnreadable` | An image or mask payload fails to decode, or states no readable `/Width` or `/Height` | The entry, with raw bytes when recoverable; missing geometry is `null` | Use `ByteLength`/`Filter`; do not trust geometry that is `null` |
| `XObjectResourceMissing` | A page paints an XObject its resources do not define, or which is not a stream | The page and every other image; the missing one is simply absent | Ignore, or report the page as having a dangling resource |
| `NamedDestinationUnresolved` | A bookmark or annotation names a destination that does not resolve | The item, with `Destination == null` | Treat the link as dead; the item itself is still listed |
| `EmbeddedFileUnreadable` | A file specification states no embedded stream, or its payload cannot be decoded | The entry with its name, size and dates; raw bytes when recoverable | List the attachment by name even if the bytes are unusable |
| `EmbeddedFileNameCollision` | Two embedded files share a file name | Both entries, each with a distinct `Id` | Key on `Id`, never on the file name |
| `XmpPacketMalformed` | The metadata stream cannot be decoded, or its packet is not well-formed XML | Undecodable stream: `Metadata is null`. Malformed packet: `Metadata` is returned with `RawPacket` intact and `Properties` empty | Use `RawPacket` or fall back to the document information dictionary, read through inspection |
| `FormFieldUnreadable` | A field, or a child of a field, cannot be resolved | Every other field, in document order; the unreadable one is skipped | Report the form as partially readable |
| `AnnotationUnreadable` | An annotation cannot be resolved | Every other annotation on the page | Report the page as having an annotation that cannot be described |

The three outline kinds (`BookmarkSkipped`, `BookmarksTruncated`, `BookmarkDestinationDropped`) predate
this feature and appear unchanged when the outline is read. `PageSizeAssumed` likewise predates it and
is reused when a page has no readable `/MediaBox`. The encryption kinds (`EncryptionNotOpened`,
`EncryptionOpened`, and the handler/dictionary variants) are raised by the open step and reused
verbatim, which is why a collection read of an encrypted document that could not be opened returns a
result whose only content is that warning. Page details do not report it — they report
`ContentStreamUnreadable` per page instead, because the page tree is readable but the content is not.

## Scope and routing

Every *collection* read returns `PdfReadResult<T>`, whose `Warnings` is the document's own remarks (from
the open step: page-tree repairs, encryption notes) followed by the remarks the read itself made. So
`ReadImagesAsync`, `ReadAnnotationsAsync`, `ReadFormFieldsAsync`, `ReadEmbeddedFilesAsync` and
`ReadOutlineAsync` report damage in one flat list; the entry a warning is about is named in the message.
Two members fall outside that shape: `ReadPageDetailsAsync` streams pages, each with its own warnings,
and `ReadMetadataAsync` returns a `PdfXmpResult`.

| Read | Where the warnings are | Page attribution |
| --- | --- | --- |
| `ReadPageDetailsAsync` | `PdfPageDetail.Warnings`, per page as it streams | The page's own collection |
| `ReadPageDetailAsync` | `PdfPageDetail.Warnings` | The page returned |
| `ReadImagesAsync` | `PdfReadResult.Warnings` | `PdfImageInfo.Pages`, plus the page name in the message |
| `ReadAnnotationsAsync` | `PdfReadResult.Warnings` | `PdfAnnotationInfo.PageNumber` |
| `ReadFormFieldsAsync` | `PdfReadResult.Warnings` | `PdfFormFieldInfo.Widgets[].PageNumber` |
| `ReadEmbeddedFilesAsync` | `PdfReadResult.Warnings` | n/a |
| `ReadOutlineAsync` | `PdfReadResult.Warnings` | n/a |
| `ReadMetadataAsync` | `PdfXmpResult.Warnings` | n/a |
| payload copy | none — the copy throws or succeeds | n/a |

A warning raised while describing a single page is attached to that page, so a caller scanning
`Pages[i].Warnings` can attribute damage without parsing text. A read of one page attaches to that page
only; it never reports the document's other pages. `ReadPageDetailsAsync` does not carry the document's
open-time remarks at all, and folds the document-wide form-field remarks into the first page it yields —
the one place where a page carries a remark that is not about it.

Because the document's own remarks are folded into every result, the same `PageTreeCycle` warning can
appear in two different results. That is intended: each result is self-contained, and comparing warnings
across results is not a supported way to find new damage.

## Conformance checklist

1. Every warning has a non-empty `Message` and a `Kind`; nothing else is on the type.
2. A page whose content stream cannot be decoded is returned with empty content and one
   `ContentStreamUnreadable` in *that page's* warnings — not in another page's.
3. An image with an unsupported filter is returned with its stored bytes, `IsDecodable == false` and one
   `ImageFilterUnsupported`; the message names the source and the filter, and neither is invented.
4. A payload withheld by the inline budget produces **no** warning in any collection.
5. A warning and a readable item are independent: for every kind in the table, the surrounding result is
   still complete and no exception is thrown.
6. Two pages drawing the same damaged image produce two warnings, one per page.
7. Two embedded files with the same name produce two entries with distinct `Id`s and one
   `EmbeddedFileNameCollision`.
8. A packet that is not well-formed XML yields `Metadata` with `RawPacket` intact, `Properties` empty,
   and `XmpPacketMalformed`; a metadata stream that cannot be decoded yields `Metadata == null` and the
   same kind.
9. An unresolvable named destination yields the annotation or bookmark with `Destination == null`.
10. An encrypted document that could not be opened yields an empty result plus `EncryptionNotOpened`
    from every collection read, and its pages with `ContentStreamUnreadable` from page details.
11. Warning collections are empty, never `null`, on every result.
12. The same read twice produces the same kinds for the same document (messages and order may differ).
13. A read of one page reports that page's damage only; the document-level fold-in is the sole exception.

## Non-goals

- No severity, category or "is fatal" flag: `Kind` is the whole taxonomy.
- No exception, stack trace or inner error carried on a warning.
- No page number, object number or byte offset as a property; the message names them for humans, the
  collection scopes them for callers.
- No ordering guarantee, no deduplication guarantee, and no filter/ignore API.
- No warnings for the budget, for an absent optional value (`Value is null`), or for a document that is
  merely unusual but intact.
