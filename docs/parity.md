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
  is a commitment, not evidence; T058, T079 and T096 close the remaining ones in 1.2.
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
| 2 | Vector drawing: lines, paths, curves, rectangles, ellipses, fill, stroke, clipping, transforms | PDFsharp: GDI+-class `XGraphics`. PdfPig: path operations in the builder. | Must: a path writer over `m l c v y h re f S B W n q Q cm gs d J j M G g RG rg K k CS cs SC sc SCN scn ca CA`. | 1.1 paths, the rest 1.2 | US1 creator | `PdfDocumentBuilderTests.Writes_a_line_as_a_stroked_subpath`, `Writes_an_ellipse_as_four_bezier_curves`, `Writes_a_clipping_path`, `Writes_a_balanced_graphics_state`, `Writes_a_translucent_fill_with_an_extgstate`, `Writes_the_line_cap_join_and_miter_limit`, `Writes_the_dash_pattern_and_its_phase`, `Writes_a_solid_line_as_an_empty_dash_array`, `Writes_fill_and_stroke_colours_in_their_space`, `Writes_a_cmyk_paint_as_four_components`, `Writes_a_translation_as_a_page_space_transform`, `Writes_a_scale_that_keeps_the_top_left_corner_still`, `Writes_a_rotation_that_turns_canvas_coordinates`, `Writes_the_transform_before_the_drawing_it_applies_to` (T022) | `python docs/tools/validate-pdf.py <shapes.pdf>` from the `draw-shapes` example (pikepdf 10.17.0 / qpdf: `qpdf check: no problems`, PDF 1.7, 1 page, 6 objects, 1937-byte content stream, 0 failed) and a PyMuPDF 1.28.5 render probed at the dash, the even-odd hole, the clip edge and the rotated band (`measurements.md` §12) | Partial (2026-10-23) — every operator a canvas call can reach is evidenced, dash/cap/join/miter/`SetTransform` included; the remainder the Release cell names is the colour-space selection set `CS`/`cs`/`SC`/`sc`/`SCN`/`scn`, which the content builder can write and no public canvas call selects |
| 3 | Text measurement, wrapping, alignment, leading, basic layout | PDFsharp: basic text layout. PdfPig: not a layout engine. | Must: typeface metrics, single-line and multi-line drawing, greedy wrapping, leading and horizontal/vertical alignment. | 1.2 | US1 creator | `PdfDocumentBuilderTests.Reports_the_metrics_of_a_standard_font`, `.Measures_a_standard_font_line_with_the_afm_widths`, `.Measures_an_embedded_face_with_its_own_metrics`, `.Wraps_a_paragraph_greedily_to_the_width`, `.Places_a_line_at_the_alignment_it_was_given`, `.Stretches_the_lines_of_a_justified_paragraph`, `.Steps_the_baselines_by_the_line_height`, `.Breaks_a_word_into_pieces_that_fit`, `.Refuses_a_word_that_cannot_fit`, `.Draws_the_layout_from_the_top_left_of_its_box` (twenty-two tests in `tests/SharpCorePDF.Tests/PdfDocumentBuilderTests.TextLayout.cs`) | `uv run --with fontTools python -c "from fontTools.ttLib import TTFont; ..."` reads the embedded face's `unitsPerEm`, `ascent`, `descent` and the 8001-unit advance of `Veuillez`, which is what the layout tests assert; a PyMuPDF read of a probe page shows every span one ascent below the origin it was drawn at, stepping by the 15-point leading and centred on the box; `validate-pdf.py` reports `qpdf check: no problems` | Evidenced (2026-10-23) — standard and embedded metrics, `PdfTypeface.Measure`, greedy wrapping, left/center/right/justify, leading, the four overflow policies and the `DrawTextLayout` round-trip; alignment is horizontal only, because no API in the row asks for a vertical one; documented in manual chapter 16 (`docs/manual/src/16-text-layout.md`) |
| 4 | PNG, JPEG, BMP and GIF images | PDFsharp: PNG, JPEG, BMP, GIF. PdfPig: image extraction, basic creation. | Must: PNG alpha/palette/16-bit policy, JPEG pass-through with EXIF/CMYK transform, BMP and first-frame GIF, explicit refusal by name. | 1.1 PNG, the rest 1.2 | US1 creator | `PdfDocumentBuilderTests.Writes_an_rgba_png_with_a_soft_mask`, `Writes_an_rgb_png_with_a_colour_key`, `Writes_a_palette_png_with_transparency`, `Writes_a_greyscale_png_with_alpha`, `Uses_the_resolution_of_a_png_for_its_natural_size`, `Writes_one_image_object_for_a_logo_used_on_two_pages`, `Refuses_an_interlaced_png`, `Refuses_a_sixteen_bit_png`, `Writes_a_jpeg_without_re_encoding_it`, `Writes_a_cmyk_jpeg_with_the_flip_pdf_needs`, `Writes_a_greyscale_jpeg_as_one_component_image`, `Uses_the_jfif_density_of_a_jpeg_for_its_natural_size`, `Writes_a_bmp_with_its_rows_in_order`, `Turns_a_top_down_bmp_the_same_way_up`, `Reads_a_thirty_two_bit_bmp_as_opaque_colour`, `Expands_the_palette_of_a_bmp_to_rgb`, `Writes_a_gif_and_expands_its_palette`, `Puts_an_interlaced_gif_back_into_row_order`, `Turns_the_transparency_of_a_gif_into_a_soft_mask`, `Draws_only_the_first_frame_of_an_animation`, `Embeds_one_image_object_for_a_file_drawn_twice`, `Declares_the_icc_profile_a_png_states`, `Reads_the_profile_out_of_the_icc_chunk`, `Reads_a_grey_profile_as_one_component`, `Refuses_a_profile_that_does_not_describe_the_samples`, `Refuses_an_icc_chunk_that_is_not_a_profile` (T024, T032) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and PyMuPDF image placement; `python docs/tools/verify-creator.py` runs the `creator-parity` example and checks the profile it wrote against the PNG it drew, byte for byte, along with the tree, the packet, both images and a render (`measurements.md` §13) | Evidenced (2026-10-23) — the PNG matrix, `/SMask`, `pHYs`, JPEG pass-through with the CMYK `/Decode` flip, BMP row order and palette expansion, the GIF first frame with its transparency and interlacing, and the by-name refusal of every variant a reader does not read are covered by the tests above; the ICC policy is decided and implemented: an `iCCP` profile is written as an `/ICCBased` colour space with `/N` and `/Alternate`, the 3144-byte sRGB profile of `creator-parity.pdf` reads back byte for byte under pikepdf 10.17.0, PyMuPDF reports the image as `ICCBased`, and a profile that does not describe the samples is refused by name |
| 5 | Page import, insert, delete, reorder, rotate, crop, resize, split, extract, append | PDFsharp: all operations. PdfPig: merge only. | Must: immutable plan, one-based page references, materialised inherited geometry, no source mutation. | 1.1 operations, external run in 1.2 | US2 editing | `PdfDocumentEditorTests.Reorders_deletes_and_duplicates_pages`, `.Adds_rotations_to_the_rotation_a_page_already_has`, `.Geometry.Crop_is_clamped_to_the_media_box_and_reported`, `.Composition.Imports_a_page_of_another_document`, `.Composition.Appends_every_page_of_another_document`, `.Composition.Splits_the_document_into_parts_of_the_requested_size` | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>`; pypdf reads order and geometry (T058, 1.2) | Partial: every listed operation is implemented and covered by `PdfDocumentEditorTests`, `.Composition` and `.Geometry` (2026-10-22); the external run and the T058 closure are 1.2 |
| 6 | Overlay, underlay, watermark, stamp | PDFsharp: yes. PdfPig: no. | Must: wrap original content in `q`/`Q`, record overlay/underlay in the requested layer with clipping and constant alpha. | 1.2 | US2 editing | `PdfDocumentEditorTests.Draws_an_overlay_page_over_the_page_it_is_applied_to`, `Draws_an_underlay_page_before_the_page_it_is_applied_to`, `Writes_the_opacity_and_blend_mode_of_a_layer_through_a_graphics_state`, `Keeps_the_layer_on_the_page_it_was_applied_to` (T040) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a PyMuPDF visual comparison | Met in 1.2 for the structures: a layer is a form XObject drawn from a stream of the page's own — an overlay appends that stream, an underlay prepends it, the original streams stay untouched between them inside a `q`/`Q` frame, which is also what keeps a transform a page states outside a `q` from reaching the layer, the visible area clips the layer and `ca`/`CA`/`BM` in an `ExtGState` carry the opacity and the blend mode (2026-10-24). The external `validate-pdf.py` run on the `watermark` example's output (pikepdf / qpdf, PDF 1.7, 2 pages, 6 streams decoded, `qpdf check: no problems`) confirms an independent implementation accepts the layer, and the text reader returns the stamp as text of the pages it was placed on; the PyMuPDF visual comparison has not been made |
| 7 | Metadata, XMP, outlines, destinations, page labels, viewer preferences | PDFsharp: yes. PdfPig: reads metadata and bookmarks. | Must: edit information dictionary and XMP, write outlines/destinations/page labels/viewer preferences; removed destinations warn. | 1.1 metadata, the rest 1.2 | US2 editing | `PdfDocumentEditorTests.Overrides_the_metadata_it_sets_and_preserves_the_rest`, `Drops_xmp_when_metadata_overrides_the_information_dictionary`, `Keeps_xmp_when_metadata_is_not_overridden`, `Replaces_the_xmp_packet_it_is_given`, `Writes_the_bookmarks_it_is_given`, `Removes_the_outline_of_the_source_when_it_is_empty`, `Writes_the_page_labels_it_is_given`, `Writes_the_viewer_preferences_it_is_given` (T041); XMP authoring and outlines are `PdfDocumentBuilderTests.Carries_the_xmp_packet_it_was_given`, `.Writes_the_outline_it_was_given` (T034) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>`; pypdf reads metadata and outline | Met in 1.2 for the writes: `SetMetadata`, `SetXmp`, `SetOutline`, `SetPageLabels` and `SetViewerPreferences` each replace what the source carried, a stated outline or label range that names a page the result does not have is refused, and `PdfOutline.Empty` / `PdfPageLabeling.None` remove rather than drop (2026-10-24). A bookmark of a page that a later operation deletes is still redirected to the first surviving page with a warning |
| 8 | AcroForm creation, filling, flattening and appearance generation | PDFsharp: yes. PdfPig: read-only forms. | Must: text, checkbox, radio, choice and push-button fields; unique qualified names; fill, reset, read-only refusal and flatten with generated appearances. | 1.2 | US2 editing | `PdfFormEditorTests.Fills_and_reads_back_form_values`, `Fills_a_field_the_document_already_carries`, `Refuses_a_read_only_field`, `Refuses_an_unsupported_field_type`, `Flattens_a_form_without_a_form_viewer`, `Resets_the_form_to_its_default_values`, `Removes_the_xfa_form_and_says_so` (T044, T055) | `python docs/tools/validate-pdf.py` on the three files the `fill-form` example writes (`form-created.pdf`, `form-filled.pdf`, `form-flattened.pdf`): `qpdf check: no problems` for all three, page 1 carrying five annotations and a 62-byte content stream before the flatten and none with 226 bytes after it; a pypdf read-back of the same three (`person.name = Grace Hopper`, `agree = /Off`, `country = BE`, and no field at all on the flattened file); and a PyMuPDF render of the flattened page that reads `Grace Hopper` as page text (T056) | Met in 1.2 (2026-10-24): the five kinds are written with unique qualified names, a field the document already carries is filled in place — its rectangle, ancestors and widgets survive — and a name it does not carry becomes a new field of the tree the name spells; `ResetForm` restores `/DV`, `FlattenForm` draws each widget into the page content and drops the field, `/NeedAppearances` is written `false` because every widget carries the appearance the engine drew, and a read-only field, a signature field and a name that would split an existing one are refused with the name they name; an XFA packet is removed with `XfaFormRemoved` and an action on a field is copied inert with `FormFieldActionPreserved` |
| 9 | Annotation creation, editing, removal and appearance streams | PDFsharp: broad annotation model. PdfPig: read-only annotations. | Must: link, text, free text, stamp, highlight, underline, strikeout, square, circle, line and ink; update/remove; appearance for every creatable kind. | 1.2 | US2 editing | `PdfAnnotationTests.Adds_a_highlight_and_reads_it_back_with_an_appearance`, `Adds_links_to_a_url_and_to_a_page`, `Updates_one_annotation_and_removes_another`, `Keeps_the_annotation_a_page_already_carried`, `Carries_an_annotation_into_a_duplicate_of_its_page`, `Writes_an_appearance_for_every_kind_that_draws_something`, `Draws_the_text_of_a_free_text_annotation_into_its_appearance`, `Drops_a_link_whose_destination_the_edit_removes`, `Clips_an_annotation_to_the_page_and_reports_it`, `Refuses_an_update_of_an_annotation_the_page_does_not_carry`, `Refuses_a_link_with_both_a_page_and_a_url`, `Refuses_a_translucent_annotation_for_pdfa_1b` (T043) | `python docs/tools/validate-pdf.py annotated.pdf` from the `annotate` example (pikepdf 10.17.0 / qpdf: `qpdf check: no problems`, PDF 1.7, 2 pages, 20 objects, page 1 `annots=6`, 6 streams decoded, 0 failed) and a PyMuPDF 2.0 render of the same file, which reports six annotations and paints the highlight quad (255, 223, 129) for `#FFE082`, the note icon (255, 211, 0), the free text in `#1F3A5F` and the stamp frame (191, 43, 31) | Met in 1.2 (2026-10-24): the ten kinds the row lists are written by `PdfAnnotations`, each with an appearance stream the engine draws unless the caller supplies one; `AddAnnotation`, `UpdateAnnotation` and `RemoveAnnotation` rebuild the page's `/Annots` without touching its content, a page the plan does not name keeps its objects, an annotation that states no identifier is named `scpdf-N`, and every refusal the contract states — unknown kind, link with both a page and a URL, missing identifier, empty geometry, opacity outside 0-1, translucency under PDF/A-1b — is made before a byte is written |
| 10 | Incremental updates that preserve existing signatures | PDFsharp: yes. PdfPig: no. | Must: append a revision, keep every prior byte, refuse a changed source, and keep the prior signature's byte range valid. | 1.2 | US2 editing | `PdfIncrementalWriterTests.Appends_a_revision_after_the_bytes_of_the_source`, `Chains_the_revision_to_the_cross_reference_section_of_the_source`, `Chains_a_second_revision_to_the_first`, `Keeps_a_signature_that_covers_the_earlier_revision`, `Refuses_an_incremental_update_of_an_encrypted_document` (T042, T050) | `uv run --with pikepdf python docs/tools/validate-pdf.py <output.pdf>` and a byte-for-byte prefix comparison | Met in 1.2 (2026-10-24): a 1220-byte source edited into a 2465-byte result whose first 1220 bytes are the source unchanged, with one `/Prev`, two `%%EOF` and two `startxref` in the file; `validate-pdf.py` on that result reports `qpdf check: no problems`, PDF 1.7, 2 pages, page 1 `rotate=90` and 4 streams decoded, so an independent reader resolves the appended revision; the signature of a signed source still validates with `CoveredRevision` 2 of the 3 revisions the edit leaves, and the same plan applied as a rewrite reports `PriorSignatureInvalidated` |
| 11 | Digital signatures: sign, validate, timestamp, report integrity and trust separately | PDFsharp: yes. PdfPig: no. | Must: detached CMS over a reserved byte range, X.509, optional caller-supplied RFC 3161 token, integrity and trust as separate results. | 1.1 | US4 trust | `PdfDocumentSignerTests.Writes_a_signature_that_verifies_against_its_own_bytes`, `Signing_a_second_time_keeps_the_first_signature_valid`, `Embeds_the_timestamp_token_the_caller_supplies` (T080, T087), `PdfDocumentSignatureValidatorTests.Reports_a_changed_byte_as_a_digest_mismatch`, `Keeps_integrity_independent_of_trust`, `Reports_a_request_for_an_online_check_without_making_one` (T081) | `python docs/tools/validate-signatures.py --trust-signer <anchor.pem> <signed.pdf>` (pyHanko, T093) and `uv run --with pikepdf python docs/tools/validate-pdf.py <signed.pdf>` | Met (2026-10-23) on pyHanko and qpdf: a signed revision reports `intact` with coverage `ENTIRE_FILE`, a twice-signed one reports `intact`/`ENTIRE_REVISION` then `intact`/`ENTIRE_FILE`, both `trusted` against the signer's own certificate as anchor, and qpdf `--check` reports no problems on either file. A timestamp token is embedded, never checked: the authority is the caller's. The `sign-and-archive` example (T094) writes a PDF/A-3b file and signs it afterwards — the order that keeps the archival bytes intact — and pyHanko reads what it wrote as field `ArchivalSignature`, `integrity intact (coverage ENTIRE_FILE)`, signed at the moment the options state (2026-10-24) |
| 12 | PDF/A-1b, PDF/A-2b and PDF/A-3b output | PDFsharp: PDF/A manager. PdfPig: no. | Must: embed every font, write XMP and an output intent, remove encryption and forbidden actions, refuse what cannot conform. | 1.1 | US4 trust | `PdfATests.Writes_the_structures_the_profile_requires`, `Refuses_a_font_it_cannot_embed`, `Refuses_transparency_for_pdf_a_1b_but_writes_it_for_2b` (T082) | `python docs/tools/validate-pdfa.py --verapdf <verapdf.bat> --profile 2b archive-2b.pdf`, and the same for `1b`/`3b`: veraPDF 1.30.3 reports `PASS` for all three profiles of an authored page and of `SafeWebCore.pdf` (T093, T096) | Met — the output intent is sRGB and built in; a caller-supplied ICC profile, a merge-time profile and PDF/A-3 `/AF` associations are out of scope, and no conformance is claimed without a validator |
| 13 | Positioned letters, words and blocks; configurable segmentation and reading order | PDFsharp: not a reading API. PdfPig: yes. | Must: `PdfLetter` with Unicode, size, font name, box and baseline; word and block strategies; raw and column-aware reading order with the strategy in the result. | 1.1 | US3 reading | `PdfLayoutReaderTests.Reads_one_letter_per_glyph_in_content_order`, `Splits_a_two_column_page_into_one_block_per_column`, `Reads_a_page_of_two_columns_left_to_right_whatever_order_the_content_used` (T059–T060), `PdfLayoutCorpusTests.Meets_SC_004_on_the_labelled_corpus`, `PdfLayoutCorpusTests.Table_page_is_still_read_column_by_column` (T076) | `python docs/tools/score-layout.py <exports> --labels tests/SharpCorePDF.Tests/Assets/parity/labelled --known-order-mismatch table` (T075) | Met on the labelled corpus (T075–T076): recall and boxes 100% on all five documents, reading order 100% on four. `table` is recorded as a known order mismatch — a table is symmetric under the XY cut, so a cell becomes a block and the order comes out column by column, which is the order a two-column page needs; table semantics are out of contract. `PdfLayoutCorpusTests.Table_page_is_still_read_column_by_column` fails if the limitation ever goes away |
| 14 | Images, hyperlinks, form values, annotations, embedded files, metadata, bookmarks | PDFsharp: partial. PdfPig: yes. | Must: expose each resource through the read side, decode image pixels when asked, and warn on an unresolved destination or unsupported filter. | 1.1 partial, pixels in 1.2 | US3 reading | `PdfLayoutReaderTests.Reads_the_links_of_a_page_and_their_uris`, `Resolves_a_direct_and_a_named_destination`, `Keeps_an_unresolved_destination_and_says_so`, `PdfContentReaderTests.Returns_every_annotation_with_its_geometry_and_text`, `Returns_the_field_tree_with_qualified_names`, `PdfContentReaderTests.Exposes_the_samples_of_a_decoded_image_as_pixels`, `Does_not_expose_a_jpeg_as_pixels`, `Does_not_expose_the_pixels_of_a_filter_it_cannot_decode` (T072) | `uv run --with pypdf python -c "from pypdf import PdfReader; ..."` and `validate-pdf.py` on the corpus, plus the `score-layout.py` run of T079 (5 documents, 0 short of a threshold) and an external parse of all four export formats (T079) | Met in 1.2 (2026-10-24): every resource the row names is exposed — images with their payload, geometry, mask and pixels, hyperlinks with resolved and unresolved destinations, form values through `PdfFormFieldInfo`, annotations, embedded files, metadata and bookmarks; pixels are `Rgb24` or `Gray8` for every filter the library decodes and `null` for one it does not, which is reported with `ImageFilterUnsupported` rather than dropped |
| 15 | Public read-only object graph and content-stream inspection | PDFsharp: partial. PdfPig: yes. | Must: immutable `PdfObjectView`/`PdfContentOperationView`, dangling references visible, operation offsets reported. | 1.1 tests, the rest 1.2 | US3 reading | `PdfObjectInspectorTests.Returns_one_node_per_listed_object_in_number_order`, `Projects_a_reference_as_a_node_and_never_follows_it`, `Reports_a_dangling_reference_once_for_the_target_it_names`, `Reads_the_operations_of_a_page_in_the_order_it_states_them` | `uv run --with pikepdf python docs/tools/validate-pdf.py <input.pdf>` plus a mutation-attempt test | Partial: twelve tests in `PdfObjectInspectorTests` are in the suite; the external run and the mutation attempt are T079's closure run (its T062 leg), in 1.2 |
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

**Row 3 since the audit (2026-10-23).** The text-layout work of T028, T029 and T023 landed under the `1.2`
release cell the row already carried, so its Status cell now reads `Evidenced` with the twenty-two tests of
`PdfDocumentBuilderTests.TextLayout.cs`, the fontTools and PyMuPDF readings recorded in
[`measurements.md`](measurements.md) §11, and the `validate-pdf.py` run on the same page. The release cell is
unchanged: the row ships in `1.2`, and the work being evidenced early is what lets T038 close it without
re-opening the design.

**Row 2 since the audit (2026-10-23).** T022 added the nine operator tests the row was missing — dash and
phase, cap, join, miter limit, the three device colour spaces, and `SetTransform` for a translation, a scale
and a rotation — and the `draw-shapes` example produced a page to run them against, so the Status cell now
names fourteen tests and a render rather than five tests and a promise. It stays `Partial` on purpose: the
Release cell calls a remainder `1.2`, and that remainder is the colour-space selection operators
(`CS`/`cs`/`SC`/`sc`/`SCN`/`scn`). They are written by the content builder — the annotation and form writers of
`1.2` select a space before they paint — but no canvas call reaches them today, so there is nothing for a
caller to test. Marking the row `Evidenced` would have claimed an operator set nothing in the public surface
exercises.


What `1.1.0` delivers, row by row: **1** (page, text, fonts), the path writer of **2**, the PNG half of
**4**, every page operation of **5** except its external run, the information dictionary of **7**, **11**
(signatures), **12** (PDF/A-1b/2b/3b), **13** (layout) and the parts of **14–16** the suite already
covers. Deferred to `1.2.0`: **3** entirely — now built and evidenced, see above — and the remainder of **2**,
**4**, **5**, **6**, **7**, **8**, **9**, **10**, **14**, **15** and **16** — 36 tasks when the decision was
taken, recorded with their numbers in `tasks.md`.

## Row closure protocol

A row moves from **Planned** to **Evidenced** only when all of the following exist on the release commit:

1. the named test passes on both `net10.0` and `net11.0`;
2. the external command has been run against an output produced by the example or fixture in the row;
3. the result, the validator version and the performance row are recorded in `docs/measurements.md`;
4. the test that would fail on revert is named in the pull request.

T038 closed the creator rows (2–4) on 2026-10-23 — 4 is `Evidenced`, 2 and 7 keep a stated remainder —
T058 the editing rows, T096 the trust rows
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
* Both statements describe the `1.1.0` tree, and this section stays as it was concluded on. The `1.2` work
  noted above changed them: `PdfTextLayouter.Layout` computes the line breaks, `PdfTypeface.Measure` measures
  before a page exists, and row 3 is now `Evidenced`.

## Phase 2 closure — creator parity (2026-10-23)

* **The five open US1 tasks are done.** T032 decided the ICC policy and implemented it (the only part of the
  Phase 1 matrix that was still open), T034 added `WithXmp`, `WithOutline`, `ImportPage` and `ImportPages`
  to `PdfDocumentBuilder` (`WithMetadata` was already there), T036 added the `creator-parity` scenario,
  T037 wrote manual chapter 17 and pointed chapters 12 and 16 at it, and T038 ran the external validation
  and closed the rows it owns.
* **The decisions, in one place.** An `iCCP` chunk is *declared*, not dropped: the profile becomes its own
  Flate stream and the image names it as `[/ICCBased n 0 R]`, with `/N` from the profile's own colour-space
  signature and `/Alternate` the device space a reader falls back to; a profile that does not describe the
  samples is refused by name. Importing a page reuses the merge engine's `PdfObjectCopier`, so an imported
  page keeps what a merged page keeps, and its inherited attributes are materialised on the copy because an
  authored document has one flat page tree. An XMP packet is written byte for byte and uncompressed, as
  PDF/A-1 requires and as a validator expects. Outline page numbers are checked when the document is
  written, because pages can still be added after the tree is set; a bookmark that dangles is refused, not
  written.
* **Tests.** Eighteen authoring tests are new — five for the profile, three for the packet, four for the outline
  and six for import — and the suite reports **1664 passed, 0 failed** (832 on `net10.0` and 832 on
  `net11.0`), with `dotnet build -c Release` at 0 warnings and 0 errors. The new tests read every result back
  through the content reader, so they assert what a consumer sees and not only the bytes the writer produced.
* **External evidence** (`measurements.md` §13). `validate-pdf.py` (pikepdf 10.17.0 / qpdf) reports
  `qpdf check: no problems` for `creator-parity.pdf`: PDF 1.7, 3 pages, 27 objects, a catalog carrying
  `/Metadata` and `/Outlines`, five bookmarks, page boxes of A4, A5 and the imported 595 × 842, 10 streams
  decoded and 0 failed. PyMuPDF reads the table of contents with its nesting and the URL entry, the 605-byte
  packet and both images — `DeviceRGB`/`DCTDecode` for the JPEG, `ICCBased` for the PNG. pikepdf resolves the
  profile stream and compares it with the Windows sRGB file it was made from: 3144 bytes, identical. pypdf
  extracts `Décomptes SPRL` with its accent from page 1 and the imported page's own text from page 3.
* **Row 4 is Evidenced** and **row 2 stays Partial** for the one thing the Release cell names and no public
  canvas call reaches: the colour-space selection operators `CS`/`cs`/`SC`/`sc`/`SCN`/`scn`, which the content
  builder can write and the canvas does not select. Row 7 stays Partial: authoring a packet and an outline is
  evidenced, *editing* an existing document's is T041.
* **The layout allocation row** is in the measure tool (`--creator`): 60 sentences wrapped at 400 pt cost
  49 KB and 1.13 ms for 60 lines, against 3,448 KB and 14.15 ms for the ten-page creator document with its
  RGBA logo.
* **Not claimed:** no EXIF rotation, no colour conversion, no sixteen bit or Adam7 PNG, no SVG, TIFF, WebP or
  JPEG 2000, and no animated GIF — each refusal has a test and a row in chapter 17. The manual is rebuilt at
  **23 chapters and 103 pages**, with chapters 18 and 19 still reserved for annotations and form fields.

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
