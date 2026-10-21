# Feature Specification: PDF Content Inspection

**Feature Branch**: `001-pdf-content-inspection`

**Created**: 2026-10-20

**Status**: Draft

**Input**: User description: "Reading what a document already contains — every image a page
range paints, annotations and links, the interactive form, the XMP packet, embedded files, the
bookmark outline and one page's detail at a time — without changing the document, under one
payload rule that keeps large payloads out of memory unless the caller asks for them."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See every image a document paints (Priority: P1)

A caller that has to classify, extract or cost a document asks a page range for its images. It
receives one entry per distinct image object, not one per draw: the pages of the range that draw
it, the area of the first draw, the stored dimensions, colour space and filter, and — when the
payload fits the inline budget — the bytes exactly as the file holds them. A JPEG is handed back
untouched, never re-encoded. A transparency mask arrives beside the samples rather than blended
into them.

**Why this priority**: Images dominate a document's size and are the part a caller most often
needs to measure or extract. The de-duplication is what makes the answer usable at all; a report
that repeats a shared logo for every page it appears on is noise.

**Independent Test**: Read a fixture in which one image object is drawn on two pages and another
is stored as a JPEG. Expect two entries, one naming both pages with its first-draw position, and
JPEG bytes identical to the stored stream.

**Acceptance Scenarios**:

1. **Given** a document where one image is drawn on pages 2 and 5, **When** the caller reads
   images for pages 1–5, **Then** exactly one entry is returned whose pages are 2 and 5 and whose
   position is the first draw.
2. **Given** an image stored with a pass-through compression, **When** it is read, **Then** the
   bytes are byte-identical to what the file stores and remain valid after the source is gone.
3. **Given** an image stored with a filter the library does not decode, **When** the range is
   read, **Then** that entry is returned marked as not decodable with exactly one warning naming
   the filter, and every other image still arrives.
4. **Given** a range that starts after the last page, **When** it is read, **Then** the result is
   empty and no exception is raised.
5. **Given** an image with an associated soft mask, **When** it is read, **Then** the mask is
   reported separately from the colour samples.

---

### User Story 2 - Read what reviewers wrote on the pages (Priority: P2)

A caller collecting review state asks for the annotations of a page range and receives every
comment, highlight, stamp and link with its page, subtype, rectangle, flags, text, author and
modification date. A link resolves to the page it points at — whether the target is stored
directly or by name — or to the web address it names. A name that does not resolve is reported
and the annotation is still returned.

**Why this priority**: Review traffic and link integrity are the second question every document
workflow asks, and they are answerable without any of the other capabilities.

**Independent Test**: Read a fixture carrying a highlight, a text note and a link with a named
destination. Expect three annotations, resolved target pages, and one warning for the destination
that was deliberately broken.

**Acceptance Scenarios**:

1. **Given** a page with a highlight, **When** its annotations are read, **Then** the entry
   carries the text, author, rectangle and modification date of that annotation.
2. **Given** a link stored with a named destination, **When** it is read, **Then** the target is
   resolved to the page the name designates.
3. **Given** a link with an external target, **When** it is read, **Then** the address is
   returned as stated.
4. **Given** an annotation whose named destination is absent, **When** the range is read,
   **Then** the annotation is returned without a target and one warning names the omission.
5. **Given** an annotation whose subtype the library does not model, **When** it is read,
   **Then** the original subtype is preserved rather than normalised away.

---

### User Story 3 - Read the interactive form without filling it (Priority: P3)

A caller inventories signatures and field values before archiving a document. It asks for the
form and receives every field with its fully-qualified name, type, current value, default value,
read-only and required flags, and every widget belonging to it with its page and rectangle. A
signature field is reported as a field; the signature is not interpreted. Nothing is filled in.

**Why this priority**: Forms are where documents carry obligation, so a caller has to be able to
list them, but a formless document must not fail and no read may alter the form.

**Independent Test**: Read a fixture with a two-level field hierarchy, a field with two widgets
and a signature field. Expect fully-qualified names, both widgets with their pages, and no value
written to any field.

**Acceptance Scenarios**:

1. **Given** a nested form, **When** fields are read, **Then** each name is the fully-qualified
   path through the field hierarchy.
2. **Given** a field with two widgets on different pages, **When** fields are read, **Then**
   both widgets are returned with their page numbers and rectangles.
3. **Given** a signature field, **When** fields are read, **Then** it appears as an ordinary
   field with its type, and the source is unchanged afterwards.
4. **Given** a field with no value, **When** it is read, **Then** its value is reported as absent
   rather than as an empty string.
5. **Given** a document with no form, **When** fields are read, **Then** the result is empty and
   no warning is recorded.

---

### User Story 4 - Index a long document one page at a time (Priority: P4)

A caller indexing a 200-page document asks for page detail and receives it as a stream: one
record per page, in page order, carrying the page's size, rotation, display size, its images, its
annotations and links, the form fields with a widget on it, and the warnings that belong to that
page alone. A caller that stops after the first page never pays for the other 199.

**Why this priority**: This is the difference between an index that runs on a large document and
one that runs out of memory, and it is the only capability that composes the others.

**Independent Test**: Enumerate detail for a 200-page fixture, stop after page 1, and confirm
that only one page's records were produced and that a blank page still appears with empty
collections.

**Acceptance Scenarios**:

1. **Given** a range of five pages, **When** detail is enumerated, **Then** five records arrive
   in ascending page order with one-based page numbers.
2. **Given** a page with nothing on it, **When** detail is enumerated, **Then** that page is still
   yielded with empty collections so later page numbers do not shift.
3. **Given** a page whose image is damaged, **When** detail is enumerated, **Then** the page is
   yielded with what could be read and a warning attributed to that page.
4. **Given** an enumeration abandoned after the first page, **When** it is disposed, **Then** the
   remaining pages were never read.
5. **Given** a page carrying form widgets, **When** detail is enumerated, **Then** those fields
   are included on the page rather than only in the document-level form read.

---

### User Story 5 - Read the bookmark tree (Priority: P5)

A caller building navigation asks for the outline and receives the top-level bookmarks in
document order, each with its title, its target and its children nested beneath it. A branch the
file stores as closed is reported as closed. An external bookmark is reported as external. A
document without an outline yields an empty result.

**Why this priority**: Navigation is a smaller slice of the value than page content, but it is
independent of it and often the only structure a reader preserves.

**Independent Test**: Read a fixture with two top-level bookmarks, one of them closed with a
child and one pointing at a web address. Expect the tree in order, the closed state preserved and
the external target returned as stated.

**Acceptance Scenarios**:

1. **Given** an outline with nested children, **When** it is read, **Then** children appear under
   their parent in document order.
2. **Given** a bookmark with a named destination, **When** it is read, **Then** the destination
   resolves to a page.
3. **Given** a bookmark whose named destination does not resolve, **When** it is read, **Then**
   the bookmark is returned without a target and one warning names the omission.
4. **Given** a document without an outline, **When** it is read, **Then** the result is empty.

---

### User Story 6 - Take the files a document carries (Priority: P6)

A caller auditing attachments asks for the embedded files and receives one entry per file in the
order the document states them, each with a distinct identifier, its name, MIME type,
relationship, modification date and true byte length. The bytes come back exactly as the file
holds them once decompressed, either inline or written to a destination the caller supplies.

**Why this priority**: Attachments are usually small and few, but when they exist they are the
reason the document was sent, and the identifier guarantee is what lets a caller address one.

**Independent Test**: Read a fixture with two attachments, one named twice, and extract both.
Expect distinct identifiers, a collision warning for the duplicate name, and bytes identical to
the originals.

**Acceptance Scenarios**:

1. **Given** two attachments, **When** they are read, **Then** each has an identifier different
   from the other's and the document's stated order is preserved.
2. **Given** two entries with the same filename, **When** they are read, **Then** each still has
   a distinct identifier and one warning records the collision.
3. **Given** an attachment larger than the inline budget, **When** it is extracted through the
   copy route, **Then** the destination receives the exact bytes and is left open.
4. **Given** a document without attachments, **When** they are read, **Then** the result is
   empty.

---

### User Story 7 - Read the document's own metadata (Priority: P7)

A caller reading provenance asks for the XMP packet and receives it byte-exact, with the common
descriptive, document and tool values parsed beside it. A document that carries no packet yields
no metadata rather than an error; a packet that is not well formed is still returned, with the
values that could be read and a warning that names the problem.

**Why this priority**: Metadata is cheap to expose and matters for provenance, but nothing else
depends on it.

**Independent Test**: Read a fixture whose packet is known, compare the returned packet with the
stored bytes, and confirm the parsed values are present; then read a fixture with no packet and
expect no metadata.

**Acceptance Scenarios**:

1. **Given** a document with a well-formed packet, **When** metadata is read, **Then** the packet
   is byte-identical to the stored one and its parsed values are available beside it.
2. **Given** a document without a packet, **When** metadata is read, **Then** no metadata is
   returned and no warning is recorded.
3. **Given** a packet that is not well-formed, **When** metadata is read, **Then** the bytes are
   still returned and one warning names the malformation.

---

### Edge Cases

- **A range that names no existing page**: the read returns what exists and nothing more; it does
  not raise, and a range wholly past the last page yields an empty result.
- **A range that mixes readable and damaged pages**: readable pages arrive in full, damaged pages
  arrive with what could be read and their own warnings; one bad page never truncates the range.
- **A document that is encrypted**: a correct user or owner password yields the same answers as
  the unencrypted equivalent; a missing or wrong password never raises — the collection reads return
  empty results with one warning that says why, and a page enumeration still yields the pages the page
  tree describes, each marked where its content should have been.
- **Content that is not a PDF at all**: a format error is raised, because there is nothing to
  describe.
- **An asset shared by many pages**: it is reported once, and the entry names every page of the
  requested range that draws it.
- **A payload exactly at the inline budget**: it stays inline; one byte above it does not, and
  the length reported remains truthful in both cases.
- **An inline budget of zero**: every read returns metadata only, and no read reports a failure
  for the payload it deliberately withheld.
- **A negative inline budget**: rejected when the configuration is validated, before any document
  is opened.
- **A caller that abandons a page enumeration early**: pages not yet asked for are never read and
  no warning is fabricated for them.
- **A source the caller keeps open**: it is left unmodified and undisposed; a destination the
  caller supplies is flushed and left open.
- **The same document read twice, or on two runs**: every ordering, identifier, byte sequence and
  warning is identical.
- **A read that is cancelled**: it stops promptly and reports cancellation rather than a partial
  result dressed as a complete one.

---

## Requirements *(mandatory)*

The requirements below are stated in terms of what a caller observes. They apply identically to
every content source the library accepts (a stream, a byte buffer or a file path) and to every
document that can be opened, encrypted or not.

### Functional Requirements

- **FR-001**: Page numbers MUST be one-based everywhere a page is named, and a page range that
  extends past the last page MUST be read as the pages that exist, without raising.
- **FR-002**: Reading images for a range MUST return one entry per distinct image object, in the
  order the document draws them first, and each entry MUST name every page of the range that
  draws it.
- **FR-003**: An image entry MUST identify the object it came from, and MUST carry a stable
  synthetic identifier for an image embedded directly in page content rather than as a separate
  object, so that every entry can be addressed.
- **FR-004**: An image entry MUST report the stored dimensions, bits per component, colour space
  and filter name, reporting an unknown value as absent rather than guessing one.
- **FR-005**: An image stored with a compression the library passes through MUST be returned with
  the bytes exactly as stored, never re-encoded.
- **FR-006**: An image entry MUST report the area of its first draw within the requested range.
- **FR-007**: An image's associated soft mask MUST be reported as a distinct value beside the
  colour samples, never merged into them.
- **FR-008**: An image stored with a filter the library does not decode MUST still be returned,
  marked as not decodable, with exactly one warning that names the filter; the remaining images
  MUST still be returned.
- **FR-009**: An image whose stored data is damaged MUST be returned with what could be read, a
  truthful byte length and a warning, and MUST NOT abort the rest of the read.
- **FR-010**: Only the pages of the requested range MUST be inspected when reading images.
- **FR-011**: Reading annotations for a range MUST return every annotation of every page in the
  range, in page order and then in the order the page states them, each with its page number,
  subtype, rectangle, flags, text, author, modification date and whether an appearance exists.
- **FR-012**: A link target MUST be resolved to a page whether the document stores it directly or
  by name, and an external target MUST be returned as the address it states.
- **FR-013**: A target name that does not resolve MUST leave the annotation or bookmark in the
  result without a target and MUST record exactly one warning naming the omission.
- **FR-014**: An annotation subtype the library does not model MUST be preserved verbatim rather
  than normalised to a generic value.
- **FR-015**: Reading the form MUST return every field with its fully-qualified name through the
  field hierarchy, its type, its current value, its default value, its flags and every widget
  belonging to it with the widget's page and rectangle.
- **FR-016**: Reading the form MUST be read-only: no value is written, no field is flattened and
  no signature is interpreted; a signature field is reported as a field of its type.
- **FR-017**: A document without a form MUST yield an empty result and no warning.
- **FR-018**: Reading metadata MUST return the XMP packet byte-exact together with the descriptive,
  document and tool values parsed from it.
- **FR-019**: A document without an XMP packet MUST yield no metadata and no warning.
- **FR-020**: A packet that is not well formed MUST still be returned with its bytes intact, with
  the values that could be parsed and with one warning naming the malformation.
- **FR-021**: Reading embedded files MUST return one entry per file in the order the document
  states them, each with its name, MIME type, relationship, modification date and true byte
  length.
- **FR-022**: Every embedded-file entry MUST have an identifier distinct from every other entry's,
  including when two entries share a filename; a shared filename MUST also be recorded as one
  warning.
- **FR-023**: An embedded file's bytes MUST be returned exactly as stored once decompressed, and
  MUST remain valid after the source is closed.
- **FR-024**: Reading the outline MUST return the top-level bookmarks in document order with their
  children nested beneath them, each with its title, its resolved target and the open or closed
  state the document stores.
- **FR-025**: A bookmark that points outside the document MUST be reported as external with the
  address it states, and a document without an outline MUST yield an empty result.
- **FR-026**: Reading page detail MUST yield exactly one record per page of the range in ascending
  order, one page at a time, so that the memory a caller commits is one page's worth and a caller
  that stops early never causes the remaining pages to be read.
- **FR-027**: A page detail record MUST carry the page number, the stored size, the rotation, the
  display size with rotation applied, the images the page draws, its annotations including links,
  the form fields with a widget on that page, and the warnings that belong to that page alone.
- **FR-028**: A page with nothing on it MUST still be yielded, with empty collections, so that page
  numbers never shift.
- **FR-029**: A page that could not be read in full MUST still be yielded with whatever could be
  read and with its own warnings.
- **FR-030**: A record MUST carry its payload inline only within a configurable inline budget
  (256 KiB by default); above that budget the payload MUST be absent, the reported byte length
  MUST remain truthful, the record MUST say that it holds no payload, and no warning is recorded
  because nothing failed.
- **FR-031**: An inline budget of zero MUST make every read return metadata only, and a negative
  budget MUST be rejected when the configuration is validated, before any document is opened.
- **FR-032**: For any payload, whatever its size, the caller MUST be able to have it written to a
  destination stream it supplies, with the bytes identical to what an inline read would return;
  the destination is flushed and left open.
- **FR-033**: A payload returned to the caller MUST remain valid and unchanged after the read that
  produced it completes and after later reads, and MUST NOT alias memory the library reuses.
- **FR-034**: No read MUST modify the source, dispose a source the caller owns, or write to a file
  or open a destination the caller did not supply.
- **FR-035**: Unusual or damaged but readable content MUST be described through warnings in the
  result rather than raised as an exception; only content that is not a PDF at all MUST raise a
  format error.
- **FR-036**: A document that is encrypted MUST be readable with either its user or its owner
  password and MUST yield the same answers as its unencrypted equivalent; one that cannot be
  opened MUST report the reason as data and MUST NOT raise: a collection read yields an empty result
  plus one warning that says why, and a page enumeration yields the pages the page tree still
  describes, each with a warning in place of the content it could not decode.
- **FR-037**: The same document read twice, or on two runs, MUST yield identical items, orderings,
  identifiers, bytes and warnings.
- **FR-038**: Every read MUST honour cancellation promptly, including during a page enumeration,
  and MUST report cancellation rather than returning a partial result as if complete.
- **FR-039**: Every failure that is reported as a warning MUST name its cause with a distinct kind,
  so that callers can tell an undecodable filter from a damaged payload, a missing resource from
  an unresolved name, a duplicate filename from a malformed packet, and an unreadable annotation,
  form field or embedded file from a clean read.
- **FR-040**: Reading text for a range MUST return the text the pages draw as blocks of words of
  glyphs, ordered down the page and then along the line, each block carrying its page, its one-based
  reading order on that page, its text, its line count, its bounding box and its words. *(Added
  2026-10-21.)*
- **FR-041**: A block MUST keep the geometry of what it read: every word carries its text, page,
  rectangle and glyphs, and every glyph its decoded character, position, width, font size and font
  name, placed as the text and graphics state draw it — including a form's `/Matrix` and the state
  `q` and `Q` save and restore.
- **FR-042**: A character MUST come from the font and never from the raw code byte: the base
  encoding and `/Differences` glyph names for a simple font, the `/ToUnicode` map for a composite
  font, and the width from `/Widths`, `/W` or `/MissingWidth` rather than from a guess.
- **FR-043**: A code the font does not map MUST be skipped and named in exactly one `TextUnreadable`
  warning per font per page; the blocks of the fonts that did read MUST still be returned.
- **FR-044**: A document that draws no text MUST yield an empty result and no warning, so that "the
  page has no text" and "the page's text could not be read" remain distinguishable.
- **FR-045**: Word, line and block boundaries MUST be decided from geometry stated in ems, so the
  rule scales with the font size; no dictionary, font program, rendering or appearance stream is
  consulted.
- **FR-046**: Reading text MUST NOT reflow, hyphenate or reconstruct columns and tables: the read
  reports the blocks and their coordinates, and any layout decision beyond that belongs to the
  caller.

### Key Entities *(include if data involved)*

- **Read result**: what a document-level read produced — the items found, in document order, and
  the warnings raised along the way. "Nothing was found" and "nothing could be read" must remain
  distinguishable.
- **Content source**: the document being read, addressed as a stream, a byte buffer or a file path;
  it is never modified and never disposed by the library.
- **Page range**: the inclusive, one-based interval of pages a read applies to.
- **Image entry**: one distinct image object — its identity, the pages drawing it, its stored
  dimensions, bits per component, colour space, filter, decodability, byte length, whether it is
  buffered, its payload when within budget, its optional soft mask, and the area of its first draw.
- **Annotation entry**: one annotation — its page, subtype, rectangle, flags, text, author,
  modification date, appearance availability, its name where the document gives one, and its
  target when it is a link.
- **Destination**: where a link or bookmark points — an internal page with the view the document
  states, or an external address.
- **Form field**: one interactive field — its fully-qualified name, type, current value, default
  value, flags including read-only and required, and its widgets.
- **Widget**: one visual instance of a field — the page it sits on and its rectangle.
- **Text block**: one paragraph a page draws — its page, its one-based reading order on that page, its
  text (lines joined with a newline, words with a space), its line count, its bounding box and its
  words, in reading order.
- **Word**: a run of glyphs that touch on one baseline — its text, page, rectangle and glyphs, and
  from its first glyph its font name, font size and baseline.
- **Glyph**: one decoded character as drawn — its text, page, x, y, width, font size, font name and
  base font. The text is the font's answer for the code, never the code byte itself.
- **Embedded file entry**: one attachment — a distinct identifier, its name, MIME type,
  relationship, modification date, filter, decodability, byte length, whether it is buffered, and
  its payload when within budget.
- **Outline item**: one bookmark — its title, its resolved destination, whether the branch is open,
  and its children in document order.
- **Page detail**: everything known about one page — its number, stored size, rotation, display
  size, images, annotations, form fields and its own warnings.
- **Metadata packet**: the document's XMP packet, byte-exact, with the descriptive, document and
  tool values parsed from it.
- **Warning**: a remark that did not stop a read — a kind that names the cause and a message that
  locates it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A caller can answer "which images does this document draw, which pages share each,
  and how large is each" for any page range in a single call, with 0 duplicate entries for an
  image drawn more than once.
- **SC-002**: 100% of pass-through image payloads and 100% of embedded-file payloads returned by a
  read compare byte-identical with the bytes the file stores; 0% are re-encoded.
- **SC-003**: 100% of the unusual and damaged fixtures complete a read — 0 exceptions — and each
  reports its cause: at least one warning naming the specific problem.
- **SC-004**: Indexing a 200-page document holds one page at a time: consuming all 200 pages of a
  fixture peaks no more than 10% above stopping at page 20, and the enumeration retains no page's
  images, annotations or fields once that page has been yielded.
- **SC-005**: Extracting payloads of 1 MB, 10 MB and 50 MB keeps working memory flat — the peak
  difference across the three sizes stays under 5% — while wall-clock time grows no faster than
  the payload size.
- **SC-006**: A read touches only the pages of its range — 0 pages outside it are read — and a
  caller that stops a page enumeration after the first page leaves the remaining pages unread, so
  the work grows with the pages consumed rather than with the document's page count.
- **SC-007**: Reading the same document twice, and in a second process run, produces identical
  items, orderings, identifiers, payloads and warning sets in 100% of cases.
- **SC-008**: 0% of reads change their source: every fixture is byte-identical before and after
  the full suite runs.
- **SC-009**: 100% of openable encrypted fixtures yield the same answers with the correct password
  as their unencrypted equivalent, and 100% of the cases that cannot be opened report the reason
  as data while 0% raise.
- **SC-010**: For 100% of fixtures — payloads below, exactly at and above the inline budget — the
  inline route and the streaming route deliver identical bytes.
- **SC-011**: A caller that needs a page's images, annotations and form fields for indexing makes
  one call per page rather than one call per capability, and receives the page's warnings with it.
- **SC-012**: A caller can tell "the document has none" from "the document has some that could not
  be read" in 100% of cases, using the result alone.
- **SC-013**: For 100% of the text fixtures, every word a page draws appears exactly once in the text
  of its blocks, in reading order, and a page whose font cannot be decoded yields fewer words plus
  one `TextUnreadable` warning naming the font rather than invented characters.

## Assumptions

- **This is a retrospective specification.** The capability is already implemented in the working
  tree and described under the changelog's unreleased section; this document records its intent
  and acceptance criteria so later changes have a baseline, and does not ask for new
  construction.
- **Reachability**: callers obtain the reader through the library's existing registration or by
  constructing it directly; no new packaging or distribution change is implied.
- **Payload**: a payload is the bytes stored for an image or an embedded file, after any
  compression the library must undo. Decoding to raw pixels is offered only where the library
  already decodes the filter; the specification never promises rendering.
- **Out of scope**: reflowing or reconstructing the layout of the text a page draws — columns,
  tables, hyphenation, a document flow — optical recognition of scans, rendering or thumbnailing,
  filling, flattening or signing forms, validating signatures, decrypting algorithms the library
  does not support, and repairing or writing a document. Reading the text a page draws is in scope
  and stated in FR-040 to FR-046.
- **Input expectations**: documents are assumed to conform to the parts of the format this library
  supports. Unsupported encryption, unsupported filters and damage inside an otherwise readable
  file are handled as described above, not as errors.
- **Identifier stability**: identifiers are stable for the same bytes across reads and runs, but
  are identifiers of a read, not global names; callers must not persist them as document identity.
- **Inline budget**: 256 KiB is the product default; zero means metadata only; the value is
  configuration, so changing it is a deployment decision rather than a new feature.
- **Page numbering** is one-based and inclusive, consistent with the rest of the product.
- **Warning text** is informative and may be reworded; the kind of a warning is the part callers
  may rely on and is therefore part of this specification.
- **Relationship to the writer**: this feature reads; every guarantee about output, layout and
  determinism of generated files belongs to the writing capabilities and is unaffected.
