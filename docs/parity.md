# PDF platform parity — capability scorecard

**Feature**: `003-pdf-platform-parity` | **Matrix source**: [`spec.md`](../specs/003-pdf-platform-parity/spec.md) | **Release**: 1.1

This is the public scorecard for creation, editing and reading parity. It records what PDFsharp 6.5 and
PdfPig 0.1.x do, what SharpCorePDF 1.1 promises, who owns the row, the test that fails if the behaviour
is reverted, and the external command that validates it.

**Status vocabulary**

* **Evidenced** — the named test exists, passes, and the external command has been run on the output.
* **Partial** — part of the row is evidenced in 1.0 or by an earlier phase; the rest is still planned.
* **Planned** — the named test and command become runnable in the task that closes the row. A planned row
  is a commitment, not evidence; T038, T058, T079 and T096 update it before the release tag.
* **Out of scope** — a declared non-goal, owned by the product boundary in
  [`ADR-001`](./adr/0001-pdf-platform-boundary.md), not by a workstream.

No “Must” row is unowned. The owner is the story that closes it: `US1 creator`, `US2 editing`,
`US3 reading`, `US4 trust`. The external commands use the corpus and the harness described in
[`docs/parity/README.md`](./parity/README.md) and `docs/measurements.md`.

| # | Capability | PDFsharp / PdfPig reference | SharpCorePDF 1.1 outcome | Release | Owner | Named test | External validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Page creation, fixed-position text, standard and embedded fonts | PDFsharp: pages, fixed-position text and standard/embedded fonts. PdfPig: basic creation only. | Must: create A4 and custom pages, draw positioned text with standard and caller-supplied TrueType fonts. | 1.1 | US1 creator | `PdfDocumentBuilderTests.Writes_an_a4_page_with_its_text`, `Embeds_a_truetype_font_and_reads_its_text_back` | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` | Evidenced (1.0 baseline) |
| 2 | Vector drawing: lines, paths, curves, rectangles, ellipses, fill, stroke, clipping, transforms | PDFsharp: GDI+-class `XGraphics`. PdfPig: path operations in the builder. | Must: a path writer over `m l c v y h re f S B W n q Q cm gs d J j M G g RG rg K k CS cs SC sc SCN scn ca CA`. | 1.1 | US1 creator | `PdfGraphicsTests.Draws_paths_with_fill_stroke_clip_and_alpha` (T023) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a PyMuPDF render of the same box | Planned |
| 3 | Text measurement, wrapping, alignment, leading, basic layout | PDFsharp: basic text layout. PdfPig: not a layout engine. | Must: typeface metrics, single-line and multi-line drawing, greedy wrapping, leading and horizontal/vertical alignment. | 1.1 | US1 creator | `PdfTextLayoutTests.Wraps_a_paragraph_to_a_measured_width` (T024) | `uv run --with fontTools python -c "from fontTools.ttLib import TTFont; ..."` for metrics, plus `validate-pdf.py` | Planned |
| 4 | PNG, JPEG, BMP and GIF images | PDFsharp: PNG, JPEG, BMP, GIF. PdfPig: image extraction, basic creation. | Must: PNG alpha/palette/16-bit policy, JPEG pass-through with EXIF/CMYK transform, BMP and first-frame GIF, explicit refusal by name. | 1.1 | US1 creator | `PdfImageTests.Embeds_a_jpeg_without_reencoding`, `Places_a_bmp_and_a_gif`, `Preserves_png_alpha_and_natural_size` (T025–T027) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and PyMuPDF image placement | Partial (PNG evidenced in 1.0; JPEG/BMP/GIF planned) |
| 5 | Page import, insert, delete, reorder, rotate, crop, resize, split, extract, append | PDFsharp: all operations. PdfPig: merge only. | Must: immutable plan, one-based page references, materialised inherited geometry, no source mutation. | 1.1 | US2 editing | `PdfDocumentEditorTests.Rotates_crops_and_resizes_a_page_tree`, `Deletes_a_page_without_dangling_references` (T039) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>`; pypdf reads order and geometry | Planned |
| 6 | Overlay, underlay, watermark, stamp | PDFsharp: yes. PdfPig: no. | Must: wrap original content in `q`/`Q`, record overlay/underlay in the requested layer with clipping and constant alpha. | 1.1 | US2 editing | `PdfDocumentEditorTests.Overlays_a_watermark_without_touching_the_source` (T040) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a PyMuPDF visual comparison | Planned |
| 7 | Metadata, XMP, outlines, destinations, page labels, viewer preferences | PDFsharp: yes. PdfPig: reads metadata and bookmarks. | Must: edit information dictionary and XMP, write outlines/destinations/page labels/viewer preferences; removed destinations warn. | 1.1 | US2 editing | `PdfDocumentEditorTests.Writes_xmp_outlines_page_labels_and_viewer_preferences` (T041) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>`; pypdf reads metadata and outline | Partial (information dictionary evidenced in 1.0) |
| 8 | AcroForm creation, filling, flattening and appearance generation | PDFsharp: yes. PdfPig: read-only forms. | Must: text, checkbox, radio, choice and push-button fields; unique qualified names; fill, reset, read-only refusal and flatten with generated appearances. | 1.1 | US2 editing | `PdfFormTests.Fills_and_reads_back_form_values`, `Flattens_a_form_without_a_form_viewer`, `Refuses_an_unsupported_field_type` (T044) | `uv run --with pypdf python -c "from pypdf import PdfReader; ..."` and `validate-pdf.py` | Partial (reading evidenced in 1.0; writing planned) |

| 9 | Annotation creation, editing, removal and appearance streams | PDFsharp: broad annotation model. PdfPig: read-only annotations. | Must: link, text, free text, stamp, highlight, underline, strikeout, square, circle, line and ink; update/remove; appearance for every creatable kind. | 1.1 | US2 editing | `PdfAnnotationTests.Adds_moves_and_removes_an_annotation`, `Writes_an_annotation_appearance` (T043) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a PyMuPDF appearance render | Partial (reading evidenced in 1.0; writing planned) |
| 10 | Incremental updates that preserve existing signatures | PDFsharp: yes. PdfPig: no. | Must: append a revision, keep every prior byte, refuse a changed source, and keep the prior signature's byte range valid. | 1.1 | US2 editing | `PdfIncrementalWriterTests.Writes_an_incremental_update`, `Preserves_the_prior_revision_bytes` (T042) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a byte-for-byte prefix comparison | Planned |
| 11 | Digital signatures: sign, validate, timestamp, report integrity and trust separately | PDFsharp: yes. PdfPig: no. | Must: detached CMS over a reserved byte range, X.509, optional caller-supplied RFC 3161 token, integrity and trust as separate results. | 1.1 | US4 trust | `PdfSignatureTests.Signs_a_document_with_a_detached_cms_signature`, `PdfSignatureValidationTests.Detects_a_modified_byte_range`, `Preserves_an_earlier_signature_after_an_incremental_update` (T080–T081) | `python docs/tools/validate-signatures.py <signed.pdf>` (pyHanko, T093) plus OpenSSL `cms -verify` | Planned |
| 12 | PDF/A-1b, PDF/A-2b and PDF/A-3b output | PDFsharp: PDF/A manager. PdfPig: no. | Must: embed every font, write XMP and an output intent, remove encryption and forbidden actions, refuse what cannot conform. | 1.1 | US4 trust | `PdfATests.Writes_pdf_a_1b_2b_and_3b`, `Refuses_a_profile_it_cannot_satisfy` (T082) | `python docs/tools/validate-pdfa.py <file.pdf> --profile 1b` (also `2b` and `3b`; veraPDF, T093) | Planned |
| 13 | Positioned letters, words and blocks; configurable segmentation and reading order | PDFsharp: not a reading API. PdfPig: yes. | Must: `PdfLetter` with Unicode, size, font name, box and baseline; word and block strategies; raw and column-aware reading order with the strategy in the result. | 1.1 | US3 reading | `PdfLayoutReaderTests.Reads_letters_with_positions_and_direction`, `Groups_a_two_column_page_in_reading_order`, `PdfLayoutCorpusTests.Meets_SC_004_on_the_labelled_corpus` (T059–T060, T076) | `python docs/tools/score-layout.py <output> --corpus docs/parity/corpus` (T075) | Planned |
| 14 | Images, hyperlinks, form values, annotations, embedded files, metadata, bookmarks | PDFsharp: partial. PdfPig: yes. | Must: expose each resource through the read side, decode image pixels when asked, and warn on an unresolved destination or unsupported filter. | 1.1 | US3 reading | `PdfLayoutReaderTests.Reads_hyperlinks_and_image_pixels` (T061), `PdfContentReaderTests.Returns_every_annotation_with_its_geometry_and_text`, `Returns_the_field_tree_with_qualified_names` | `uv run --with pypdf python -c "from pypdf import PdfReader; ..."` and `validate-pdf.py` | Partial (annotations, fields, images and metadata evidenced in 1.0; hyperlinks and pixel decode planned) |
| 15 | Public read-only object graph and content-stream inspection | PDFsharp: partial. PdfPig: yes. | Must: immutable `PdfObjectView`/`PdfContentOperationView`, dangling references visible, operation offsets reported. | 1.1 | US3 reading | `PdfObjectInspectorTests.Projects_the_object_graph_read_only` (T062) | `uv run --with pikepdf python docs/tools/validate-pdf.py <input.pdf>` plus a mutation-attempt test | Planned |
| 16 | Export to ALTO, PAGE XML, hOCR and SVG text | PDFsharp: no. PdfPig: yes. | Must: four exporters in page space, escaped text, a valid empty-page element, and a result that names the strategy used. | 1.1 | US3 reading | `PdfLayoutExportTests.Exports_alto_pagexml_hocr_and_svg` (T063) | schema validation for ALTO/PAGE XML, `xmllint` where available, and a well-formedness check for hOCR/SVG | Planned |
| 17 | Rendering pages to images or raster previews | PDFsharp: no. PdfPig: no. | Non-goal: no rasteriser, thumbnail generator, viewer or print pipeline. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `README.md`, `docs/manual/src/02-what-it-is-not.md` and ADR-001 | Out of scope |
| 18 | OCR, HTML-to-PDF, PDF-to-Word/HTML, reflow, tagged-PDF/PDF-UA authoring | PDFsharp: no. PdfPig: no. | Non-goal: no OCR, document understanding, table semantics, Office conversion, reflow or accessibility conformance claim. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `README.md` and ADR-001 | Out of scope |
| 19 | MigraDoc-style flow engine with automatic page breaks | PDFsharp: separate MigraDoc product. PdfPig: no. | Non-goal: no flow layout engine; the caller places content on pages. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `docs/manual/src/02-what-it-is-not.md` and ADR-001 | Out of scope |
| 20 | JavaScript execution, 3D, multimedia, geospatial, public-key encryption | PDFsharp: no or partial. PdfPig: no. | Non-goal: actions may be copied or refused with a warning; nothing is executed, and no new encryption surface is added. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | Out-of-scope statement in `README.md` and ADR-001 | Out of scope |
| 21 | Drop-in source or binary compatibility with PDFsharp or PdfPig | N/A | Non-goal: capability parity only; no reference package is referenced by the suite. | 1.1 (excluded) | Product boundary (ADR-001) | None by design | ADR-001 and the dependency check in quickstart step 8 | Out of scope |

## Row closure protocol

A row moves from **Planned** to **Evidenced** only when all of the following exist on the release commit:

1. the named test passes on both `net10.0` and `net11.0`;
2. the external command has been run against an output produced by the example or fixture in the row;
3. the result, the validator version and the performance row are recorded in `docs/measurements.md`;
4. the test that would fail on revert is named in the pull request.

T038 closes the creator rows (1–4), T058 the editing rows (5–10), T096 the trust rows (11–12) and T079
the reading rows (13–16). T101 re-reads every recorded budget; a regression above 10% blocks the release
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


