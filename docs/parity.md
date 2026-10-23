# PDF platform parity — capability scorecard

**Feature**: `003-pdf-platform-parity` | **Matrix source**: [`spec.md`](../specs/003-pdf-platform-parity/spec.md) | **Releases**: 1.1 (what ships) and 1.2 (the rest)

This is the public scorecard for creation, editing and reading parity. It records what PDFsharp 6.5 and
PdfPig 0.1.x do, what SharpCorePDF promises and in which release, who owns the row, the test that fails if
the behaviour is reverted, and the external command that validates it.

**Status vocabulary**

* **Evidenced** — the named test exists, passes, and the external command has been run on the output.
* **Partial** — part of the row is evidenced in 1.0 or by an earlier phase; the rest is still planned for
  the release named in the Release cell.
* **Planned** — the named test and command become runnable in the task that closes the row. A planned row
  is a commitment, not evidence; T038, T058, T079 and T096 close the remaining ones in 1.2.
* **Deferred** — a row that `1.1.0` does not complete, after the scope decision of 2026-10-22 below. It is
  still **Must**; only its release moved, and the task that completes it is named.
* **Out of scope** — a declared non-goal, owned by the product boundary in
  [`ADR-001`](./adr/0001-pdf-platform-boundary.md), not by a workstream.

No “Must” row is unowned. The owner is the story that closes it: `US1 creator`, `US2 editing`,
`US3 reading`, `US4 trust`. The external commands use the corpus and the harness described in
[`docs/parity/README.md`](./parity/README.md) and `docs/measurements.md`.

| # | Capability | PDFsharp / PdfPig reference | SharpCorePDF outcome | Release | Owner | Named test | External validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Page creation, fixed-position text, standard and embedded fonts | PDFsharp: pages, fixed-position text and standard/embedded fonts. PdfPig: basic creation only. | Must: create A4 and custom pages, draw positioned text with standard and caller-supplied TrueType fonts. | 1.1 | US1 creator | `PdfDocumentBuilderTests.Writes_an_a4_page_with_its_text`, `Embeds_a_truetype_font_and_reads_its_text_back` | `uv run --with pikepdf --with pypdf --with pymupdf python docs/tools/verify-cover.py` (C1–C3 in one command), or `validate-pdf.py <output.pdf>` alone | Evidenced (1.0 baseline; re-run 2026-10-22, automated 2026-10-23) |
| 2 | Vector drawing: lines, paths, curves, rectangles, ellipses, fill, stroke, clipping, transforms | PDFsharp: GDI+-class `XGraphics`. PdfPig: path operations in the builder. | Must: a path writer over `m l c v y h re f S B W n q Q cm gs d J j M G g RG rg K k CS cs SC sc SCN scn ca CA`. | 1.1 paths, the rest 1.2 | US1 creator | `PdfDocumentBuilderTests.Writes_a_line_as_a_stroked_subpath`, `Writes_an_ellipse_as_four_bezier_curves`, `Writes_a_clipping_path`, `Writes_a_balanced_graphics_state`, `Writes_a_translucent_fill_with_an_extgstate`; dash, cap, join and `SetTransform` are T022 | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a PyMuPDF render of the same box | Partial (paths, ellipses, clipping and alpha evidenced 2026-10-22; dash/cap/join/transform tests are T022) |
| 3 | Text measurement, wrapping, alignment, leading, basic layout | PDFsharp: basic text layout. PdfPig: not a layout engine. | Must: typeface metrics, single-line and multi-line drawing, greedy wrapping, leading and horizontal/vertical alignment. | 1.2 | US1 creator | `PdfTextLayoutTests.Wraps_a_paragraph_to_a_measured_width` — planned in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.TextLayout.cs`, with the metrics of T028 | `uv run --with fontTools python -c "from fontTools.ttLib import TTFont; ..."` for metrics, plus `validate-pdf.py` | Planned (not a condition of `002-page-authoring`, whose Non-Goals name word wrap and a `MeasureString` API) — **T105 audit 2026-10-23: deferred to `1.2`, T028 supplies the metrics** |
| 4 | PNG, JPEG, BMP and GIF images | PDFsharp: PNG, JPEG, BMP, GIF. PdfPig: image extraction, basic creation. | Must: PNG alpha/palette/16-bit policy, JPEG pass-through with EXIF/CMYK transform, BMP and first-frame GIF, explicit refusal by name. | 1.1 PNG, the rest 1.2 | US1 creator | `PdfDocumentBuilderTests.Writes_an_rgba_png_with_a_soft_mask`, `Writes_an_rgb_png_with_a_colour_key`, `Writes_a_palette_png_with_transparency`, `Writes_a_greyscale_png_with_alpha`, `Uses_the_resolution_of_a_png_for_its_natural_size`, `Writes_one_image_object_for_a_logo_used_on_two_pages`, `Refuses_an_interlaced_png`, `Refuses_a_sixteen_bit_png`; JPEG/BMP/GIF tests are T024 | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and PyMuPDF image placement | Partial: the PNG matrix, `/SMask`, `pHYs` and the by-name refusals are evidenced 2026-10-22; JPEG, BMP and GIF are planned (T030, T031) |
| 5 | Page import, insert, delete, reorder, rotate, crop, resize, split, extract, append | PDFsharp: all operations. PdfPig: merge only. | Must: immutable plan, one-based page references, materialised inherited geometry, no source mutation. | 1.1 operations, external run in 1.2 | US2 editing | `PdfDocumentEditorTests.Reorders_deletes_and_duplicates_pages`, `.Adds_rotations_to_the_rotation_a_page_already_has`, `.Geometry.Crop_is_clamped_to_the_media_box_and_reported`, `.Composition.Imports_a_page_of_another_document`, `.Composition.Appends_every_page_of_another_document`, `.Composition.Splits_the_document_into_parts_of_the_requested_size` | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>`; pypdf reads order and geometry (T058, 1.2) | Partial: every listed operation is implemented and covered by `PdfDocumentEditorTests`, `.Composition` and `.Geometry` (2026-10-22); the external run and the T058 closure are 1.2 |
| 6 | Overlay, underlay, watermark, stamp | PDFsharp: yes. PdfPig: no. | Must: wrap original content in `q`/`Q`, record overlay/underlay in the requested layer with clipping and constant alpha. | 1.2 | US2 editing | `PdfDocumentEditorTests.Overlays_a_watermark_without_touching_the_source` (T040) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a PyMuPDF visual comparison | Planned — **T105 audit 2026-10-23: deferred to `1.2` (T040)**, which is the row's own Release cell |
| 7 | Metadata, XMP, outlines, destinations, page labels, viewer preferences | PDFsharp: yes. PdfPig: reads metadata and bookmarks. | Must: edit information dictionary and XMP, write outlines/destinations/page labels/viewer preferences; removed destinations warn. | 1.1 metadata, the rest 1.2 | US2 editing | `PdfDocumentEditorTests.Overrides_the_metadata_it_sets_and_preserves_the_rest`, `Drops_xmp_when_metadata_overrides_the_information_dictionary`, `Keeps_xmp_when_metadata_is_not_overridden`; XMP authoring, outlines, labels and viewer preferences are T041 | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>`; pypdf reads metadata and outline | Partial: `SetMetadata` edits the information dictionary and drops or keeps the XMP packet as documented (2026-10-22); XMP, outlines, page labels and viewer preferences are 1.2 |
| 8 | AcroForm creation, filling, flattening and appearance generation | PDFsharp: yes. PdfPig: read-only forms. | Must: text, checkbox, radio, choice and push-button fields; unique qualified names; fill, reset, read-only refusal and flatten with generated appearances. | 1.2 | US2 editing | `PdfFormTests.Fills_and_reads_back_form_values`, `Flattens_a_form_without_a_form_viewer`, `Refuses_an_unsupported_field_type` (T044) | `uv run --with pypdf python -c "from pypdf import PdfReader; ..."` and `validate-pdf.py` | Partial (reading evidenced in 1.0; writing planned) |
| 9 | Annotation creation, editing, removal and appearance streams | PDFsharp: broad annotation model. PdfPig: read-only annotations. | Must: link, text, free text, stamp, highlight, underline, strikeout, square, circle, line and ink; update/remove; appearance for every creatable kind. | 1.2 | US2 editing | `PdfAnnotationTests.Adds_moves_and_removes_an_annotation`, `Writes_an_annotation_appearance` (T043) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a PyMuPDF appearance render | Partial (reading evidenced in 1.0; writing planned) |
| 10 | Incremental updates that preserve existing signatures | PDFsharp: yes. PdfPig: no. | Must: append a revision, keep every prior byte, refuse a changed source, and keep the prior signature's byte range valid. | 1.2 | US2 editing | `PdfIncrementalWriterTests.Writes_an_incremental_update`, `Preserves_the_prior_revision_bytes` (T042) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a byte-for-byte prefix comparison | Planned — **T105 audit 2026-10-23: deferred to `1.2` (T042)**, which is the row's own Release cell |
| 11 | Digital signatures: sign, validate, timestamp, report integrity and trust separately | PDFsharp: yes. PdfPig: no. | Must: detached CMS over a reserved byte range, X.509, optional caller-supplied RFC 3161 token, integrity and trust as separate results. | 1.1 | US4 trust | `PdfDocumentSignerTests.Writes_a_signature_that_verifies_against_its_own_bytes`, `Signing_a_second_time_keeps_the_first_signature_valid`, `Embeds_the_timestamp_token_the_caller_supplies` (T080, T087), `PdfDocumentSignatureValidatorTests.Reports_a_changed_byte_as_a_digest_mismatch`, `Keeps_integrity_independent_of_trust`, `Reports_a_request_for_an_online_check_without_making_one` (T081) | `python docs/tools/validate-signatures.py --trust-signer <anchor.pem> <signed.pdf>` (pyHanko, T093) and `uv run --with pikepdf python docs/tools/validate-pdf.py <signed.pdf>` | Met (2026-10-23) on pyHanko and qpdf: a signed revision reports `intact` with coverage `ENTIRE_FILE`, a twice-signed one reports `intact`/`ENTIRE_REVISION` then `intact`/`ENTIRE_FILE`, both `trusted` against the signer's own certificate as anchor, and qpdf `--check` reports no problems on either file. A timestamp token is embedded, never checked: the authority is the caller's |
| 12 | PDF/A-1b, PDF/A-2b and PDF/A-3b output | PDFsharp: PDF/A manager. PdfPig: no. | Must: embed every font, write XMP and an output intent, remove encryption and forbidden actions, refuse what cannot conform. | 1.1 | US4 trust | `PdfATests.Writes_the_structures_the_profile_requires`, `Refuses_a_font_it_cannot_embed`, `Refuses_transparency_for_pdf_a_1b_but_writes_it_for_2b` (T082) | `python docs/tools/validate-pdfa.py --verapdf <verapdf.bat> --profile 2b archive-2b.pdf`, and the same for `1b`/`3b`: veraPDF 1.30.3 reports `PASS` for all three profiles of an authored page and of `SafeWebCore.pdf` (T093, T096) | Met — the output intent is sRGB and built in; a caller-supplied ICC profile, a merge-time profile and PDF/A-3 `/AF` associations are out of scope, and no conformance is claimed without a validator |
| 13 | Positioned letters, words and blocks; configurable segmentation and reading order | PDFsharp: not a reading API. PdfPig: yes. | Must: `PdfLetter` with Unicode, size, font name, box and baseline; word and block strategies; raw and column-aware reading order with the strategy in the result. | 1.1 | US3 reading | `PdfLayoutReaderTests.Reads_one_letter_per_glyph_in_content_order`, `Splits_a_two_column_page_into_one_block_per_column`, `Reads_a_page_of_two_columns_left_to_right_whatever_order_the_content_used` (T059–T060), `PdfLayoutCorpusTests.Meets_SC_004_on_the_labelled_corpus`, `PdfLayoutCorpusTests.Table_page_is_still_read_column_by_column` (T076) | `python docs/tools/score-layout.py <exports> --labels tests/SharpCorePDF.Tests/Assets/parity/labelled --known-order-mismatch table` (T075) | Met on the labelled corpus (T075–T076): recall and boxes 100% on all five documents, reading order 100% on four. `table` is recorded as a known order mismatch — a table is symmetric under the XY cut, so a cell becomes a block and the order comes out column by column, which is the order a two-column page needs; table semantics are out of contract. `PdfLayoutCorpusTests.Table_page_is_still_read_column_by_column` fails if the limitation ever goes away |
| 14 | Images, hyperlinks, form values, annotations, embedded files, metadata, bookmarks | PDFsharp: partial. PdfPig: yes. | Must: expose each resource through the read side, decode image pixels when asked, and warn on an unresolved destination or unsupported filter. | 1.1 partial, pixels in 1.2 | US3 reading | `PdfLayoutReaderTests.Reads_the_links_of_a_page_and_their_uris`, `Resolves_a_direct_and_a_named_destination`, `Keeps_an_unresolved_destination_and_says_so`, `PdfContentReaderTests.Returns_every_annotation_with_its_geometry_and_text`, `Returns_the_field_tree_with_qualified_names` | `uv run --with pypdf python -c "from pypdf import PdfReader; ..."` and `validate-pdf.py` (T079) | Partial: annotations, fields, images, metadata and hyperlinks are evidenced (2026-10-22); decoded pixels are T072 and the external run is T079, both in 1.2 |
| 15 | Public read-only object graph and content-stream inspection | PDFsharp: partial. PdfPig: yes. | Must: immutable `PdfObjectView`/`PdfContentOperationView`, dangling references visible, operation offsets reported. | 1.1 tests, the rest 1.2 | US3 reading | `PdfObjectInspectorTests.Returns_one_node_per_listed_object_in_number_order`, `Projects_a_reference_as_a_node_and_never_follows_it`, `Reports_a_dangling_reference_once_for_the_target_it_names`, `Reads_the_operations_of_a_page_in_the_order_it_states_them` | `uv run --with pikepdf python docs/tools/validate-pdf.py <input.pdf>` plus a mutation-attempt test | Partial: twelve tests in `PdfObjectInspectorTests` are in the suite; the external run and the mutation attempt are T072, in 1.2 |
| 16 | Export to ALTO, PAGE XML, hOCR and SVG text | PDFsharp: no. PdfPig: yes. | Must: four exporters in page space, escaped text, a valid empty-page element, and a result that names the strategy used. | 1.1 exporters, the rest 1.2 | US3 reading | `PdfLayoutExportTests.Exports_alto_pagexml_hocr_and_svg`, `Writes_a_page_element_for_a_page_with_no_text`, `Escapes_text_that_looks_like_markup`, `Returns_the_blocks_it_wrote_with_the_strategy_that_produced_them` (T063) | schema validation for ALTO/PAGE XML (T079) | Partial: four exporters and their tests are in the suite; the schema run is T079 |
| 17 | Rendering pages to images or raster previews | PDFsharp: no. PdfPig: no. | Non-goal: no rasteriser, thumbnail generator, viewer or print pipeline. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `README.md`, `docs/manual/src/02-what-it-is-not.md` and ADR-001 | Out of scope |
| 18 | OCR, HTML-to-PDF, PDF-to-Word/HTML, reflow, tagged-PDF/PDF-UA authoring | PDFsharp: no. PdfPig: no. | Non-goal: no OCR, document understanding, table semantics, Office conversion, reflow or accessibility conformance claim. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `README.md` and ADR-001 | Out of scope |
| 19 | MigraDoc-style flow engine with automatic page breaks | PDFsharp: separate MigraDoc product. PdfPig: no. | Non-goal: no flow layout engine; the caller places content on pages. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `docs/manual/src/02-what-it-is-not.md` and ADR-001 | Out of scope |
| 20 | JavaScript execution, 3D, multimedia, geospatial, public-key encryption | PDFsharp: no or partial. PdfPig: no. | Non-goal: actions may be copied or refused with a warning; nothing is executed, and no new encryption surface is added. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `README.md` and ADR-001 | Out of scope |
| 21 | Drop-in source or binary compatibility with PDFsharp or PdfPig | N/A | Non-goal: capability parity only; no reference package is referenced by the suite. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | ADR-001 and the dependency check in quickstart step 8 | Out of scope |

## 1.1 scope decision (D3, 2026-10-22)

`1.1.0` ships what the tree implements and evidences, and this table names `1.2` in the Release cell of
every row it does not finish. The decision, its reasoning and the task list behind it are in
[`tasks.md`](../specs/003-pdf-platform-parity/tasks.md) Gate 0; the specification carries the matching
amendment. It changes four things here and nothing else:

1. **Release cells name the release that ships the row.** `1.1` means the row is complete in `1.1.0`;
   `1.1 paths, the rest 1.2` means the part in the name is evidenced and the remainder is a `1.2`
   commitment. A task named in the Status cell of such a row is a `1.2` task.
2. **A `Deferred` row is still `Must`.** The column that moves is the release, not the requirement, and
   the row keeps its owner, its named test and its command. Nothing is deleted and nothing is re-labelled
   “out of scope”.
3. **No row is left `Planned` in a tagged release.** `1.1.0` tags only rows whose Release cell names
   `1.1`; the rest carry `1.2` in the same commit, which is what T105 checks.
4. **SC-001 reads “100% of the rows that name 1.1”.** The full matrix is still the 1.2 exit condition, and
   that sentence lives in `spec.md`, not here, so the promise is amended where it was made.

**T105's audit of the table (2026-10-23)** — the check D3 item 3 asks for, run over all 21 rows: eleven name
`1.1` (rows 1, 2, 4, 5, 7, 11, 12, 13, 14, 15, 16), five are non-goals excluded from the release (17–21), and
five name `1.2` only (3, 6, 8, 9, 10). No row claims `Evidenced` or `Met` without both a named test and an
external command — the only rows that claim it (1, 11, 12, 13) name tests that are in the suite and a command
that was run, and their Status cell says which run. Rows 3, 6 and 10 still read `Planned` after the scope
decision, which is the one label D3 item 3 rules out, so they now say `Deferred` and keep their 1.2 task; rows
8 and 9 stay `Partial` because the reading half is evidenced and the writing half is the 1.2 task. Nothing in
the table was moved from `1.2` to `1.1`, and no row was relabelled out of scope.


What `1.1.0` delivers, row by row: **1** (page, text, fonts), the path writer of **2**, the PNG half of
**4**, every page operation of **5** except its external run, the information dictionary of **7**, **11**
(signatures), **12** (PDF/A-1b/2b/3b), **13** (layout) and the parts of **14–16** the suite already
covers. Deferred to `1.2.0`: **3** entirely, and the remainder of **2**, **4**, **5**, **6**, **7**,
**8**, **9**, **10**, **14**, **15** and **16** — 36 tasks, recorded with their numbers in `tasks.md`.

## Row closure protocol

A row moves from **Planned** to **Evidenced** only when all of the following exist on the release commit:

1. the named test passes on both `net10.0` and `net11.0`;
2. the external command has been run against an output produced by the example or fixture in the row;
3. the result, the validator version and the performance row are recorded in `docs/measurements.md`;
4. the test that would fail on revert is named in the pull request.

T038 closes the remaining creator rows (2–4) in 1.2, T058 the editing rows (5–10), T096 the trust rows
(11–12) and T079 the reading rows (13–16). T101 re-reads every recorded budget; a regression above 10%
blocks the release
and requires a decision recorded in `docs/measurements.md`.

## Phase 0 verification (2026-10-21)

* `dotnet test ... --filter "FullyQualifiedName~ParityCorpusTests"` — **8 passed on both target
  frameworks**, including every manifest entry's SHA-256, 76 PDFs opening with the reader, 60
  born-digital documents, five labelled documents and the image refusal variants.
* The full suite is **569 passed on `net10.0` and `net11.0`**, and the Release solution builds with
  **0 warnings and 0 errors** — the 1.0 baseline of 561 plus the eight corpus tests, no regression.
* `python docs/tools/make-fixtures.py --parity <temp-dir>` — **95 fixtures plus the manifest**, all 96
  files byte-for-byte identical to the committed tree (zero differences), so regeneration produces no
  unexpected change.
* `python docs/tools/validate-pdf.py` on **all 76 PDFs** — `qpdf check: no problems`, every content
  stream and image decoded, zero failures.
* Creator: 10 pages / 200 lines / RGBA logo — **3,484 KB, 14.69 ms, 74,094 B** (workstation GC, median
  of five). Editor: 100 pages / 200 operations — **1,733 KB, 3.64 ms, 80,004 B**. Both are baselines,
  not release claims; see `docs/measurements.md` §9.

No “Must” row is unowned. The rows still marked Planned are the work of Phases 1–5; none is claimed as
delivered by this document.

## Phase 2 progress — authoring (2026-10-22)

* The question's own scenario is an example, not a fixture: `dotnet run -c Release --project
  .\examples\SharpCorePDF.Examples -- author-page` writes `cover.pdf` (1 page, 8,835 bytes) and
  `cover-and-summary.pdf` (3 pages) under `%TEMP%\SharpCorePDF.Examples\output`, and prints page 1's text
  back through the library. The run reports `ok (222 ms)` for the whole scenario, merge included, against
  SC-001's 400 ms budget.
* `python docs/tools/validate-pdf.py` on both files — **`qpdf check: no problems`**: 1 page / 14 objects
  and 3 pages / 19 objects, 6 and 8 streams decoded, 0 failed. pikepdf 10.17.0 carries qpdf, so this is
  FR-012's `qpdf --check` leg and the “no syntax problems” clause of SC-004.
* `pypdf` 6.19.0 with `strict=True`: 3 pages, page 1's MediaBox `595.276 × 841.89`, and the extracted text
  is exactly `Décomptes SPRL` … `Référence : é è à ç ô ï ' / |`. `/Resources/XObject` holds `/Im1`,
  `/Subtype /Image`, 152 × 87, with an `/SMask`. That is AC2, AC3 and FR-009.
* PyMuPDF (MuPDF 1.28.5) at 72 dpi: `(100, 45)` = `(31, 58, 95)` inside the logo and `(50, 45)` = white in
  its transparent margin, which is SC-004's render clause.
* `dotnet test -c Release --filter "FullyQualifiedName~PdfDocumentBuilderTests"` — **33 passed** on
  `net10.0` and on `net11.0`; the full suite is **738 passed on both legs**, with 0 warnings and 0 errors.
  Determinism is one of the 33 (`Writes_the_same_bytes_for_the_same_input`), which is AC4.
* Not claimed: rows 2 and 4 stay **Partial** because dash/cap/join/transform and JPEG/BMP/GIF are T022,
  T024, T030 and T031; row 3 stays **Planned** because metrics, `MeasureString` and wrapping are T028 and
  T029. None of those is a condition of `002-page-authoring`; Gate 0 in
  `specs/003-pdf-platform-parity/tasks.md` records the decision they hang from (D1/D2/D3).
* Limits recorded with the code: text is drawn one line per call — a line break is refused by name — and
  the layout types (`PdfTextLayout`, `PdfTextLine`, `PdfTextRun`) carry lines and runs, but nothing in the
  release computes a line break from a paragraph.

## Phase 4 progress — reading (2026-10-22)

* The reading surface is in the tree behind `IPdfLayoutReader` — `ReadLettersAsync`, `ReadWordsAsync`,
  `ReadHyperlinksAsync`, `ReadBlocksAsync` and `ExportAsync` — reached through `PdfLayoutReaderService` and
  `AddSharpCorePDF()`, and constructible by hand.
* `dotnet test -c Debug` — **1396 passed, 0 failed** (698 on `net10.0` and 698 on `net11.0`), with
  0 warnings and 0 errors.
* SC-004 is scored, not asserted. `docs/tools/score-layout.py` reads a folder of ALTO exports and the
  label files beside the labelled PDFs, and prints one line per threshold; on 2026-10-22 it measured
  recall 100% and box agreement 100% on all five documents, and reading order 100% on four of them.
* `table` is the fifth, and it is a recorded limitation rather than a pass: a table is symmetric under
  the XY cut — the gap between its columns is as wide as the gap between its rows — so a cell becomes a
  block and the order comes out column by column (66.7% order agreement), which is the order a
  two-column page needs. Telling a table from two columns whose lines align is a semantic judgement, and
  table semantics are out of contract. `PdfLayoutCorpusTests.Table_page_is_still_read_column_by_column`
  fails if the limitation ever goes away, so the exception cannot outlive the behaviour it records.
* Row 13 is **Met** on that evidence; rows 14 and 16 stay **Partial** and row 15 stays **Planned**: the
  ALTO/PAGE XML schema run is T079, the hyperlink and pixel-decode gaps are row 14's own, and the
  read-only object graph (T062, T070–T071) is not in the tree yet.
* Limits recorded with the code: an export describes text only — `PdfLayoutOptions.IncludeImages` is reserved
  and has no effect in this release — and every exporter writes the page's own coordinates with the origin
  moved to the top left, because none of the four formats has a bottom-left one.

## Phase 4 progress — signatures (2026-10-23)

* The signing surface is in the tree behind `IPdfDocumentSigner` and `IPdfDocumentSignatureValidator`,
  reached through `PdfDocumentSigner`, `PdfDocumentSignatureValidator` and `AddSharpCorePDF()`, and
  constructible by hand. Signing appends one revision; validation reads every signature field the
  document holds and costs one pass over the bytes each one covers.
* `dotnet test SharpCorePDF.slnx` — **1452 passed, 0 failed** (726 on `net10.0` and 726 on `net11.0`),
  and `dotnet build -c Release` — 0 warnings, 0 errors.
* The evidence is external, because a signature that only its own writer believes is not a signature.
  A document signed by the engine and the same document signed twice were checked by two independent
  implementations:

  ```powershell
  uv run --with pyhanko python docs/tools/validate-signatures.py `
    --trust-signer <the signer's certificate, PEM> --require-trusted signed.pdf signed-twice.pdf
  uv run --with pikepdf python docs/tools/validate-pdf.py signed.pdf signed-twice.pdf
  ```

  pyHanko reports `integrity: intact` and `trust: trusted` for both signatures, with coverage
  `ENTIRE_FILE` for the single signature, and `ENTIRE_REVISION` then `ENTIRE_FILE` for the pair — the
  first signature covers the revision it signed and stops there, which is what an appended revision
  must look like. qpdf reports `no problems` on both files. `--require-trusted` is what makes the run
  fail on an untrusted chain, so the exit code is evidence and not decoration.
* The external run found two defects the unit tests had not, and both are fixed in this tree: the
  incremental cross-reference table wrote entries 19 bytes long instead of the 20 the specification
  fixes, and the `signatureTimeStampToken` attribute was written without the `[1] IMPLICIT` wrapper its
  `unsignedAttrs` field carries. `PdfDocumentSignerTests.Writes_cross_reference_entries_as_twenty_byte_records`
  and `Embeds_the_timestamp_token_the_caller_supplies` hold them shut.
* Row 11 is **Met** on that evidence with one limit stated in the row: a timestamp token is embedded for
  the caller and never validated here — checking a TSA's own signature over the token needs trust anchors
  the caller has not supplied in this release.

## Phase 4 progress — archival output (2026-10-22)

* The archival surface is in the tree behind `IPdfAWriter`, reached through `PdfAWriterService`,
  `PdfAWriter` and `AddSharpCorePDF()`, and constructible by hand. `PdfEditOutputOptions.PdfAProfile`
  routes an ordinary edit through the same writer, so an archive does not need a second document pipeline.
* `dotnet test SharpCorePDF.slnx` — **1476 passed, 0 failed** (738 on `net10.0` and 738 on `net11.0`),
  and `dotnet build -c Release` — 0 warnings, 0 errors.
* The writer produces the structures each profile names — the header version, the XMP packet with
  `pdfaid:part` and `pdfaid:conformance`, an `/N 3` sRGB output intent built by the library, the document
  information mirrored into the packet, `/NeedAppearances false`, no encryption — and refuses what it
  cannot write correctly instead of labelling it. Every font must be embedded; a standard font, a
  transparency for 1b, a `JPXDecode`/`LZWDecode` stream for 1b, an attachment for 1b/2b, optional content
  for 1b, an annotation without an appearance, a forbidden action and an external file stream are refusals
  that leave the destination empty.
* The evidence is external, because a profile is a claim a validator makes. veraPDF 1.30.3 was run over
  the documents the `archive-pdfa` example writes from one authored page, and over the same three profiles
  of the six-page `SafeWebCore.pdf` fixture written by "Microsoft: Print To PDF":
  ```powershell
  python docs/tools/validate-pdfa.py --verapdf <verapdf.bat> --profile 2b archive-2b.pdf
  python docs/tools/validate-pdfa.py --verapdf <verapdf.bat> archive-1b.pdf archive-3b.pdf
  ```
  All six files report `PASS`. The run found one defect the unit tests had not: a CID font subset without
  a `/CIDSet` stream fails ISO 19005-1 clause 6.3.5-3, and the authoring writer did not emit one. It does
  now, listing every CID the preserved numbering makes present, which also satisfies the PDF/A-2 rule that
  a set, when present, must name every CID of the program. A source whose subset states no set cannot be
  given an accurate one, so that document is refused for 1b with the font named.
* A `Text` annotation without an appearance stream is refused for all three profiles rather than written:
  the appearance is content, and generating one would invent it. The `Outlined.pdf` fixture is that case.
* Row 12 is **Met** on that evidence with the limits stated in the row: a caller-supplied ICC profile, a
  PDF/A-3 `/AF` association and PDF/UA are out of scope, and the library never claims conformance itself.
