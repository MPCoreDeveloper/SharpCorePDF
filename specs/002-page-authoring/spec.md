# Feature Specification: Authoring a Page

**Feature Branch**: `002-page-authoring`

**Created**: 2026-10-21

**Status**: Implemented

**Input**: User description: "Generate a one-page A4 PDF — a few text lines plus a PNG logo — and then
merge it with other PDFs, without PdfSharpCore. Create pages, draw text with a standard or an embedded
TrueType font, draw a PNG, save to a stream or bytes, and use the result directly as a merge source.
Text must round-trip, including `é è à ç ô ï` and an apostrophe; the output must be deterministic and
pass qpdf, pypdf and a rendered check."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Put a cover in front of a document (Priority: P1)

An application generates a cover page for every document: a logo top left, an address block, two lines
of localized text and a fixed marker line. It writes the page in memory, merges it in front of the
source document and returns one file, with no temporary file and no third-party PDF engine.

**Why this priority**: This is the whole reason the feature exists. The merge side already works; the
missing step is the page it merges.

**Independent Test**: Build an A4 cover with a 152 × 87 PNG and the six text lines of the issue, merge
it with a two-page fixture, and inspect the result: three pages, page 1 is A4, the text of page 1 is
the cover text, and the logo is an image XObject with the requested position and size.

**Acceptance Scenarios**:

1. **Given** no document at all, **When** a caller adds one A4 page and builds, **Then** the result is
   a readable PDF whose page size is 595.276 × 841.89 points.
2. **Given** a cover page and a two-page source, **When** the cover is put first in a `MergePlan`,
   **Then** the result has three pages, in that order, and page 1 carries the cover text.
3. **Given** an RGBA PNG, **When** it is drawn, **Then** the image is present with a `/SMask` and the
   pixels at the requested position are not blank.
4. **Given** a fixed creation date and document identifier, **When** the same pages are built twice,
   **Then** the two files are byte-identical.

### User Story 2 - Text that comes back out (Priority: P2)

A cover is written in French, with `é è à ç ô ï` and an apostrophe. A caller extracts the text later
with `pypdf`, `pdftotext` or `IPdfContentReader.ReadTextAsync` and expects exactly the characters that
were drawn, not a picture of them.

**Why this priority**: A page whose text cannot be read back is not searchable, not translatable and
not testable. Glyph ids alone would satisfy a viewer and fail every consumer of the text.

**Independent Test**: Draw the accent line with a standard font and with an embedded DejaVu subset;
extract page 1 with the library and with `pypdf`; expect the same string from both.

**Acceptance Scenarios**:

1. **Given** a standard font, **When** the text is `é è à ç ô ï ' / |`, **Then** every character is
   extracted unchanged.
2. **Given** a character outside Windows-1252 and a standard font, **When** the document is built,
   **Then** the build is refused with the character and its code point named.
3. **Given** an embedded TrueType font, **When** the page is written, **Then** the font is a subset
   with a `/ToUnicode` map and extracts the same text.
4. **Given** a container with no fonts installed, **When** the page is built, **Then** the result is
   identical: no system font is consulted.

### User Story 3 - A logo without an imaging dependency (Priority: P3)

The logo is a 152 × 87 PNG, eight-bit RGBA with `sRGB gAMA pHYs iTXt IDAT IEND` chunks. The library
must embed it without ImageSharp or another imaging package, and must keep the alpha channel.

**Why this priority**: The package must stay dependency-free, and the logo is the part of the page the
application does not draw itself.

**Independent Test**: Draw the PNG and read the image back: width and height in samples, `/SMask`
present, `Position` equal to the requested rectangle; render the page and check one pixel inside the
logo and one in its transparent margin.

**Acceptance Scenarios**:

1. **Given** an eight-bit RGBA PNG, **When** it is drawn, **Then** colour and alpha are separate
   streams and the alpha is a `/SMask`.
2. **Given** an eight-bit RGB or greyscale PNG, **When** it is drawn, **Then** its `IDAT` data is
   embedded unchanged with a PNG predictor.
3. **Given** an indexed PNG with `tRNS`, **When** it is drawn, **Then** it is expanded to RGB and a
   soft mask.
4. **Given** a `pHYs` chunk, **When** the image is drawn without dimensions, **Then** its natural size
   follows from the resolution.
5. **Given** an interlaced or sixteen-bit PNG, **When** it is handed over, **Then** it is refused with
   an explanation rather than decoded wrongly.

### Edge Cases

- A document with no page is refused before any byte is written.
- An empty text string draws nothing; a line break inside one call is refused with the instruction to
  draw each line separately.
- A page may be blank, and a document may mix page sizes.
- The same image drawn on several pages is one image object; the same standard font used by several
  pages is one font object; an embedded font collects the glyphs of every page into one subset.
- Text may be drawn outside the page; the coordinates are not clamped.
- A `DocumentId` that is not 32 hexadecimal digits, or an empty `Producer`, makes the document
  unusable and is reported before the first object is written.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST create an empty document in memory, add pages of any `PdfPageSize`, and
  return the bytes to the caller as a `byte[]`, through a `Stream`, or as an `IPdfContentSource`.
- **FR-002**: The result MUST be usable directly as a `MergePlan` source, from memory, without a
  temporary file.
- **FR-003**: `DrawText` MUST place one line at a position in points, with a documented origin. The
  origin is the top-left corner, x to the right, y downwards; `y` is the baseline.
- **FR-004**: `DrawText` MUST accept a font size in points, an RGB colour and any number of
  independent calls on the same page.
- **FR-005**: A standard font MUST be available without any font file, using `WinAnsiEncoding`.
- **FR-006**: A TrueType program supplied as bytes MUST be embedded as a subset, without reading a
  system font, and MUST carry a `/ToUnicode` map so the text extracts.
- **FR-007**: A PNG supplied as bytes MUST be embedded at a position and an explicit size, with
  `/SMask` for an alpha channel; natural size from `pHYs` is supported.
- **FR-008**: The PNG reader MUST NOT add an imaging dependency; `System.IO.Compression` is the only
  compression code used, and an opaque `IDAT` stream is embedded as it stands.
- **FR-009**: The text MUST round-trip through `pypdf` and `pdftotext`: `é è à ç ô ï`, `'`, `/` and `|`
  are extracted exactly.
- **FR-010**: A fixed `CreationDate` and an explicit `DocumentId` MUST make the output reproducible
  byte for byte.
- **FR-011**: The document information dictionary MUST carry title, author, subject, keywords, creator,
  producer and dates, and custom entries.
- **FR-012**: The authored document MUST pass `qpdf --check` (through `pikepdf`) and `pypdf` without
  warnings, and its page size MUST read back through `IPdfInspector`.
- **FR-013**: The authoring surface MUST be analyser clean, documented on every public member, listed in
  the approved-API snapshot, and covered by tests on both `net10.0` and `net11.0`.
- **FR-014**: A character the selected face cannot encode, a missing glyph, a line break, an interlaced
  PNG and a sixteen-bit PNG MUST be refused with a message that names the problem; nothing is guessed
  or silently replaced.

### Key Entities

- **`PdfDocumentBuilder`**: the document under construction; owns pages, metadata and options.
- **`PdfPageCanvas`**: the drawing surface of one page; records text and image operations.
- **`PdfTypeface`**: a standard face or a parsed TrueType program, immutable and shareable.
- **`PdfColor`**: an RGB colour as three bytes.
- **`StandardFont`**: the twelve standard faces that need no font file.
- **`PdfAuthoringOptions`**: creation date, document identifier and producer.
- **`PdfByteArraySource`**: the built document as a merge source (existing type).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An A4 cover with one logo and six text lines is built, merged in front of a document and
  read back in under 400 ms on the recorded machine, including the merge and the text read.
- **SC-002**: The authored page is 8.6 KB in the example before merging; the embedded subset is 5.3 KB
  from a 17.5 KB program, and the full `DejaVuSans.ttf` is 739 KB.
- **SC-003**: 23 new tests pass on both target frameworks; the whole suite is 549 tests, 0 failed.
- **SC-004**: `pikepdf` reports no syntax problems for the cover or the merged document; `pypdf` and
  PyMuPDF extract the accented text exactly; a 72 dpi render shows the logo colour at (100, 45) and the
  page white at (50, 45), the transparent margin of the image.
- **SC-005**: Release build: 0 warnings, 0 errors, both frameworks.

## Assumptions

- The caller supplies the PNG and, when it wants a face beyond the standard twelve, the TrueType
  program. The library ships no font: a licence to redistribute one is the caller's decision.
- `WinAnsiEncoding` is the right default for a standard font; text outside it is an embedded-font case,
  not a silent fallback.
- The fixed-position model is enough for a cover, an address block and a marker line. A caller that
  needs flow, tables or vector drawing uses a reporting library and merges the result.
- The PDF version written is 1.7 with a classic cross-reference table, matching the merge engine.
- The output is not PDF/A or PDF/UA validated, and the authored page is not encrypted.

## Non-Goals

- Word wrap, flow layout, tables, lists, vector graphics, forms, annotations, signing and redaction.
- JPEG, GIF, SVG, 16-bit images, Adam7 interlacing and image decoding beyond what a PNG needs.
- Reading system fonts, font fallback, text shaping and kerning beyond the font program's own tables.
- Encrypting the authored page; the merge output can be encrypted as before.
- A `MeasureString` API: metrics belong to the font file and to a text shaper.
