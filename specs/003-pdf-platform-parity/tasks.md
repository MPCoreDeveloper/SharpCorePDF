# Tasks: PDF Platform Parity

**Feature**: `003-pdf-platform-parity` | **Plan**: [plan.md](./plan.md) | **Spec**: [spec.md](./spec.md)

**Input**: Design documents from `specs/003-pdf-platform-parity/` — plan.md, spec.md, research.md,
data-model.md, contracts/, quickstart.md and constitution 2.0.0.

**Tests**: Required. FR-021 and Constitution IV make a test that fails on revert part of every
behaviour change; the test tasks below are not optional.

**Organization**: Phase 0 closes the boundary and evidence gate. Phase 1 builds the shared values and
writer that every story needs. Phase 2 (US1) is the MVP. Phases 3–5 deliver US2 editing, US3 reading
and US4 trust. Phase 6 hardens and releases.

**Baseline**: `a5d816c` — Release build 0 warnings/0 errors, 561 tests pass on `net10.0` and
`net11.0`. Do not regress that count or either leg.

**Task count**: 106 tasks. **Stories**: US1 creator (17), US2 editing (20), US3 reading (21), US4
trust (17). **Gate/foundation**: 21 tasks. **Polish**: 10 tasks. **MVP**: T001–T038.

---

## Phase 0: Boundary and Evidence (blocking)

**Purpose**: CA-001 is closed, and every “Must” parity row gains an owner, evidence and a
reproducible corpus before implementation starts.

- [x] T001 Ratify constitution 2.0.0 and record the creation/editing/reading boundary in `.specify/memory/constitution.md`.
- [x] T002 Accept the boundary decision in `docs/adr/0001-pdf-platform-boundary.md`, update `README.md`, `docs/manual/src/00-preface.md`, `docs/manual/src/02-what-it-is-not.md`, `docs/README.md`, `CHANGELOG.md` and `docs/release-checklist.md`.
- [x] T003 [P] Create `docs/parity.md` from the spec matrix: one row per capability with reference behaviour, SharpCorePDF outcome, release, owner, named test and validation command; no “Must” row may be unowned.
- [x] T004 [P] Create `docs/parity/README.md` with the corpus manifest, source and licence for every fixture, and the regeneration command.
- [x] T005 [P] Extend `docs/tools/make-fixtures.py` with `--parity`: deterministic generation of the born-digital corpus, image-filter fixtures, forms, annotations, signatures and PDF/A candidates.
- [x] T006 Generate at least 50 born-digital documents into `tests/SharpCorePDF.Tests/Assets/parity/corpus/` and commit them as binary PDFs.
- [x] T007 [P] Add the human-labelled reading-order subset and expected character/box data under `tests/SharpCorePDF.Tests/Assets/parity/labelled/`.
- [x] T008 [P] Add image fixtures for PNG, JPEG, BMP and GIF variants under `tests/SharpCorePDF.Tests/Assets/parity/images/`, including the variants the contract refuses.
- [x] T009 [P] Add form, annotation, unsigned/signed and PDF/A candidate fixtures under `tests/SharpCorePDF.Tests/Assets/parity/{forms,annotations,signatures,pdfa}/`.
- [x] T010 [P] Add `tests/SharpCorePDF.Tests/ParityCorpusTests.cs`: every manifest entry exists, every PDF opens, and regenerating the corpus produces no unexpected diff.
- [x] T011 [P] Record the Phase 0 API review in `docs/api/1.1-api-review.md`: Domain/Application/Infrastructure placement, immutable values, no mutable public graph, no new package and no `System.Drawing`, `SkiaSharp` or `PdfSharp` reference.
- [x] T012 [P] Add creator and editor scenarios to `tools/SharpCorePDF.Measure/Program.cs` and fixtures to `tools/SharpCorePDF.Measure/ContentFixtures.cs` before implementation.
- [x] T013 Re-read the 1.0 baseline and record the creator/editor budgets in `docs/measurements.md`; no existing budget may regress by more than 10%.

**Checkpoint**: `quickstart.md` step 2 passes; Phase 1 is unblocked.

---

## Phase 1: Shared Foundation

**Purpose**: Values, diagnostics and one content writer shared by authoring and editing.

- [ ] T014 [P] Add `PdfPoint`, `PdfMatrix`, `PdfPath` and `PdfPathSegment` in `src/SharpCorePDF.Domain/ValueObjects/`.
- [ ] T015 [P] Add `PdfPaint`, `PdfStroke`, `PdfGraphicsState`, `PdfLineCap`, `PdfLineJoin`, `PdfFillRule` and `PdfBlendMode` in `src/SharpCorePDF.Domain/ValueObjects/`.
- [ ] T016 [P] Add `PdfImageSource`, `PdfImageColorSpace` and the image option values in `src/SharpCorePDF.Domain/ValueObjects/`.
- [ ] T017 [P] Add `PdfTextStyle`, `PdfTextMetrics`, `PdfTextLayoutOptions`, `PdfTextLayout`, `PdfTextLine` and the alignment/overflow/direction enums in `src/SharpCorePDF.Domain/ValueObjects/`.
- [ ] T018 [P] Add the new `PdfWarningKind` members and `PdfUnsupportedFeatureException` cases required by the data model in `src/SharpCorePDF.Domain/`.
- [ ] T019 Add `ContentStreamBuilder` and `PathWriter` in `src/SharpCorePDF.Infrastructure/Authoring/`, covering `m l c v y h re f S B W n q Q cm gs d J j M G g RG rg K k CS cs SC sc SCN scn ca CA`.
- [ ] T020 Add `GraphicsState` and the resource registry in `src/SharpCorePDF.Infrastructure/Authoring/`, including balanced `q`/`Q`, clipping and `ExtGState` alpha/blend modes.
- [ ] T021 [P] Add failing-first tests for geometry, path serialisation and balanced graphics state in `tests/SharpCorePDF.Tests/PdfContentStreamBuilderTests.cs`.

**Checkpoint**: the writer can serialise a path without a page, and the 1.0 suite is unchanged.

---

## Phase 2: User Story 1 — Build a Real Document (Priority: P1) 🎯 MVP

**Goal**: A ten-page report with vector paths, wrapped text, two typefaces, a JPEG and a PNG, built
in memory and mergeable with existing PDFs.

**Independent Test**: `quickstart.md` step 3 passes: the builder tests are green, the
`creator-parity` example writes the document, and `docs/tools/validate-pdf.py --check-syntax --text
--render --images` accepts it.

### Tests for User Story 1

- [ ] T022 [P] [US1] Add failing tests for path, colour, dash, cap, join, clip and transform operators in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.Drawing.cs`.
- [ ] T023 [P] [US1] Add failing tests for metrics, greedy wrapping, alignment, leading, accents and overflow refusal in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.TextLayout.cs`.
- [ ] T024 [P] [US1] Add failing tests for JPEG pass-through, EXIF orientation, CMYK/APP14 refusal, BMP, GIF first frame and PNG variants in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.Images.cs`.
- [ ] T025 [P] [US1] Add a byte-identity test for the same pages, fonts, images, `CreationDate` and `DocumentId` in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.Determinism.cs`.

### Implementation for User Story 1

- [ ] T026 [US1] Implement `PdfPageCanvas.DrawLine`, `DrawRectangle`, `DrawEllipse`, `DrawPath`, `SaveState`, `RestoreState`, `SetClip` and `SetTransform` in `src/SharpCorePDF.Infrastructure/PdfPageCanvas.cs`.
- [ ] T027 [US1] Implement `DrawTextLayout` and the layout-to-content translation in `src/SharpCorePDF.Infrastructure/Authoring/TextLayoutWriter.cs`.
- [ ] T028 [US1] Extend `PdfTypeface` with `Ascent`, `Descent`, `LineGap`, `CapHeight`, `XHeight`, `Measure` and the `head`/`hhea`/`maxp`/`hmtx`/`cmap` readers in `src/SharpCorePDF.Infrastructure/PdfTypeface.cs` and `src/SharpCorePDF.Infrastructure/Authoring/TrueTypeFont.cs`.
- [ ] T029 [US1] Implement deterministic greedy wrapping, line height, left/center/right/justify alignment and overflow handling in `src/SharpCorePDF.Infrastructure/Authoring/TextLayoutWriter.cs`.
- [ ] T030 [US1] Add `JpegImage` with SOF/SOS parsing, EXIF orientation and Adobe APP14 transform detection in `src/SharpCorePDF.Infrastructure/Authoring/JpegImage.cs`.
- [ ] T031 [US1] Add `BmpImage` and first-frame `GifImage` decoding in `src/SharpCorePDF.Infrastructure/Authoring/BmpImage.cs` and `GifImage.cs`.
- [ ] T032 [US1] Extend `PngImage` for the Phase 1 matrix (interlaced, 16-bit, palette `tRNS`, ICC) in `src/SharpCorePDF.Infrastructure/Authoring/PngImage.cs`; refuse by name what is not supported.
- [ ] T033 [US1] Add `PdfImage.Open/FromJpeg/FromPng/FromBmp/FromGif` and the image registry in `src/SharpCorePDF.Infrastructure/Authoring/PdfImage.cs`; one image drawn twice is one object.
- [ ] T034 [US1] Add `PdfDocumentBuilder.WithMetadata`, `WithXmp`, `WithOutline`, `ImportPage` and `ImportPages` in `src/SharpCorePDF.Infrastructure/PdfDocumentBuilder.cs`; imported pages keep content, resources, annotations and geometry.
- [ ] T035 [US1] Enforce fixed `CreationDate`/`DocumentId` determinism through `PdfAuthoringOptions` and the writer in `src/SharpCorePDF.Infrastructure/PdfAuthoringOptions.cs`.
- [ ] T036 [US1] Add the `creator-parity` scenario to `examples/SharpCorePDF.Examples/Scenarios.cs` and a creator example file beside `AuthoringExamples.cs`.
- [ ] T037 [US1] Write manual chapters `15-drawing.md`, `16-text-layout.md` and `17-image-formats.md`, and update `docs/README.md` and `docs/manual/build-manual.py`.
- [ ] T038 [US1] Run T022–T025 plus `validate-pdf.py --check-syntax --text --render --images`; record the results and close the creator rows in `docs/parity.md`.

**Checkpoint**: US1 is independently shippable; existing merge, inspection and editing tests still pass.

---

## Phase 3: User Story 2 — Edit an Existing Document (Priority: P2)

**Goal**: Rotate, crop, resize, remove, duplicate, reorder, split and append pages; overlay or
underlay content; edit metadata, outlines and labels; add annotations; fill and flatten forms.

**Independent Test**: `quickstart.md` step 4 applies the scripted page edits to the fixture, reopens
the result, checks order/geometry/metadata, and passes `validate-pdf.py --check-syntax --pages
--geometry`. Annotation and form scenarios read their values back in two readers.

### Tests for User Story 2

- [ ] T039 [P] [US2] Add failing tests for crop, resize, import, insert, reorder, split, extract and append validation in `tests/SharpCorePDF.Tests/PdfDocumentEditorTests.Geometry.cs`.
- [ ] T040 [P] [US2] Add failing tests for overlay/underlay layer order, clipping and untouched original content in `tests/SharpCorePDF.Tests/PdfDocumentEditorTests.Overlay.cs`.
- [ ] T041 [P] [US2] Add failing tests for XMP, outline, page-label, viewer-preference and metadata edits in `tests/SharpCorePDF.Tests/PdfDocumentEditorTests.Document.cs`.
- [ ] T042 [P] [US2] Add failing tests for incremental append, prior bytes unchanged, prior signature still covering its revision, and complete-rewrite warnings in `tests/SharpCorePDF.Tests/PdfIncrementalWriterTests.cs`.
- [ ] T043 [P] [US2] Add failing tests for every annotation kind, appearance generation, update/remove and dangling destinations in `tests/SharpCorePDF.Tests/PdfAnnotationTests.cs`.
- [ ] T044 [P] [US2] Add failing tests for text/checkbox/radio/choice/push-button fields, duplicate names, read-only refusal, fill and flatten in `tests/SharpCorePDF.Tests/PdfFormTests.cs`.

### Implementation for User Story 2

- [ ] T045 [US2] Extend `PdfEditPlan` and `PdfEditOperation` with `CropPage`, `ResizePage`, `InsertPage`, `ImportPage`, `Append`, `ReorderPages`, `SplitDocument`, `ExtractPages`, `Overlay`, `Underlay`, `SetXmp`, `SetOutline`, `SetPageLabels` and `SetViewerPreferences` in `src/SharpCorePDF.Domain/Aggregates/PdfEditPlan.cs` and `src/SharpCorePDF.Domain/ValueObjects/PdfEditOperation.cs`.
- [ ] T046 [US2] Implement crop and resize (`Scale` and `Preserve`) with explicit materialised geometry and normalisation warnings in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Pages.cs` and `src/SharpCorePDF.Infrastructure/Editing/PageOperations.cs`.
- [ ] T047 [US2] Implement import, insert, append, reorder, split and extract through the existing object copier in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Pages.cs`.
- [ ] T048 [US2] Implement `Overlay`/`Underlay` as recorded content in front of or behind the original stream, with clipping and `ExtGState` opacity in `src/SharpCorePDF.Infrastructure/Editing/OverlayWriter.cs`.
- [ ] T049 [US2] Implement XMP, outline, page-label and viewer-preference edits in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Document.cs`; a destination to a removed page is removed or redirected with a warning.
- [ ] T050 [US2] Implement `IncrementalWriter` in `src/SharpCorePDF.Infrastructure/Editing/IncrementalWriter.cs`: append a revision, preserve every prior byte, and refuse a non-seekable or changed source.
- [ ] T051 [US2] Add the annotation model and `PdfAnnotations` factories in `src/SharpCorePDF.Domain/ReadModels/PdfAnnotation.cs` and `src/SharpCorePDF.Domain/ValueObjects/`; validate link/markup/stamp/ink rules before the source opens.
- [ ] T052 [US2] Generate appearance streams for every creatable annotation kind in `src/SharpCorePDF.Infrastructure/Annotations/AppearanceStreams.cs` and write `/Annots` changes in `AnnotationWriter.cs`.
- [ ] T053 [US2] Add `AddAnnotation`, `UpdateAnnotation` and `RemoveAnnotation` to the plan and engine; untouched pages keep their objects.
- [ ] T054 [US2] Add `PdfForm`, `PdfFormField` and the field factories in `src/SharpCorePDF.Domain/`, including unique qualified names and value/type validation.
- [ ] T055 [US2] Implement field appearances, `SetFormField`, `ResetForm` and `FlattenForm` in `src/SharpCorePDF.Infrastructure/Forms/FieldAppearance.cs` and `AcroFormWriter.cs`; flatten leaves a page that renders without a form viewer.
- [ ] T056 [US2] Add the `edit-pages`, `watermark`, `annotate` and `fill-form` scenarios to `examples/SharpCorePDF.Examples/Scenarios.cs`.
- [ ] T057 [US2] Write manual chapters `18-annotations.md` and `19-forms.md`; extend `docs/manual/src/14-editing.md` with crop, resize, overlays, incremental updates and document-level edits.
- [ ] T058 [US2] Run T039–T044, `quickstart.md` step 4 and the annotation/form read-back checks; close the US2 rows in `docs/parity.md`.

**Checkpoint**: US2 is independently shippable; US1 and the 1.0 suite still pass.

---

## Phase 4: User Story 3 — Read a Document the Way a Consumer Needs It (Priority: P3)

**Goal**: Letters with positions, configurable words/blocks, reading order, hyperlinks, decoded
image pixels, a read-only object graph and ALTO/PAGE XML/hOCR/SVG exports.

**Independent Test**: `quickstart.md` step 7 scores at least 99.5% character recall, 95% box
agreement and 95% reading-order agreement on the labelled corpus; every export validates.

### Tests for User Story 3

- [ ] T059 [P] [US3] Add failing tests for letter fields, ordering, direction and unreadable-glyph warnings in `tests/SharpCorePDF.Tests/PdfLayoutReaderTests.Letters.cs`.
- [ ] T060 [P] [US3] Add failing tests for each word, segmentation and reading-order strategy, including two-column pages, in `tests/SharpCorePDF.Tests/PdfLayoutReaderTests.Layout.cs`.
- [ ] T061 [P] [US3] Add failing tests for hyperlinks, image pixel decoding and unsupported filters in `tests/SharpCorePDF.Tests/PdfLayoutReaderTests.Hyperlinks.cs`.
- [ ] T062 [P] [US3] Add failing tests for the read-only object graph, dangling references and content-operation offsets in `tests/SharpCorePDF.Tests/PdfObjectInspectorTests.cs`.
- [ ] T063 [P] [US3] Add failing tests for ALTO, PAGE XML, hOCR and SVG text exports in `tests/SharpCorePDF.Tests/PdfLayoutExportTests.cs`.

### Implementation for User Story 3

- [ ] T064 [P] [US3] Add `PdfLetter`, `PdfHyperlink`, `PdfPageSegment` and the word/segmentation/reading-order option enums in `src/SharpCorePDF.Domain/ReadModels/` and `src/SharpCorePDF.Domain/ValueObjects/`.
- [ ] T065 [US3] Add `IPdfLayoutReader` to `src/SharpCorePDF.Domain/Ports/IPdfLayoutReader.cs` and `PdfLayoutReaderService` to `src/SharpCorePDF.Application/Services/`.
- [ ] T066 [US3] Implement `LetterExtractor` over `PdfTextScanner`, `PdfFontDecoder` and `PdfToUnicodeCMap` in `src/SharpCorePDF.Infrastructure/Reading/LetterExtractor.cs`.
- [ ] T067 [US3] Implement `ReadWordsAsync` and the raw/nearest-neighbour strategies in `src/SharpCorePDF.Infrastructure/Reading/LayoutAnalysis/WordExtractor.cs`; a strategy only groups glyphs.
- [ ] T068 [US3] Implement recursive XY cut and Docstrum-style segmentation plus column-aware reading order in `src/SharpCorePDF.Infrastructure/Reading/LayoutAnalysis/`.
- [ ] T069 [US3] Implement `ReadHyperlinksAsync` for URI and internal destinations, with `NamedDestinationUnresolved` warnings in `src/SharpCorePDF.Infrastructure/Reading/HyperlinkReader.cs`.
- [ ] T070 [P] [US3] Add `PdfObjectView` and `PdfContentOperationView` in `src/SharpCorePDF.Domain/ReadModels/`; no setter and no mutable child collection.
- [ ] T071 [US3] Add `IPdfObjectInspector` and `PdfObjectInspectorService`, and implement the projection in `src/SharpCorePDF.Infrastructure/Reading/ObjectGraphView.cs`; dangling references stay visible.
- [ ] T072 [US3] Add optional decoded pixels and `IsDecodable` to the existing image read in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Images.cs` and `src/SharpCorePDF.Infrastructure/Pdf/`.
- [ ] T073 [US3] Implement the four exporters in `src/SharpCorePDF.Infrastructure/Reading/Exporters/`; coordinates are page space, text is escaped, and an empty page still produces a valid element.
- [ ] T074 [US3] Register `IPdfLayoutReader` and `IPdfObjectInspector` in `src/SharpCorePDF.DependencyInjection/SharpCorePDFServiceCollectionExtensions.cs`.
- [ ] T075 [P] [US3] Add `docs/tools/score-layout.py` for character recall, box agreement and reading-order scoring, and a `docs/parity/README.md` section naming the scoring command.
- [ ] T076 [US3] Add `tests/SharpCorePDF.Tests/PdfLayoutCorpusTests.cs` to enforce the SC-004 thresholds over `tests/SharpCorePDF.Tests/Assets/parity/`.
- [ ] T077 [US3] Add the `extract-layout` and `inspect-objects` scenarios to `examples/SharpCorePDF.Examples/Scenarios.cs`.
- [ ] T078 [US3] Write manual chapters `20-extraction.md`, `21-layout-analysis.md` and `22-object-graph.md`, including export limits and the strategy result field.
- [ ] T079 [US3] Run T059–T063, `score-layout.py` and the export validators; close the US3 rows in `docs/parity.md`.

**Checkpoint**: US3 is independently shippable and can run in parallel with US2 after Phase 1.

---

## Phase 5: User Story 4 — Trust the Document (Priority: P4)

**Goal**: Sign with an X.509 certificate, embed a caller-supplied timestamp, validate integrity and
trust separately, preserve earlier signatures across incremental updates, and write PDF/A-1b, -2b
and -3b.

**Independent Test**: `quickstart.md` step 6 signs a fixture, pyHanko reports a valid signature,
a one-byte mutation is rejected, an annotation added afterwards leaves the first signature valid,
and veraPDF reports conformance for all three profiles.

### Tests for User Story 4

- [ ] T080 [P] [US4] Add failing tests for detached CMS signing, byte range, visible appearance, certificate-without-key refusal and prior-revision preservation in `tests/SharpCorePDF.Tests/PdfSignatureTests.cs` (depends on T050).
- [ ] T081 [P] [US4] Add failing tests for valid/invalid/unknown integrity, separate trust, multiple signatures, tampering and unsupported algorithms in `tests/SharpCorePDF.Tests/PdfSignatureValidationTests.cs`.
- [ ] T082 [P] [US4] Add failing tests for PDF/A-1b, -2b and -3b output, encryption conflict, unembeddable font refusal and unsatisfied requirements in `tests/SharpCorePDF.Tests/PdfATests.cs`.

### Implementation for User Story 4

- [ ] T083 [P] [US4] Add `PdfSignatureOptions`, `PdfSignatureInfo`, `PdfSignatureValidation`, `PdfSignatureProblem` and the hash/trust enums in `src/SharpCorePDF.Domain/ValueObjects/` and `src/SharpCorePDF.Domain/ReadModels/`.
- [ ] T084 [US4] Add `IPdfSigner` and `IPdfTimestampProvider` in `src/SharpCorePDF.Domain/Ports/`, and `PdfSignerService` in `src/SharpCorePDF.Application/Services/`.
- [ ] T085 [US4] Implement `CmsSignatureWriter` with `System.Security.Cryptography.Pkcs` in `src/SharpCorePDF.Infrastructure/Signatures/CmsSignatureWriter.cs`; write a detached CMS over the reserved byte range.
- [ ] T086 [US4] Reserve and fill the signature byte range in an incremental revision in `src/SharpCorePDF.Infrastructure/Signatures/SignatureFieldWriter.cs`; the original bytes are untouched.
- [ ] T087 [US4] Implement the optional RFC 3161 timestamp handshake through the caller callback in `src/SharpCorePDF.Infrastructure/Signatures/TimestampEmbedder.cs`; the library opens no socket.
- [ ] T088 [US4] Implement `SignatureValidator` parsing every signature and computing `Integrity` in `src/SharpCorePDF.Infrastructure/Signatures/SignatureValidator.cs`; unreadable structures are `Unknown`, never `Valid`.
- [ ] T089 [US4] Implement certificate-chain trust as a separate result against caller-supplied roots in `src/SharpCorePDF.Infrastructure/Signatures/TrustEvaluator.cs`; no machine store and no online revocation.
- [ ] T090 [US4] Add `PdfAProfile` and `PdfAConversionResult` in `src/SharpCorePDF.Domain/ValueObjects/` and `src/SharpCorePDF.Domain/ReadModels/`.
- [ ] T091 [US4] Implement `PdfAWriter` in `src/SharpCorePDF.Infrastructure/Authoring/PdfAWriter.cs`: embed fonts, write XMP and output intent, and remove or refuse profile-forbidden features.
- [ ] T092 [US4] Enforce profile restrictions and the `IsWritten = false` refusal path in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Document.cs`; never label an unconformant file as PDF/A.
- [ ] T093 [P] [US4] Add `docs/tools/validate-signatures.py` and `docs/tools/validate-pdfa.py` with the pyHanko and veraPDF commands from `quickstart.md`.
- [ ] T094 [US4] Add the `sign-and-archive` scenario to `examples/SharpCorePDF.Examples/Scenarios.cs`, using local certificates and a caller-supplied timestamp token; ship no key.
- [ ] T095 [US4] Write manual chapters `23-signatures.md` and `24-pdf-a.md`; state the integrity/trust split, the no-network rule and the external-validator evidence requirement.
- [ ] T096 [US4] Run T080–T082, pyHanko, veraPDF and the incremental-signature scenario; close the US4 rows in `docs/parity.md`.

**Checkpoint**: US4 is independently shippable once US2's incremental writer exists.

---

## Phase 6: Polish and Release

- [ ] T097 [P] Update every `PublicAPI.Unshipped.txt` for the four layers and add a public-surface contract test that fails on an undocumented addition.
- [ ] T098 [P] Write `docs/migration/pdfsharp.md` and `docs/migration/pdfpig.md`, mapping every parity row to an example or an explicit out-of-scope statement.
- [ ] T099 [P] Complete the manual index, rebuild `docs/SharpCorePDF-Manual-1.1.0.pdf` from 25 chapters, and verify the contents, bookmarks and page count.
- [ ] T100 [P] Update `examples/README.md` and run every example scenario; each must print its success marker and exit 0.
- [ ] T101 Re-run the creator, editor, extraction and merge measurements; record them in `docs/measurements.md` and block any regression above 10%.
- [ ] T102 Run the determinism, empty-font, no-network and no-new-package checks from `quickstart.md` step 8.
- [ ] T103 Run the full Release build and both test legs; require 0 warnings, 0 errors, all 1.0 tests unchanged and at least 90% coverage on new code.
- [ ] T104 Run every `quickstart.md` phase command on the release commit and record the commands, versions and outputs in the pull request.
- [ ] T105 Confirm `docs/parity.md` has no unevidenced “Must” row, then update the 1.1 entry in `CHANGELOG.md`, bump `<Version>`, and complete `docs/release-checklist.md` G1–G9.
- [ ] T106 Run `git diff --check`, confirm the tree is clean apart from the release commit, and tag only the commit whose SonarCloud analysis is the newest on `main`.

---

## Dependencies and Execution Order

### Phase dependencies

- **Phase 0** blocks every implementation phase. A parity row without evidence is not done.
- **Phase 1** blocks US1, US2 and US3. US4 also needs T050 from US2.
- **US1 (P1)** is the MVP: Phase 0 + Phase 1 + T022–T038.
- **US2 (P2)** starts after Phase 1; T050 (incremental writer) blocks US4.
- **US3 (P3)** starts after Phase 1 and may run in parallel with US2 and US4.
- **US4 (P4)** starts after T050; signing and PDF/A can then run in parallel.
- **Phase 6** starts only when the desired stories are complete.

### Story dependencies

- **US1**: no dependency on another story.
- **US2**: may integrate with US1 canvases and images but is testable alone.
- **US3**: independent of US1/US2; reuses the existing `PdfTextScanner`.
- **US4**: depends on US2's `IncrementalWriter`; PDF/A also reuses US1 font and image work.

### Within each story

Tests first, then Domain values, then Infrastructure, then Application/DI, then examples and docs.
A test must fail before its implementation task starts.

### Parallel opportunities

- Phase 0: T003–T005 and T007–T012 are separate files.
- Phase 1: T014–T018 are independent value objects.
- US1: T022–T025 in parallel; JPEG, BMP/GIF and PNG work are separate files.
- US2: T039–T044 tests in parallel; annotations (T051–T053) and forms (T054–T055) are separate files.
- US3: T059–T063 tests in parallel; object inspection (T070–T071) is independent of layout analysis.
- US4: signatures (T083–T089) and PDF/A (T090–T092) are separate after T050.

---

## Implementation Strategy

### MVP first

1. Finish Phase 0 evidence and Phase 1 foundation.
2. Deliver US1 only (T022–T038).
3. Stop and validate `quickstart.md` step 3 independently.
4. Ship the creator preview only when the evidence is recorded.

### Incremental delivery

1. Phase 0 + Phase 1 → foundation ready.
2. US1 → creator parity, preview 1.
3. US2 → editing, annotations and forms, preview 2–3.
4. US4 → signatures and PDF/A, preview 4.
5. US3 → reading and analysis, preview 5.
6. Phase 6 → hardening and 1.1.

### Parallel team

After Phase 1, one developer can take US1, one US2, one US3, and one US4 once T050 lands. Phase 0
must be finished by the whole team first; no story may add a fixture outside the corpus generator.

---

## Notes

- Page numbers are one-based; a page reference resolves after all preceding plan operations.
- Stream bytes are copied verbatim unless the operation requires a rewrite.
- Every lossy decision becomes a `PdfWarning` with a stable `Kind`; never a silent drop.
- A public addition goes into `PublicAPI.Unshipped.txt` in the same commit.
- No new package, native binary, `System.Drawing`, `SkiaSharp` or `PdfSharp`/`PdfPig` reference.
- No network in the library; timestamps are caller-supplied callbacks.
- Commit after each logical group; stop at every checkpoint and run the phase command.




