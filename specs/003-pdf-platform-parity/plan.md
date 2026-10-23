# Implementation Plan: PDF Platform Parity

**Feature**: `003-pdf-platform-parity` | **Spec**: [spec.md](./spec.md) | **Status**: Design complete;
Phase 0 complete (CA-001 closed; `tasks.md` generated; parity matrix, corpus, API review and measurement
scenarios recorded). Phase 1 is unblocked.

## Summary

Release 1.1 grows the existing merge, inspection and fixed-position authoring engine into a
general-purpose, dependency-free PDF platform. It reaches **capability parity** with PDFsharp 6.5 for
creation and processing and with PdfPig 0.1.x for reading and layout analysis, while keeping every PDF
byte inside this repository and keeping the four-layer architecture. The parity target is reached across
two releases: `1.1.0` ships the capability implemented and evidenced on the branch, and the remaining
**Must** rows are `1.2.0` commitments under the scope amendment of 2026-10-22 recorded in
[`spec.md`](./spec.md).

The work is delivered in six gated phases: the product-boundary amendment and parity matrix, creator
parity, editing and incremental writing, annotations and forms, signatures and PDF/A, then reading and
layout analysis, followed by a hardening release. The existing 1.0 surface stays intact; new capability
is additive under a 1.1 minor version.

## Technical Context

- **Language/version**: C# 14 on `net10.0`, C# 15 preview on `net11.0`, matching `src/Directory.Build.props`.
- **Primary dependencies**: the .NET base class library only. `System.Security.Cryptography` provides
  CMS/PKCS#7, X.509 and hashing; `System.IO.Compression` provides Flate; `System.Xml` provides XMP,
  ALTO and PAGE XML. No third-party PDF engine, native library or platform binary is added.
- **Storage**: in-memory documents and caller-supplied streams/files; no database, cache or service.
- **Testing**: xUnit v3 on Microsoft.Testing.Platform on both target frameworks, the existing fixture
  corpus, a new parity corpus, and external validators run from `docs/tools`.
- **Target platform**: .NET 10 and .NET 11 class libraries; Windows, Linux and macOS with no
  platform-specific code path.
- **Project type**: layered library plus examples and tools; no CLI, service or renderer is added.
- **Performance goals**: no merge or authoring budget in `docs/measurements.md` regresses by more than
  10%; the creator, editor and extraction scenarios in `spec.md` meet their stated wall-time budgets on
  the recorded machine.
- **Constraints**: zero warnings, 90% coverage on new code, `PublicAPI.*.txt` in the same commit,
  deterministic output with a fixed date, no network, no logging, no secret storage, and no silent data
  loss.
- **Scale/scope**: documents up to 2,000 pages and 250 MB, ten thousand vector operations per document,
  and 1,000-page extraction runs. These are test envelopes, not hard limits, until a documented engine
  option says otherwise.

## Constitution Check

*GATE: evaluated against constitution 2.0.0 before Phase 0; re-evaluated after Phase 1 design.*

| Principle | Result | Evidence / consequence |
| --- | --- | --- |
| I. Every PDF byte is ours | PASS | All new readers, writers, parsers and signers are implemented in this repository; BCL crypto only. |
| II. Dependencies point inward | PASS | No new assembly and no new package. Pure value objects and read models go in Domain; orchestration in Application; file structure, editing, forms, signatures and layout analysis in Infrastructure. |
| III. Copy bytes, never re-encode | PASS WITH RULE | Merge and unaffected streams stay byte-copied. Editing and signing necessarily rewrite the revision; incremental mode preserves prior revisions byte-for-byte and every lossy decision becomes a `PdfWarning`. |
| IV. A test that fails on revert | PASS | Every phase names its failing test and its external validation command before implementation starts. |
| V. Public surface is snapshotted | PASS | Additions go to `PublicAPI.Unshipped.txt`; Release 1.1 is additive and 1.0 signatures remain. |
| VI. Zero warnings | PASS | The existing Release gate applies to every phase; no new analyzer suppression is allowed without a recorded reason. |
| VII. The boundary is part of the API | PASS (CA-001) | Constitution 2.0.0 permits creation and editing; rendering, conversion, OCR, conformance validation, service and CLI remain excluded. |

**Gate result: PASS after CA-001.** The constitution amendment is applied; the remaining CA-001
deliverables are tracked in Phase 0:

1. Constitution 2.0.0 amends Principle VII: SharpCorePDF is a dependency-free PDF creation, editing
   and reading library; it is **not** a renderer, OCR engine, HTML/Office converter, PDF/A or PDF/UA
   validator, hosted service or CLI.
2. Update `README.md`, `docs/manual/src/01-what-it-is.md`, `docs/manual/src/02-what-it-is-not.md`,
   the preface and `CHANGELOG.md` in the same commit.
3. Add an ADR recording the product decision, the PDFsharp/PdfPig parity target and the excluded
   surface.
4. Record the amendment in the 1.1 release checklist before any phase branch is cut.

The gate is not waived. A feature that changes the product boundary is legitimate only when the
constitution says so first.

## Architecture

No fifth assembly is introduced. The existing four layers absorb the new capabilities:

```text
src/SharpCorePDF.Domain/
  ValueObjects/
    PdfPoint.cs, PdfMatrix.cs, PdfPath.cs, PdfPathSegment.cs
    PdfPaint.cs, PdfStroke.cs, PdfTextStyle.cs, PdfTextMetrics.cs
    PdfAProfile.cs
  ReadModels/
    PdfLetter.cs, PdfPageSegment.cs, PdfHyperlink.cs
    PdfSignatureInfo.cs, PdfSignatureValidation.cs
  Aggregates/
    PdfEditPlan.cs                 ordered, validated document edits
  Ports/
    IPdfDocumentEditor.cs          editing use case
    IPdfLayoutReader.cs            letter, segment, link and export use case
    IPdfObjectInspector.cs         read-only graph and content audit
src/SharpCorePDF.Application/
  Services/
    PdfDocumentEditorService.cs    opens a source, applies a plan, writes the result
    PdfLayoutReaderService.cs      orchestrates extraction and export
    PdfObjectInspectorService.cs   opens a source and projects its graph
src/SharpCorePDF.Infrastructure/
  PdfDocumentEditor.cs             page operations, overlays, metadata, outlines
  Authoring/
    GraphicsState.cs, ContentStreamBuilder.cs, PathWriter.cs
    TextLayoutWriter.cs, ImageSource.cs, JpegImage.cs, BmpImage.cs, GifImage.cs
    PdfAWriter.cs
  Editing/
    IncrementalWriter.cs, PageOperations.cs, OverlayWriter.cs
  Annotations/
    AnnotationWriter.cs, AppearanceStreams.cs
  Forms/
    AcroFormWriter.cs, FieldAppearance.cs
  Signatures/
    CmsSignatureWriter.cs, SignatureValidator.cs, IPdfTimestampProvider.cs
  Reading/
    LetterExtractor.cs, LayoutAnalysis/, ObjectGraphView.cs, Exporters/
```

The internal object model (`PdfObject`, `PdfDictionary`, `PdfStream`, `PdfReference`, `PdfFileReader`,
`PdfWriter`) is reused, not duplicated. The new public object graph is a **read-only projection**
(`PdfObjectView`) over that model; mutation stays inside `PdfDocumentEditor`.

`PdfDocumentBuilder` and `PdfPageCanvas` keep their current members and gain overloads. Existing 1.0
code continues to compile; new optional parameters and new types are additive. A member that cannot be
kept is marked `[Obsolete]` for the 1.x line, not removed inside 1.x.

### Structure Decision

`SharpCorePDF.Infrastructure` already owns the file structure, so the editor, form writer, signature
writer, object-graph projection and layout analysers live there. Domain gains only values and plans
that can be described without a file. Application gains ports/services that orchestrate a source and a
destination without parsing syntax. DependencyInjection registers the new ports. The meta-package needs
no change beyond its version, because it already depends on all four layers.

A separate `SharpCorePDF.Pdf` assembly was rejected: it would create a fifth package, duplicate the
existing object model, and violate the four-layer decision in Principle II. A public mutable
`PdfObject` hierarchy was rejected for the same reason plus the snapshot cost; the projection is
read-only and can grow additively.

## Key Decisions

1. **Capability parity, not API cloning.** PDFsharp’s `XGraphics` and PdfPig’s `PdfDocument` names are
   not reproduced. The migration guide maps jobs, not signatures, so the public surface can follow this
   repository’s domain-first conventions and avoid copying another project’s API or code.
2. **One content writer for creation and editing.** Paths, text, images and layers are recorded as
   typed operations and serialised by one content-stream builder. An overlay is the same operation list
   written behind or in front of existing content, so creation and editing cannot drift.
3. **Two writers, one serializer.** Full rewrite stays the default and keeps the classic
   cross-reference table. `IncrementalWriter` appends a revision and is mandatory for signatures; a
   document with no prior revision may still use a full rewrite.
4. **No network in the library.** RFC 3161 timestamping is expressed as an `IPdfTimestampProvider`
   callback. The caller performs the HTTP exchange; the library parses and embeds the returned token.
   This preserves the constitution’s “opens no socket” rule.
5. **Trust is reported in two parts.** `PdfSignatureValidation` returns cryptographic integrity and
   certificate-chain trust separately. A valid signature from an untrusted certificate is not reported
   as a failed signature, and a trusted certificate over altered bytes is not reported as valid.

6. **Fonts are caller-supplied and measured locally.** `PdfTypeface` gains metrics from `head`, `hhea`,
   `maxp`, `hmtx` and `cmap`; wrapping is deterministic greedy line breaking. There is no system font
   lookup, no font fallback and no text shaping dependency. Complex-script shaping is out of scope and
   refused rather than approximated.
7. **Images are pass-through first.** JPEG is embedded as `DCTDecode` when no colour transform or
   orientation change is needed; PNG keeps its `IDAT` predictor path; BMP and GIF are decoded to
   samples. Unsupported variants are refused by name, never silently re-encoded.
8. **PDF/A is a writer profile, not a validator claim.** The library writes the structures a profile
   requires and reports what it cannot satisfy. Conformance is proven by veraPDF outside the build; no
   “PDF/A compliant” claim ships before that evidence exists.
9. **Reading is layered over the existing scanner.** `LetterExtractor` reuses `PdfTextScanner`,
   `PdfFontDecoder` and `PdfToUnicodeCMap`; layout analysis is a separate, configurable stage, so a
   caller can choose raw order, nearest-neighbour words, recursive XY cut or Docstrum-style blocks.
10. **No PDFsharp or PdfPig in the build.** Their documented behaviour is the reference; their code and
    packages are not dependencies. Parity is tested against expected structures and independent tools,
    not by linking a competitor into the test suite.

## Delivery Phases

Each phase ends with a green Release build, both test legs, the public-API snapshot update, the
manuals and examples, and a recorded external validation. A phase is not started until its predecessor’s
exit criteria are evidenced in the pull request.

| Phase | Release | Scope | Exit criteria |
| --- | --- | --- | --- |
| 0. Boundary and evidence | 1.1 groundwork | CA-001; `docs/parity.md`; corpus; ADR; API design review | Constitution 2.0.0 merged; every “Must” row has an owner, test name and validation command |
| 1. Creator parity | 1.1 preview 1 | Geometry, graphics state, paths, text metrics/layout, JPEG/BMP/GIF | User Story 1 passes; qpdf, pypdf, PyMuPDF and fontTools accept the fixtures |
| 2. Editing parity | 1.1 preview 2 | Page operations, overlays, metadata/outlines/labels, incremental writer | User Story 2 page scenarios pass; prior revisions byte-identical after an incremental update |
| 3. Annotations and forms | 1.1 preview 3 | Annotation model and appearances; AcroForm fill/create/flatten | Form values read back in two readers; flattened appearance visible without a form viewer |
| 4. Trust and archival | 1.1 preview 4 | CMS signatures, timestamp provider, validation, PDF/A-1b/2b/3b | pyHanko accepts signatures and rejects tampering; veraPDF passes every profile fixture |
| 5. Reading and analysis | 1.1 preview 5 | Letter model, segmentation, reading order, hyperlinks, object graph, exporters | User Story 3 metrics pass on the labelled corpus |
| 6. Hardening and 1.1 | 1.1 | Performance, migration guide, docs, package and release checklist | All success criteria pass; 1.0 suite unchanged; zero warnings; release checklist signed |

Phase 2 precedes Phase 4 because signatures require the incremental writer. Phase 3 and Phase 5 may
run in parallel after Phase 1, but both depend on the Phase 0 corpus and API review.

### Phase 0 — Boundary and Evidence (blocking)

**Deliverables**

- CA-001: constitution 2.0.0, ADR, README/manual/changelog correction, release-checklist entry.
- `docs/parity.md`: the matrix from `spec.md`, one row per capability, with the PDFsharp/PdfPig
  reference behaviour, the SharpCorePDF outcome, the release, the named test and the validation command.
- `docs/parity/` corpus: at least 50 born-digital documents, a human-labelled reading-order subset,
  image fixtures for every supported filter, form fixtures, annotation fixtures, unsigned/signed
  fixtures and PDF/A candidates. Generated by `docs/tools/make-fixtures.py`, binary in Git, licences
  recorded.
- API review: names and layering for the new ports and value objects; no mutable object graph; no new
  package; no `System.Drawing`, `SkiaSharp` or `PdfSharp` reference.
- `tools/SharpCorePDF.Measure` grows the new scenarios before implementation, so the baseline exists
  before the code does.

**Exit criteria**: constitution 2.0.0 is merged; the parity matrix has no unowned “Must” row; the
corpus is reproducible from the generator; the API review is recorded in the pull request; the 1.0
baseline measurements are re-read.

### Phase 1 — Creator parity

**Deliverables**

- `PdfPoint`, `PdfMatrix`, `PdfPath`, `PdfPathSegment`, `PdfPaint`, `PdfStroke`, `PdfGraphicsState` in
  Domain.
- `ContentStreamBuilder`, `PathWriter` and `GraphicsState` in Infrastructure; the writer covers `m`,
  `l`, `c`, `v`, `y`, `h`, `re`, `f`, `S`, `B`, `W`, `n`, `q`, `Q`, `cm`, `gs`, `d`, `J`, `j`, `M`,
  `G`, `g`, `RG`, `rg`, `K`, `k`, `CS`, `cs`, `SC`, `sc`, `SCN`, `scn` and `ca`/`CA` where needed.
- `PdfPageCanvas.DrawLine`, `DrawRectangle`, `DrawEllipse`, `DrawPath`, `DrawTextLayout`, and
  `DrawImage` auto-detecting PNG, JPEG, BMP and GIF; explicit overloads for a JPEG stream.
- `PdfTypeface` metrics (`Ascent`, `Descent`, `LineGap`, `CapHeight`, `Measure`), `PdfTextMetrics`,
  `PdfTextStyle`, `PdfTextLayout` with greedy wrapping, leading and horizontal/vertical alignment.
- `JpegImage` with SOF/SOS parsing, EXIF orientation and Adobe APP14 transform detection; `BmpImage`;
  `GifImage` first-frame decode; `PngImage` extended for interlaced/16-bit input or an explicit refusal.
- Dedupe key extended across formats; deterministic object order; `PdfAuthoringOptions` keeps fixed
  dates and IDs.

**Tests**: `Draws_paths_with_fill_stroke_clip_and_alpha`, `Wraps_a_paragraph_to_a_measured_width`,
`Embeds_a_jpeg_without_reencoding`, `Places_a_bmp_and_a_gif`, `Preserves_png_alpha_and_natural_size`,
`Builds_the_creator_report_deterministically`. External: `pikepdf`/`qpdf --check`, `pypdf` text,
PyMuPDF render and image positions, `fontTools` for metrics.

**Exit criteria**: User Story 1 passes; all image formats have positive and refusal tests; the creator
scenario meets SC-002; no existing merge/authoring budget regresses by more than 10%.

### Phase 2 — Editing parity

**Deliverables**

- `PdfEditPlan` in Domain: ordered operations, each validated before a document is opened; no source is
  modified.
- `PageOperations`: import, insert, delete, duplicate, reorder, rotate, crop, resize, split, extract and
  append. Inherited `/MediaBox`, `/CropBox`, `/Rotate` and `/Resources` are materialised onto surviving
  pages; references to removed pages are resolved or reported.
- `OverlayWriter`: overlay, underlay, watermark and stamp; original content streams are wrapped in
  `q`/`Q` and are not edited in place.
- Document-level edits: information dictionary, XMP, outlines, named destinations, page labels and
  viewer preferences.
- `IncrementalWriter`: appends objects and a new cross-reference section; prior bytes are not rewritten;
  the full writer remains the default.
- `IPdfDocumentEditor` port and `PdfDocumentEditorService`; DI registration.

**Tests**: `Rotates_crops_and_resizes_a_page_tree`, `Deletes_a_page_without_dangling_references`,
`Overlays_a_watermark_without_touching_the_source`, `Writes_an_incremental_update`,
`Preserves_the_prior_revision_bytes`, `Reopens_the_edited_document`. External: `qpdf --check`,
`pypdf` page geometry, PyMuPDF visual comparison.

**Exit criteria**: User Story 2 page scenarios pass; the 100-page editor scenario meets SC-003; the
incremental fixture’s original byte range is unchanged; warnings cover every dropped reference.

### Phase 3 — Annotations and forms

**Deliverables**

- Writable annotation model in Domain and `AnnotationWriter`/`AppearanceStreams` in Infrastructure:
  link, text, free text, stamp, highlight, underline, strikeout, square, circle, line and ink.
- Annotation geometry, flags, colour, border, opacity, contents and destination; appearance streams are
  generated for every create/update path so a viewer that does not synthesise an appearance still shows
  the annotation.
- `PdfForm`/`PdfFormField` write model; `AcroFormWriter` supports text, checkbox, radio, choice and
  push-button fields; fill, reset, read-only, hidden and flatten. Flattening writes the appearance into
  the page content and removes the widget from the interactive form.
- No JavaScript execution and no `/AA` action execution; action dictionaries may be copied or refused
  with a warning.
- `PdfEditPlan` gains annotation and form operations; the same editor applies them.

**Tests**: `Adds_moves_and_removes_an_annotation`, `Writes_an_annotation_appearance`,
`Fills_and_reads_back_form_values`, `Flattens_a_form_without_a_form_viewer`,
`Refuses_an_unsupported_field_type`. External: `pypdf` field values, PyMuPDF annotation/appearance
render, `qpdf --check`.

**Exit criteria**: User Story 2 annotation and form scenarios pass; flattened pages render identically
with and without a form-capable viewer; every field type has a value and a flatten test.

### Phase 4 — Trust and archival

**Deliverables**

- `CmsSignatureWriter`: detached CMS/PKCS#7 over the PDF byte range, SHA-256/384/512, X.509 certificate
  chain, optional certificate inclusion, `SubFilter` `adbe.pkcs7.detached` and ETSI CAdES where the
  profile requires it.
- `IPdfTimestampProvider`: caller-supplied callback for RFC 3161; the library parses and embeds the
  token but never opens a socket. A missing or failed timestamp is a warning, not a silent success.
- `SignatureValidator`: parses `/ByteRange`, verifies the digest and CMS signature, reports integrity
  and chain trust separately, and lists every revision/signature in a multi-signed document.
- `PdfAWriter`: PDF/A-1b, -2b and -3b profiles; embed every font, add the output intent and XMP,
  remove encryption and forbidden actions, and refuse or warn on colour spaces it cannot map.
- `PdfAProfile` in Domain; `PdfAuthoringOptions` and `PdfEditPlan` select a profile.

**Tests**: `Signs_a_document_with_a_detached_cms_signature`, `Detects_a_modified_byte_range`,
`Preserves_an_earlier_signature_after_an_incremental_update`, `Validates_a_timestamped_signature`,
`Writes_pdf_a_1b_2b_and_3b`. External: pyHanko sign/validate/verify, OpenSSL `cms -verify`, veraPDF for
every profile, `qpdf --check`.

**Exit criteria**: SC-005 and SC-006 pass; signatures validate after an incremental annotation update;
the timestamp path is proven with a local test token and no network call from the library.

### Phase 5 — Reading and analysis

**Deliverables**

- `PdfLetter` read model and `LetterExtractor` over the existing text scanner: Unicode, font size, font
  name when available, bounding box, baseline start/end, text direction.
- `LayoutAnalysis`: nearest-neighbour word extraction, recursive XY cut, Docstrum-style page segments
  and configurable reading-order detectors, with raw content order as the default.
- `PdfHyperlink` and hyperlink extraction; image metadata plus decoded pixels for Flate/PNG predictors,
  DCT/JPEG, BMP and GIF; explicit `ImageFilterUnsupported` for JPX/CCITT.
- `PdfObjectView` read-only projection and `PdfContentOperationView`; `IPdfObjectInspector` port.
- Exporters for ALTO, PAGE XML, hOCR and SVG text; XML is written with `System.Xml` and validated
  against the published schemas where one exists.
- `IPdfLayoutReader` port and service; DI registration; existing `IPdfContentReader` members remain.

**Tests**: `Reads_letters_with_positions_and_direction`, `Groups_a_two_column_page_in_reading_order`,
`Reads_hyperlinks_and_image_pixels`, `Projects_the_object_graph_read_only`,
`Exports_alto_pagexml_hocr_and_svg`, `Reports_an_unsupported_image_filter`. External: pypdf/PyMuPDF
coordinate comparison, schema validation, `xmllint` where available.

**Exit criteria**: SC-004 passes on the labelled corpus; every exporter is well-formed; the object graph
cannot mutate the document; no existing reader result changes without a documented additive field.

### Phase 6 — Hardening and 1.1

**Deliverables**

- Performance work against the recorded budgets; every new scenario added to `docs/measurements.md`.
- A migration guide that maps PDFsharp and PdfPig jobs to SharpCorePDF examples, including the “use
  PDFsharp/PdfPig still” cases for rendering, OCR and flow layout.
- Manual chapters for drawing, text layout, images, editing, annotations, forms, signatures, PDF/A,
  extraction, layout analysis and object inspection; `docs/README.md` contents updated.
- Examples for every user story, compiled and executed by the existing example runner.
- `docs/parity.md` completed with evidence links; `CHANGELOG.md` 1.1 entry; release checklist G1–G8.
- Package version 1.1.0 and the five packages; the 1.0 line remains supported for fixes.

**Exit criteria**: every success criterion in `spec.md` passes; the 1.0 test suite is unchanged and
green; the parity matrix has no unproven “Must”; the release checklist is signed; the tag is created
only after `ci.yml` is green on the analysed commit.

## Testing Strategy

| Level | What it proves |
| --- | --- |
| Unit | Geometry, metrics, parsers, path serialisation, colour, wrapping and warning identity. |
| Contract | Every new public type has a test that fails when the member or its documented behaviour is removed; `PublicAPI.Unshipped.txt` matches. |
| Integration | A real document is built, edited, signed or read end to end through the public ports and reopened through the library’s own reader. |
| External structure | `qpdf --check` via `pikepdf`, `pypdf` strict parsing, PyMuPDF render/text/image geometry. |
| External conformance | veraPDF for PDF/A, pyHanko and OpenSSL for signatures, `fontTools` for embedded fonts, schema validation for ALTO/PAGE XML/hOCR/SVG. |
| Determinism | The same inputs and fixed dates produce identical bytes on both legs and in an empty-font container. |
| Cross-platform | The suite runs on Windows and Linux with no font directory and no network. |
| Performance | `tools/SharpCorePDF.Measure` records wall time, allocations, retained heap and output size against budgets. |

The test suite never references PDFsharp or PdfPig. Their documentation defines the reference
behaviour; this repository’s fixtures and independent tools prove that the behaviour is matched. That
keeps the product’s “no third-party engine” promise honest and avoids importing an AGPL/commercial
dependency into an MIT repository.

## Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| The scope becomes “all of PDF” and never ships | Release 1.1 is limited to the “Must” rows. A new row enters only by changing the parity matrix and its release gate, not by adding a branch to a phase. |
| PDF/A is declared before it passes | veraPDF is the gate. No profile is advertised until every fixture passes; unsupported colour spaces and fonts are refusals or warnings. |
| Signature trust is confused with integrity | Two separate result fields, two separate tests, and a documentation section that shows both outcomes. Timestamp is caller-supplied, so the library stays offline. |
| Incremental writing breaks an earlier signature | The fixture asserts the original byte range is untouched, and pyHanko validates every revision after the append. |
| Text layout drifts or breaks non-Latin scripts | Greedy wrapping is specified and tested with Latin, accented and right-to-left refusal cases; complex shaping is explicitly out of scope rather than approximated. |
| JPEG/PNG variants produce wrong colours | Pass-through when possible; explicit EXIF/APP14 handling; refusal tests for CMYK, 16-bit and unsupported colour spaces; PyMuPDF pixel comparison. |
| The public object graph becomes a second API to maintain | Read-only projection only; no mutation; a small set of node kinds; all new members are additive and snapshot-reviewed. |
| The 1.0 surface breaks | 1.1 additive minor, `[Obsolete]` instead of removal, the full 1.0 suite unchanged, and an API diff in the release checklist. |
| Performance regresses on merge | The existing measurement gate is re-run in every phase; a regression above 10% blocks the phase. |
| A contributor copies code from PDFsharp/PdfPig | The plan and ADR state capability parity only; the test suite has no reference package; review asks for the source of any algorithm. |

## Complexity Tracking

| Decision | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| Change the product boundary (CA-001) | The requested parity makes creation and editing the product, not a bounded accessory to merging. | Keeping a merge-only product leaves the exact gaps that force callers to PDFsharp/PdfPig; hiding the new capability behind “internal” APIs would make the documentation false. |
| Add a second writer (incremental) | Signatures and multi-revision preservation require appending bytes without rewriting earlier revisions. | A full rewrite invalidates every prior signature and cannot preserve the original byte range; “sign last” cannot work when a later annotation is added. |
| Add a read-only object graph | PdfPig-level inspection requires a caller to audit arbitrary structures without parsing raw bytes. | Exposing the internal mutable model creates a second, unstable API and lets callers corrupt the engine’s invariants. |
| Keep all code in four assemblies | The constitution’s dependency graph is part of the product. | A fifth `SharpCorePDF.Pdf` package would duplicate the object model and change the package shape for a capability that fits Infrastructure. |

## Release Gates

Release 1.1 is tagged only when all of the following are true:

1. **G1 — Boundary**: constitution 2.0.0 and the ADR are merged; README/manual/changelog describe the
   new product and the remaining exclusions.
2. **G2 — Surface**: every new public member is documented, in `PublicAPI.Unshipped.txt`, and covered by
   a contract test; no 1.0 signature is removed.
3. **G3 — Evidence**: every “Must” parity row whose Release cell names the version being tagged has a
   named test and an external validation command; the commands have been run on the release commit.
4. **G4 — Quality**: Release build has zero warnings on both legs; the full suite is green on both legs;
   new code meets the 90% coverage condition.
5. **G5 — Determinism**: the fixed-date fixtures are byte-identical across repeated runs and in a Linux
   container with no fonts.
6. **G6 — Performance**: existing merge/authoring budgets are within 10%; new scenarios have recorded
   budgets in `docs/measurements.md`.
7. **G7 — Documentation**: manual chapters, examples, migration guide and `docs/parity.md` are complete;
   all example code compiles and runs.
8. **G8 — Release**: `CHANGELOG.md` has the 1.1 entry, the release checklist is signed, and `ci.yml` is
   green on the analysed commit before the tag exists.

## Documentation and Migration

- `docs/parity.md` is the public scorecard: capability, reference behaviour, SharpCorePDF outcome,
  release and evidence.
- `docs/migration/pdfsharp.md` and `docs/migration/pdfpig.md` map jobs and show a runnable example for
  each mapped capability; they explicitly say what remains out of scope.
- Manual chapters 14–23 cover drawing, text layout, image formats, editing, annotations, forms,
  signatures, PDF/A, extraction/analysis and object inspection.
- `README.md` changes from “one capability done properly” to “a dependency-free PDF creation,
  editing and reading library”, with the merge story still first and the exclusions still explicit.
- `docs/manual/src/02-what-it-is-not.md` keeps a shorter, sharper boundary: no renderer, OCR, HTML/Office
  conversion, PDF/UA validation, JavaScript execution or MigraDoc flow.

## Out of Scope for This Plan

- A renderer, raster preview, print pipeline or PDF viewer.
- OCR, document understanding or semantic table extraction.
- HTML-to-PDF, PDF-to-Word/HTML or reflow.
- A MigraDoc-compatible layout object model with automatic pagination.
- PDF/UA, PDF/X, WCAG or accessibility certification.
- Public-key encryption, JavaScript execution, 3D, multimedia, geospatial or JPEG 2000.
- Source or binary compatibility with PDFsharp or PdfPig.
- A CLI, hosted service or network client inside the library.

## Next Actions

1. Review and merge **CA-001**; do not start Phase 1 before it lands. **Complete** in `a5d816c`
   plus the ADR, documentation and release-checklist record.
2. Execute Phase 0 tasks T003–T013 in [`tasks.md`](./tasks.md): create `docs/parity.md`, the corpus,
   the API review and the measurement scenarios; run `/speckit-tasks` again only if the design changes.
3. Re-run the 1.0 measurements and record the baseline for the creator and editor scenarios.
4. Start Phase 1 only after the gate in `spec.md` is satisfied.
