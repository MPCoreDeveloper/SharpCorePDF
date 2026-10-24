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
trust (17). **Gate/foundation**: 21 tasks. **Polish**: 10 tasks. **MVP of the parity target**: T001–T038.
**What `1.1.0` needs**: T097–T106 — see the release lanes in Gate 0.

**Status (2026-10-24)**: 105 of 106 tasks done, 1 open — T096, the US4 closure run, which is blocked on a
tool that is not installed here (veraPDF; `validate-pdfa.py` exits 2 without it). US1, US2 and US3 are
closed, and every open task is the 1.2 backlog, so
**nothing is left on the 1.1.0 path**. Lanes A and B (T097–T100) are closed, every Gate 0 item `C1`–`C6` is
closed, lanes C and D are closed: T101, T102, T104 and T105 are done and recorded, T103's coverage measured
77.76% against 90% and is recorded rather than waived, signed in ADR-002 on 2026-10-23, and T106 is done —
`v1.1.0` is tagged on `effe5de`, the commit whose SonarCloud analysis was newest on `main`, and the five
packages are published to nuget.org.
**D3 is decided** (Gate 0): 1.1.0 ships what this tree already implements, and the 11 story tasks it left open —
US2's 5, US3's 4 and US4's 2 — were the 1.2 backlog. Ten of them are now closed, with the tests, the examples,
the manual chapters and the external runs recorded against them; T096 is the eleventh and its box stays
unchecked until the veraPDF leg can be re-run.
The annotation write path (T043, T051–T053) landed on 2026-10-24: the model, the appearances, the plan
operations and the engine are in. The form model (T054) landed the same day — `PdfForm`, `PdfFormField`,
the five factories, the derived flags, the three operations and the plan's refusals — with the suite at
1788 executions with 0 failures; the engine half (T055) is what remains before `fill-form` can run.

---

## Gate 0: the cover-page question (`002-page-authoring`) — priority 0

`specs/002-page-authoring/spec.md` is the request this repository exists to answer: an A4 page with six
lines of text and a 152 × 87 PNG, written in memory and used directly as a `MergePlan` source, without
PdfSharpCore. It is implemented on this branch — it is the authoring block of `CHANGELOG.md`
`[Unreleased]` — and it is not in the tagged `v1.0.0`, so the release that delivers it is the next one,
`1.1.0`. That spec declares word wrap, a `MeasureString` API, vector graphics and JPEG/GIF as
**non-goals**, so T028–T031, T033 and T034 are parity commitments for `docs/parity.md` rows 2–4; they
are not conditions of the question. Everything the question does ask for was verified on this tree on
2026-10-22:

| Condition | Evidence | Status |
| --- | --- | --- |
| US1/AC1 — create a page, get the bytes | `PdfDocumentBuilderTests.Writes_an_a4_page_with_its_text`; `pypdf` 6.19.0 (strict) reads `595.276 × 841.89` pt | Met |
| US1/AC2 — merge from memory | `Merges_the_authored_page_in_front_of_a_document`; `author-page` prints 3 pages, page 1 A4, page 1 text = the cover text; `Build()` returns a `PdfByteArraySource`, so no temporary file | Met |
| US1/AC3 — RGBA PNG with a soft mask | `Writes_an_rgba_png_with_a_soft_mask`; `pypdf` reads `/Im1 /Image 152 × 87` with `/SMask`; PyMuPDF 72 dpi: `(100, 45)` = `(31, 58, 95)`, `(50, 45)` = white | Met |
| US1/AC4 — byte-identical rebuild | `Writes_the_same_bytes_for_the_same_input` (a changed date changes the bytes; a derived identifier stays repeatable) | Met |
| US2/FR-005, FR-009 — accents round-trip | `Round_trips_the_accented_characters_of_a_standard_font`; `pypdf` extracts `é è à ç ô ï`, the apostrophe, the slash and the vertical bar unchanged, `WinAnsiEncoding` only | Met |
| US2/FR-006 — embedded face, no system font | `Embeds_a_truetype_font_and_reads_its_text_back`; the full DejaVu Sans (756 KB) writes a 58 KB PDF whose text extracts | Met |
| US3/FR-007, FR-008 — PNG policy | `Writes_an_rgba_png_with_a_soft_mask`, `Writes_an_rgb_png_with_a_colour_key`, `Writes_a_palette_png_with_transparency`, `Writes_a_greyscale_png_with_alpha`, `Uses_the_resolution_of_a_png_for_its_natural_size`, `Writes_one_image_object_for_a_logo_used_on_two_pages`, `Refuses_an_interlaced_png`, `Refuses_a_sixteen_bit_png` | Met |
| FR-011 — document information | `Records_the_metadata`; `PdfMetadata.CustomProperties` carries the custom entries | Met |
| FR-012 — qpdf, pypdf and the inspector | `python docs/tools/validate-pdf.py` (pikepdf 10.17.0, which runs qpdf) — `qpdf check: no problems` on `cover.pdf` (1 page, 14 objects) and `cover-and-summary.pdf` (3 pages, 19 objects), 6 and 8 streams decoded, 0 failed; `pypdf` reads the page size back | Met |
| FR-013, SC-003, SC-005 — suite, snapshot, legs | 33 authoring tests pass on `net10.0` and `net11.0`; full suite 738/738 on both legs; Release build 0 warnings, 0 errors | Met (T103 re-runs it on the release commit) |
| FR-014 — refusals by name | `Refuses_text_a_standard_font_cannot_encode`, `Refuses_a_line_break_inside_a_text_operation`, `Refuses_a_missing_glyph_of_an_embedded_font`, `Refuses_an_interlaced_png`, `Refuses_a_sixteen_bit_png`, `Refuses_an_unusable_identifier`, `Refuses_a_document_without_a_page` | Met |
| SC-001, SC-002 — cost and size | `author-page` Release run: `ok (222 ms)` including the merge and the read-back; `cover.pdf` is 8,835 bytes | Met |

**What the question still needs is bookkeeping, not code:**

- [x] C1 Run `qpdf --check` through pikepdf on the example's two outputs — 2026-10-22, no problems.
- [x] C2 Run the strict `pypdf` read of page size, text and image XObject — 2026-10-22, exact.
- [x] C3 Run the 72 dpi PyMuPDF pixel check inside and outside the logo — 2026-10-22, as recorded above.
- [x] C4 Record C1–C3 and the two test counts in `docs/parity.md` rows 1–4 and `docs/measurements.md` §8 —
  2026-10-22, under “Phase 2 progress — authoring” and “Issue-closure re-run”.
- [x] C5 Repeat C1–C3 on the release commit and attach both transcripts to the pull request (T104); the test
  counts are already both legs (33 authoring tests and 738 suite tests on `net10.0` and `net11.0`).
  - **Provisional pass, 2026-10-23.** The three checks are one command now:
    `uv run --with pikepdf --with pypdf --with pymupdf python docs/tools/verify-cover.py`. It runs the
    `author-page` example, asks qpdf, `pypdf` and PyMuPDF the three questions, asserts every value
    recorded on 2026-10-22, prints a markdown transcript and exits non-zero if one of them moves. The
    rehearsal on this branch head broke 0 of them: 8,835 bytes, `ok (290 ms)` (202–392 ms across five
    runs), 1 and 3 pages, 14 and 19 objects, 6 and 8 streams decoded with none failing, `/Im1` 152 × 87
    with `/SMask`, MediaBox 595.276 × 841.890 pt, `(100, 45)` = `(31, 58, 95)` and `(50, 45)` white.
  - What closes it: lane C re-runs the same command on the tagged commit (`verify-cover.py <cover.pdf>
    <merged.pdf>`) and pastes its output into the pull request, which is T104's step. The run is a
    paste, not a repeat of the work.
- [x] C6 Settle D1, D2 or D3 before `1.1.0` is tagged — **D3 decided by the owner on 2026-10-22**; T105
  forbids shipping an unevidenced “Must” row, so every row D3 does not complete is renamed in the same
  commit that tags the release.

### Decided: D3 — `1.1.0` ships what is implemented (2026-10-22)

`1.1.0` delivers the surface the branch already implements and evidences, and every row it does not
complete is rewritten in `docs/parity.md` as a `1.2` promise **in the same commit that tags it**. The
alternative — 36 story tasks, 36–51 developer-days — was rejected as too slow for a release whose reason
to exist (the cover page of `002-page-authoring`) is already implemented and evidenced.

**Ships in 1.1.0**

- Authoring: A4 and custom pages, standard and embedded TrueType, one line per `DrawText`, PNG with an
  `/SMask`, a colour key, a palette or greyscale, an in-memory `PdfByteArraySource`, metadata and a
  byte-identical rebuild. That is row 1 and the PNG half of row 4.
- The path writer of row 2: `m l c v y h re f S B W n q Q cm gs`, ellipses as Béziers, clipping and
  constant alpha.
- Editing: rotate, delete, move, duplicate, reorder, crop, resize, extract, split, import, insert, append
  and `SetMetadata`, with verbatim page copy, materialised inherited geometry and reproducible output
  options. Every page operation row 5 lists is implemented; only its external closure run is 1.2.
- Reading: letters, words, blocks, reading order, the ALTO/PAGE XML/hOCR/SVG exporters, hyperlinks,
  annotations, form fields, images, metadata, bookmarks and the object graph — row 13 and the parts of
  14–16 the suite already covers.
- Signatures with pyHanko evidence and PDF/A-1b/2b/3b with veraPDF evidence — rows 11 and 12 complete.

**Moves to 1.2** — the 36 open story tasks, unchanged and unchecked:

| 1.2 work | Tasks | Rows |
| --- | --- | --- |
| Font metrics, `MeasureString`, greedy wrapping, leading, alignment | T023, T028, T029 | 3 |
| Dash/cap/join/`SetTransform` tests, `PdfImage` factories, PNG ICC and 16-bit policy | T022, T032, T033 | 2, 4 |
| JPEG pass-through, BMP and GIF | T024, T030, T031 | 4 |
| Metadata, XMP, outline and page import; the `creator-parity` scenario, chapters 15–17 and the creator closure run | T034, T036, T037, T038 | 2, 3, 4, 6 |
| Page-operation plan, overlay and underlay; XMP, outline, page-label and viewer-preference edits | T045, T048, T049 | 5, 6, 7 |
| Incremental writer, annotations and forms, and the tests that cover them | T040–T044, T050–T058 | 8, 9, 10 |
| Hyperlink and pixel-decode tests, segmentation strategies, closure runs | T061, T064, T072, T079 | 14, 15, 16 |
| `sign-and-archive` and the US4 closure run | T094, T096 | 11, 12 |

**Since this table was written** (both on 2026-10-23): T023, T028 and T029 landed, which closed row 3, and
T022 landed with the `draw-shapes` artifact, which closed row 2. The table is left as it was recorded at the
`1.1.0` cut because it documents the D3 scope decision and not the progress against it; the live count is
32 of those 36 tasks, and `docs/parity.md` carries the row states.

**What it costs, said plainly**: `1.1.0` is not the parity release the Product Outcome of `spec.md`
describes, so that document carries a dated scope amendment, `docs/parity.md` names `1.2` in the Release
cell of every incomplete row, and SC-001 becomes “100% of the rows that name 1.1”. A caller who installs
`1.1.0` expecting word wrap, `MeasureString`, JPEG/BMP/GIF, overlays, annotations, forms or incremental
updates gets a `1.2` promise with a task number — not a silent absence and not a “Must” row that was
quietly dropped.

### 1.1.0 release lanes — four workers, one tag

Phase 6 is ten tasks and only its last two are genuinely ordered. Each lane writes its own files and
nothing else; that ownership rule is what makes parallel work possible without a merge conflict.

| Lane | Tasks | Files only this lane writes | Blocked by |
| --- | --- | --- | --- |
| A — public surface | T097, T098 | `src/*/PublicAPI.Unshipped.txt`, the public-surface contract test, `docs/migration/**` | nothing |
| B — manual and examples | T099, T100 | `docs/manual/**`, `examples/**`, `docs/SharpCorePDF-Manual-1.1.0.pdf` | nothing |
| C — evidence | T101, T102, T103, T104 | `docs/measurements.md`, the pull-request transcript | A for the snapshot count, B for the example runs |
| D — release | T105, T106 | `CHANGELOG.md`, `<Version>`, `docs/release-checklist.md`, `docs/parity.md` | A, B and C |

Three rules keep the lanes honest:

1. **One lane, one branch, one commit series.** `CHANGELOG.md` and `docs/parity.md` belong to lane D
   alone; a lane that needs a line there asks for it in the pull request instead of editing the file.
2. **A lane is done when its command output is attached**, not when its prose reads well. C4 and C5 exist
   because “looks right” has already been wrong once — and C5 is now the template: one command,
   `docs/tools/verify-cover.py`, that prints the transcript and fails on a changed value.
3. **No lane changes a public signature to make its own task easier.** T097 is additive; a rename is a
   design decision and goes back to the plan.

The critical path is D → tag, so the fastest arrangement is A and B together, C as soon as either
finishes, and D last. That is roughly 7.5–10.5 developer-days of work: 2–3 calendar days with four hands
on four lanes (or four agent sessions in four worktrees), 4–6 days with two. The bottleneck is the review
of what comes back, not the typing.

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

- [x] T014 [P] Add `PdfPoint`, `PdfMatrix`, `PdfPath` and `PdfPathSegment` in `src/SharpCorePDF.Domain/ValueObjects/`.
- [x] T015 [P] Add `PdfPaint`, `PdfStroke`, `PdfGraphicsState`, `PdfLineCap`, `PdfLineJoin`, `PdfFillRule` and `PdfBlendMode` in `src/SharpCorePDF.Domain/ValueObjects/`.
- [x] T016 [P] Add `PdfImageSource`, `PdfImageColorSpace` and the image option values in `src/SharpCorePDF.Domain/ValueObjects/`.
- [x] T017 [P] Add `PdfTextStyle`, `PdfTextMetrics`, `PdfTextLayoutOptions`, `PdfTextLayout`, `PdfTextLine` and the alignment/overflow/direction enums in `src/SharpCorePDF.Domain/ValueObjects/`.
- [x] T018 [P] Add the new `PdfWarningKind` members and `PdfUnsupportedFeatureException` cases required by the data model in `src/SharpCorePDF.Domain/`.
- [x] T019 Add `ContentStreamBuilder` and `PathWriter` in `src/SharpCorePDF.Infrastructure/Authoring/`, covering `m l c v y h re f S B W n q Q cm gs d J j M G g RG rg K k CS cs SC sc SCN scn ca CA`.
- [x] T020 Add `GraphicsState` and the resource registry in `src/SharpCorePDF.Infrastructure/Authoring/`, including balanced `q`/`Q`, clipping and `ExtGState` alpha/blend modes.
- [x] T021 [P] Add failing-first tests for geometry, path serialisation and balanced graphics state in `tests/SharpCorePDF.Tests/PdfContentStreamBuilderTests.cs`.

**Checkpoint**: the writer can serialise a path without a page, and the 1.0 suite is unchanged.

---

## Phase 2: User Story 1 — Build a Real Document (Priority: P1) 🎯 MVP

**Goal**: A ten-page report with vector paths, wrapped text, two typefaces, a JPEG and a PNG, built
in memory and mergeable with existing PDFs.

**Independent Test**: `quickstart.md` step 3 passes: the builder tests are green, the creator example
writes the document, and `python docs/tools/validate-pdf.py <output.pdf>` (pikepdf/qpdf) plus a `pypdf`
read and a PyMuPDF render accept it.

> **Scope note (2026-10-22).** Gate 0 closes the question this repository exists for; the tasks below are
> parity work for `docs/parity.md` rows 2–4. Word wrap, `MeasureString`, vector graphics and JPEG/GIF are
> named non-goals of `002-page-authoring`, so T028–T031 and T033 are not conditions of that question.
> Under decision D3 they are `1.2` work: rows 2–4 name `1.2` in `docs/parity.md`, and nothing in US1 is a
> condition of tagging `1.1.0`.

### Tests for User Story 1

- [x] T022 [P] [US1] Add failing tests for path, colour, dash, cap, join, clip and transform operators in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.Drawing.cs`.
  - **Done 2026-10-23.** Nine tests join the eleven that were already there, and the class passes 67
    tests on `net10.0`: `Writes_the_line_cap_join_and_miter_limit` (checks `w`, `J`, `j` and `M` as the
    numbers PDF assigns to `Round`, `Bevel` and a limit of 4), `Writes_the_dash_pattern_and_its_phase`
    (the `[4 2] 1 d` operators, and that the pattern precedes the `S` that applies it),
    `Writes_a_solid_line_as_an_empty_dash_array` (`[ ] 0 d`), `Writes_fill_and_stroke_colours_in_their_space`
    (`1 0 0 rg`, `0 G`), `Writes_a_cmyk_paint_as_four_components` (`0 1 1 0 k`),
    `Writes_a_translation_as_a_page_space_transform` (`1 0 0 1 10 -20 cm` — the page height taken out of the
    y axis), `Writes_a_scale_that_keeps_the_top_left_corner_still`, `Writes_a_rotation_that_turns_canvas_coordinates`
    (the written `cm` maps the PDF image of a point to the PDF image of the rotated point, so the flip is
    checked rather than restated) and `Writes_the_transform_before_the_drawing_it_applies_to` (the `q`/`cm`/
    path/`Q` order). Every numeric assertion parses the stream and compares to three decimals, the precision
    the writer emits in.
  - The tests corrected one assumption in the spec text: a stroke's style operators follow the path they
    style, because PDF takes the style at the painting operator. The dash test therefore asserts
    `d` before `S`, not before `m`.
- [x] T023 [P] [US1] Add failing tests for metrics, greedy wrapping, alignment, leading, accents and overflow refusal in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.TextLayout.cs`.
  - Done in that file, which now exists: twenty-two tests over the standard-font metrics and AFM widths, the
    embedded face measured against the numbers fontTools reads from the same program, greedy wrapping,
    blank paragraphs, the four alignments, leading and baseline stepping, every overflow policy, and the
    placement of a drawn layout against the same baselines drawn with `DrawText`.
- [x] T024 [P] [US1] Add failing tests for JPEG pass-through, EXIF orientation, CMYK/APP14 refusal, BMP, GIF first frame and PNG variants in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.Images.cs`.
  - Done in that file, which now exists: thirteen tests over `DrawImage` reading back through the content
    reader — a JPEG whose stream is the file byte for byte under `DCTDecode`, the CMYK `/Decode` array, a
    greyscale JPEG, the JFIF natural size, a BMP's rows in order from a bottom-up and a top-down file, a
    thirty-two bit BMP as opaque colour, palette expansion, a GIF's palette, its interlace put back into row
    order, its transparent entry as a soft mask, the first frame of an animation, and one object for a file
    drawn on two pages. `tests/SharpCorePDF.Tests/PdfImageTests.cs` covers the same four readers through
    `PdfImage` — the reported format, size, colour space, frame count, natural size and key, and every file
    each reader refuses by name.
- [x] T025 [P] [US1] Add a byte-identity test for the same pages, fonts, images, `CreationDate` and `DocumentId` in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.Determinism.cs`.
  - Done in `PdfDocumentBuilderTests.Writes_the_same_bytes_for_the_same_input` (the base file, not the
    planned suffix): fixed date and identifier give identical bytes, a later date changes them, and a
    derived identifier is still repeatable.

### Implementation for User Story 1

- [x] T026 [US1] Implement `PdfPageCanvas.DrawLine`, `DrawRectangle`, `DrawEllipse`, `DrawPath`, `SaveState`, `RestoreState`, `SetClip` and `SetTransform` in `src/SharpCorePDF.Infrastructure/PdfPageCanvas.cs`.
- [x] T027 [US1] Implement `DrawTextLayout` and the layout-to-content translation in `src/SharpCorePDF.Infrastructure/Authoring/TextLayoutWriter.cs`.
- [x] T028 [US1] Extend `PdfTypeface` with `Ascent`, `Descent`, `LineGap`, `CapHeight`, `XHeight`, `Measure` and the `head`/`hhea`/`maxp`/`hmtx`/`cmap` readers in `src/SharpCorePDF.Infrastructure/PdfTypeface.cs` and `src/SharpCorePDF.Infrastructure/Authoring/TrueTypeFont.cs`.
  - Done as `PdfTypeface.Metrics`, a `PdfTextMetrics` in the face's own font units, rather than five loose
    properties, because the contract states one value object and `LineHeight(size)` belongs on it. The
    readers are in `TrueTypeFont` — `head`/`hhea`/`maxp`/`hmtx`/`cmap`, with the cap and x height falling
    back to the outline extremes of `H` and `x` when `OS/2` states neither. Standard fonts read
    `Authoring/StandardFontMetrics.cs`, generated from the AFMs by
    `docs/tools/make_standard_font_metrics.py`, which cross-checks pdfminer against reportlab and aborts
    on a disagreement. `Measure` has the two overloads the contract needs, and `Authoring/WinAnsiEncoding.cs`
    is now the one character-to-code table, shared with the font resource.
- [x] T029 [US1] Implement deterministic greedy wrapping, line height, left/center/right/justify alignment and overflow handling in `src/SharpCorePDF.Infrastructure/Authoring/TextLayoutWriter.cs`.
  - Done in `Authoring/PdfTextLayouter.cs`: `DrawTextLayout` was already the writer (T027), so the wrapping
    sits in a static layouter that hands the page a `PdfTextLayout` to draw. The file keeps a paragraph per
    line break, wraps greedily by measured word width, places each line at the alignment it was given,
    stretches inter-word gaps on a justified line but not its last one, takes the line height from the
    options, then the style's leading, then the font's ascent, descent and line gap, and decides what a word
    wider than the line becomes: refused, broken into pieces that fit, clipped, or left to overflow.
  - The layout's coordinates are its own: the origin is the top-left corner of the box and y grows downwards,
    so the first baseline is one ascent below the point `DrawTextLayout` is given. `DrawTextLayout` used to
    subtract `Bounds.Top` — the height of a y-up rectangle — from every baseline, which put a five-line block
    71 points above its origin; `PdfPageCanvas` now places each run at its own start point and
    `Draws_the_layout_from_the_top_left_of_its_box` pins that against `DrawText`.
- [x] T030 [US1] Add `JpegImage` with SOF/SOS parsing, EXIF orientation and Adobe APP14 transform detection in `src/SharpCorePDF.Infrastructure/Authoring/JpegImage.cs`.
  - Done: the marker walk reads the frame, the JFIF density and any `APP1` EXIF orientation and `APP14`
    Adobe transform, and the file's own bytes become the stream, decoded by `DCTDecode` — no entropy-coded
    data is walked or rewritten. `SOF0`, `SOF1` and `SOF2` are accepted; arithmetic, lossless, differential
    and hierarchical frames, a precision other than eight bits, an orientation other than the identity, and
    four components without an Adobe transform are refused with `PdfUnsupportedFeatureException` naming the
    feature; five or more components are a `NotSupportedException`. One, three and four components become
    `DeviceGray`, `DeviceRGB` and `DeviceCMYK`, the last with `/Decode [1 0 1 0 1 0 1 0]`.
- [x] T031 [US1] Add `BmpImage` and first-frame `GifImage` decoding in `src/SharpCorePDF.Infrastructure/Authoring/BmpImage.cs` and `GifImage.cs`.
  - Done: `BmpImage` reads `BITMAPINFOHEADER` and its longer forms, eight bits through a palette and
    twenty-four or thirty-two bits, either row order, expands to RGB and compresses with a PNG predictor
    through `FlateDecode`; `BI_RLE8`, `BI_RLE4`, `BI_BITFIELDS`, the embedded-image headers, one, four and
    sixteen bit samples and the OS/2 core header are refused by name. `GifImage` walks the block stream,
    keeps the first image descriptor, decodes its LZW codes into palette indices, expands a global or a
    local palette, puts an interlaced frame back into row order, turns a graphic-control transparent index
    into a soft mask, and counts the remaining frames without decoding them.
- [x] T032 [US1] Extend `PngImage` for the Phase 1 matrix (interlaced, 16-bit, palette `tRNS`, ICC) in `src/SharpCorePDF.Infrastructure/Authoring/PngImage.cs`; refuse by name what is not supported.
  - Done 2026-10-23: colour types 0, 2, 3, 4 and 6 at eight bits, palette `tRNS` expanded to RGB plus an
    `/SMask`, `pHYs` natural size, and by-name refusal of interlaced and sixteen-bit images are in the
    tree and tested. **The ICC policy is decided: an `iCCP` chunk is declared, not dropped.** The chunk's
    zlib profile is inflated once and validated (128-byte header, `acsp` at 36, colour-space signature at
    16), the component count comes from that signature, and `ImageResource` writes the profile as its own
    Flate stream that the image names as `[/ICCBased n 0 R]` with `/N` and `/Alternate`. A profile whose
    signature disagrees with the components the image is written with (a `GRAY` profile on an RGB PNG, a
    `CMYK` profile at all) is a `NotSupportedException` naming both; a chunk that does not hold a profile is
    an `ArgumentException` naming the missing `acsp`. `PdfImage.ColorSpace` reports `IccBased` for such a
    file, the profile is part of the registry key, and `sRGB`/`gAMA`/`cHRM` are documented as needing no
    work. Five tests in `PdfDocumentBuilderTests.Images.cs` hold it, and `creator-parity.pdf` carries a
    3144-byte sRGB profile that pikepdf reads back byte for byte.
- [x] T033 [US1] Add `PdfImage.Open/FromJpeg/FromPng/FromBmp/FromGif` and the image registry in `src/SharpCorePDF.Infrastructure/Authoring/PdfImage.cs`; one image drawn twice is one object.
  - Done: `PdfImage` opens a file once and exposes `Format`, `WidthPixels`, `HeightPixels`, `ColorSpace`,
    `BitsPerComponent`, `HasAlpha`, `NaturalWidth`, `NaturalHeight`, `FrameCount` and `Key`; the four
    `From…` factories are the same read with the signature checked first, so a file that is not the stated
    format is refused rather than guessed at. `DrawImage` takes either bytes or a `PdfImage`, and the
    registry keys the prepared content — SHA-256 over the samples, filter, colour space, mask, decode
    parameters — so two opens of one file, or one image drawn on forty pages, is one image object.
    `FrameCount` is how the caller learns that a GIF held more than the one frame PDF can draw: the
    authoring side has no warning channel, unlike the read side.
- [x] T034 [US1] Add `PdfDocumentBuilder.WithMetadata`, `WithXmp`, `WithOutline`, `ImportPage` and `ImportPages` in `src/SharpCorePDF.Infrastructure/PdfDocumentBuilder.cs`; imported pages keep content, resources, annotations and geometry.
  - Done 2026-10-23. `WithMetadata` was already there. `WithXmp` takes bytes or a `PdfXmpMetadata` read from
    another document and writes the packet into the catalog as an uncompressed `/Metadata` stream, byte for
    byte; bytes with neither `x:xmpmeta` nor `rdf:RDF` are refused at the call. `WithOutline` takes the
    `PdfOutlineItem` tree the reader returns — titles, nesting, `IsOpen`, page destinations, URLs, and the
    `/XYZ` corner an `Area` names — and validates page numbers when the document is written, because pages
    can be added afterwards: a dangling bookmark throws `ArgumentOutOfRangeException` naming it instead of
    being written, an external file destination or an unresolvable name throws
    `PdfUnsupportedFeatureException`. `ImportPage`/`ImportPages` read the source when they are called
    (`PdfFileReader`, password and range checked there) and copy the page with the merge engine's
    `PdfObjectCopier`, materialising the attributes the page inherited and folding links to pages that were
    not imported onto the one that was — so an imported page keeps exactly what a merged page keeps. Pages
    interleave in call order: `AddPage`, `ImportPage`, `AddPage` is the document's order. Thirteen tests in
    the new `PdfDocumentBuilderTests.Metadata.cs`, including annotations (8 kept), inherited `/MediaBox` and
    `/Rotate 90`, range order, the two refusals, and a nested bookmark checked like a top level one.
- [x] T035 [US1] Enforce fixed `CreationDate`/`DocumentId` determinism through `PdfAuthoringOptions` and the writer in `src/SharpCorePDF.Infrastructure/PdfAuthoringOptions.cs`.
  - Done: `PdfAuthoringOptions.CreationDate`/`DocumentId` are validated by `Validate()` before the first
    object is written, the identifier is derived from the content when it is absent, and T025's test holds
    it byte for byte.
- [x] T036 [US1] Add the `creator-parity` scenario to `examples/SharpCorePDF.Examples/Scenarios.cs` and a creator example file beside `AuthoringExamples.cs`.
  - Done 2026-10-23: `CreatorExamples.cs` registers `creator-parity`, which writes a whole document in one
    chain — an A4 cover with a JPEG that keeps its own bytes, an A5 summary page with the PNG whose `iCCP`
    profile becomes `/ICCBased`, a four-entry outline (one nested child, one URL), a metadata packet, and a
    third page imported from the sample summary — then reads it back through the inspector, the outline
    reader, the metadata reader and the image reader. Two small assets make it self-contained:
    `Assets/Logo.jpg` (320 × 320, 17 KB) and `Assets/Logo-icc.png` (152 × 87 with the sRGB profile). The run
    prints 3 pages (595 × 842, 419 × 595, 595 × 842), the four bookmarks, the title from both the dictionary
    and the packet, `320 x 320 px, DCTDecode, DeviceRGB` and `152 x 87 px, ICCBased`. `author-page` keeps its
    place as the 1.1 scenario.
- [x] T037 [US1] Write manual chapters `15-drawing.md`, `16-text-layout.md` and `17-image-formats.md`, and update `docs/README.md` and `docs/manual/build-manual.py`.
  - **All three chapters are written. `17-image-formats.md` landed 2026-10-23** — the last piece of this
    task — and `docs/README.md` lists it, `00-preface.md` counts chapters 15 to 17 as the authoring run and
    keeps only 18 and 19 reserved, and `specs/003-pdf-platform-parity/quickstart.md` no longer calls chapter
    17 reserved. `build-manual.py` orders by file name and needed no change; the edition builds 23 chapters
    and 103 pages.
  - **What chapter 17 covers.** An availability banner (the JPEG, BMP and GIF readers and the PNG profile
    are `1.2`; `1.1.0` embeds PNG), then one call and four formats — signature detection, the `From…`
    factories, and a table of what `PdfImage` reports — the PNG matrix with its three shapes of transparency
    and the pass-through of `IDAT`, a subsection on colour profiles that shows the `[/ICCBased n 0 R]` and
    the `/N`/`/Alternate` stream, states why dropping a profile is a silent colour shift, and lists the two
    refusals; JPEG pass-through with the component/colour-space table and the `/Decode` flip; BMP with its
    header forms, palette, row order and padding byte; GIF with its first frame, LZW, interlacing,
    transparency and `FrameCount`; natural size and stretching (the box is filled axis by axis); what a draw
    costs; a refusals table naming the exception each one throws; and what the surface deliberately does not
    do. `12-authoring.md` keeps the short version and points at it, `16-text-layout.md` keeps its own head.
  - **`15-drawing.md` done 2026-10-23.** The chapter covers the canvas coordinate system and the trap in
    `PdfRectangle(left, bottom, right, top)` — on a top-left canvas `bottom` is the edge *nearer the top* —
    lines and the cap, rectangles as one `B`, the ellipse as four Béziers at 0.5522847498 × the radius
    (0.03% worst-case radial error), the five path segments with the four construction rules and
    `Close` versus `isClosed`, the even-odd/NonZero hole rule, `PdfPaint` and `PdfStroke` in a member table
    that names the operator each one writes, alpha as an `/ExtGState` (one entry per distinct combination;
    alpha 1 costs nothing), the dash array in points rather than multiples of the width, the refusals, the
    fact that style follows the path in the stream, clipping, transforms (including that `Rotate(90)` turns
    content *clockwise* because the canvas y axis points down, and that a transform moves text and images
    too), the saved graphics state and its two refusals, and a closing list of what the surface deliberately
    does not do.
  - The chapter's transform example was corrected against a render: `a.Multiply(b)` applies `a` first, so a
    group is composed `Rotate(θ).Multiply(Translate(x, y))`. `Translate(60, 40).Multiply(Rotate(-15))`
    rotates the point the group was moved to, which the PyMuPDF probe showed by putting the group's text a
    hundred points from where the prose said it would be.
  - `12-authoring.md` keeps the rest of the surface — coordinates, standard and embedded faces, the image
    policy, determinism and the subset size — and now points at Chapter 15 for shapes and Chapter 16 for
    measuring and wrapping. `00-preface.md` no longer calls Chapter 15 reserved; `docs/README.md` lists both
    chapters, and `specs/003-pdf-platform-parity/quickstart.md` names chapters 17–19 as the reserved ones.
  - `examples/SharpCorePDF.Examples/DrawingExamples.cs` adds the `draw-shapes` scenario — a round-capped
    dashed rule with a phase, a two-subpath path whose even-odd fill leaves a hole, a clipped column of
    stripes and a rotated, translated group — registered in `Scenarios.cs` and documented in
    `examples/README.md`. It is also the artifact T038's row-2 evidence was run on.
  - **`16-text-layout.md` (written 2026-10-23, before this note was rewritten)** covers measuring
    (`PdfTypeface.Measure` both overloads, `PdfTypeface.Metrics`, `PdfTextMetrics.LineHeight`),
    `PdfTextLayouter.Layout` with every option and default, the greedy wrap and its word rule (spaces only;
    tabs, non-breaking spaces and hyphens do not break), the four alignments with justification that never
    touches the last line, the `Refuse`/`BreakWords`/`Clip`/`Continue` policies including the
    single-character case, the right-to-left refusal, `DrawTextLayout` and the box's coordinate space (origin
    at the top-left, first baseline one ascent below it, `Bounds` is a description and not an offset), the
    result records, determinism, and the limits. Its head states that the `1.1.0` package does not carry what
    it describes. `13-cookbook.md` gains a sub-recipe for a paragraph that comes from a template, which
    computes the box corner from the face's ascent and places the next block at `Bounds.Height`.
  - No longer owed: `17-image-formats.md` was written on 2026-10-23 (see the first bullet of this task). Its
    code had landed the same day with T030/T031/T032 — the JPEG marker walk and `DCTDecode` pass-through, the
    BMP reader, the GIF LZW decoder, and the PNG matrix with its by-name refusals — so the chapter was prose
    only, and Chapter 12 carries the short version with a pointer to it rather than the policy in full.
  - Build: `python docs/manual/build-manual.py` → 22 chapters, 92 pages before Chapter 15 was added; the
    figures for the edition that includes both chapters are in the note under T038.
- [x] T038 [US1] Run T022–T025 plus `validate-pdf.py --check-syntax --text --render --images`; record the results and close the creator rows in `docs/parity.md`.
  - **Closed 2026-10-23.** The run is `validate-pdf.py <file>` — the flags this task names do not exist — plus
    `docs/tools/verify-creator.py`, a committed tool that runs the example and checks fourteen recorded
    readings with qpdf, pypdf, PyMuPDF and pikepdf, all recorded in `docs/measurements.md` §13 with the
    commands that produced them. `creator-parity.pdf`: `qpdf check: no problems`, PDF 1.7, 3 pages, 27
    objects, a catalog with `/Metadata` and `/Outlines`, five bookmarks, page boxes A4/A5/595 × 842, 10
    streams decoded and 0 failed; PyMuPDF reads the nested table of contents, the external entry, the
    605-byte packet and the two images (`DeviceRGB`/`DCTDecode`, `ICCBased`); pikepdf resolves the profile
    and compares it with the profile inside the PNG the example drew — identical, 3144 bytes; pypdf extracts
    the accented title line and the imported page's text. The suite is 1664 passed on two legs,
    `dotnet build -c Release` is 0 warnings.
  - Rows: **4 is Evidenced** (the ICC half is what was open), **2 stays Partial** for `CS`/`cs`/`SC`/`sc`/
    `SCN`/`scn`, which no canvas call selects yet, and **7 stays Partial** because authoring a packet and an
    outline is evidenced while editing one is T041. The layout allocation row this task owed is in
    `tools/SharpCorePDF.Measure --creator`: 60 sentences wrapped at 400 pt cost 49 KB and 1.13 ms for 60
    lines.
  - Half done, 2026-10-22: `validate-pdf.py` (pikepdf 10.17.0 / qpdf) reports `qpdf check: no problems` for
    `cover.pdf` (1 page, 14 objects) and `cover-and-summary.pdf` (3 pages, 19 objects); `pypdf` 6.19.0
    strict reads `595.276 × 841.89` pt, extracts the accented line exactly and shows `/Im1` 152 × 87 with
    `/SMask`; PyMuPDF at 72 dpi gives `(31, 58, 95)` at (100, 45) and white at (50, 45); 33 authoring tests
    pass on `net10.0` and `net11.0`. Rows 1 and 4's PNG half are closed; row 2 stays Partial (no dash, cap,
    join or transform test) and row 3 stays Planned (T023, T028, T029).
  - **Row 3 was closed on 2026-10-23**, after this note was written: T028, T029 and T023 delivered the
    metrics, `Measure` and the layouter, and the row in `docs/parity.md` now reads `Evidenced` with the
    readings in `docs/measurements.md` §11. What T038 still owed at that point — the row-2 and row-4
    remainder, and the layout's own allocation row in `tools/SharpCorePDF.Measure` — is settled in the
    closure bullet at the end of this note.
  - **Row 2 was closed on 2026-10-23.** T022 supplied the operator tests and the `draw-shapes` example
    supplied the artifact. `python docs/tools/validate-pdf.py <shapes.pdf>` (pikepdf 10.17.0, qpdf) reports
    `qpdf check: no problems`, PDF 1.7, 1 page, 6 objects, a 1937-byte content stream decoded and 0 failed.
    PyMuPDF 1.28.5 at 72 dpi renders the page, and the probe points read back as drawn: black at (62, 120) on
    the first dash of the `[10, 4]` pattern and `(179, 179, 179)` at (67, 120), which is the half-covered
    pixel between two round caps at phase 5; `(31, 57, 95)` in the even-odd path's outer ring with white in
    its hole; `(122, 138, 161)` where a stripe crosses the clip and white at (315, 200) where the same stripe
    was drawn outside it; `(228, 231, 236)` inside the translated and rotated band with white below it, and
    the group's text line reported with `dir (0.97, -0.26)`, a fifteen degree turn. The same run caught the
    composition error described under T037, which is what a render is for.
  - **The command in this task does not exist as written.** `validate-pdf.py` takes paths and an optional
    `--password`; it has no `--check-syntax`, `--text`, `--render` or `--images` flags, and passing them makes
    `pikepdf.Job` raise `unrecognized argument --check-syntax`. The run that closed row 2 is
    `validate-pdf.py <file>` for the syntax and decode half, plus separate PyMuPDF and pypdf probes for the
    render and text halves. The task text is left as it was written and corrected here rather than rewritten,
    so the record shows both.

**Checkpoint**: US1 is independently shippable; existing merge, inspection and editing tests still pass.

---

## Phase 3: User Story 2 — Edit an Existing Document (Priority: P2)

**Goal**: Rotate, crop, resize, remove, duplicate, reorder, split and append pages; overlay or
underlay content; edit metadata, outlines and labels; add annotations; fill and flatten forms.

**Independent Test**: `quickstart.md` step 4 applies the scripted page edits to the fixture, reopens
the result, checks order/geometry/metadata, and passes `validate-pdf.py --check-syntax --pages
--geometry`. Annotation and form scenarios read their values back in two readers.

### Tests for User Story 2

- [x] T039 [P] [US2] Add failing tests for crop, resize, import, insert, reorder, split, extract and append validation in `tests/SharpCorePDF.Tests/PdfDocumentEditorTests.Geometry.cs` and `PdfDocumentEditorTests.Composition.cs`.
- [x] T040 [P] [US2] Add failing tests for overlay/underlay layer order, clipping and untouched original content in `tests/SharpCorePDF.Tests/PdfDocumentEditorTests.Overlay.cs`.
  - Done 2026-10-24. Seven tests: an overlay is read after the page's own text and an underlay before it,
    by stream order in `/Contents` and by the text read back; the `ExtGState` carries `ca`, `CA` and
    `BM`; the layer follows its page through `MovePage`, is shared by `DuplicatePage`, is drawn on a
    page the edit resized, and a layer document with more than one page is refused.
- [x] T041 [P] [US2] Add failing tests for XMP, outline, page-label, viewer-preference and metadata edits in `tests/SharpCorePDF.Tests/PdfDocumentEditorTests.Document.cs`.
  - Done 2026-10-24. Nine tests: the preference flags, the number tree of two label ranges, labels
    stated after a reorder, `None` removing the source labels without a `PageLabelsDropped` warning, a
    range past the last page, the bookmark tree (titles, nesting, page and URL destinations),
    `PdfOutline.Empty` removing the panel, a bookmark past the last page, and `SetXmp` replacing the
    packet without an `XmpPacketDropped` warning.
- [x] T042 [P] [US2] Add failing tests for incremental append, prior bytes unchanged, prior signature still covering its revision, and complete-rewrite warnings in `tests/SharpCorePDF.Tests/PdfIncrementalWriterTests.cs`.
  - Done 2026-10-24. Thirteen tests: the bytes of the source as the prefix of the result, the `/Prev`
    chain to the source's own `startxref` and the earlier `%%EOF` still inside the result, offsets counted
    from the start of a destination that can seek, a source that does not end on a newline still getting
    its revision on a line of its own, a second revision chained to the first, the signature of the source
    still valid with `CoveredRevision` 2 of the 3 the signed result has, the `PriorSignaturePreserved` and
    `IncrementalUpdateAppended` remarks, `PriorSignatureInvalidated` when the same plan is a complete
    rewrite, the version of a PDF 1.4 source kept with `IncrementalUpdateVersionKept`, and the refusals — a
    forward-only source, a destination that already holds bytes, an encrypted source, encryption asked for
    in the plan, and a table the reader had to rebuild by scanning.
- [x] T043 [P] [US2] Add failing tests for every annotation kind, appearance generation, update/remove and dangling destinations in `tests/SharpCorePDF.Tests/PdfAnnotationTests.cs`.
  - Done 2026-10-24. Twelve tests through the editor and the public read side: a highlight read back with
    its quad, colour and identifier; a URL link and a page link; an update and a removal in one plan; an
    update of an identifier the page does not carry refused with the destination still empty; a link with
    both a page and a URL refused by `Validate()`; a rectangle past the page edge clipped with an
    `AnnotationClipped` warning; the annotations a page already carried kept by a second edit; an
    annotation carried into a duplicate of its page; a link whose page the plan deletes dropped with an
    `AnnotationDestinationUnresolved` warning; the text of a free-text annotation found in the decoded
    appearance operators; an appearance present for all ten kinds that draw something; a translucent
    annotation refused for PDF/A-1b. Suite: 1748 executions, 0 failures.
- [x] T044 [P] [US2] Add failing tests for text/checkbox/radio/choice/push-button fields, duplicate names, read-only refusal, fill and flatten in `tests/SharpCorePDF.Tests/PdfFormTests.cs`.
  - Done 2026-10-24. `PdfFormTests.cs` holds the 20 model facts of T054 — the five factories, the derived
    flags, names with an empty part, an off-page field, a choice value that is not an option, repeated and
    empty options, a button that states a value, multiline with password, a value over the maximum length,
    options and lengths on the wrong kind, two fields with one name, a plan that writes one field twice,
    the page of the call winning over the page of the field, and `FlattenForm(0)`.
  - `PdfFormEditorTests.cs` holds the seven facts that need a document, which is where the three names row 8
    of `docs/parity.md` already promised live: `Fills_and_reads_back_form_values` (a text field, a check
    box, a choice and a push button written, read back through `ReadFormFieldsAsync`, with
    `NeedAppearances false` and four roots in `/Fields`), `Fills_a_field_the_document_already_carries` (the
    `empty` field of the fixture is filled while `person.name` keeps `Alice` and its 10/10/110/30 widget),
    `Refuses_a_read_only_field` (the fixture's read-only `person.name`, refused by name with nothing
    written to the destination), `Refuses_an_unsupported_field_type` (the fixture's `/FT /Sig` field),
    `Flattens_a_form_without_a_form_viewer` (no field on the page survives, `/Annots` keeps only the
    signature widget, the page grows a second content stream and `/SCField1 Do` … `/SCField4 Do` are in the
    bytes), `Resets_the_form_to_its_default_values` (`/V Alice` → `/DV Bob`, and `empty` emptied) and
    `Removes_the_xfa_form_and_says_so` (a hand-built `/XFA` source, which the result no longer carries).


### Implementation for User Story 2

- [x] T045 [US2] Extend `PdfEditPlan` and `PdfEditOperation` with `CropPage`, `ResizePage`, `InsertPage`, `ImportPage`, `Append`, `ReorderPages`, `SplitDocument`, `ExtractPages`, `Overlay`, `Underlay`, `SetXmp`, `SetOutline`, `SetPageLabels` and `SetViewerPreferences` in `src/SharpCorePDF.Domain/Aggregates/PdfEditPlan.cs` and `src/SharpCorePDF.Domain/ValueObjects/PdfEditOperation.cs`.
  - Half done, 2026-10-22: `PdfCropPageOperation`, `PdfResizePageOperation`, `PdfInsertPageOperation`,
    `PdfImportPageOperation`, `PdfAppendOperation`, `PdfReorderPagesOperation`,
    `PdfSplitDocumentOperation` and `PdfExtractPagesOperation` exist and are covered by
    `PdfDocumentEditorTests`, `.Composition` and `.Geometry`.
  - Done 2026-10-24: the six that were missing — `PdfOverlayOperation` (one kind, two layers),
    `PdfSetXmpOperation`, `PdfSetOutlineOperation`, `PdfSetPageLabelsOperation` and
    `PdfSetViewerPreferencesOperation` — are values with their builders on `PdfEditPlan`, their
    `Kind`, their `ValidateOperation` arm and their entries in `PublicAPI.Unshipped.txt`.
- [x] T046 [US2] Implement crop and resize (`Scale` and `Preserve`) with explicit materialised geometry and normalisation warnings in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Pages.cs` and `src/SharpCorePDF.Infrastructure/Editing/PageOperations.cs`.
- [x] T047 [US2] Implement import, insert, append, reorder, split and extract through the existing object copier in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Pages.cs`.
- [x] T048 [US2] Implement `Overlay`/`Underlay` as recorded content in front of or behind the original stream, with clipping and `ExtGState` opacity in `src/SharpCorePDF.Infrastructure/Editing/OverlayWriter.cs`.
  - Done 2026-10-24, in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Overlays.cs` rather than a separate
    writer: the form and the stream that draws it have to be allocated in the same pass that writes the
    pages, because object numbers and the resource names of the page are decided there. The layer is a
    form XObject holding the decoded content of the layer page and its own resources; the page's
    `/Contents` becomes an array with the drawing stream first for an underlay and last for an overlay,
    the original streams untouched between them; `Clip` closes a `re W n` path, and `ca`/`CA`/`BM` travel
    in an `ExtGState` named against the page's own resources.
- [x] T049 [US2] Implement XMP, outline, page-label and viewer-preference edits in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Document.cs`; a destination to a removed page is removed or redirected with a warning.
  - Done 2026-10-24. `BuildCatalog` takes the last operation of each kind, so a later operation replaces
    an earlier one. The outline is written by `PdfOutlineWriter` (its digest is now optional) after every
    bookmark page is checked against the pages the edit leaves; labels become a `/PageLabels` number
    tree; the preferences write `/PageLayout`, `/PageMode` and `/ViewerPreferences`; `SetXmp` writes the
    packet as given, uncompressed, and the source packet is no longer reported as dropped in that case.
    An operation that states one of the four replaces the source value instead of copying it.
- [x] T050 [US2] Implement `IncrementalWriter` in `src/SharpCorePDF.Infrastructure/Editing/IncrementalWriter.cs`: append a revision, preserve every prior byte, and refuse a non-seekable or changed source.
  - Done 2026-10-24. `Begin` copies the bytes the reader opened — not a second read of the stream, so a
    source that changes underneath the edit cannot desynchronise the revision from the bytes it was
    recorded against — ends the copy on a line boundary when the source did not, and opens a
    `PdfWriter.Append` whose offsets are absolute in the output. New objects are numbered above
    `CrossReferenceTable.HighestObjectNumber`, and the trailer carries `/Prev` to the source's own
    `startxref`, so a reader follows the chain instead of failing over the old objects. `PdfEditEngine`
    refuses a forward-only source, a destination that is not empty, an encrypted source and a table that
    was rebuilt by scanning, and pins the header version to the source with
    `IncrementalUpdateVersionKept` when the plan asks for another.
- [x] T051 [US2] Add the annotation model and `PdfAnnotations` factories in `src/SharpCorePDF.Domain/ReadModels/PdfAnnotation.cs` and `src/SharpCorePDF.Domain/ValueObjects/`; validate link/markup/stamp/ink rules before the source opens.
  - Done 2026-10-24. `PdfAnnotation`, `PdfAnnotationKind`, `PdfAnnotationFlags`, `PdfBorder` (+
    `PdfBorderStyle`), `PdfAnnotationAppearance`, `PdfStandardTextStyle` and the twelve `PdfAnnotations`
    factories. `PdfAnnotation.Validate()` enforces the kind's geometry (a markup needs a quad, an ink a
    stroke, a line two different points, a stamp an icon or an appearance), and the plan refuses a link
    with both a page and a URL, an empty rectangle, an opacity outside 0-1 and an undefined flag bit.
  - Two decisions the contract left open. The model lives in `Domain/ValueObjects`, not
    `Domain/ReadModels`: a read model describes what was read, and this one is written and carried by
    the plan. And the text of a free-text annotation takes `PdfStandardTextStyle` (a standard font,
    size and colour) rather than the authoring `PdfTextStyle`, because that type lives in
    `Infrastructure/Authoring` and `Domain` cannot reference it; an appearance is meant to stay small
    and portable, so it uses a non-embedded font by design. `PdfAnnotationAppearance` carries a caller's
    own operator stream plus the standard font it names as `/Helv`.
- [x] T052 [US2] Generate appearance streams for every creatable annotation kind in `src/SharpCorePDF.Infrastructure/Annotations/AppearanceStreams.cs` and write `/Annots` changes in `AnnotationWriter.cs`.
  - Done 2026-10-24. `AppearanceStreams` draws a form XObject for all ten kinds that have something to
    draw — a note icon with a folded corner, free text auto-sized to its box, a stamp frame with its
    caption centred by measurement, quad fills and rules, rectangle, elliptical arcs, lines and ink
    strokes — in the annotation's own space, compressed, with the font and the opacity graphics state it
    names. The `/Annots` rewrite lives in `PdfEditEngine.Annotations.cs` instead of a separate
    `AnnotationWriter.cs`: it needs the page's copier, the output page numbers for `/GoTo` targets and
    the writer's object numbers, all of which the engine already holds, and a second type would only
    pass them through. Source annotations are copied by the copier and keep their objects; a removed
    annotation simply becomes unreachable and is never written.
- [x] T053 [US2] Add `AddAnnotation`, `UpdateAnnotation` and `RemoveAnnotation` to the plan and engine; untouched pages keep their objects.
  - Done 2026-10-24. The three plan methods, the three operations and their `Kind`s, the engine's
    `BuildLayout` cases and the page's `/Annots` rebuild. An annotation without an identifier is named
    `scpdf-N`, where N is the operation's position, so a later operation of the same plan can name it.
    An update or a removal is resolved against the page's own array while the layout is built, so a
    missing identifier is refused before a byte is written; a page the plan leaves alone never has its
    `/Annots` touched, and a page it does touch keeps every annotation it already had. A rectangle that
    reaches past the page is clipped with an `AnnotationClipped` warning, a link whose page the plan
    removes is dropped with an `AnnotationDestinationUnresolved` warning, and an appearance the engine
    drew is reported once per page with `AnnotationAppearanceRegenerated`.
- [x] T054 [US2] Add `PdfForm`, `PdfFormField` and the field factories in `src/SharpCorePDF.Domain/`, including unique qualified names and value/type validation.
  - Done 2026-10-24. `PdfFormFieldKind` (text, check box, radio button, choice, push button — no signature
    field, because signing is a separate surface), `PdfFormFieldAlignment`, `PdfFormField`, `PdfForm` and
    the five `PdfFormFields` factories, all in `Domain/ValueObjects`. The page comes first in every
    factory, matching `RotatePage`/`CropPage`, and a field carries it so a `PdfForm` is nothing but a list
    plus the style a field that states none is drawn in.
  - The flags are computed from the properties — `Flags` is an `int` the writer reads, not a public
    `[Flags]` enum a caller writes: CA1711 refuses a type name ending in `Flags`, and a caller has no
    reason to set a bit the editor cannot honour. So `IsMultiline` becomes 4096, `IsReadOnly` 1,
    `IsRequired` 2, `IsCombo` 131072 and the rest likewise.
  - `Validate()` checks the name (blank, a null or control character, a leading, trailing or doubled dot —
    a qualified name with an empty part addresses nothing), the rectangle, the page, the border width and
    the style, then the kind: a choice value that is not an option unless the combo box is editable,
    repeated and empty options, a button that states a `Value`, a `Value` longer than `MaxLength`,
    multiline with password, and options or a maximum length on a kind that holds neither. `PdfForm`
    adds the one rule a single field cannot state: two fields cannot share a name.
  - `PdfSetFormOperation`, `PdfResetFormOperation` and `PdfFlattenFormOperation` plus
    `SetForm`, `SetFormField(field)`, `SetFormField(page, field)`, `ResetForm()` and `FlattenForm()`
    (whole document or one page). The plan refuses a field that validation rejects, two operations that
    write one name (the second would replace the first) and `FlattenForm(0)`. The page stated by
    `SetFormField(page, field)` wins over the one the field carries, so a factory result can be reused.
  - 94 new public API entries, and the engine half — writing the fields and flattening them — is T055,
    so the model is verified by `PdfFormTests` alone for now: 20 facts, suite at 1788 executions.
- [x] T055 [US2] Implement field appearances, `SetFormField`, `ResetForm` and `FlattenForm` in `src/SharpCorePDF.Infrastructure/Forms/FieldAppearance.cs` and `AcroFormWriter.cs`; flatten leaves a page that renders without a form viewer.
  - Done 2026-10-24. `Forms/FieldAppearance.cs` draws one appearance per kind in the field's own box: a
    frame and a fill, the value wrapped or shrunk to fit, `*` per character for a password, a highlighted
    option in a list box, a tick in a check box, a dot in a radio button and a centred caption on a push
    button, with `Helv` and the field's standard font as the only resource. `Forms/AcroFormWriter.cs`
    reads the source's tree (`/Fields`, `/Kids`, inherited `/FT` and `/Ff`, widgets, `/DV`, `/Opt`,
    `/MaxLen`, `/AA`, `/XFA`) and writes the result:
  - A field the document carries is filled **in place** by rewriting its body and its widgets as the copier
    copies them (`PdfObjectCopier.Override`), so the page's `/Annots`, the parent's `/Kids` and the
    catalog's `/Fields` keep pointing at the same objects and only `/V`, `/AS` and `/AP` change. A name the
    document does not carry becomes a new field; a dotted name becomes a tree, one node per segment, with
    the widget on the leaf. A name that would sit inside an existing one is refused.
  - `/NeedAppearances` is written `false`, `/DR` holds every font the fields name and `/DA` selects the
    first. A read-only field and a signature field are refused with the field's name; `ResetForm` restores
    `/DV` (emptying a field that states none) and regenerates the appearances; `FlattenForm` draws each
    widget's appearance into a new content stream of its page — named `SCFieldN` in the page's
    `/XObject` resources, drawn last so nothing covers it — takes the widget out of `/Annots` and out of
    its field's `/Kids`, and drops the form from the catalog once its last widget is gone. A widget that
    states no appearance of its own gets one generated from its value, and a widget this writer cannot
    draw (a signature field) stays interactive rather than losing its mark.
  - Two warning kinds were added, which is the only public API the engine half adds:
    `XfaFormRemoved` (an XFA packet cannot be kept beside the fields this writer writes) and
    `FormFieldActionPreserved` (an action is copied as it stands and never executed). `PdfWarningKind` now
    has 65 members, and `PublicAPI.Unshipped.txt` was renumbered from the two insertions.
  - Suite: 1808 executions on `net10.0` and `net11.0`, 0 failures; `dotnet build` with 0 warnings and 0
    errors.
- [x] T056 [US2] Add the `edit-pages`, `watermark`, `annotate` and `fill-form` scenarios to `examples/SharpCorePDF.Examples/Scenarios.cs`.
  - Done 2026-10-24: `edit-pages`, `watermark` and `annotate` were registered earlier; `fill-form` joins them
    as `PdfEditExamples.FillFormAsync`, and all 20 scenarios run.
  - `watermark` builds its stamp
    as a document of its own — one A4 page whose `DRAFT` is turned forty-five degrees by
    `PdfMatrix.Rotate(45).Multiply(PdfMatrix.Translate(120, 290))` inside `q`/`Q` — and places it on every page
    with `Overlay(watermark, pageNumber, new PdfOverlayOptions { Opacity = 0.18, Placement = Fit, BlendMode =
    Multiply })`. The run prints `2` pages, `2557` bytes, the two operations, one warning (its own
    `OutputVerified` signal) and the page's text after stamping: the sentence it drew for itself comes back
    whole and the turned stamp a letter per block, `D R A F T`, because each turned glyph has its own
    baseline in page space. `validate-pdf.py` on `watermarked.pdf` reports `qpdf check: no problems`, PDF 1.7,
    2 pages and 6 streams decoded, and `pikepdf` reads page 1 as two `/Contents` entries with one `/Form`
    XObject and one `/ExtGState` holding `/BM /Multiply`, `/CA 0.18` and `/ca 0.18` — the opacity and blend
    mode stay inside the layer.
  - `annotate` (2026-10-24) comments on `summary.pdf` without touching its content: a highlight, a sticky
    note, free text in Helvetica-Oblique `#1F3A5F`, an `Approved` stamp, a URL link and a link to page 2,
    with the highlight then updated by identifier. The run prints seven operations, `3685` bytes, two
    warnings (`AnnotationAppearanceRegenerated` for the page and the engine's own `OutputVerified`) and
    the six annotations the reader returns, each with its rectangle, its identifier and whether it
    carries an appearance. `validate-pdf.py` reports `qpdf check: no problems`, PDF 1.7, 2 pages, 20
    objects, `annots=6` on page 1, 6 streams decoded and 0 failed; a PyMuPDF 2.0 render paints the
    highlight at (255, 223, 129), the note icon at (255, 211, 0), the free text in `#1F3A5F` and the
    stamp frame at (191, 43, 31).
  - `fill-form` (2026-10-24) writes `form-created.pdf` from `summary.pdf` with two text fields, a check box,
    a choice and a push button, fills `person.name`, `agree` and `country` by name in a second pass that
    leaves the other two fields as they were, and flattens the result into `form-flattened.pdf`. The run
    prints five fields after the first pass, the same five with `Grace Hopper`, `Off` and `BE` after the
    second, and none after the flatten, which drops the file from 5,313 to 3,614 bytes. `validate-pdf.py`
    reports `qpdf check: no problems` for all three; `pypdf` reads the three values back and reports no
    field on the flattened file; PyMuPDF renders the flattened page and reads `Grace Hopper` as page text.
- [x] T057 [US2] Write manual chapters `18-annotations.md` and `19-forms.md`; extend `docs/manual/src/14-editing.md` with crop, resize, overlays, incremental updates and document-level edits.
  - Done 2026-10-24. `18-annotations.md` (the kinds and their factories, appearance streams, updating and
    removing by identifier, clipping, the four warnings, the two deliberate behaviours) and `19-forms.md`
    (the field model and its factories, writing a form, filling by name, appearances, reset, flatten, the
    refusals, the four warnings, XFA, the read-back and the example evidence). `14-editing.md` gains the
    seven form and annotation operations in its table, the three operation types it was missing, and a
    paragraph pointing at chapters 18 and 19. The manual was rebuilt: 25 chapters, 213 headings, 111 pages,
    `docs/SharpCorePDF-Manual-1.1.0.pdf`; `docs/README.md` and `00-preface.md` no longer say chapters 18 and
    19 are reserved.
- [x] T058 [US2] Run T039–T044, `quickstart.md` step 4 and the annotation/form read-back checks; close the US2 rows in `docs/parity.md`.
  - Done 2026-10-24: `dotnet test` on the solution ran 1802 tests with 0 failures, all 20 examples run, and
    rows 8 and 9 of `docs/parity.md` are `Met in 1.2` with their named tests and their external evidence.

**Checkpoint**: US2 is independently shippable; US1 and the 1.0 suite still pass.

---

## Phase 4: User Story 3 — Read a Document the Way a Consumer Needs It (Priority: P3)

**Goal**: Letters with positions, configurable words/blocks, reading order, hyperlinks, decoded
image pixels, a read-only object graph and ALTO/PAGE XML/hOCR/SVG exports.

**Independent Test**: `quickstart.md` step 7 scores at least 99.5% character recall, 95% box
agreement and 95% reading-order agreement on the labelled corpus; every export validates.

### Tests for User Story 3

- [x] T059 [P] [US3] Add failing tests for letter fields, ordering, direction and unreadable-glyph warnings in `tests/SharpCorePDF.Tests/PdfLayoutReaderTests.Letters.cs`.
- [x] T060 [P] [US3] Add failing tests for each word, segmentation and reading-order strategy, including two-column pages, in `tests/SharpCorePDF.Tests/PdfLayoutReaderTests.Layout.cs`.
- [x] T061 [P] [US3] Add failing tests for hyperlinks, image pixel decoding and unsupported filters in `tests/SharpCorePDF.Tests/PdfLayoutReaderTests.Hyperlinks.cs`.
  - Half done, 2026-10-22: `PdfLayoutReaderTests.Hyperlinks.cs` holds four green tests
    (`Reads_the_links_of_a_page_and_their_uris`, `Resolves_a_direct_and_a_named_destination`,
    `Keeps_an_unresolved_destination_and_says_so`, `Reads_a_page_without_links_as_no_links`). Pixel
    decoding and the unsupported-filter cases are T072's 1.2 work.
  - Done 2026-10-24: the pixels landed with T072 and are tested in `PdfContentReaderTests.Images.cs` —
    `Exposes_the_samples_of_a_decoded_image_as_pixels` (the 2 × 2 `FlateDecode` fixture comes back as
    `Rgb24` with its twelve samples), `Does_not_expose_a_jpeg_as_pixels` and
    `Does_not_expose_the_pixels_of_a_filter_it_cannot_decode`. The unsupported-filter cases were already
    in place from the 1.1 read: `Returns_an_unsupported_filter_as_raw_bytes`,
    `Reports_a_filter_it_cannot_decode`, and the mask and budget facts that assert `IsDecodable`.
- [x] T062 [P] [US3] Add failing tests for the read-only object graph, dangling references and content-operation offsets in `tests/SharpCorePDF.Tests/PdfObjectInspectorTests.cs`.
- [x] T063 [P] [US3] Add failing tests for ALTO, PAGE XML, hOCR and SVG text exports in `tests/SharpCorePDF.Tests/PdfLayoutExportTests.cs`.

### Implementation for User Story 3

- [x] T064 [P] [US3] Add `PdfLetter`, `PdfHyperlink`, `PdfPageSegment` and the word/segmentation/reading-order option enums in `src/SharpCorePDF.Domain/ReadModels/` and `src/SharpCorePDF.Domain/ValueObjects/`.
  - Decided: `PdfLetter`, `PdfHyperlink` and the four option enums are in; `PdfPageSegment` is not. A page's
    reading is `PdfWord` → `PdfTextBlock`, and the exports of T073 write that, because a segment that also
    carried images, ruled lines and table candidates would promise a segmentation this release does not do
    (images and tables are out of contract). Revisit only when a consumer needs one type per page region.
  - Done 2026-10-24: `PdfLetter` (`ReadModels/PdfLetter.cs`), `PdfHyperlink`
    (`ReadModels/PdfHyperlink.cs`), `PdfWordSegmentationStrategy` and `PdfReadingOrderStrategy` are in the
    tree and are what `PdfLayoutReader`, `WordExtractor` and the corpus test use; `PdfPageSegment` is the
    one type the decision above leaves out, and `PdfLayoutExport` writes `PdfWord` → `PdfTextBlock` instead.
- [x] T065 [US3] Add `IPdfLayoutReader` to `src/SharpCorePDF.Domain/Ports/IPdfLayoutReader.cs` and `PdfLayoutReaderService` to `src/SharpCorePDF.Application/Services/`.
- [x] T066 [US3] Implement `LetterExtractor` over `PdfTextScanner`, `PdfFontDecoder` and `PdfToUnicodeCMap` in `src/SharpCorePDF.Infrastructure/Reading/LetterExtractor.cs`.
- [x] T067 [US3] Implement `ReadWordsAsync` and the raw/nearest-neighbour strategies in `src/SharpCorePDF.Infrastructure/Reading/LayoutAnalysis/WordExtractor.cs`; a strategy only groups glyphs.
- [x] T068 [US3] Implement recursive XY cut and Docstrum-style segmentation plus column-aware reading order in `src/SharpCorePDF.Infrastructure/Reading/LayoutAnalysis/`.
- [x] T069 [US3] Implement `ReadHyperlinksAsync` for URI and internal destinations, with `NamedDestinationUnresolved` warnings in `src/SharpCorePDF.Infrastructure/Reading/HyperlinkReader.cs`.
- [x] T070 [P] [US3] Add `PdfObjectView` and `PdfContentOperationView` in `src/SharpCorePDF.Domain/ReadModels/`; no setter and no mutable child collection.
- [x] T071 [US3] Add `IPdfObjectInspector` and `PdfObjectInspectorService`, and implement the projection in `src/SharpCorePDF.Infrastructure/Reading/ObjectGraphView.cs`; dangling references stay visible.
- [x] T072 [US3] Add optional decoded pixels and `IsDecodable` to the existing image read in `src/SharpCorePDF.Infrastructure/PdfPageContentReader.Images.cs` and `src/SharpCorePDF.Infrastructure/Pdf/`.
  - Done 2026-10-24. `PdfImageInfo` gains `DecodedPixels` and `PixelFormat`, and
    `Infrastructure/Pdf/ImagePixels.cs` decides when a payload is pixels: the library decoded it, the
    geometry is stated, every sample is eight bits, the colour space is `Gray8` or `Rgb24` in kind
    (`DeviceGray`, `CalGray`, `DeviceRGB`, `CalRGB`) and the payload length matches the geometry exactly.
    A payload that fails any of those stays a payload — a palette image still needs its palette, a
    sixteen-bit image its byte order.
  - What a caller gets: `Rgb24` or `Gray8` bytes, tightly packed, top row first, for every filter the
    library decodes. A `DCTDecode` image is deliberately **not** pixels: it is handed over as the JPEG the
    file holds, which is a format a consumer already reads, and the read model says so.
  - `IsDecodable` is unchanged and still states whether the payload is in the form the caller asked for;
    the pixels are withheld whenever it is `false`.
  - Suite: 1808 executions, 0 failures.
- [x] T073 [US3] Implement the four exporters in `src/SharpCorePDF.Infrastructure/Reading/Exporters/`; coordinates are page space, text is escaped, and an empty page still produces a valid element.
- [x] T074 [US3] Register `IPdfLayoutReader` and `IPdfObjectInspector` in `src/SharpCorePDF.DependencyInjection/SharpCorePDFServiceCollectionExtensions.cs`.
  - Half done: `PdfLayoutReader` is registered as itself and as `IPdfPageLayoutReader`, and `PdfLayoutReaderService`
    as `IPdfLayoutReader`, with `AddSharpCorePDFTests` asserting the resolution; `IPdfObjectInspector` joins the
    same block in T071.
- [x] T075 [P] [US3] Add `docs/tools/score-layout.py` for character recall, box agreement and reading-order scoring, and a `docs/parity/README.md` section naming the scoring command.
- [x] T076 [US3] Add `tests/SharpCorePDF.Tests/PdfLayoutCorpusTests.cs` to enforce the SC-004 thresholds over `tests/SharpCorePDF.Tests/Assets/parity/`. The `table` page is a recorded limitation (a table is symmetric under the XY cut, so it reads column by column) and `Table_page_is_still_read_column_by_column` fails if it ever stops being one.
- [x] T077 [US3] Add the `extract-layout` and `inspect-objects` scenarios to `examples/SharpCorePDF.Examples/Scenarios.cs`.
- [x] T078 [US3] Write manual chapters `20-extraction.md`, `21-layout-analysis.md` and `22-object-graph.md`, including export limits and the strategy result field.
  - Done 2026-10-23: `20-extraction.md` (the `PdfReadResult<T>` shape, letters/words/blocks, the four formats, the coordinate flip and reproducible bytes), `21-layout-analysis.md` (the four segmentations, two reading orders, the em-based word rules, decorations versus prose, how to compare two readings, where the analysis stops) and `22-object-graph.md` (one node per listed object, references reported and never followed, `DanglingReference`, streams without bytes, operators, `MaxNestingDepth`). All three are in the 1.1.0 PDF built by T099 (pages 69, 73 and 76).
- [x] T079 [US3] Run T059–T063, `score-layout.py` and the export validators; close the US3 rows in `docs/parity.md`.
  - Done 2026-10-24. A scratch runner outside the repository exported the five labelled corpus documents
    with `PdfLayoutReaderService.ExportAsync`, and the scorer read the folder:
    `python docs/tools/score-layout.py <exports> --labels tests/SharpCorePDF.Tests/Assets/parity/labelled
    --known-order-mismatch table` → **5 documents scored, 0 short of a threshold**; recall and boxes 100%
    on all five, reading order 100% on four, `table` recorded at 66.7% (the known XY-cut limitation, which
    `Table_page_is_still_read_column_by_column` guards).
  - The same document written in all four formats was parsed by an external XML parser: ALTO (`alto`
    root), PAGE XML (`PcGts`), SVG (`svg`) are well-formed, and the hOCR file carries its 22 `ocrx_word`
    spans. There is no schema file in the repository — the 1.1 decision was to score with the tool the
    project ships and parse the exports rather than to vendor four XSDs — so "the export validators" is
    that parse plus `PdfLayoutExportTests`, which asserts the root element and the page-space coordinates
    of each format.
  - Rows 13, 14, 15 and 16 of `docs/parity.md` are closed with this run; row 14's pixel half is T072.

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

- [x] T080 [P] [US4] Add failing tests for detached CMS signing, byte range, visible appearance, certificate-without-key refusal and prior-revision preservation in `tests/SharpCorePDF.Tests/PdfDocumentSignerTests.cs` (depends on T050).
- [x] T081 [P] [US4] Add failing tests for valid/invalid/unknown integrity, separate trust, multiple signatures, tampering and unsupported algorithms in `tests/SharpCorePDF.Tests/PdfDocumentSignatureValidatorTests.cs`.
- [x] T082 [P] [US4] Add failing tests for PDF/A-1b, -2b and -3b output, encryption conflict, unembeddable font refusal and unsatisfied requirements in `tests/SharpCorePDF.Tests/PdfATests.cs`.

### Implementation for User Story 4

- [x] T083 [P] [US4] Add `PdfSignatureOptions`, `PdfSignatureInfo`, `PdfSignatureValidation`, `PdfSignatureProblem` and the hash/trust enums in `src/SharpCorePDF.Domain/ValueObjects/` and `src/SharpCorePDF.Domain/ReadModels/`.
- [x] T084 [US4] Add `IPdfSigner` and `IPdfTimestampProvider` in `src/SharpCorePDF.Domain/Ports/`, and `PdfSignerService` in `src/SharpCorePDF.Application/Services/`.
- [x] T085 [US4] Implement the detached CMS writer in `src/SharpCorePDF.Infrastructure/Signatures/CmsBuilder.cs`, encoding the DER directly rather than through `System.Security.Cryptography.Pkcs`: a detached CMS over the reserved byte range.
- [x] T086 [US4] Reserve and fill the signature byte range in an incremental revision in `src/SharpCorePDF.Infrastructure/Signatures/SignatureFieldWriter.cs`; the original bytes are untouched.
- [x] T087 [US4] Implement the optional RFC 3161 timestamp handshake through the caller callback in `src/SharpCorePDF.Infrastructure/Signatures/CmsBuilder.cs`; the library opens no socket.
- [x] T088 [US4] Implement `SignatureValidator` parsing every signature and computing `Integrity` in `src/SharpCorePDF.Infrastructure/Signatures/SignatureValidator.cs`; unreadable structures are `Unknown`, never `Valid`.
- [x] T089 [US4] Implement certificate-chain trust as a separate result against caller-supplied roots in `src/SharpCorePDF.Infrastructure/Signatures/TrustEvaluator.cs`; no machine store and no online revocation.
- [x] T090 [US4] Add `PdfAProfile` and `PdfAConversionResult` in `src/SharpCorePDF.Domain/ValueObjects/` and `src/SharpCorePDF.Domain/ReadModels/`.
  - Also `PdfAConversionOptions`, `IPdfAWriter`, the application port and service, and `PdfEditOutputOptions.PdfAProfile`/`Encryption`; the profile/encryption conflict is validated there.
- [x] T091 [US4] Implement `PdfAWriter` in `src/SharpCorePDF.Infrastructure/Authoring/PdfAWriter.cs`: embed fonts, write XMP and output intent, and remove or refuse profile-forbidden features.
  - The analysis in `PdfARequirements` runs before a byte is written; `XmpPacket` mirrors the information dictionary, and `SRgbColorProfile` builds the `/N 3` output intent in code.
- [x] T092 [US4] Enforce profile restrictions and the `IsWritten = false` refusal path in `src/SharpCorePDF.Infrastructure/PdfEditEngine.Document.cs`; never label an unconformant file as PDF/A.
- [x] T093 [P] [US4] Add `docs/tools/validate-signatures.py` and `docs/tools/validate-pdfa.py` with the pyHanko and veraPDF commands from `quickstart.md`.
  - Both scripts are in the tree and take `--help`. `validate-pdfa.py` finds veraPDF on `PATH`, through `VERAPDF_HOME` or with `--verapdf`, states the flavour with `--profile`, and exits 0/1/2 for pass/fail/cannot-run.
- [x] T094 [US4] Add the `sign-and-archive` scenario to `examples/SharpCorePDF.Examples/Scenarios.cs`, using local certificates and a caller-supplied timestamp token; ship no key.
  - Done 2026-10-24. `ArchiveExamples.SignAndArchiveAsync` authors a page in an embedded face, archives it
    for PDF/A-3b, then signs **the archive** — the order that keeps both promises, because a signature is an
    incremental revision and converting first would rewrite the bytes a signature names. The certificate is
    created when the run starts (`CertificateRequest` + `CreateSelfSigned`, round-tripped through PKCS#12 so
    the private key stays attached) and is never written to disk; the timestamp token is the caller's, read
    from `SCPDF_TIMESTAMP_TOKEN` as base64 DER and handed to the signer through a one-method
    `IPdfTimestampProvider`, so a run with no authority signs without one rather than pretending.
  - The run prints `written True` and 6,778 bytes for the archive, field `ArchivalSignature`, signer
    `CN=SharpCorePDF Example Signer, O=SharpCorePDF, C=AT`, `Sha256`, `timestamp False`, 25,749 bytes for the
    signed file, and the validation `integrity Valid, trust Trusted, is valid True` with the certificate as
    the anchor.
- [x] T095 [US4] Write manual chapters `23-signatures.md` and `24-pdf-a.md`; state the integrity/trust split, the no-network rule and the external-validator evidence requirement.
  - Done: `docs/manual/src/23-signatures.md` and `docs/manual/src/24-pdf-a.md` are written — the appended revision and the integrity/trust split, the profile table, what is removed versus refused, archival edits, and the pyHanko/qpdf/veraPDF evidence. Both carried the unreleased banner until the release boundary; T099 removed it, because 1.1.0 ships both surfaces.
- [ ] T096 [US4] Run T080–T082, pyHanko, veraPDF and the incremental-signature scenario; close the US4 rows in `docs/parity.md`.
  - Half done: T080–T082 pass on both frameworks, pyHanko and qpdf are recorded under "Phase 4 progress — signatures", veraPDF 1.30.3 reports `PASS` for 1b/2b/3b of an authored page and of `SafeWebCore.pdf` under "Phase 4 progress — archival output", and parity rows 11 and 12 are closed as Met. The `sign-and-archive` scenario (T094) is still missing.
  - 2026-10-24: the example exists (T094) and pyHanko was run on what it wrote — `signature 1: field
    'ArchivalSignature'`, the expected signer, `signed at 2026-01-01 00:00:00+00:00`, `integrity intact
    (coverage ENTIRE_FILE)`, `trust not checked (no anchor supplied)`, which is the split this library keeps
    confirmed by an independent implementation.
  - What still keeps the box open: the veraPDF leg cannot be re-run here — `validate-pdfa.py` reports
    `veraPDF was not found: put it on PATH, set VERAPDF_HOME, or pass --verapdf <path>` and exits 2 — so the
    conformance record remains the 2026-10-23 run rather than a fresh one. The incremental-signature leg is
    covered by `PdfDocumentSignerTests.Signing_a_second_time_keeps_the_first_signature_valid`, which is green;
    the example signs once.

**Checkpoint**: US4 is independently shippable once US2's incremental writer exists.

---

## Phase 6: Polish and Release

- [x] T097 [P] Update every `PublicAPI.Unshipped.txt` for the four layers and add a public-surface contract test that fails on an undocumented addition.
  - Done 2026-10-23: `tests/SharpCorePDF.Tests/PublicApiSurfaceTests.cs` — the shipped snapshot and the live assembly are compared as a bijection in both directions, duplicates are refused, and every snapshot starts with `#nullable enable`. The test is deliberately name-based (`Type.Member` for every public and protected member) rather than signature-based, so it does not duplicate the analyzer. Negative control: an entry added to a snapshot without a member (and a member without an entry) fails the test.
- [x] T098 [P] Write `docs/migration/pdfsharp.md` and `docs/migration/pdfpig.md`, mapping every parity row to an example or an explicit out-of-scope statement.
  - Done 2026-10-23: both guides carry a substitution table, a "differences that don't survive" section, the full 21-row parity table with a *Where to look* column (Ships / 1.2 / Out of scope), a note that the *Must* rows 3, 6, 8, 9 and 10 are owed in 1.2 with their task numbers, and a "what to read next" list. Row order matches `docs/parity.md`, which both name as the authority.
- [x] T099 [P] Complete the manual index, rebuild `docs/SharpCorePDF-Manual-1.1.0.pdf` from 25 chapters, and verify the contents, bookmarks and page count.
  - Done 2026-10-23, with the chapter count settled by D3: the edition is built from the **20 chapters that describe what 1.1.0 ships** (00–14 and 20–24), not 25. Chapters `15`–`19` document the standalone drawing surface, text layout, the remaining image formats and writing annotations and form fields — all `1.2` work owned by T037, T057 and the US2 backlog — so their numbers are reserved and the preface says so in a paragraph of its own. The index additions are `20-extraction.md`, `21-layout-analysis.md`, `22-object-graph.md` (T078) plus the removal of the "unreleased" banners from chapters 14, 23 and 24, which now ship.
  - Build: `python docs/manual/build-manual.py` → 20 chapters, 154 headings (contents lists 154, every number resolved), 86 pages, footer ok, `docs/SharpCorePDF-Manual-1.1.0.pdf`. Verified with `pypdf`: 86 pages, 154 outline entries with chapters 1–14 and 20–24 at the top level, contents rows read `Preface 6` … `24. Archival PDF (PDF/A) 83`, and the footer reads `SharpCorePDF 1.1.0 | The Manual Page 2 of 86`. `docs/README.md`, the root `README.md` link and the cover/metadata in `build-manual.py` now name the 1.1.0 edition.
  - Chapters 2 and 12 were corrected in the same pass, because the boundary they state had fallen behind the code: chapter 2 now records fixed-position vector drawing, cropping, signing, PDF/A writing and the read-only object graph as shipped, and chapter 12 documents `DrawLine`/`DrawRectangle`/`DrawEllipse`/`DrawPath`/`SetClip`/`SaveState` plus `PdfPaint`/`PdfStroke` in its surface table instead of claiming there is no vector drawing.
  - **The manual was rebuilt again on 2026-10-23** when T037 wrote Chapter 16, so the figures above describe the 1.1.0 edition as first built and not the current artifact: `python docs/manual/build-manual.py` now reports 21 chapters, 168 headings (contents lists 163), **92 pages**, footer ok, and `pypdf` reopens that file as 92 pages with a top-level `16. Text Layout` bookmark and a contents row `16. Text Layout 70`. `docs/README.md` was updated to 92 pages. The chapter states at its head that the `1.1.0` package does not carry the text-layout surface it documents — the edition is 1.1.0 plus that one `1.2` chapter, included so the code in the tree is documented where it is.
- [x] T100 [P] Update `examples/README.md` and run every example scenario; each must print its success marker and exit 0.
  - Done 2026-10-23: `examples/README.md` lists fifteen examples, including `edit-pages` (authoring a page with a stroked frame, a translucent band, a dashed rule and an ellipse, then insert/rotate/delete/crop in one pass). `dotnet run --project examples/SharpCorePDF.Examples -c Release` ran all 15 scenarios, printed 15 `ok (...)` markers and `All 15 example(s) ran.`, and exited 0. The `edit-pages` crop was corrected to an upright `new PdfRectangle(36, 36, 806, 559)`: page 3 crops with no warning, the result reads back as 3 pages with page 2 `Clockwise90`, and the only warning is the `OutputVerified` success signal.
- [x] T101 Re-run the creator, editor, extraction and merge measurements; record them in `docs/measurements.md` and block any regression above 10%.
  - Done 2026-10-23: all five harness modes re-run on the release tree and recorded in `docs/measurements.md` §10, each row beside the figure it was first recorded at. No allocation row moved by more than 10% (worst: +2.0% on the 150 × 6.7 KB merges), and every merge wrote the same number of bytes as before, including the 139,645,394 B corpus row.
  - The budgets, as measured: 150 × 1.0 MB merges in 0.26 s at 544.3 MB/s (budget 10 s / 15 MB/s), fixed cost 0.36 ms per extra source (budget 1 ms), creator 13.32 ms against SC-002's 2 s, editor 3.92–4.53 ms against SC-003's 3 s. The scaling ratio is 1.43x at 75 MB and 0.90x at 150 MB — the coin flip §2 already records as not reliably met; this run neither fixes nor hides it.
  - Two readings are recorded rather than smoothed: the 50 MB copy route allocated 116,837 KB on the first run of the day, after the 300 MB merge rows, and 51,301 KB on every run after it (§10 keeps the reproducible value and the outlier beside it); the creator writes about 4.7 KB more than when §9 was recorded, on a document whose budget is time and not size.
- [x] T102 Run the determinism, empty-font, no-network and no-new-package checks from `quickstart.md` step 8.
  - Done 2026-10-23, all four:
    - **Determinism.** `author-page` twice, in two processes, wrote byte-identical `cover.pdf`: SHA-256 `499520B9B79F5476A9C45160134DDBF5783391AD3CF72FCD8A74937D340D7A3D`. A default merge is *not* reproducible — `/ModDate` is the current time and the file identifier is derived from the file, so `cover-and-summary.pdf` differed between the two runs (`7AF7A3B7…` then `BB82DBB9…`). That is the documented behaviour (manual chapter 6): with `ModificationDate` pinned, `options` prints `identical True` for its two in-process merges and writes the same `report.pdf` in a second process (`D32E1E6E7A99EA57C74E89F0B47BBC932A22D4C7AC083DB99EB444A6D8B65EB3`). Step 8 now states exactly this instead of implying that any merge is byte-stable.
    - **Empty font.** `PdfTypeface.FromTrueType` refuses an empty program, a 12-byte zero program and a 4-byte program with `ArgumentException`, and the empty case names `fontProgram`. This was the one documented refusal with no test, so `Refuses_an_empty_font_program` was added to `PdfDocumentBuilderTests`; both legs are now 743 tests.
    - **No network.** A code-only search of `src/` for `HttpClient`, `WebRequest`, `WebClient`, `Socket`, `TcpClient`, `UdpClient` and `Dns` returns nothing. The unfiltered search returns two hits, both of them doc comments saying the library never opens a socket (`IPdfTimestampProvider.cs:9`, `PdfSignatureOptions.cs:17`) — step 8 now carries the `Where-Object` that drops comment lines, and says why, so a green result is not an artifact of the filter being dropped by accident.
    - **No new package.** `dotnet list … package --include-transitive` lists 108 package lines and none of PDFsharp, PdfPig, SkiaSharp or System.Drawing; `git diff v1.0.0 -- 'src/**/*.csproj'` adds no `PackageReference`. The four shipped assemblies still reference no runtime package at all — only the two analyzers — and `SharpCorePDF.DependencyInjection` still references the same three `Microsoft.Extensions.*` 10.0.12 assemblies `1.0` carried.

- [x] T103 Run the full Release build and both test legs; require 0 warnings, 0 errors, all 1.0 tests unchanged and at least 90% coverage on new code.
  - **Build and both legs: met, 2026-10-23.** `dotnet build .\SharpCorePDF.slnx -c Release -t:Rebuild` → `0 Warning(s)`, `0 Error(s)`, 11 s. `dotnet test … -p:TargetFramework=net11.0` → 743 passed, 0 failed, 0 skipped, 3.5 s; `dotnet test … -p:TargetFramework=net10.0 -r win-x64` → 743 passed, 0 failed, 0 skipped, 3.5 s. The 1.0 baseline was 561 and has grown to 743; `git diff v1.0.0 -- tests` removes no `[Fact]`/`[Theory]` (0 removed, 1 deletion in 134 files), so no 1.0 test was dropped or renamed to make the number.
  - **Coverage on new code: not met.** One instrumented run, the CI command: `./.tools/dotnet-coverage collect -f coverage -o artifacts/coverage/net11.0.coverage "dotnet test … -p:TargetFramework=net11.0"`, then `merge -f xml`. Measured with the new `python docs/tools/coverage-on-new-code.py artifacts/coverage/net11.0.xml`, which takes "new code" from `git diff --unified=0 v1.0.0 -- src` and counts only the added lines the report instruments: **6,718 of 8,798 (76.36%)** against the required 90%.
  - The shipped surface in the same report: Domain 997/1,255 (79.44%), Application 306/375 (81.60%), Infrastructure 8,255/9,579 (86.18%), DependencyInjection 55/63 (87.30%). The same report split at the same diff — the 162 files the diff touches hold 7,363/9,550 executable lines (77.10%), the files it does not touch hold 2,235/2,542 (87.92%) — so the miss is the 1.1 surface and not the measurement. Of the 2,080 added lines the suite never executes, 1,243 sit in twenty files, led by `PdfContentScanner` (166 of its 330 added lines; file at 49.70%), `TrueTypeFont` (94; 59.66%), `PdfTextScanner` (93; 65.04%), `SignatureValidator` (85; 62.39%) and `PdfARequirements` (84; 57.36%).
  - The measurement was taken twice: the first run above, and a second instrumented run of the same tree (743/743) that reproduced 6,718 of 8,798 exactly — the number does not move between runs. `artifacts/coverage/` now holds the later runs as well: `net11.0-t103b.xml` is the intermediate 77.48%, `net11.0-final.xml` the 748-test 77.76%, and `net11.0-s2583.xml` the release figure after the quality-gate fix.
  - **The decision was taken on 2026-10-23: record it, do not fake it.** Two further runs measured the same tree after the tests below, so the progression is a test count and not a definition: 6,718 of 8,798 (76.36%) at 743 tests → 6,817 (77.48%) after three inline-image tests → **6,841 of 8,798 (77.76%) at 748 tests** after two container-level signing tests, and **6,842 of 8,799 (77.76%)** once the quality-gate fix below added one covered line. Shipped surface in the final report: Domain 997/1,255 (79.44%), Application 330/373 (88.47%), Infrastructure 8,346/9,585 (87.07%), DependencyInjection 59/63 (93.65%).
  - The five tests are behaviour, not line-filler: the inline-image abbreviations a producer actually writes (computed sample count, indexed colour space as an array, image mask with a decode array, an `AHx` payload whose length the header does not state, a header with a comment and a literal string) plus an image with no `EI`; and the signing pair resolved from `AddSharpCorePDF()` — sign, validate as `Valid`/`Trusted`, and the three arguments `PdfSignerService` refuses before opening the source. They took `PdfContentScanner` from 166 uncovered added lines to 74.
  - `docs/adr/0002-new-code-coverage-1.1.0.md` records the decision: `1.1.0` ships with 77.76% named in the ADR, in G9 and in the release notes; the 90% floor is unchanged and is re-measured on 1.2's new code; no test is written whose only effect is to execute a line; and the eight parser/validator files that hold the rest become a 1.2 task. The ADR carries a signature table for the release owner — signed 2026-10-23 — because an unsigned waiver is the one outcome the gate exists to prevent.
  - **What is left on T103 was that signature, and it is signed.** With the ADR signed, the box closes on a measured, named number; without it, the box would have stayed open and T106 would not tag. Everything else the task asks for — 0/0 build, both legs at 748, no 1.0 test removed, a number measured three times — is done and recorded above.
  - **Signed 2026-10-23.** `docs/adr/0002-new-code-coverage-1.1.0.md` reads `Accepted`, its approval table reads `MPCoreDeveloper` / `2026-10-23`, and `docs/adr/README.md` agrees. The box above closes on the signature, not on the threshold: 77.76% is still what `1.1.0` ships with, and the 90% floor still applies to 1.2's new code.
- [x] T104 Run every `quickstart.md` phase command on the release commit and record the commands, versions and outputs in the pull request.
  - The gate's own transcript is not a phase command: run
    `uv run --with pikepdf --with pypdf --with pymupdf python docs/tools/verify-cover.py --output artifacts\c5-transcript.md`
    and attach that file — it is C5's evidence for the tagged commit.
  - **Run 2026-10-23**, Windows 11 x64, .NET SDK 11.0.100-rc.2.26504.105, Python 3.12.10, pikepdf 10.17.0 (qpdf inside), pypdf 6.19.0, PyMuPDF 2.0. Transcript: `artifacts/t104-release-transcript.md`; C5's own run: `artifacts/c5-transcript.md` → `pass — 0 expectation(s) broken`, 8,835-byte `cover.pdf`, `ok (246 ms)`.
    - **Step 1 and step 8** — build 0/0; both legs 743/743; all 15 examples `ok (…)`; the four T102 checks (determinism, empty font, no network, no new package) as recorded there.
    - **Phase 1** — 34 builder tests pass; `author-page` `ok (228 ms)`; `verify-cover.py` breaks 0 expectations; `validate-pdf.py`: qpdf clean on `cover.pdf` (1 page, 14 objects, 6 streams decoded, 0 failed) and on `cover-and-summary.pdf` (3 pages, 19 objects, 8 streams).
    - **Phase 2** — 37 editor tests pass; `edit-pages` `ok (151 ms)` → 3 pages, page 2 `Clockwise90`, page 3 cropped, the only warning its `OutputVerified` signal; `validate-pdf.py` qpdf clean, 3 streams decoded; `pypdf` reads page 3's `cropbox` as `[36, 36, 806, 559]`.
    - **Phase 4** — 12 signer, validator and PDF/A tests pass; `archive-pdfa` `ok (188 ms)` → 1b, 2b, 3b each with 0 unsatisfied requirements and the unembedded-Helvetica page refused with 0 bytes written; `validate-pdf.py` qpdf clean on all three, 6 streams each; `validate-pdfa.py` exits 2, *cannot run* — veraPDF is not installed here, so the conformance claim rests on the recorded veraPDF 1.30.3 `PASS` under “Phase 4 progress”, not on this run.
    - **Phase 5** — 78 layout, export, corpus and object-graph tests pass; `extract-layout inspect-objects` `ok (24 ms)` → 8 objects, 0 dangling references, 5 operations in paint order. `score-layout.py` is T079 (open) and needs a folder of ALTO exports of the five labelled documents.
  - **Commands corrected while running them.** Phases 3 and 4 named test classes and scenarios that do not exist: `PdfAnnotationTests`/`PdfFormTests`/`annotate-and-fill` are 1.2 work (chapters 15–19 are reserved and the tree has no annotation or form API), `PdfSignatureTests` is `PdfDocumentSignerTests`/`PdfDocumentSignatureValidatorTests`, and `sign-and-archive` is T094. `creator-parity` (T036) does not exist either, so step 8 hashes `author-page`. The `validate-pdf.py`/`validate-signatures.py` calls carried flags the tools never had (`--check-syntax`, `--pages`, `--geometry`, `--annotations`, `--form-values`); both tools print everything they check for the files named on the command line. `quickstart.md` now runs the 1.1 path with the commands that exist and marks the 1.2 ones as 1.2, which is what makes this the release's reproducible run book.
  - **CI found one thing three local runs could not.** On the Linux leg,
    `ParityCorpusTests.Every_manifest_entry_exists_and_matches_its_hash` failed on
    `labelled/header-body.json`: the manifest records the bytes the generator wrote, the generator used the
    platform's newline, and Git committed the LF form of a CRLF file — so the corpus matched its own manifest
    on the machine that made it and nowhere else, and the only place that shows is a Linux checkout. Three
    fixes, one per level: `make_parity_fixtures.py` writes LF explicitly (`newline="\n"` for the manifest and
    the labelled exports), `.gitattributes` marks `tests/SharpCorePDF.Tests/Assets/parity/**` as `-text` so no
    checkout converts a fixture again, and the five labelled hashes and byte counts in `manifest.json` were
    recomputed from the LF bytes. Verified against the index blobs — which is what every platform checks out —
    95 entries, 0 mismatched, and 748/748 locally.
  - **The gate then failed for a reason that was not coverage.** With the tests green, SonarCloud's quality
    gate still went red: `new_coverage` was **83.5%** against its own floor of 80, and the failing condition
    was `new_reliability_rating` = 3 — one new bug, `csharpsquid:S2583` at `PdfImageFilters.cs:287`,
    "condition so that it does not always evaluate to `False`". It is not a real bug: `written` is assigned
    inside the row callback `Push` invokes synchronously, and the analyser does not follow the delegate. But a
    gate is read, not argued with, so the code says what it means instead: `PredictorRowDecoder` gained a
    `Push(ReadOnlySpan<byte>, Memory<byte>)` overload that returns the bytes it wrote, and the caller branches
    on that return value. `Memory<byte>` rather than `Span<byte>` because a span cannot be captured by the
    callback inside the overload — the compiler said so (CS9108) on the first attempt. The four lines it adds
    are covered, so the release figure is unchanged at two decimals: 6,842 of 8,799 (77.76%).
  - **CI is green on `ea95c78`**: both test legs, the coverage report, and `QUALITY GATE STATUS: PASSED` from
    SonarCloud — the first run of this branch that passed it.
  - **Opened 2026-10-23: [#4](https://github.com/MPCoreDeveloper/SharpCorePDF/pull/4)** —
    `003-pdf-platform-parity` → `main`, twelve commits, not a draft. `artifacts/t104-release-transcript.md` is
    Git-ignored, so it is in the body under a collapsed block rather than attached to the conversation, and the
    body says so. It also says that the transcript's own summary row reading 76.36% predates the five tests in
    the release commit — 77.76% is the figure the release ships with. The body's headline is the gate that is
    not met and the signature it waits on, not the parts that passed; the durable summary is still
    `docs/release-checklist.md` §“The 1.1.0 run of quickstart.md (T104)”.

- [x] T105 Confirm `docs/parity.md` has no unevidenced “Must” row, then update the 1.1 entry in `CHANGELOG.md`, bump `<Version>`, and complete `docs/release-checklist.md` G1–G9.
  - The first half starts at Gate 0: `C5`/`C6` prove the question's rows, and `C6` is decided — D3. A “Must”
    row that 1.1 does not deliver is either implemented or moved with the recorded decision; it is never
    left Planned in a tagged release, and `docs/parity.md` is edited in the same commit as the tag.
  - **Done 2026-10-23.** `docs/parity.md` was audited row by row — all 21 of them, with the outcome written
    into the D3 section of that file: eleven name `1.1` (1, 2, 4, 5, 7, 11, 12, 13, 14, 15, 16), five are
    excluded non-goals (17–21), five name `1.2` only (3, 6, 8, 9, 10). No row claims `Evidenced` or `Met`
    without both a named test and an external command: the four that do (1, 11, 12, 13) name tests that are in
    the suite and a command that was run, and say which run. Rows 3, 6 and 10 still read `Planned` after D3 —
    the one label D3 item 3 rules out in a tagged release — so they now read `Deferred` and keep their 1.2
    task (T028, T040, T042); rows 8 and 9 stay `Partial`, because the reading half is evidenced and the
    writing half *is* the 1.2 task. No row moved from 1.2 to 1.1 and none was relabelled out of scope.
  - `CHANGELOG.md`: `## [Unreleased]` became `## [1.1.0] - 2026-10-23`, and a *Known limits at 1.1.0* section
    was added — the 77.76% new-code figure with a link to ADR-002, and the two limits 1.0.0 already carried
    (no symbol package, no code signing). `<Version>` is `1.1.0` and `AssemblyVersion`/`FileVersion` are
    `1.1.0.0` in `src/Directory.Build.props`, whose `PackageReleaseNotes` now describe 1.1 rather than 1.0.
  - `docs/release-checklist.md` gained **G1–G9 at `1.1.0`**: each gate restated with its state and where the
    evidence is (G0 closed, G1 true for the 1.1 surface, G2 unchanged, G3 met, G4 still partly, G5 outside the
    tree, G6 to be re-run, G7 to be cut, G8 after the push, G9 recorded at 77.76%), and the three things
    between that table and a tag: the ADR-002 signature, the pull request, and the tag naming the commit whose
    analysis is newest on `main`.
  - `dotnet build SharpCorePDF.slnx -c Release` after the bump: **0 warnings, 0 errors**; both legs 748/748.
- [x] T106 Run `git diff --check`, confirm the tree is clean apart from the release commit, and tag only the commit whose SonarCloud analysis is the newest on `main`.
  - **`git diff --check` was not clean; it is now, 2026-10-23.** The working-tree check passed — 33 changed
    paths before the release commit, none after it — but the check this gate is really about is against the
    last tag, and `git diff --check v1.0.0 -- .` found 31 blank lines before end-of-file in files 1.1 touched
    (nine manual chapters, five contracts, six spec files, four test files, `PdfFontDecoder`,
    `PdfAConversionOptions`, `make_parity_fixtures.py`, `PdfEditExamples`) and two trailing spaces inside the
    DejaVu licence copies.
  - The fix is whitespace only, and checkable as such: every one of those diffs is deletions and nothing else,
    and each file still ends on the line it ended on. The blank lines are gone. The two licence lines lost the
    trailing space a hard-wrapped upstream file carries — that is the one edit worth reading closely, because
    it is the only place 1.1's text differs from the distributed DejaVu licence by a byte, and it changes no
    word of the licence.
  - **Verified after the fix:** `dotnet build SharpCorePDF.slnx -c Release` **0 warnings, 0 errors**; both legs
    **748/748**; `git diff --check` and `git diff --check v1.0.0` both exit 0.
  - **The release commit** — *Close the 1.1.0 release gates and record the coverage it ships with* — sits on
    `003-pdf-platform-parity` holding lanes A, B, C and D, with a clean tree behind it.
  - **Not done then, and done on 2026-10-23: the tag.** It waited on the ADR-002 signature (T103) and on the
    merge of [#4](https://github.com/MPCoreDeveloper/SharpCorePDF/pull/4) (T104), because this gate tags the
    commit whose SonarCloud analysis is the newest on `main`, and that commit did not exist until both were
    true. `v1.1.0` must also equal `<Version>` 1.1.0 — G7 — which `publish.yml` checks for itself.
  - **The merge.** `gh pr merge 4 --rebase` replayed the branch's twenty-one commits onto `main`; the tip is
    `effe5de` — *Sign the coverage record: 1.1.0 ships with 77.76% named, not waived*. The rebase rewrote the
    branch SHAs, so the pre-merge hashes quoted above (`ea95c78`, `2d85a69`) no longer name commits on `main`;
    the runs they identify are still there, and the merge commit is the one this gate tags. CI on `effe5de`
    passed, and SonarCloud's newest analysis of `main` is that same revision.
  - **The gate, read before the tag:** six conditions, all `OK` — `new_coverage` **84.2%** against its 80 floor,
    `new_duplicated_lines_density` 1.3%, the three ratings at 1, hotspots reviewed 100%.
  - **G6, on `main`, after the merge and before the tag** (`publish: false`, run `39967722848`): sonar-gate
    `success`, then build, both legs, pack, the pre-push checks and the local-feed handshake `success`, with
    `NuGet login` and `Push to nuget.org` correctly `skipped`. This is why the dry run cannot be dispatched from
    the branch: its first job compares `github.sha` with the newest analysis of `main`.
  - **The tag.** `git tag -a v1.1.0 effe5de` — annotated, message `SharpCorePDF 1.1.0`, matching `<Version>`
    1.1.0 and naming the analysed commit. The tag push ran `publish` (`39968375808`): sonar-gate `success`,
    OIDC login `success`, **`Push to nuget.org` `success`**, with the local-feed handshake `skipped` because the
    run was publishing rather than rehearsing.
  - **G8, after the push.** All five packages resolve at `1.1.0` from `api.nuget.org`
    (`sharpcorepdf`, `.domain`, `.application`, `.infrastructure`, `.dependencyinjection`); the
    [`SharpCorePDF` page](https://www.nuget.org/packages/SharpCorePDF/1.1.0) renders the README, the logo and the
    four dependencies at `>= 1.1.0` for both legs, and carries no assemblies of its own. An empty `net11.0`
    console app that references only `SharpCorePDF` 1.1.0 merged two corpus documents —
    **6 pages, 6,623 bytes, PDF 1.7, 0 warnings** — and the inspector reported it readable with 6 outline
    items and a uniform 595.28 × 841.89 pt page; `validate-pdf.py` said `qpdf check: no problems`, version 1.7,
    6 pages, 6 bookmarks, 6 streams decoded, 0 failed. The same program on `net10.0` merged `SafeWebCore.pdf`
    with a third corpus document — **9 pages, 934,348 bytes, 0 warnings**, readable, 3 outline items, page size
    not uniform (Letter and A4) — and validated the same way. One thing a consumer learns and the README does
    not say: `Microsoft.Extensions.DependencyInjection` is needed to build the provider that
    `AddSharpCorePDF()` registers into, because the four layer packages reference the abstractions and not the
    container.
  - **API compatibility, which G8 asks for as a report and this tree can prove as a diff:**
    `git diff v1.0.0 -- 'src/*/PublicAPI.Shipped.txt'` is **empty** — no shipped member was removed or changed
    since 1.0.0 — while the unshipped snapshots grow by **1,788 lines** (Domain 1,551, Infrastructure 152,
    Application 85). The release is additive by construction: `RS0016`/`RS0017` are build errors, so an addition
    cannot be made without being recorded and a removal cannot be made without failing the build.

---

## Dependencies and Execution Order

### Phase dependencies

- **Phase 0** blocks every implementation phase. A parity row without evidence is not done.
- **Phase 1** blocks US1, US2 and US3. US4 also needs T050 from US2.
- **US1 (P1)** is the MVP of the parity target: Phase 0 + Phase 1 + T022–T038. After D3 it is the first
  block of `1.2.0`, not of `1.1.0`.
- **US2 (P2)** starts after Phase 1; T050 (incremental writer) blocks US4.
- **US3 (P3)** starts after Phase 1 and may run in parallel with US2 and US4.
- **US4 (P4)** starts after T050; signing and PDF/A can then run in parallel.
- **Phase 6** normally starts only when the desired stories are complete. Under D3 it starts now, because
  the desired stories for `1.1.0` are the ones already in the tree; T050 and the rest of US2–US4 are
  `1.2.0` work that no longer blocks this release.

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

### 1.1.0 path (D3)

1. **Gate 0 is closed** — `C6` decided (D3) and `C5` reduced to one command, `docs/tools/verify-cover.py`,
   whose rehearsal on 2026-10-23 broke none of the recorded values. Lane C re-runs it on the tagged commit
   and pastes the transcript.
2. Lanes A and B in parallel (T097–T100).
3. Lane C (T101–T104) once A and B are green.
4. Lane D (T105–T106): parity rows, changelog entry, `<Version>`, checklist G1–G9, tag.
5. `1.2.0` opens with the 36 story tasks in the order US1 → US2 → US3 → US4; its first commit restores
   rows 2–16 to their full `1.2` target and a second scope decision is written if that slips again.

### Remaining effort, 2026-10-23 (D3)

| Scope | Tasks | What is left | Estimate |
| --- | --- | --- | --- |
| **1.1.0 — Gate 0** | C5, C6 | **closed 2026-10-23**: `verify-cover.py` is C1–C3 in one command, and its rehearsal broke 0 recorded values | spent |
| **1.1.0 — lane A** | T097, T098 | public-surface snapshot and contract test, two migration guides | 2–3 days |
| **1.1.0 — lane B** | T099, T100 | manual index and the 1.1.0 PDF from the chapters that describe what ships (D3: 20, not 25), every example run | 2–3 days |
| **1.1.0 — lane C** | T101–T104 | measurements, determinism/no-network/no-package, both legs, quickstart and C1–C3 transcripts | 2 days |
| **1.1.0 — lane D** | T105, T106 | parity rewrite, changelog, `<Version>`, checklist G1–G9, tag | 1–2 days |
| **1.1.0 total** | 10 tasks | | **7.5–10.5 days serial, 2–3 days on four lanes** |
| 1.2 — US1 creator | 13 | metrics, wrap, JPEG/BMP/GIF, `PdfImage`, import/outline, chapters | 12–16 days |
| 1.2 — US2 editing | 17 | overlay, XMP/outlines, incremental writer, annotations, forms | 20–28 days |
| 1.2 — US3 reading | 4 | links, pixels, closure run | 4–6 days |
| 1.2 — US4 trust | 2 | `sign-and-archive`, closure run | 1 day |
| **1.2 total** | **36** | | **36–51 days (7–10 weeks solo)** |

The 1.1.0 rows are the release. The 1.2 rows are the parity promise the specification still describes:
nothing there is cut, it is moved, with a date and a task number, and `1.2.0` cannot be tagged without
them because its own SC-001 names the rows it must evidence.

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
