# Tasks: Authoring a Page

**Input**: Design documents from `/specs/002-page-authoring/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [contracts/authoring.md](./contracts/authoring.md), [quickstart.md](./quickstart.md)

**Tests**: included and required. Every behaviour below is carried by a named test in
`tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.cs`; the assertions go through the library's own read
side, so a document that only looks right in its own bytes does not pass.

**Status**: implemented. `[x]` means the task is present in the working tree and its evidence was
reproduced: the Release build reports 0 warnings and 0 errors; the full suite reports 549 passed on
`net10.0` and on `net11.0`; the authoring tests also report 23/23 and the full suite 549/549 in a
`mcr.microsoft.com/dotnet/sdk:10.0` Linux container with an empty font directory (0 font files), which
closes the no-system-font acceptance scenario; the cover built there is byte-identical to the host cover;
the example writes a cover that `pikepdf` and the real
`qpdf --check` accept with no problems, `pypdf`, `pdftotext` and PyMuPDF extract the required characters,
and PyMuPDF and `pdftoppm` render with the logo visible and its transparent margin blank. `[ ]` is
outstanding.

## Phase 1: Setup

- [x] T001 Confirm the layered solution and the build gate in `SharpCorePDF.slnx` and
  `src/Directory.Build.props`: both legs, nullable enabled, SonarAnalyzer active, RS0016/RS0017 as
  errors. No project is added.
- [x] T002 [P] Add the font fixture `tests/SharpCorePDF.Tests/Assets/DejaVuSans-subset.ttf` (17,572
  bytes, subsetted from DejaVu Sans 2.37 with `fontTools.subset` to Latin-1, the typographic
  characters and the euro) with `DejaVu-LICENSE.txt` beside it, and copy `*.ttf` in
  `SharpCorePDF.Tests.csproj`.

## Phase 2: Foundational

- [x] T003 [P] Add `src/SharpCorePDF.Domain/ValueObjects/PdfColor.cs`: RGB bytes, `Black`, `White`,
  `FromRgb`, `FromHex`, validated and documented.
- [x] T004 [P] Add `src/SharpCorePDF.Domain/ValueObjects/StandardFont.cs`: the twelve Latin standard
  faces, with the reason Symbol and ZapfDingbats are absent.
- [x] T005 [P] Add `src/SharpCorePDF.Infrastructure/PdfAuthoringOptions.cs`: `CreationDate`,
  `DocumentId` (32 hexadecimal digits), `Producer`, and `Validate()`.
- [x] T006 [P] Add `src/SharpCorePDF.Infrastructure/Authoring/TrueTypeFont.cs`: sfnt and table
  directory parsing, `head`/`hhea`/`maxp`/`hmtx`/`loca`/`glyf` access, `cmap` formats 0/4/6/12,
  advances, bounding box, cap height, italic angle, PostScript name, composite components.
- [x] T007 [P] Add `src/SharpCorePDF.Infrastructure/Authoring/TrueTypeSubsetWriter.cs`: glyph closure,
  `glyf`/`loca` rebuild, short and long offsets, `head.indexToLocFormat`, copied optional tables,
  sorted directory and sfnt checksums.
- [x] T008 [P] Add `src/SharpCorePDF.Infrastructure/Authoring/PngImage.cs`: signature and `IHDR`
  validation, `PLTE`/`tRNS`/`pHYs`/`IDAT`, predictor pass-through, inflate/unfilter (five filters),
  alpha split, palette expansion, colour key, natural size, and refusals for interlace and 16-bit.

## Phase 3: User Story 1 — a cover in front of a document

- [x] T009 Add `src/SharpCorePDF.Infrastructure/PdfPageCanvas.cs` with the top-left coordinate system
  and `DrawText`/`DrawImage`, plus validation of every argument; a line break is refused.
- [x] T010 Add `src/SharpCorePDF.Infrastructure/PdfDocumentBuilder.cs`: `Create`, `WithMetadata`,
  `AddPage`, `ToArray`, `SaveAsync`, and `Build` returning `PdfByteArraySource`.
- [x] T011 Add `src/SharpCorePDF.Infrastructure/Authoring/PdfAuthoringWriter.cs`: catalog, one pages
  node, page objects with `/MediaBox`/`/Resources`/`/Contents`, information dictionary, trailer, and
  the content-derived `/ID`.
- [x] T012 Add `src/SharpCorePDF.Infrastructure/Authoring/PageContentWriter.cs`: `BT/Tf/Tm/Tj/ET`,
  `rg`, `q/cm/Do/Q`, hex strings for composite fonts and escaped literals for standard fonts, and the
  top-left to PDF conversion.
- [x] T013 Add `src/SharpCorePDF.Infrastructure/Authoring/AuthoringResources.cs`,
  `PageResources.cs` and `FontResource.cs`: one object per image and face, per-page resource names,
  standard `/Type1` dictionaries, and the `/Type0` + `/CIDFontType2` + `/FontFile2` + `/ToUnicode`
  stack for embedded faces.
- [x] T014 Write `Writes_an_a4_page_with_its_text` and `Writes_the_cover_the_issue_describes`.
- [x] T015 Write `Merges_the_authored_page_in_front_of_a_document`: the cover source through
  `MergePlan`, three pages, A4 first, text on page 1.
- [x] T016 Write `Writes_the_same_bytes_for_the_same_input`, `Records_the_metadata` and
  `Saves_to_a_stream`.

## Phase 4: User Story 2 — text that comes back out

- [x] T017 Write `Round_trips_the_accented_characters_of_a_standard_font` with
  `é è à ç ô ï ' / |`.
- [x] T018 Write `Embeds_a_truetype_font_and_reads_its_text_back`, asserting `/CIDFontType2`,
  `/FontFile2`, `/ToUnicode`, `/Identity-H` and the exact text.
- [x] T019 Write `Refuses_text_a_standard_font_cannot_encode`, `Refuses_a_missing_glyph_of_an_embedded_font`
  and `Refuses_a_line_break_inside_a_text_operation`.
- [x] T020 [P] Validate the embedded program outside the library: extract `/FontFile2` with `pypdf`,
  parse it with `fontTools` (`checkChecksums=2`), and record 5,332 bytes from 17,572 source bytes.

## Phase 5: User Story 3 — a logo without an imaging dependency

- [x] T021 Write `TestPng.cs`: RGBA, RGB, greyscale, palette with `tRNS`, interlaced and 16-bit PNGs,
  with a CRC-32 and a zlib writer.
- [x] T022 Write `Writes_an_rgba_png_with_a_soft_mask`, `Writes_a_palette_png_with_transparency`,
  `Writes_a_greyscale_png_with_alpha` and `Writes_an_rgb_png_with_a_colour_key`, asserting the mask
  kind, dimensions, colour space and the decoded samples.
- [x] T023 Write `Uses_the_resolution_of_a_png_for_its_natural_size` at 300 dpi.
- [x] T024 Write `Writes_one_image_object_for_a_logo_used_on_two_pages`.
- [x] T025 Write `Refuses_an_interlaced_png` and `Refuses_a_sixteen_bit_png`.

## Phase 6: Polish

- [x] T026 Update `src/SharpCorePDF.Domain/PublicAPI.Unshipped.txt` and
  `src/SharpCorePDF.Infrastructure/PublicAPI.Unshipped.txt` with
  `docs/tools/update-public-api.ps1`; no `PublicAPI.Shipped.txt` entry changes.
- [x] T027 Fix every analyzer warning: ambiguous cref, empty record, nested if and ternary, unused
  parameter, `stackalloc` in a loop, interface naming, return type and constant arrays. Release build:
  0 warnings.
- [x] T028 [P] Add `examples/SharpCorePDF.Examples/AuthoringExamples.cs`, the `author-page` scenario and
  the DejaVu subset beside the example.
- [x] T029 [P] Add `docs/manual/src/12-authoring.md`, rename the cookbook to `13-cookbook.md`, add the
  cover recipe, and update `02-what-it-is-not.md`, `00-preface.md`, `04-mergeplan-basics.md`,
  `docs/README.md`, `README.md` and `CHANGELOG.md`.
- [x] T030 [P] Rebuild `docs/SharpCorePDF-Manual-1.0.0.pdf` from the chapters and record the page count
  and heading count in `docs/README.md`.
- [ ] T031 Second reader accepts the wording of `spec.md` (the same open item every feature spec in this
  repository carries before its release checklist is signed).
