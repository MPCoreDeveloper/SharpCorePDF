# Tasks: PDF Content Inspection

**Input**: Design documents from `/specs/001-pdf-content-inspection/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md), `.specify/memory/constitution.md`

**Tests**: included and required. Constitution Principle IV ("A Test That Fails on Revert") and `plan.md` Prerequisites row IV both make a named regression test per behaviour part of the change; the test tasks below name the test that carries each behaviour.

**Status of the boxes**: this is a *retrospective* specification (spec.md, Assumptions). `[x]` means the task is present in the working tree and its evidence was reproduced while this file was generated — `PdfContentReaderTests` reports `Total: 50, Errors: 0, Failed: 0` on `net11.0` and on `net10.0/win-x64`, and the full `net11.0` suite reports `Total: 497, Errors: 0, Failed: 0`. `[ ]` means outstanding. The fifty-two tasks below include the documentation corrections `plan.md` requires under Principle VII (Phase 10), which are now done; the only open box is T051, the human review of the specification's wording.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task delivers (`US1` … `US7`); absent in Setup, Foundational and Polish
- Every task names the exact file it touches

## Path Conventions

Single project, library layout from `plan.md`: `src/<layer>/…`, `tests/SharpCorePDF.Tests/…`, `docs/…`, `examples/SharpCorePDF.Examples/…`, `specs/001-pdf-content-inspection/…`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the repository facts every later task builds on. No project is created: the feature extends the four existing layers (`plan.md`, Structure Decision).

- [x] T001 Confirm the layered solution and its build gate in `SharpCorePDF.slnx` and `src/Directory.Build.props`: both legs (`net10.0`, `net11.0`), nullable enabled, `SonarAnalyzer.CSharp` active, `RS0016`/`RS0017` promoted to errors.
- [x] T002 [P] Confirm the test host in `tests/SharpCorePDF.Tests/SharpCorePDF.Tests.csproj` and `global.json`: xUnit v3 4.0.0 on Microsoft.Testing.Platform 2.4.0, both target frameworks, and the `net10.0` leg buildable only with `-r win-x64`.
- [x] T003 [P] Confirm the design inputs exist under `specs/001-pdf-content-inspection/`: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/content-reader.md`, `contracts/page-content-reader.md`, `contracts/payload-budget.md`, `contracts/warnings.md`, `quickstart.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the vocabulary, ports, engine plumbing and payload rule all seven capabilities need. No user story can be built or tested before this phase is complete.

**⚠️ CRITICAL**: no user story work begins until T004–T024 are done.

### Domain vocabulary

- [x] T004 [P] Add warning kinds 26–35 to `src/SharpCorePDF.Domain/ValueObjects/PdfWarningKind.cs`: `ContentStreamUnreadable`, `ImageFilterUnsupported`, `ImageDataUnreadable`, `XObjectResourceMissing`, `NamedDestinationUnresolved`, `EmbeddedFileUnreadable`, `EmbeddedFileNameCollision`, `XmpPacketMalformed`, `FormFieldUnreadable`, `AnnotationUnreadable`. Reuse `BookmarkSkipped`, `BookmarksTruncated`, `BookmarkDestinationDropped`, `EncryptionNotOpened` and `EncryptionOpened` unchanged.
- [x] T005 [P] Add `PdfReadResult<T>` in `src/SharpCorePDF.Domain/ReadModels/PdfReadResult.cs`: `Items` (`IReadOnlyList<T>`, never null), `Warnings` (`IReadOnlyList<PdfWarning>`, defaults empty) and derived `Count`. "Nothing found" is empty items with no warnings; "nothing could be read" is empty items with at least one warning.
- [x] T006 [P] Add the image read models in `src/SharpCorePDF.Domain/ReadModels/PdfImageInfo.cs` and `PdfImageMask.cs`: `Id` required (`"12 0"`, `"inline:3:2"`, `"resource:3:1"`), `Pages` distinct and ascending, `Width`/`Height`/`BitsPerComponent`/`ColorSpace`/`Filter` nullable, `ByteLength` truthful, `IsBuffered`/`Data`, optional `Mask` and `Position`; mask `Kind` required (`SoftMask`/`ExplicitMask`/`ColorKey`), `ColorKey` empty for an image mask, `ByteLength == 0` for a colour key.
- [x] T007 [P] Add the annotation and destination read models in `src/SharpCorePDF.Domain/ReadModels/PdfAnnotationInfo.cs` and `PdfDestination.cs`: `PageNumber` and `Subtype` required, plus `Rectangle`, `Flags`, `Contents`, `Author`, `ModificationDate`, `HasAppearance`, `Name`, `Target` and derived `IsLink`; a destination states exactly one of internal page, external URI, external file or unresolved name, with derived `IsInternal`, `IsExternal`, `IsUnresolved`.
- [x] T008 [P] Add the form read models in `src/SharpCorePDF.Domain/ReadModels/PdfFormFieldInfo.cs`: `PdfFormFieldInfo` (`Name` required and fully qualified `parent.child`, `FieldType` one of `Tx`/`Btn`/`Ch`/`Sig` or null, `Value` null for a signature and for an absent value, `DefaultValue`, `Flags`, `Widgets`, derived `IsReadOnly` from flag bit 1 and `IsRequired` from flag bit 2) and `PdfWidgetInfo` (`PageNumber` nullable, `Rectangle` nullable).
- [x] T009 [P] Add the embedded-file, outline and page-detail read models in `src/SharpCorePDF.Domain/ReadModels/PdfEmbeddedFileInfo.cs`, `PdfOutlineItem.cs` and `PdfPageDetail.cs`: file `Id`/`Name` required with `Id` unique inside one read; bookmark `Title` required, `IsOpen` defaulting to `true`, recursive `Children`; page detail requiring `PageNumber`, `Size` and `Rotation`, with `Images` in draw order, `Annotations`, `FormFields`, own `Warnings`, derived `Links` and `DisplaySize = Size.Rotate(Rotation)`.
- [x] T010 [P] Add the XMP read models in `src/SharpCorePDF.Domain/ReadModels/PdfXmpResult.cs` and `PdfXmpMetadata.cs`: result `Metadata` nullable plus `Warnings`; metadata `RawPacket` and `Text` required, `Properties` keyed by prefixed names with `Ordinal` comparison, derived `Title`/`Producer`/`CreatorTool`.

### Ports and adapters

- [x] T011 Define `IPdfContentReader` in `src/SharpCorePDF.Domain/Ports/IPdfContentReader.cs` with exactly ten members and no overloads: `ReadImagesAsync`, `CopyImageDataAsync`, `CopyImageMaskDataAsync`, `ReadAnnotationsAsync`, `ReadFormFieldsAsync`, `ReadMetadataAsync` (→ `PdfXmpResult`), `ReadEmbeddedFilesAsync`, `CopyEmbeddedFileDataAsync`, `ReadOutlineAsync`, `ReadPageDetailsAsync` (→ `IAsyncEnumerable<PdfPageDetail>`); every member ends `string? password = null, CancellationToken cancellationToken = default` and takes `IPdfContentSource` first.
- [x] T012 [P] Extend `src/SharpCorePDF.Infrastructure/PdfEngineOptions.cs` with `MaxInlineContentBytes`, default `256 * 1024`, and make `Validate()` report a negative value at configuration time, before any document is opened.
- [x] T013 Define the engine-side twin `IPdfPageContentReader` in `src/SharpCorePDF.Application/Ports/IPdfPageContentReader.cs`: the same ten capabilities taking `(string sourceName, Stream content, …)`, `ArgumentException` when `sourceName` is null, empty or whitespace, and no extension conveniences.
- [x] T014 Implement the adapter `PdfContentReaderService` in `src/SharpCorePDF.Application/Services/PdfContentReaderService.cs`: turn an `IPdfContentSource` into a seekable stream positioned at the document's first byte, keep the `try/finally` that releases it, throw `ArgumentNullException` for a null source before the document is touched and `ArgumentException` for a destination that is not writable.
- [x] T015 Implement the payload rule and the copy routes in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.cs` (shared helper), `PdfPageContentReader.Images.cs` and `PdfPageContentReader.Metadata.cs`: compare with `<=`, buffer exactly at the budget, withhold one byte above it with a truthful `ByteLength` and `IsBuffered = false`, raise **no** warning, and stream copies through the pooled scratch buffer (`src/SharpCorePDF.Infrastructure/Pdf/PooledByteBuffer.cs`) flushing and leaving the destination open.
- [x] T016 [P] Write the budget and copy-route tests in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Budget.cs`: `Carries_a_payload_exactly_at_the_budget`, `Leaves_a_payload_above_the_budget_out_of_the_record`, `Carries_nothing_when_the_budget_is_zero`, `Copies_an_unbuffered_payload_through_the_copy_route`, `Returns_a_damaged_image_with_a_warning`, `Rejects_a_negative_inline_budget`.

### Engine plumbing

- [x] T017 [P] Implement `src/SharpCorePDF.Infrastructure/Pdf/PdfContentScanner.cs` and `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Content.cs`: tokenise a content stream, track the graphics state and CTM for `cm`/`q`/`Q`/`Do`/`BI…ID…EI`, resolve XObject resources, and report `ContentStreamUnreadable`/`XObjectResourceMissing` per page instead of throwing.
- [x] T018 [P] Implement the decoding policy in `src/SharpCorePDF.Infrastructure/Pdf/PdfImageFilters.cs`: pass `DCTDecode` and `JPXDecode` through untouched, decode the filters the library already supports (`FlateDecode`, `LZWDecode`, `ASCII85Decode`, `ASCIIHexDecode`, with predictors), and set `IsDecodable = false` with `ImageFilterUnsupported` for anything else.
- [x] T019 Register the pair in `src/SharpCorePDF.DependencyInjection/SharpCorePDFServiceCollectionExtensions.cs` with `TryAddSingleton`: `PdfPageContentReader` over `PdfEngineOptions`, `IPdfPageContentReader` resolved from it, and `IPdfContentReader` → `PdfContentReaderService`. The DI package's `PublicAPI.Unshipped.txt` must not change.
- [x] T020 [P] Add the new surface to `src/SharpCorePDF.Domain/PublicAPI.Unshipped.txt`, `src/SharpCorePDF.Application/PublicAPI.Unshipped.txt` and `src/SharpCorePDF.Infrastructure/PublicAPI.Unshipped.txt`; promote nothing to `PublicAPI.Shipped.txt`.
- [x] T021 [P] Write `tests/SharpCorePDF.Tests/PdfContentFixtures.cs`: build every content fixture in code (two-page image document with JPEG, Flate image and soft mask; JPX image; inline image; form; outline; malformed XMP packet; oversized payload) so an assertion can name the exact byte, filter or destination; take only the encrypted cases from `tests/SharpCorePDF.Tests/TestAssets.cs`.
- [x] T022 Implement `src/SharpCorePDF.Application/Extensions/PdfContentReaderExtensions.cs`: single-page `ReadImagesAsync`/`ReadAnnotationsAsync`, `ReadImageDataAsync`, `SaveImageDataAsync`, `ReadEmbeddedFileDataAsync`, `SaveEmbeddedFileAsync` and `ReadPageDetailAsync`; document that the materialising reads are unsuitable for large payloads and that `Save*` creates the directory and overwrites an existing file.
- [x] T023 [P] Write `tests/SharpCorePDF.Tests/PdfSourceContentTests.cs`: a file, a byte buffer and a stream yield the same document (`Reads_a_document_from_a_file`, `Reads_a_document_from_bytes`, `Reads_a_document_from_a_stream`), plus `Reports_a_cancelled_read`, `Reports_a_file_that_is_not_there`, `Refuses_a_file_without_a_path` and `Reports_the_name_and_the_size_it_knows`.
- [x] T024 [P] Keep `tests/SharpCorePDF.Tests/PdfWarningTests.cs` asserting that a warning carries its kind and a non-empty text, and refuses a missing or empty message.

**Checkpoint**: vocabulary, ports, page walk, decoding policy, payload rule and registration exist — every user story below can be implemented and tested on its own.

---

## Phase 3: User Story 1 - See every image a document paints (Priority: P1) 🎯 MVP

**Goal**: one entry per distinct image object for a page range, naming every page that draws it, with the first-draw position and — within budget — the stored bytes.

**Independent Test**: read the fixture that draws one image object on two pages and stores another as a JPEG. Expect two entries, the shared one naming both pages with the first draw's position, and JPEG bytes identical to the stored stream.

### Tests for User Story 1

- [x] T025 [P] [US1] `Returns_each_image_once_with_every_page_that_draws_it`, `Reads_only_the_pages_of_the_range`, `Reports_the_position_of_the_first_draw` and `Does_not_throw_for_a_range_past_the_last_page` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Images.cs`.
- [x] T026 [P] [US1] `Returns_a_jpeg_untouched` and `Keeps_the_jpeg_alive_after_everything_else_is_gone` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Images.cs`: bytes byte-identical to the stored `DCTDecode` stream and still valid after the source is gone.
- [x] T027 [P] [US1] `Reports_an_unsupported_filter_with_exactly_one_warning` and `Returns_an_inline_image_with_a_synthetic_identifier` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Images.cs`: one `ImageFilterUnsupported` warning naming the filter, every other image still returned, and an `Id` of the form `inline:3:2`.
- [x] T028 [P] [US1] `Decodes_the_samples_and_returns_the_soft_mask_separately` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Images.cs`: the mask reported beside the colour samples, never blended into them.
- [x] T029 [P] [US1] `Returns_the_same_result_twice`, `Leaves_the_source_untouched`, `Reads_an_encrypted_document_with_its_password` and `Reports_an_encrypted_document_it_cannot_open` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Images.cs`: deterministic records, a byte-identical fixture before and after, and an unopenable document reported as an empty result plus `EncryptionNotOpened` without raising.

### Implementation for User Story 1

- [x] T030 [US1] Implement `ReadImagesAsync` and `CopyImageDataAsync` in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Images.cs`: walk only the pages of the range, de-duplicate by object number (plus a per-page ordinal for an inline image and `resource:` for a reference-less value), sort `Pages` ascending, record the `Position` of the first draw, keep geometry, filter and decodability identical whether or not the payload was captured, and apply the budget from T015.

**Checkpoint**: User Story 1 alone answers "which images does this range paint, on which pages, how large, and may I have the bytes" — the MVP.

---

## Phase 4: User Story 2 - Read what reviewers wrote on the pages (Priority: P2)

**Goal**: every annotation of a range with its page, subtype, geometry, text, author and date; links resolved to a page or an address; an unresolved name reported without losing the annotation.

**Independent Test**: read the fixture carrying a highlight, a text note and a link with a named destination. Expect three annotations, resolved target pages, and one warning for the destination that was deliberately broken.

- [x] T031 [P] [US2] `Returns_every_annotation_with_its_geometry_and_text`, `Resolves_direct_and_named_destinations`, `Resolves_an_external_link_to_its_uri`, `Reports_a_named_destination_that_does_not_resolve` and `Keeps_an_unknown_subtype` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Annotations.cs`.
- [x] T032 [US2] Implement `ReadAnnotationsAsync` in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Annotations.cs`: page order then the order the page states, `Subtype` verbatim (`Text`, `Highlight`, `Stamp`, `Link`, anything else unnormalised), `HasAppearance` without rendering, destination resolution through the `/Dests` name tree indexed once per call, and exactly one `NamedDestinationUnresolved` warning per annotation that keeps its place in the result with `Target == null`.

**Checkpoint**: review state and link integrity are answerable without any other capability.

---

## Phase 5: User Story 3 - Read the interactive form without filling it (Priority: P3)

**Goal**: every field with its fully-qualified name, type, value, default, flags and widgets; nothing written.

**Independent Test**: read the fixture with a two-level field hierarchy, a field with two widgets and a signature field. Expect fully-qualified names, both widgets with their pages, and no value written to any field.

- [x] T033 [P] [US3] `Returns_the_field_tree_with_qualified_names`, `Returns_every_widget_of_a_field`, `Reports_a_signature_field_without_interpreting_it`, `Leaves_an_absent_value_null` and `Returns_an_empty_result_for_a_document_without_a_form` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Form.cs`; the last one also asserts zero warnings.
- [x] T034 [US3] Implement `ReadFormFieldsAsync` in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Form.cs`: walk `/AcroForm /Fields` in document order, join the hierarchy into `parent.child`, inherit `FieldType` and `Flags` from ancestors, collect every widget with its page and rectangle, leave `Value` null for a signature and for an absent `/V` (never `""`), report `FormFieldUnreadable` for a field that could not be read, and write nothing.

**Checkpoint**: a caller can inventory signatures and values, and a formless document yields an empty result with no warning.

---

## Phase 6: User Story 4 - Index a long document one page at a time (Priority: P4)

**Goal**: one `PdfPageDetail` per page of a range, lazily, with the page's own warnings; stopping early never reads the rest.

**Independent Test**: enumerate a 200-page fixture, stop after page 1, and confirm that only one page's records were produced and that a blank page still appears with empty collections.

- [x] T035 [P] [US4] `Yields_one_detail_per_page`, `Yields_a_page_with_nothing_on_it_with_empty_collections`, `Yields_a_detail_for_a_damaged_image`, `Carries_the_form_widgets_of_the_page`, `Yields_nothing_for_a_range_past_the_last_page`, `Gathers_annotations_and_links` and `Reports_pages_of_a_document_whose_content_could_not_be_decrypted` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Pages.cs`.
- [x] T036 [US4] Implement `ReadPageDetailsAsync` in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Pages.cs`: open the cross-reference table, object cache and page tree once per enumeration, yield a page only after it is read and never pre-read the next, carry `Size`, `Rotation`, `DisplaySize`, draw-order `Images`, `Annotations`/`Links`, the fields with a widget on that page and only that page's warnings; fold document-wide form-field warnings into the first page; keep `EncryptionNotOpened` off pages — an unopenable encrypted document still yields the pages the page tree describes, each with empty collections and `ContentStreamUnreadable`; release the document when the enumerator is disposed and honour cancellation between pages.

**Checkpoint**: the index runs in one page's worth of memory and stops when the caller stops.

**Note**: US4 gathers images, annotations and form fields, so its implementation follows T030, T032 and T034; the story is still independently testable through `PdfPageDetail`.

---

## Phase 7: User Story 5 - Read the bookmark tree (Priority: P5)

**Goal**: top-level bookmarks in document order with nested children, resolved destinations and the stored open or closed state.

**Independent Test**: read the fixture with two top-level bookmarks, one of them closed with a child and one pointing at a web address. Expect the tree in order, the closed state preserved and the external target returned as stated.

- [x] T037 [P] [US5] `Returns_the_tree_in_document_order`, `Resolves_a_direct_destination`, `Resolves_a_named_destination`, `Reports_a_bookmark_whose_named_destination_does_not_resolve`, `Keeps_a_closed_branch_closed`, `Returns_an_external_bookmark_as_external` and `Returns_an_empty_result_for_a_document_without_an_outline` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Outline.cs`.
- [x] T038 [US5] Implement `ReadOutlineAsync` in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Outline.cs`: sibling order as stored, `IsOpen` from the count/flag pair with `true` as the default, destinations resolved through the shared name index, an unresolved name kept with one `NamedDestinationUnresolved` warning, externals reported as external, depth bounded with `BookmarkSkipped` (and `BookmarksTruncated` when the limit cuts a branch) so a cyclic outline terminates, and an empty result with no warning for a document without an outline.

**Checkpoint**: navigation is answerable independently of page content.

---

## Phase 8: User Story 6 - Take the files a document carries (Priority: P6)

**Goal**: one entry per attachment in document order with a distinct identifier, its metadata and a truthful length, extractable inline or through the copy route.

**Independent Test**: read the fixture with two attachments, one named twice, and extract both. Expect distinct identifiers, a collision warning for the duplicate name, and bytes identical to the originals.

- [x] T039 [P] [US6] `Returns_every_embedded_file_with_a_distinct_identifier`, `Extracts_an_embedded_file_byte_exact`, `Copies_an_embedded_file_through_the_copy_route` and `Returns_an_empty_result_for_a_document_without_embedded_files` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Metadata.cs`.
- [x] T040 [US6] Implement `ReadEmbeddedFilesAsync` and `CopyEmbeddedFileDataAsync` in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Metadata.cs`: walk the embedded-file name tree in the order the document states, return the bytes as stored once decompressed, disambiguate a repeated filename with a numeric suffix so `Id` stays unique while raising exactly one `EmbeddedFileNameCollision` warning, report `EmbeddedFileUnreadable` for a file that cannot be read without dropping its siblings, keep `ByteLength` truthful when the payload is withheld, and flush the copy destination without disposing it.

**Checkpoint**: attachments are addressable and extractable, and a document without attachments returns an empty result.

---

## Phase 9: User Story 7 - Read the document's own metadata (Priority: P7)

**Goal**: the XMP packet byte-exact beside the values parsed from it, with absent, malformed and parsed as three distinguishable answers.

**Independent Test**: read the fixture whose packet is known, compare the returned packet with the stored bytes and confirm the parsed values; then read the fixture with no packet and expect no metadata.

- [x] T041 [P] [US7] `Returns_the_packet_byte_exact_and_its_values`, `Returns_null_metadata_for_a_document_without_a_packet` and `Reports_a_packet_that_is_not_xml_and_keeps_the_bytes` in `tests/SharpCorePDF.Tests/PdfContentReaderTests.Metadata.cs`.
- [x] T042 [US7] Implement `ReadMetadataAsync` in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Metadata.cs`: return the `/Metadata` stream after decompression as `RawPacket` with `Text` decoded as UTF-8, parse the `dc:`, `pdf:` and `xmp:` values into `Properties` with `Ordinal` keys, return `Metadata == null` with no warning when the stream is absent, and for a malformed packet return a non-null `Metadata` with `RawPacket` intact, empty `Properties` and exactly one `XmpPacketMalformed` warning.

**Checkpoint**: provenance is readable, and "no packet" never looks like "unparsable packet".

**Note**: T040 and T042 both edit `PdfPageContentReader.Metadata.cs`; they are separate capabilities but must not be worked in parallel on that file.

---

## Phase 10: Polish and Cross-Cutting Concerns

**Purpose**: the corrections `plan.md` requires under Principle VII, plus the gates that decide whether the
change may be reviewed. The documentation rows are tasks because a caller reading the current text is
misled, not because a release note is missing.

- [X] T043 [P] Add the content-reading paragraph to `README.md`: `IPdfContentReader` and its seven reads, the two copy routes and the four byte helpers, the `PdfReadResult<T>` shape, and the 256 KiB inline budget in the same breath.
- [X] T044 [P] Extend the `README.md` package table: `IPdfContentReader` and the content read models in the Domain row, `PdfContentReaderService` and `PdfContentReaderExtensions` in the Application row, `PdfPageContentReader` and the content scanner in the Infrastructure row. Also resolve the reader in the dependency-injection snippet.
- [X] T045 [P] Correct `docs/manual/src/08-inspection.md`: the opening now calls inspection the *summary* read side and points at the content reader; a new section covers the seven capabilities, `PdfReadResult<T>` and the two empty states, the inline budget and copy routes, one page at a time with early abandonment, and the ten content-reading warning kinds.
- [X] T046 Rebuild `docs/SharpCorePDF-Manual-1.0.0.pdf` with `python docs/manual/build-manual.py`: 13 chapters, 101 headings, contents lists 101, footer ok, 55 pages. `docs/README.md` records the new page count.
- [X] T047 [P] Add the executable example: `ContentSampleDocument.cs` writes `RichContent.pdf` (two pages, an image, a comment, a link, a form field, a bookmark, an attachment and an XMP packet); `ContentInspectionExamples.cs` reads every capability and extracts the payloads; `ExampleContext` exposes `IPdfContentReader` and the sample path; `Scenarios` registers `read-content`; `examples/README.md` lists all of it.
- [X] T048 [P] Record the read-side measurements in `docs/measurements.md` §7 and add the `--content` mode to `tools/SharpCorePDF.Measure` with `ContentFixtures.cs`: 200 pages at 2,161 KB / 9.48 ms, abandonment after 10 pages at 1,100 KB / 3.40 ms, and payload copies of 1, 10 and 50 MB through the copy route.
- [X] T049 Confirm the public API snapshot needs no edit: `RS0016`/`RS0017` are errors in `src/Directory.Build.props`, and the solution builds with zero warnings, so every shipped member is already declared in `PublicAPI.Unshipped.txt`.
- [X] T050 Validate the change: solution build green on both legs with zero warnings; `PdfContentReaderTests` 50/50 green on `net11.0` and on `net10.0/win-x64`; the full `net11.0` suite 497/497 green; `read-content` ran and printed every read model.
- [ ] T051 Review `spec.md` FR-036 and the edge-case list for wording a second reader can accept, then open the pull request. This is a human gate: the code and the documents are done, and the specification's wording is the one thing this task list cannot verify against itself. Two questions to settle while reviewing: whether "one warning that says why" in FR-036 means *exactly* one even when the reader also carries repair warnings from the source, and whether "each with a warning in place of the content it could not decode" covers a page whose content is blocked by encryption but whose annotations are still readable.
- [X] T052 Confirm the `CHANGELOG.md` `[Unreleased]` entry already describes the reader, the payload rule and the ten warning kinds, so the release notes do not need a second entry for this change. *(Superseded by T053: the text increment adds two `Added` bullets and makes the warning-kind count eleven.)*
- [X] T053 Record the text-reading increment in the same documents, because the capability was added after this task list was written and every one of them would otherwise describe a reader that cannot read text: `spec.md` gains FR-040 to FR-046, SC-013, the three text entities and a corrected out-of-scope line; `data-model.md` gains the capability row, `PdfTextBlock`/`PdfWord`/`PdfGlyph` and INV-17 to INV-19; `contracts/content-reader.md` gains the eleventh member, its contract section, the single-page extension and conformance item 11; the manual gains §*Reading the text* in Chapter 8, `TextUnreadable` in the warning table and the corrected boundary in Chapter 2; the README, `Scenarios`, `ContentSampleDocument`, `ContentInspectionExamples` and `examples/README.md` gain the text read; and `docs/measurements.md` §7 gains the two text rows with `ContentFixtures.Text` and the `--content` harness (16,400 words over 200 pages at 26,854 KB and 98.59 ms, ten pages at 2,302 KB and 8.07 ms). The manual is rebuilt with the new section: 13 chapters, 103 headings and 56 pages.

**Checkpoint**: the manual, the README, the examples and the measurement record agree with the code, and the
only open box is the human review.

---

## Dependencies and Execution Order

### Phase dependencies

- **Phase 1 → Phase 2**: the vocabulary, the ports, the adapter, the payload rule and the engine plumbing block every story. No story is testable before T004–T024.
- **Phase 3 (US1, the MVP)** depends only on Phase 2. It can ship alone.
- **Phase 4 (US2)** depends on T007 (annotation and destination models) and the shared destination resolution from T016; it does not depend on US1.
- **Phase 5 (US3)** depends on T008 (form models) and the page walk; it does not depend on images or annotations.
- **Phase 6 (US4)** is the integration point: `PdfPageDetail` composes what the other readers find, so it depends on T030, T032 and T034.
- **Phase 7 (US5)** depends on T009 and the same destination resolution US2 uses.
- **Phase 8 (US6)** and **Phase 9 (US7)** depend on T009 and T010 and on the stream helpers in `PdfPageContentReader.cs`; T040 and T042 edit the same file and must run in order.
- **Phase 10** depends on Phases 3–9, because it documents behaviour that has to exist first.

### Within each story

1. Tests first (the story's `[P]` test task), then the implementation task.
2. The implementation task carries the file, so two stories that only touch different files can proceed at once.
3. `[P]` marks tasks with no dependency on an incomplete task **and** a different file. Tasks in the same
   file are never `[P]`, which is why T040 and T042 are called out.


## Parallel Execution Examples

```text
# After Phase 2, five stories can be tested at once — one file each:
US2 T031  tests/SharpCorePDF.Tests/PdfContentReaderTests.Annotations.cs
US3 T033  tests/SharpCorePDF.Tests/PdfContentReaderTests.Form.cs
US4 T035  tests/SharpCorePDF.Tests/PdfContentReaderTests.Pages.cs
US5 T037  tests/SharpCorePDF.Tests/PdfContentReaderTests.Outline.cs
US6 T039  tests/SharpCorePDF.Tests/PdfContentReaderTests.Metadata.cs

# US1's tests are one file, so T025–T029 are sequential by file, not by story.

# Once the tests of a story are green, the implementations are one file each and can run together:
T030  src/SharpCorePDF.Infrastructure/PdfPageContentReader.Images.cs
T032  src/SharpCorePDF.Infrastructure/PdfPageContentReader.Annotations.cs
T034  src/SharpCorePDF.Infrastructure/PdfPageContentReader.Form.cs
T036  src/SharpCorePDF.Infrastructure/PdfPageContentReader.Pages.cs
T038  src/SharpCorePDF.Infrastructure/PdfPageContentReader.Outline.cs

# Then, strictly in this order, because it is one file:
T040  PdfPageContentReader.Metadata.cs   (embedded files)
T042  PdfPageContentReader.Metadata.cs   (XMP)

# Phase 10 after the code is settled, four different files at once:
T043 README.md   T045 docs/manual/src/08-inspection.md   T047 examples/   T048 docs/measurements.md
# T046 follows T045, because the PDF is generated from the chapter.
```

## Implementation Strategy

**MVP**: US1 — T025 to T030. "Which images does this range paint, on which pages, how large, and may I have
the bytes" is the question the port exists to answer, and it exercises the inline budget, the copy route and
the per-capability warning set. It is the smallest slice that can be published.

**Increment order**: US1 (images) → US2 (annotations) → US3 (form) → US4 (page details, the integration
point) → US5 (outline) → US6 (attachments) → US7 (XMP). Each step adds one partial file and one test file,
and US4 is the first step whose tests read records the three previous steps produced.

**Retrospective note**: the boxes record what was verified while this file was written, not what was
planned; the code landed before the list did. Every `[x]` was reproduced (see the record below), and the one
`[ ]` is a review gate that a machine cannot close on its own.

## Verification record

| Command | Result |
| --- | --- |
| `dotnet build .\SharpCorePDF.slnx -c Release` | Build succeeded, 0 warnings, 0 errors, both legs, `RS0016`/`RS0017` enforced |
| `dotnet build .\examples\SharpCorePDF.Examples\SharpCorePDF.Examples.csproj -c Release` | Build succeeded, 0 warnings, 0 errors |
| `dotnet build .\tools\SharpCorePDF.Measure\SharpCorePDF.Measure.csproj -c Release` | Build succeeded, 0 warnings, 0 errors |
| `SharpCorePDF.Tests.exe -class SharpCorePDF.Tests.PdfContentReaderTests` (`net11.0`) | `Total: 50, Errors: 0, Failed: 0` |
| the same on `net10.0/win-x64` | `Total: 50, Errors: 0, Failed: 0` |
| `SharpCorePDF.Tests.exe` (`net11.0`, no filter) | `Total: 497, Errors: 0, Failed: 0` |
| `dotnet run --project .\examples\SharpCorePDF.Examples -- read-content` | the example ran and printed the image, the comment, the link target, the field, the bookmark, the extracted attachment, the XMP values and both page details |
| `dotnet run --project .\tools\SharpCorePDF.Measure -- --content` | the rows recorded in `docs/measurements.md` §7 |
| `python .\docs\manual\build-manual.py` | 13 chapters, 101 headings, contents 101, footer ok, 55 pages |
| every test method named in this file | 57 names, all present in `tests/SharpCorePDF.Tests` |
| `SharpCorePDF.Tests.exe -class SharpCorePDF.Tests.PdfContentReaderTests` (`net10.0`, after the review corrections) | `Total: 68, Errors: 0, Failed: 0` |
| `SharpCorePDF.Tests.exe` (`net10.0`, no filter, after the review corrections) | `Total: 515, Errors: 0, Failed: 0` |
| `SharpCorePDF.Tests.exe` (`net11.0`, no filter, after the review corrections) | `Total: 515, Errors: 0, Failed: 0` |
| `dotnet build .\SharpCorePDF.slnx -c Release` (after the review corrections) | Build succeeded, 0 warnings, 0 errors, both legs |

## Corrections after review

A second review pass over the image pipeline found seven defects, each in a behaviour the tests above did
not reach: an image that decoded to the wrong samples still looked like a success, and a mask above the
budget could be described but never fetched. All seven are closed, and each one now has a fixture and a
test rather than only a changed line.

| Id | What was wrong | What the code does now | Proof |
| --- | --- | --- | --- |
| F-01 | A `/Predictor` image was inflated and predicted in one buffer, so a large predicted payload had to be held whole before the budget rule could apply | `PdfFilters.Predictors.cs` decodes one row at a time (`PredictorRowDecoder`, three pooled rows, 8 MB row guard); `PdfImageFilters.cs` inflates with a cap (`TryInflatePredictorCapped`) or streams rows to the destination (`InflatePredictorToAsync`), so an over-budget payload is counted but never retained and the copy route writes as it produces | `Undoes_every_png_filter_type_row_by_row`, `Leaves_a_predicted_payload_above_the_budget_out_of_the_record`, `Copies_a_predicted_payload_identical_to_the_read` |
| F-02 | An inline dictionary whose value was a name (`/CS /G`) was read as a key followed by a second key, so the colour space was lost and the entry that followed was misread | The inline dictionary reader consumes a name value after a key | `Reports_the_metadata_of_an_inline_image` |
| F-03 | A mask could be described but not fetched: `CopyImageDataAsync` addresses an image entry, and a directly attached mask has no object number to address | `PdfImageMask` carries the `ImageId` it belongs to, and `CopyImageMaskDataAsync` resolves the pair `(ImageId, Kind)` on both ports, the service and the reader; a colour key mask and a mask the document does not contain are `ArgumentException` | `Copies_a_mask_through_the_copy_route`, `Copies_an_unbuffered_mask_byte_for_byte`, `Rejects_copying_a_colour_key_mask`, `Rejects_a_mask_that_does_not_belong_to_the_document` |
| F-04 | An inline image's data was located by scanning for `EI`, so a sample stream containing a space followed by `EI` was cut short; a missing `EI` was silent and a `EI` followed by a delimiter was missed | The length the header states (`/W`, `/H`, `/BPC`, colour space) decides the end, the separator after `ID` is consumed once, `EI` is accepted when followed by a delimiter, and an unterminated image keeps what it read plus an `ImageDataUnreadable` warning; a page's streams are joined with the separator *between* them, so the last byte of the last stream is the last byte of the content | `Keeps_an_inline_image_whose_samples_contain_a_terminator`, `Reads_an_inline_image_that_ends_at_a_delimiter`, `Warns_when_an_inline_image_is_not_terminated`, `Joins_the_content_streams_of_a_page_with_a_separator` |
| F-05 | The cases above had no fixture, so nothing would have caught a regression | `PdfContentFixtures` gained `PredictedSamples`, `Noise`, `PredictorImage`, `LargePredictorImage`, `FilterChains`, `InlineVariants`, `UnterminatedInline`, `LargeMaskedImage`, `ColorKeyMaskedImage`, `SplitContent` and the two ASCII encoders, with thirteen tests in `PdfContentReaderTests.Images.cs` and four budget tests in `PdfContentReaderTests.Budget.cs` | the tests named in the rows above, plus `Leaves_a_mask_above_the_budget_out_of_the_record` and `Copies_an_unbuffered_mask_byte_for_byte` |
| F-06 | An image warning said what went wrong but not where: no page and no image identifier, so a 200-page report could not be triaged | Every warning the image pipeline raises names the source and the page, and the image identifier wherever the failure is attached to one (an unsupported filter, a damaged mask) | the kind assertions at those sites; the wording itself is deliberately not asserted, because `PdfWarning.Message` is documented as rewordable |
| F-07 | An inline image could not be told apart from an object without re-reading its identifier | `PdfImageInfo.IsInline` is derived from the `inline:` identifier prefix and set once, at record construction | `Reports_the_metadata_of_an_inline_image` |

**Open caveat (A-01)**: the seven corrections were verified by the test suite and a clean build, not by a
second run of the L1/L3 review layers that produced the findings. Those layers should be re-run against this
tree before the change is signed off; the table above is the input to that re-run.

## Notes

- **Two tasks touch one file.** T040 and T042 both edit `PdfPageContentReader.Metadata.cs`; they are separate
  capabilities and must not be worked in parallel on that file.
- **The byte helpers are Application, not Domain.** `ReadImageDataAsync`, `SaveImageDataAsync`,
  `ReadEmbeddedFileDataAsync` and `SaveEmbeddedFileAsync` are extension members over the port, so the
  interface a caller mocks stays at the eleven members the capability needs — the ten T011 named, plus
  `ReadTextAsync` from T053.
- **The source is read into memory once.** Every row in §7 of `docs/measurements.md` includes that cost, and
  the manual says so, because a 50 MB attachment still means a 50 MB document.
- **`[ ]` means outstanding.** Only T051 remains: a second reader accepting the `spec.md` wording is the one
  gate this list cannot close by rerunning a command.

