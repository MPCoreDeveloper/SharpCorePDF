# Research: PDF Content Inspection

**Feature**: `001-pdf-content-inspection` | **Date**: 2026-10-20 | **Plan**: [plan.md](./plan.md)

This file records the decisions the implementation rests on, why each one was taken, and what was
rejected. It is retrospective in the same sense as the specification: the code exists, and these are
the reasons it looks the way it does. No decision below is open; nothing here is `NEEDS CLARIFICATION`.

## D1. Read models are domain records, not infrastructure types

**Decision**: every answer is a `sealed record` in `SharpCorePDF.Domain.ReadModels`, with `init`-only
properties, `required` where a value is always known, and collections that are never `null`. Anything
that computes a derived value (`PdfPageDetail.DisplaySize`, `PdfImageInfo.IsInline`,
`PdfDestination.IsUnresolved`, `PdfFormFieldInfo.IsReadOnly`) computes it from the stored fields
instead of storing a second copy.

**Rationale**: Principle II puts the vocabulary of an answer in the domain and the mechanism of
producing it in infrastructure. Records give value equality, so two reads of the same document compare
equal without a hand-written comparer, and `init`-only properties make a read model safe to hand to
another thread and to keep after the read has finished.

**Alternatives considered**: mutable DTOs in infrastructure (rejected: callers would depend on the
engine's assembly, and a second engine could not answer the same port); anonymous or
`IReadOnlyDictionary` payloads (rejected: no discoverability, no documentation surface to snapshot);
classes with setters and a comparer (rejected: more code, and mutation is exactly what a read must not
invite).

## D2. Two ports: the caller takes a source, the engine takes a stream

**Decision**: `IPdfContentReader` lives in `SharpCorePDF.Domain.Ports` and takes an
`IPdfContentSource`; `IPdfPageContentReader` lives in `SharpCorePDF.Application.Ports` and takes a
`Stream`. `PdfContentReaderService` in the application layer opens the source and forwards;
`PdfPageContentReader` in infrastructure implements the stream port.

**Rationale**: the domain must not know what a file is (Principle II), and the engine must not decide
how a document was addressed. The split also lets the application layer expose the same answers for a
path, a stream and a byte array without a second implementation.

**Alternatives considered**: a single port taking `Stream` in the domain (rejected: `Stream` is I/O in
the domain and would let callers bypass source discipline); a single port taking a path (rejected:
excludes byte arrays and streams, which the rest of the library already accepts).

## D3. One payload rule for every read that returns bytes

**Decision**: `PdfEngineOptions.MaxInlineContentBytes` (256 KiB by default) is the only budget.
`Data` carries the payload exactly when the payload is at most the budget; above it, `Data` is `null`,
`IsBuffered` is `false`, `ByteLength` is still the true length, and **no warning is recorded**. `0`
means metadata only. A negative value is rejected by `PdfEngineOptions.Validate()` before a document
is opened. The copy routes (`CopyImageDataAsync`, `CopyImageMaskDataAsync`, `CopyEmbeddedFileDataAsync`,
and the extension methods over them) produce the same bytes for any size.

**Rationale**: three independent limits — one per capability — is how a report silently disagrees with
itself. One budget makes the answer predictable, and "withheld on purpose" is distinguishable from
"absent" without reading the warning list, because `IsBuffered` and `ByteLength` are both truthful.
Recording a warning would be a lie: nothing failed.

**Alternatives considered**: always buffer (rejected: enumerating a scanned document loads every
scan); never buffer (rejected: every caller pays a copy for an icon); a byte-count cap that truncates
(rejected: a truncated payload presented as a payload is worse than an absent one); a warning when
the budget bites (rejected: it turns a documented decision into noise and makes "no warnings" mean
less).

## D4. An image is identified by the object that holds it

**Decision**: the identifier is `"<object> <generation>"` (the form a PDF uses), built once per object
and used as the key of a dictionary. An image drawn four times on one page, or by two pages of the
range, is one entry whose `Pages` is the distinct, ascending list of pages that draw it and whose
`Position` is the transform of the **first** draw. An image with no reachable object — inline
(`BI … ID … EI`) or a direct resource value — gets a synthetic identifier: `inline:<page>:<ordinal>`
where the ordinal counts inline images on that page, or `resource:<page>:<ordinal>`.

**Rationale**: de-duplication is what makes the answer usable; a shared logo repeated for every page
is noise, and a caller extracting assets would write the same file twenty times. Object identity is
the document's own identity, so it is stable across reads. The synthetic identifiers keep every entry
addressable, which matters because the copy routes take an entry and must find it again.

**Alternatives considered**: one entry per draw (rejected: duplicates, and no way to ask "how large is
this document's imagery"); hashing the payload (rejected: two identical images are still two objects,
and hashing costs a full read); identifier `ObjectNumber` alone (rejected: a reused object number in
different generations would collide).

## D5. Ordering is insertion order, never hash order

**Decision**: the walk keeps a `List<ImageEntry>` for order and a `Dictionary<string, ImageEntry>` for
identity, and emits in the order the document first draws each image. `Pages` is appended in visit
order and sorted before it is exposed. Every other collection is built in document order; warnings are
appended in the order the conditions are met.

**Rationale**: determinism is a contract (the same document must yield the same report on the next run
and in the next process), and hash order is not a contract even when it is stable today. Sort at the
edge, once, where the ordering is stated.

**Alternatives considered**: `HashSet` and `Dictionary` enumeration (rejected: order depends on
insertion and hash collisions, and a rehash can change it); sorting images by identifier (rejected:
identifiers are numbers with no visual meaning, and the drawing order is the only order a caller can
reason about).

## D6. Page detail is an iterator, and the caller's stop is the stop

**Decision**: `ReadPageDetailsAsync` is an `async IAsyncEnumerable<PdfPageDetail>` that opens the
document, builds the named-destination index, the page map and the form field list **once**, then
walks only the selected pages and yields each record before reading the next. Cancellation is checked
per page. Form-level warnings travel with the first yielded page, because they belong to no page.

**Rationale**: an index over a long document must not scale with the document; a caller that stops
after page one must not pay for page 200. Opening and indexing once keeps per-page cost to the page
itself, and the page map/destination index would otherwise be rebuilt per page.

**Alternatives considered**: returning `PdfReadResult<PdfPageDetail>` with every page (rejected: the
memory the feature exists to bound); re-opening per page (rejected: parsing the file per page);
`IEnumerable` with sync-over-async (rejected: the engine reads asynchronously and blocking here is how
a library deadlocks a UI thread).

## D7. Images are per page inside page detail, and de-duplicated across a range read

**Decision**: `ReadImagesAsync` walks the whole range with one `ImageWalker`, so an object shared by
pages 2 and 5 appears once with both pages. `ReadPageDetailsAsync` creates one walker per page, so a
page's `Images` is what *that page* draws, even if another page draws the same object.

**Rationale**: the two questions are different. "Which images does this document paint, and where" is
a document question, so de-duplication is the answer. "What does page 7 paint" is a page question, and
omitting an image because page 3 drew it first would make the page's own detail a lie.

**Alternatives considered**: sharing one walker across the iterator (rejected: it would have to retain
every image seen so far, defeating the one-page-at-a-time guarantee, and would give page 7 a record
with no image on it).

## D8. Payload bytes are decoded only where the library already decodes

**Decision**: `FlateDecode` (with or without a predictor), `ASCIIHexDecode` and `ASCII85Decode` are
decoded to the samples they describe; a single `DCTDecode` as the last filter is passed through
untouched, because a JPEG is already a format the caller reads; every other filter (`JPXDecode`,
`JBIG2Decode`, `CCITTFaxDecode`, `LZWDecode`, `RunLengthDecode` and any unknown name) leaves
`IsDecodable` cleared, returns the stored bytes and records one `ImageFilterUnsupported` warning
naming the filter. While decoding, the payload is counted to its full length even when only the first
budget-sized bytes are kept, so `ByteLength` stays truthful without materialising a decompression
bomb.

**Rationale**: Principle III forbids re-encoding what we cannot reproduce. Decoding is not
re-encoding: the decoded samples are the image the document means, but re-compressing them would
produce a different file. Pass-through is therefore the default for an already-compressed image
format, and refusal is explicit rather than silent.

**Alternatives considered**: always return raw stream bytes (rejected: a Flate-compressed scan would
arrive compressed, which no caller expects from "the image"); always decode and reject unsupported
filters (rejected: an unsupported image would disappear from the report, and "not decodable" is
information); rasterise to pixels (rejected: rendering is out of scope, and a decoder is not a
renderer).

## D9. One destination value, resolved once per read

**Decision**: a link or bookmark target is always a `PdfDestination`. A destination the document
states directly and one it states by name resolve to the same shape, so `PageNumber` is set and
`NamedDestination` records the name that was used. An external target keeps `Uri` or `FileName` as
written. A name that does not resolve leaves `PageNumber` null, keeps `NamedDestination`, reports
`IsUnresolved`, and records exactly one `NamedDestinationUnresolved` warning per occurrence. Page
numbers are one-based.

**Rationale**: a caller publishing links should not have to implement the name tree. Keeping the
unresolved name makes the warning actionable ("this name is missing", not "something is missing"),
and preserving the entry keeps the outline navigable where the document itself is not.

**Alternatives considered**: dropping an unresolved annotation or bookmark (rejected: it hides a
defect and shortens a list a caller is counting); resolving lazily on property access (rejected:
resolution needs the document, which a record must not hold); exposing the raw destination array
(rejected: it hands PDF syntax to a caller the domain is meant to protect from it).

## D10. Damage is described through warnings; exceptions are for caller mistakes

**Decision**: every condition that does not stop a read is a `PdfWarning` with a kind from an
append-only `PdfWarningKind`, one warning per condition, message prose that names the source and the
object or page. A damaged stream, an unsupported filter, an unreadable annotation, field or
attachment, a malformed XMP packet, a missing XObject and an unresolved name all follow that route.
Exceptions are reserved for a caller's mistake (a null source, a page range below one) and for content
that is not a PDF at all.

**Rationale**: the repository's diagnostics rule is that a document being damaged is data, not an
error: a caller merging twenty files cannot be stopped by the nineteenth, and a warning whose kind can
be matched on is something a caller can act on. A message that may be reworded is not something a
caller can match on, which is why the kind is the contract.

**Alternatives considered**: throwing an aggregate exception (rejected: one bad page would hide
nineteen good ones); returning `null` items (rejected: a caller cannot tell "absent" from "unreadable",
which the specification makes a success criterion); string-typed warning codes (rejected: typos and
renames become silent behaviour changes).

## D11. Encryption is the same path, not a second one

**Decision**: the reader opens a document through the same `PdfFileReader` the inspection and merge
paths use, passing the password through. A correct user or owner password yields the same answers as
the unencrypted equivalent. A document that cannot be opened yields empty items plus one warning that
says why — never an exception — except for a file that is not a PDF, which raises. Page details are
the one place where the items are not empty: the page tree is not encrypted, so the pages are still
yielded, each with a `ContentStreamUnreadable` warning in place of its content.

**Rationale**: a second decryption path would be a second place for the standard security handler to
disagree with itself, and the repository already has one that is tested per revision. "Empty plus a
warning" is what lets a batch job continue and report.

**Alternatives considered**: throwing an encryption exception (rejected: the library already answers
"is a password what stands in the way" through `PdfDocumentInfo.RequiresPassword`, and a second
vocabulary for the same situation is worse than none); pre-decrypting to a temporary file (rejected:
the no-temporary-file and read-only guarantees).

## D12. Every returned byte is owned by the record, and copies are bounded

**Decision**: a buffered payload is materialised into a byte array the record owns, so it stays valid
after the read, after the source is closed and after later reads; no pooled buffer is ever exposed.
The copy route reads through a fixed 64 KiB buffer straight into the caller's stream, flushes it and
leaves it open. Neither route closes or disposes a stream the caller owns.

**Rationale**: an array a pool takes back is a payload that changes under the caller — the worst kind
of bug, because it is intermittent and depends on unrelated traffic. A fixed buffer keeps a 50 MB copy
from becoming a 50 MB allocation, which is what the flat-memory criterion measures.

**Alternatives considered**: `ArrayPool` for the returned payload (rejected: aliasing, and a caller
cannot know when to return it); buffering the whole payload then writing (rejected: it defeats the
reason the route exists); returning a `Stream` handle (rejected: engine state would outlive the call
and the disposal contract would leak into the API).

## D13. Tests assert bytes and lengths, not "not null"

**Decision**: the feature is covered by xUnit v3 tests that run on both legs; fixtures are written by
`PdfContentFixtures` at test time rather than committed as binaries; each capability has a partial
class of its own; the budget boundary is tested at exactly the budget, one byte above, at zero and at
a negative value; sources are tested for parity (file, stream and byte array give the same answer);
and a determinism test compares two reads field by field, including payload bytes.

**Rationale**: a test that asserts "the list is not empty" passes on a reader that returns garbage.
Asserting the exact stored bytes is what turns "never re-encode" and "byte-exact XMP" into tests that
fail when the behaviour is reverted. Generating fixtures keeps cross-reference offsets out of Git and
makes a damaged fixture intentional rather than an accident of hand editing.

**Alternatives considered**: committed binary fixtures (rejected: opaque, and an edit that changes
offsets breaks unrelated tests); snapshot files for whole records (rejected: a reworded warning message
would fail every test, which the warning contract forbids); mocking the parser (rejected: the value of
these tests is that real bytes are really parsed).

## Open Questions

None. Every `NEEDS CLARIFICATION` the specification raised was answered by it — the read surface is
additive, the page range is a `PageRange`, the warning kinds are named, and the boundary excludes
reflowing or reconstructing the text a page draws, rendering, form filling, signing and repair. Reading
the text itself was added after this research was written and is stated in FR-040 to FR-046. The
specification's *Assumptions* section holds the remaining judgement calls (inline-image identifiers,
first-draw geometry, unresolved names, source-level memory), and each is a documented decision rather
than a gap.

## Consequences

- The public surface grows by one port, fourteen read models (eleven plus the text block, word and
  glyph), one mask, one destination, two result shapes, an options property and eleven warning kinds;
  all of them are in `PublicAPI.Unshipped.txt`, and none is promoted to `Shipped` before the release
  that carries it.
- The documentation has to stop describing a single read surface; the concrete edits are listed in
  [plan.md](./plan.md#documentation-follow-ups).
- Performance claims about the feature wait for numbers in `docs/measurements.md`; until then the
  design only claims the shape of the memory (one page, one fixed copy buffer), not a figure.
