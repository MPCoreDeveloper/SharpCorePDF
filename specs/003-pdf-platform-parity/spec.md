# Feature Specification: PDF Platform Parity

**Feature Branch**: `003-pdf-platform-parity`

**Created**: 2026-10-21

**Status**: Draft — Phase 0 in progress; CA-001 complete, tasks generated, parity matrix and corpus pending

**Input**: User description: "Reach PDFsharp and PdfPig feature parity through a staged, dependency-free
PDF platform: complete authoring with vector graphics, text layout and common image formats; page
editing; forms; annotations; digital signatures; PDF/A profiles; and extraction and layout analysis,
without becoming a rendering, OCR or HTML-conversion engine."

## Product Outcome

SharpCorePDF 1.0 is a merge engine with inspection and a bounded cover-page author. This feature turns
it into a general-purpose, dependency-free .NET PDF platform whose documented capability surface is
comparable to **PDFsharp 6.5** for creation and processing and to **PdfPig 0.1.x** for reading and
layout analysis.

Parity is **capability parity, not API compatibility**. A caller must be able to complete the same
jobs, with the same observable outcomes and the same level of data preservation, by using the
SharpCorePDF API. Existing PDFsharp or PdfPig source code does not have to compile unchanged, and no
PDFsharp or PdfPig code, package or binary is used at runtime or in the test suite.

“Level” is defined as **Release 1.1 parity**: every capability marked **Must** in the parity matrix
below is implemented, documented, measured and covered by a test that fails when the capability is
removed. Capabilities marked **Later** are not part of Release 1.1 and must not be advertised as if
they were.

## Parity Contract

The parity matrix is a repository document (`docs/parity.md`) and a test corpus, not a marketing
claim. Each row names the PDFsharp or PdfPig behaviour it matches, the SharpCorePDF outcome, the
release that delivers it, and the evidence that proves it. A row without evidence is not “done”.

| Capability area | PDFsharp 6.5 | PdfPig 0.1.x | SharpCorePDF target |
| --- | --- | --- | --- |
| Page creation, fixed-position text, standard and embedded fonts | Yes | Basic creation only | Must, 1.1 |
| Vector drawing: lines, paths, curves, rectangles, ellipses, fill, stroke, clipping, transforms | GDI+-class `XGraphics` | Path operations in the builder | Must, 1.1 |
| Text measurement, wrapping, alignment, leading, basic layout | Basic text layout | Not a layout engine | Must, 1.1 |
| PNG, JPEG, BMP and GIF images | PNG, JPEG, BMP, GIF | Image extraction; basic creation | Must, 1.1 |
| Page import, insert, delete, reorder, rotate, crop, resize, split, extract, append | Yes | Merge only | Must, 1.1 |
| Overlay, underlay, watermark, stamp | Yes | No | Must, 1.1 |
| Metadata, XMP, outlines, destinations, page labels, viewer preferences | Yes | Read metadata and bookmarks | Must, 1.1 |
| AcroForm creation, filling, flattening and appearance generation | Yes | Read-only forms | Must, 1.1 |
| Annotation creation, editing, removal and appearance streams | Broad annotation model | Read-only annotations | Must, 1.1 |
| Incremental updates that preserve existing signatures | Yes | No | Must, 1.1 |
| Digital signatures: sign, validate, timestamp, report integrity and trust separately | Yes | No | Must, 1.1 |
| PDF/A-1b, PDF/A-2b and PDF/A-3b output | PDF/A manager | No | Must, 1.1 |
| Positioned letters, words and blocks; configurable segmentation and reading order | Not a reading API | Yes | Must, 1.1 |
| Images, hyperlinks, form values, annotations, embedded files, metadata, bookmarks | Partial | Yes | Must, 1.1 |
| Public read-only object graph and content-stream inspection | Partial | Yes | Must, 1.1 |
| Export to ALTO, PAGE XML, hOCR and SVG text | No | Yes | Must, 1.1 |
| Rendering pages to images or raster previews | No | No | Non-goal |
| OCR, HTML-to-PDF, PDF-to-Word/HTML, reflow, tagged-PDF/PDF-UA authoring | No | No | Non-goal |
| MigraDoc-style flow engine with automatic page breaks | Separate MigraDoc product | No | Non-goal |
| JavaScript execution, 3D, multimedia, geospatial, public-key encryption | No or partial | No | Non-goal |
| Drop-in source or binary compatibility with PDFsharp or PdfPig | N/A | N/A | Non-goal |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build a real document, not a cover (Priority: P1)

An application produces a report without a reporting library: a title page, a vector chart drawn from
paths, wrapped paragraphs in two typefaces, a JPEG photograph and a PNG logo, page numbers, headers and
metadata. It writes the document in memory and merges it with existing PDFs.

**Why this priority**: This is the largest gap between SharpCorePDF and PDFsharp and the reason most
callers still need a second PDF library. A creator that cannot draw a chart or wrap a paragraph is not
at PDFsharp’s level.

**Independent Test**: Build a ten-page document containing at least 200 wrapped paragraphs, 20 vector
paths, two embedded fonts, one JPEG and one PNG. Validate it with independent readers, render it, and
assert the text, glyph positions, colours, image placements and page geometry.

**Acceptance Scenarios**:

1. **Given** a blank page, **When** the caller draws a filled and stroked cubic path, a dashed line, a
   rectangle and an ellipse with alpha, **Then** the output contains the corresponding path, colour,
   dash, line-cap, line-join and transparency operators and renders without a syntax error.
2. **Given** a paragraph and a measured width, **When** the caller asks for wrapped text with a line
   height and alignment, **Then** the line breaks are deterministic, every line fits the width, and
   the text extracts with spaces and accents intact.
3. **Given** a JPEG and a PNG, **When** both are placed on a page, **Then** the JPEG’s original stream
   is embedded without re-encoding, the PNG keeps its alpha, and both report their pixel dimensions and
   natural resolution through the read side.
4. **Given** the same pages, fonts, images and fixed creation date, **When** the document is built
   twice, **Then** the bytes are identical.

---

### User Story 2 - Edit an existing document (Priority: P2)

An application receives a third-party PDF and has to rotate two pages, crop the margins, insert a
cover, remove an obsolete page, add a “CONFIDENTIAL” watermark, attach a link and a highlight, fill a
ten-field form, and flatten the result for archival delivery.

**Why this priority**: PDFsharp is used as much for processing existing documents as for creating new
ones. Merge alone cannot rotate, crop, annotate, fill or flatten.

**Independent Test**: Apply a scripted set of page, annotation and form edits to a 100-page fixture,
reopen the result, and assert page order, geometry, annotation geometry, field values, flattened
appearance and metadata. Validate the output with `qpdf` and a second reader.

**Acceptance Scenarios**:

1. **Given** a document with inherited page attributes, **When** pages are rotated, cropped, resized,
   removed, duplicated and reordered, **Then** every surviving page has explicit geometry and no
   reference points at a removed page.
2. **Given** a page and a watermark, **When** the watermark is applied as an overlay or underlay,
   **Then** it appears in the specified layer, is clipped to the page, and does not alter the source
   page’s original content stream.
3. **Given** an AcroForm, **When** fields are set and the form is flattened, **Then** the values are
   visible without a form-capable viewer, the fields are no longer interactive, and the appearance
   streams match the declared values.
4. **Given** an annotation, **When** it is added, moved, restyled or removed, **Then** the page’s
   annotation array and appearance streams reflect the change and the result passes an independent
   syntax check.

---

### User Story 3 - Read a document the way a consumer needs it (Priority: P3)

A search and analytics application opens a born-digital document, extracts letters with positions,
groups them into words and blocks, selects a reading order, reads images, hyperlinks, form values,
annotations and embedded files, walks the object graph for a custom audit, and exports the page as
ALTO, PAGE XML, hOCR or SVG text.

**Why this priority**: PdfPig’s value is not only “text comes out” but “the layout comes out”. The
current reader returns blocks, but it does not expose the letter-level model, configurable segmentation,
reading-order strategies, hyperlinks, an object graph or standard layout exports.

**Independent Test**: Run the extraction pipeline over a labelled corpus of at least 50 documents,
compare characters and coordinates with an independent extractor, and score reading order against a
human-labelled subset.

**Acceptance Scenarios**:

1. **Given** a page of text, **When** it is read, **Then** every letter carries its Unicode value,
   font size, font name when available, bounding box, baseline start and end, and text direction.
2. **Given** a page with two columns, **When** the caller selects column-aware segmentation and
   reading order, **Then** the returned block order follows the columns rather than the raw content
   stream.
3. **Given** a page with a link, **When** hyperlinks are requested, **Then** the URI or internal
   destination and the clickable rectangle are returned; **When** an image is requested, **Then** its
   filter, dimensions, colour space and decodable pixel data are returned when the filter is supported.
4. **Given** any readable page, **When** it is exported as ALTO, PAGE XML, hOCR or SVG text, **Then**
   the export is well-formed, preserves coordinates and text, and can be consumed by a standard tool.

---

### User Story 4 - Trust the document (Priority: P4)

An application signs an invoice with an X.509 certificate, adds a trusted timestamp, later validates
the signature, and reports separately whether the bytes are intact and whether the certificate chain is
trusted. For archival delivery it writes PDF/A-2b and proves the result with an independent validator.

**Why this priority**: Signing and archival conformance are the two capabilities that most often force
a team onto a commercial SDK. Without them “PDFsharp level” is incomplete.

**Independent Test**: Sign a fixture, validate it with an independent signature validator, alter one
byte and confirm detection, append an incremental update and confirm the earlier signature still
validates, then convert the unsigned fixture to PDF/A-2b and pass an independent PDF/A validator.

**Acceptance Scenarios**:

1. **Given** an unsigned document and a signing certificate, **When** it is signed, **Then** the
   signature covers the intended byte range, the document is written as an incremental update, and an
   independent validator reports a cryptographically valid signature.
2. **Given** a signed document, **When** any covered byte is changed, **Then** validation reports an
   integrity failure and does not report the document as valid.
3. **Given** a signed document, **When** an annotation is added by an incremental update, **Then** the
   original signature remains valid and the new revision is reported.
4. **Given** an ordinary document, **When** it is written as PDF/A-1b, PDF/A-2b or PDF/A-3b, **Then**
   an independent PDF/A validator reports conformance for the requested profile.

---

### Edge Cases

- A font contains no glyph for a requested character, or the character is outside the font’s encoding.
  The operation is refused with the character and code point, never silently replaced.
- A JPEG uses CMYK, EXIF orientation, or an Adobe APP14 transform. The colour transform and
  orientation are applied explicitly or the image is refused with the reason.
- A PNG is interlaced, sixteen-bit, palette-based with `tRNS`, or has an embedded ICC profile. The
  first two are either supported or refused by name; the last two preserve their meaning.
- A page’s `/MediaBox`, `/CropBox`, `/BleedBox`, `/TrimBox` or `/ArtBox` is inherited, missing or
  inverted. The editor materialises it and reports the repair.
- A page tree contains a cycle, a reference to a deleted page, or an annotation whose destination no
  longer resolves.
- A form field is read-only, hidden, has no appearance stream, or is duplicated across pages.
- A signature covers an object stream, is detached, uses a timestamp that cannot be reached, or uses
  an algorithm the platform does not support.
- A PDF/A conversion meets a font that cannot be embedded, an image with an unsupported colour space,
  or an encryption dictionary that must be removed.
- A scanned page has no text, a page uses a font with no `/ToUnicode`, or two text runs overlap.
- A document is encrypted, damaged, linearised, or larger than the configured source limit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The engine MUST remain self-contained: no shipped package may depend on a third-party PDF
  engine, native library or platform-specific binary, and every PDF byte MUST still be read and written
  by code in this repository.
- **FR-002**: The authoring surface MUST support lines, rectangles, ellipses, cubic and quadratic
  curves, arbitrary paths, fill and stroke paints, dash patterns, line caps, line joins, miter limits,
  clipping paths, affine transforms and constant-alpha transparency.
- **FR-003**: The authoring surface MUST support text measurement, single-line and multi-line drawing,
  greedy wrapping to a width, leading, horizontal and vertical alignment, and per-run colour and font.
- **FR-004**: The image surface MUST support PNG, JPEG, BMP and GIF input, preserve a JPEG’s encoded
  payload when no transform is required, honour EXIF orientation, preserve alpha, and report natural
  resolution from the image metadata.
- **FR-005**: The editor MUST support page import, insertion, deletion, duplication, reordering,
  rotation, cropping, resizing, splitting, extraction and appending without changing the source file.
- **FR-006**: The editor MUST support overlaying and underlaying a page or a document, including
  watermarks and stamps, without modifying the original page content stream.
- **FR-007**: The editor MUST support reading and writing document information, XMP packets, outlines,
  named destinations, page labels and viewer preferences.
- **FR-008**: The annotation surface MUST support creating, reading, updating and deleting the common
  annotation types named in the parity matrix, with appearance streams for each type it can create.
- **FR-009**: The form surface MUST support reading, creating, filling and flattening AcroForm text,
  checkbox, radio, choice and push-button fields, with appearance generation and no JavaScript
  execution.
- **FR-010**: The signature surface MUST support signing with an X.509 certificate, a detached CMS
  signature, an optional RFC 3161 timestamp, validation of integrity and certificate trust as separate
  results, and preservation of earlier signatures across incremental updates.
- **FR-011**: The compliance surface MUST support writing PDF/A-1b, PDF/A-2b and PDF/A-3b, including
  font embedding, output intent, XMP metadata and the restrictions each profile requires.
- **FR-012**: The reading surface MUST expose letters with Unicode text, font size, font name when
  available, bounding box, baseline endpoints and text direction; words and blocks MUST be groupable
  through a caller-selectable strategy.
- **FR-013**: The reading surface MUST expose images with filter, dimensions, colour space, mask,
  placement and decodable pixels when the filter is supported; it MUST NOT silently re-encode an image
  it cannot decode.
- **FR-014**: The reading surface MUST expose hyperlinks, form values, annotations, embedded files,
  metadata and bookmarks, and MUST report unresolved destinations through warnings.
- **FR-015**: The reading surface MUST provide a read-only object graph and a content-stream operation
  view sufficient for a caller to audit a document without parsing raw bytes.
- **FR-016**: The reading surface MUST export pages to ALTO, PAGE XML, hOCR and SVG text.
- **FR-017**: The writer MUST support both a complete rewrite and an incremental update, and the
  incremental path MUST preserve the byte ranges of prior revisions.
- **FR-018**: Every operation that loses, changes or cannot represent information MUST report a
  `PdfWarning` whose `Kind` identifies the condition; a character, glyph, image, field or annotation is
  never silently dropped.
- **FR-019**: A fixed creation date and document identifier MUST keep every authored, edited or
  converted document byte-identical across repeated runs with the same inputs.
- **FR-020**: The public surface MUST remain layered, snapshotted and documented; additions go into
  `PublicAPI.Unshipped.txt` in the same commit, and the 1.0 API is not broken without a documented
  breaking-change release.
- **FR-021**: The implementation MUST be covered by tests on `net10.0` and `net11.0`, with new code at
  or above the repository’s 90% coverage condition and a Release build with zero warnings.
- **FR-022**: The repository MUST publish a parity matrix in which every “Must” row has a named test,
  an external validation command and a release milestone; a claim without evidence is removed.
- **FR-023**: A caller MUST be able to migrate from PDFsharp or PdfPig by following a documented
  capability mapping and a set of runnable examples; source compatibility is not required.
- **FR-024**: Large operations MUST be measured against recorded budgets for wall time, allocations and
  output size, and no existing merge or authoring budget may regress by more than 10% without an
  explicit decision recorded in `docs/measurements.md`.

### Key Entities

- **`PdfPoint`, `PdfMatrix`, `PdfPath`, `PdfPathSegment`**: geometry used by the drawing surface.
- **`PdfPaint`, `PdfStroke`, `PdfGraphicsState`**: fill, stroke and graphics-state values.
- **`PdfImageSource`**: a decoded or pass-through image, its pixels, dimensions, colour space and mask.
- **`PdfTextStyle`, `PdfTextMetrics`, `PdfTextLayout`**: font selection, measurement and wrapped runs.
- **`PdfDocumentEditor`, `PdfPageEdit`**: an ordered set of edits applied to an existing document.
- **`PdfAnnotation`**: the writable annotation model and its appearance.
- **`PdfForm`, `PdfFormField`**: the writable AcroForm model, field values and flattening state.
- **`PdfSignature`, `PdfSignatureValidation`**: signing options, byte range and separate integrity and
  trust results.
- **`PdfAProfile`**: the target archival profile and the restrictions it imposes.
- **`PdfLetter`, `PdfWord`, `PdfTextBlock`, `PdfPageSegment`**: the positioned reading model.
- **`PdfObjectView`**: a read-only projection of the internal object graph.
- **`PdfParityRow`**: a documented capability, its reference behaviour and its evidence.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every capability marked “Must” in `docs/parity.md` resolves to at least one named test and
  one external validation command; 100% of rows are evidenced before Release 1.1 is tagged.
- **SC-002**: The ten-page creator scenario in User Story 1 builds, merges and reads back in under 2
  seconds on the recorded machine, with no existing authoring budget regressed by more than 10%.
- **SC-003**: The 100-page editor scenario in User Story 2 applies 200 page, annotation and form edits
  in under 3 seconds and passes `qpdf --check` and a second reader with no syntax warning.
- **SC-004**: On the labelled 50-document corpus, at least 99.5% of expected characters are extracted,
  at least 95% of their bounding boxes are within 0.5 points of the independent extractor’s boxes, and
  reading order agrees with the human-labelled subset on at least 95% of pages.
- **SC-005**: An independent signature validator accepts 100% of signatures produced by the signing
  fixture set, rejects 100% of tampered fixtures, and accepts the original signature after a subsequent
  incremental update in 100% of cases.
- **SC-006**: An independent PDF/A validator reports conformance for 100% of PDF/A-1b, -2b and -3b
  fixtures produced by the library.
- **SC-007**: The Release build reports zero warnings and zero errors on both target frameworks; the
  full suite passes on both legs; new code meets the 90% coverage condition.
- **SC-008**: All 1.0 tests continue to pass unchanged, and no 1.0 public API signature is removed or
  changed outside a documented breaking-change release.
- **SC-009**: The migration guide maps every PDFsharp and PdfPig capability in the parity matrix to a
  SharpCorePDF example or states that it is out of scope; 100% of mapped examples compile and run.
- **SC-010**: No shipped package gains a runtime dependency, and an empty-font, offline container still
  produces byte-identical output for the deterministic fixtures.

## Assumptions

- “PDFsharp level” means the documented 6.5 creation and processing surface, not MigraDoc’s flow
  layout, GDI+ rendering or every historical PDFsharp 1.x compatibility quirk.
- “PdfPig level” means its documented reading, extraction, layout-analysis, object-graph and export
  surface. PdfPig does not render, edit annotations or fill forms, and neither will this feature claim
  to.
- Rendering, OCR, HTML-to-PDF, PDF-to-Word/HTML conversion, JavaScript execution, 3D/multimedia,
  public-key encryption, JPEG 2000 and tagged-PDF/PDF-UA authoring remain out of scope.
- Release 1.1 is an additive minor release because the public surface grows without breaking the 1.0
  API; the 1.0 line remains supported for fixes during the transition.
- The two target frameworks remain `net10.0` and `net11.0`; adding or dropping a framework is a
  separate architectural decision.
- All external validation tools (`qpdf`/`pikepdf`, `pypdf`, PyMuPDF, veraPDF, pyHanko, fontTools) run
  outside the shipped packages and outside CI unless a workflow is added deliberately.
- A caller supplies fonts, certificates and timestamps; the library ships no font, private key or
  trusted-root bundle.

## Non-Goals

- A page rasteriser, thumbnail generator, PDF viewer or print pipeline.
- OCR, document understanding, table extraction as a semantic product, or conversion to Office formats.
- A MigraDoc-compatible document object model with automatic pagination, tables, charts and styles.
- Source or binary compatibility with PDFsharp or PdfPig, including their namespaces and type names.
- PDF/UA, PDF/X, WCAG or accessibility conformance claims.
- Network access, logging, secret storage or a hosted service.
- Editing content that cannot be represented in the current object model without a documented loss
  warning.







