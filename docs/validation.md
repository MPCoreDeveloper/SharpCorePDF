# Independent validation

What implementations that share no code with this library make of the documents it writes, and what the
engine does with documents it did not write. A writer that only reads its own output back cannot see the
mistakes it shares with itself; every check on this page asks someone else.

Two workflows run them. [`validation.yml`](../.github/workflows/validation.yml) runs on every push and pull
request and checks what the library writes. [`robustness.yml`](../.github/workflows/robustness.yml) runs
weekly and checks what it reads: fuzzing, public corpora, and the PDF/A conversion judged by veraPDF.
[`compliance.yml`](../.github/workflows/compliance.yml) remains the PDF/A gate.

## What is checked

| # | Check | Readers | Tool | Workflow |
| --- | --- | --- | --- | --- |
| 1 | Coverage-guided fuzzing of the document parser and of the DCT, CCITT, LZW, Flate and RunLength decoders | libFuzzer through SharpFuzz | `tools/SharpCorePDF.Validation` (`FuzzTargets.cs`) | `robustness.yml` weekly; the seeds of every target replay on every push |
| 2 | The file parses, every page reads, every stream decodes | qpdf (pikepdf) | `validate-pdf.py` | `validation.yml` |
| 3 | Signatures resist forged byte ranges, an emptied value and a wrapped gap; another validator agrees on integrity, trust and coverage | the library's own validator; pyHanko | `PdfSignatureAttackTests`; `validate-signatures.py --manifest` | test suite; `validation.yml` |
| 4 | Every dictionary holds only keys its type defines, with the right types, for the header's version | veraPDF's Arlington service | `validate-arlington.py` | `validation.yml`, report-only until its first run sets the baseline |
| 5 | Two engines paint the same pages | PDFium (Chrome) and MuPDF | `render-compare.py` | `validation.yml` |
| 6 | Every embedded font program reads cleanly and its widths match the PDF | fontTools and OTS | `validate-fonts.py` | `validation.yml` |
| 7 | When the PDF/A writer says it wrote a profile, the result conforms | veraPDF over the veraPDF test corpus | `validate-preflight-corpus.py` | `robustness.yml`, report-only until its first run sets the baseline |
| 8 | The text that was drawn is the text that is extracted | pypdf, pdfminer.six and MuPDF | `validate-text.py` | `validation.yml` |
| 9 | RC4-40, RC4-128, AES-128 and AES-256 open with the right password, refuse the wrong one, and state the revision and permissions that were asked for | qpdf, MuPDF, pypdf and pdf.js | `validate-encryption.py`, `validate-encryption-pdfjs.mjs` | `validation.yml` |
| 10 | Every revision of an incremental update stands alone, `/ID[0]` survives, and an update only appends what changed | qpdf; the library's own reader | `validate-incremental.py`; `PdfIncrementalInvariantTests` | `validation.yml`; test suite |
| 11 | The PDF/A-4 documents are read by a second and a third implementation too | PDFBox (parse) and Ghostscript (rewrite) | `validate-pdfa-cross.py --profile 4` | `compliance.yml` |
| 12 | Documents nobody here wrote are read, or refused with a `PdfException`, never broken on | the probe, one process per file, under a time and a managed-heap limit | `corpus-robustness.py` | `robustness.yml` (veraPDF and pdf.js corpora); `validation.yml` (four synthesized hostile documents) |

Every external input is pinned: the Python validators in
[`tools/validation-requirements.txt`](tools/validation-requirements.txt), pdfjs-dist and the libFuzzer driver
(by tag and SHA-256) in the workflows, SharpFuzz in `.config/dotnet-tools.json`, the veraPDF corpus and the
pdf.js corpus by commit, veraPDF by version.

## The validation corpus

`dotnet run --project tools/SharpCorePDF.Validation -- corpus <dir>` writes the documents the checks read,
and a `manifest.json` that says what each one has to say. A validator that only reports "no problems" proves
a file is well formed, not that it carries what was written into it; the manifest is the other half.

| Documents | What the manifest states |
| --- | --- |
| `text-standard.pdf`, `text-embedded.pdf`, `text-merged.pdf` | the lines of every page: WinAnsi punctuation and accents through the standard fonts, a TrueType subset, and both after a merge |
| `encrypted-<algorithm>-<permissions>.pdf` (eight) and `encrypted-aes-256-owner-only.pdf` | the algorithm, `V`, `R` and key length, both passwords, the permissions, and the text |
| `incremental.pdf` over `incremental-source.pdf` | three revisions, the rotations each one gives the pages, and the source's identifier |
| `signed.pdf`, `signed-twice.pdf`, `signed-then-edited.pdf`, `signer.pem` | each signature field and the coverage pyHanko must report for it |

Passwords and the signing key are made for the run and exist only in the corpus directory, which is an
artifact of the run and never committed.

## Running the checks locally

```powershell
uv venv; uv pip install -r docs/tools/validation-requirements.txt
dotnet run --project .\examples\SharpCorePDF.Examples -c Release
dotnet run --project .\tools\SharpCorePDF.Validation -c Release -- corpus $env:TEMP\corpus

python docs/tools/validate-text.py $env:TEMP\corpus
python docs/tools/validate-encryption.py $env:TEMP\corpus
python docs/tools/validate-incremental.py $env:TEMP\corpus
python docs/tools/validate-signatures.py --manifest $env:TEMP\corpus\manifest.json
python docs/tools/validate-fonts.py $env:TEMP\SharpCorePDF.Examples\output
python docs/tools/render-compare.py --exclude protected.pdf $env:TEMP\SharpCorePDF.Examples\output

npm install --prefix $env:TEMP\pdfjs pdfjs-dist@6.4.299
node docs/tools/validate-encryption-pdfjs.mjs $env:TEMP\corpus $env:TEMP\pdfjs\node_modules\pdfjs-dist\legacy\build\pdf.mjs

# One document, every read use case, then a merge and an edit read back: 0 read, 2 refused, 3 engine fault
dotnet run --project .\tools\SharpCorePDF.Validation -c Release -- probe <file.pdf>
python docs/tools/corpus-robustness.py --synthesize $env:TEMP\hostile
python docs/tools/corpus-robustness.py --probe .\tools\SharpCorePDF.Validation\bin\Release\net11.0\SharpCorePDF.Validation.dll <dir>
```

Fuzzing needs Linux: `robustness.yml` shows the whole sequence (publish twice, instrument the engine with
`dotnet sharpfuzz`, run `libfuzzer-dotnet` with `SCPDF_FUZZ_TARGET` naming the target). An input libFuzzer
saves reproduces on any platform with `-- replay <target> <file>` on a build that was not instrumented.

## Known defects

Each check that finds a defect which is recorded and not yet fixed names it with `--known-defect` (or
`--known`, or a skipped test whose reason names it). The flags are strict: when the defect is fixed the check
passes, the flag no longer matches, and the run fails until the flag is removed in the same change as the
fix. This is the list on 2026-11-16, every entry reproduced, not inferred:

| Defect | Found by | Evidence |
| --- | --- | --- |

Fixed since the list was first drawn up, each with the test that fails without its fix:

| Defect | Fixed by | Test |
| --- | --- | --- |
| The signature validator accepted a `/ByteRange` gap holding more than the `/Contents` string (signature wrapping) | the gap must be exactly one hex string, equal to the dictionary's `/Contents` | `PdfSignatureAttackTests.Does_not_report_a_gap_that_holds_more_than_the_signature_value_as_covering_the_document` |
| No limit on the decoded size of a Flate, LZW or RunLength stream | `PdfEngineOptions.MaxDecodedStreamBytes` (128 MiB) | `PdfFilterTests.Refuses_a_flate_stream_that_decodes_past_the_limit` and its LZW, RunLength and content-stream companions |
| A chain of indirect `/Length` references overflowed the stack | the chain is followed no deeper than `MaxNestingDepth`, then the stream is found by its `endstream` | `PdfReaderRecoveryTests.Reads_a_stream_whose_length_is_a_chain_of_sixty_thousand_streams` |
| An object stream's `/N`, and a JPEG frame's width and height, sized allocations unchecked | `/N` is bounded by what the stream can hold; a JPEG above the caller's budget is measured from its header, and working sizes are computed in 64 bits | `PdfReaderRecoveryTests.Reads_an_object_stream_whose_count_its_header_cannot_hold`, `PdfJpegFilterTests.Measures_a_frame_too_large_for_the_budget_without_allocating_it` |
| The JPEG decoder used a scan's 4-bit Huffman table selectors, and a progressive scan's spectral end, as indexes unchecked | `ParseScan` refuses a selector above 3, and a progressive scan whose selection is not `Ss ≤ Se ≤ 63` or whose approximation is above 13 | `PdfJpegFilterTests.Refuses_a_scan_that_selects_a_table_beyond_the_four_there_are`, `Refuses_a_progressive_scan_outside_the_block` |
| The empty name as a dictionary key (`/` alone, which PDF allows) stopped the document opening | `PdfDictionary.Set` accepts it; the entry is kept and written back as `/` | `PdfObjectModelTests.Keeps_an_entry_whose_key_is_the_empty_name`, `Opens_a_document_that_uses_the_empty_name_as_a_key` |
| A glyph drawn upside down (negative font size) faulted text extraction | `PdfGlyph.BoundingBox` takes the box between descender and ascender whichever way round | `PdfTextScannerTests.Gives_a_glyph_drawn_upside_down_a_box` |
| `PdfDate.TryParse` threw for a time-zone offset beyond 14 hours | an offset no time zone has is read as no offset | `PdfSerializationTests.Reads_an_offset_no_time_zone_has_as_no_offset` |
| An incremental update wrote a new identifier into both elements of `/ID` | the first element of the source's `/ID` is kept; only the second is new | `PdfIncrementalInvariantTests.Keeps_the_first_identifier_element_across_an_incremental_update` |
| `UseIncrementalUpdate` appended a renumbered copy of the whole document, rewriting the signature dictionary after signing | the edited document's objects keep their numbers, and an object written exactly as the source states it is left out: rotating one page of a signed document appends 716 bytes and three objects instead of 20,969 bytes and twelve, and pyHanko places the signature at `ENTIRE_REVISION` | `PdfIncrementalInvariantTests.Appends_only_the_objects_an_edit_changes`, `Leaves_the_signature_dictionary_out_of_an_appended_revision` |
| A merge copied a page that states no `/MediaBox` (and inherits none) without one | the page is written with the A4 box the inspection describes it with, and `PageSizeAssumed` says so | `PdfMergeFidelityTests.Writes_a_media_box_for_a_page_that_has_none` |
| Reals a merge or an edit copied were re-formatted to five decimals (`0.000012` became `0.00001`), and a real too large for the formatter's buffer failed the write of the whole document — the first crash the `document` fuzz target found | a real read from a document is written from its own text when that text is a well-formed PDF real; other values are formatted at any magnitude | `PdfMergeFidelityTests.Writes_the_reals_it_copies_as_the_source_states_them`, `Writes_a_real_of_any_magnitude_in_the_form_pdf_defines` |
| The copy route of an image let the decompressor's `InvalidDataException`, and for other inputs its `ZLibException`, escape for damaged Flate data — the two crashes the `flate` fuzz target found | the copy route refuses damaged data with `PdfSourceFormatException`, with or without a predictor | `PdfContentReaderTests.Refuses_to_copy_an_image_whose_flate_data_is_damaged` |
| The edit engine threw `ArgumentOutOfRangeException` for a `/Rotate` that is not a multiple of 90, and read a real or indirect one as none — the second crash the `document` fuzz target found | the edit reads the rotation as the inspection does, to the nearest quarter turn | `PdfMergeFidelityTests.Edits_a_page_whose_rotation_is_not_a_quarter_turn` |
| A free text annotation was written without the `/DA` ISO 32000-1 requires, and annotation appearances without `/Resources` — both reported by the Arlington check's first run | `/DA` states the font, size and colour the appearance draws with; every appearance states its resources, empty if it needs none | `PdfDocumentEditorTests.States_the_default_appearance_and_resources_of_a_free_text_annotation`, `States_resources_on_an_appearance_that_needs_none` |
| A table that parsed but whose offsets had moved was used as it was, and reconstruction could not see into object streams | the table is rebuilt when its offsets miss their objects; reconstruction indexes object streams and takes `/Root` from a cross-reference stream's dictionary | `PdfReaderRecoveryTests.Rebuilds_a_table_whose_offsets_no_longer_point_at_their_objects`, `Rebuilds_a_table_from_object_streams_when_the_cross_reference_stream_is_lost`, `Rebuilds_a_document_whose_catalog_lives_in_an_object_stream` |
| AESV2 with a short declared key, wrapped predictor row sizes, truncated `endstream` candidates, ignored generations | sixteen-byte AESV2 keys; 64-bit row sizes on both predictor paths; Adler-32-checked candidates, at most sixteen; a mismatched generation resolves to null | `PdfMergeEngineTests.Opens_an_aesv2_document_whose_crypt_filter_declares_a_short_key`, `PdfPredictorTests.Refuses_a_predicted_row_larger_than_the_engine_decodes`, `PdfStreamParsingTests.Scans_past_a_candidate_whose_compressed_data_is_cut_short`, `PdfReaderRecoveryTests.Resolves_a_reference_to_a_generation_the_table_does_not_list_as_null` |
| A revision appended to a file that uses cross-reference streams ended in a classic table | the revision ends in a cross-reference stream; qpdf and pyHanko accept both an edit and two signatures appended this way | `PdfIncrementalWriterTests.Appends_a_cross_reference_stream_to_a_document_that_uses_them`, `PdfDocumentSignerTests.Signs_a_document_that_uses_cross_reference_streams_with_one` |
| A merge copied the outline recursively, so a source nested about fifty thousand levels deep overflowed the stack; a looping or shared outline was walked again at every visit — a crash the `document` fuzz target found | the walk stops at 256 levels with `BookmarksTruncated`; a bookmark reached twice is copied once with `BookmarkSkipped` | `PdfMergeOutlineLimitsTests` |
| Rebuilding a table sliced the document at offsets the damaged table stated past its end, or beyond an index's range, and threw `ArgumentOutOfRangeException` — a crash the `document` fuzz target found | an offset outside the document is passed over, and the rebuild finds the catalog | `PdfReaderRecoveryTests.Rebuilds_a_table_that_states_an_offset_past_the_end_of_the_file` |
| Copying an attachment the listing reported, whose specification embeds no stream, threw `ArgumentException` — the public-corpora job's fault on `isartor-6-1-11-t01-fail-a.pdf` | it writes no bytes, matching the `ByteLength` of zero the listing reports | `PdfContentReaderTests.Copies_nothing_for_a_listed_file_that_embeds_no_stream` |
| Looking for signatures walked every path of the form's field tree, so a form whose levels each list the next twice (forty levels, 2.6 KB) never finished — a timeout the `document` fuzz target found | each field is visited once | `PdfDocumentSignatureValidatorTests.Finishes_looking_for_signatures_in_a_form_whose_levels_each_list_the_next_twice` |
| A media box whose digits overflow a double read as infinite, and the inspection, the page reader and the edit engine passed it to a constructor that refuses it — a 378-byte crash the `document` fuzz target found on the first Robustness run of this branch | a number that is not finite is unreadable, so the page is described as A4 with a remark, as one with no box is | `PdfMergeFidelityTests.Assumes_a_size_for_a_page_whose_media_box_overflows`, `PdfDocumentEditorTests.Edits_a_page_whose_media_box_overflows` |
| A hybrid-reference file read as having no pages: its classic table lists the objects it keeps in object streams as free, and the free entry hid the real one in `/XRefStm` | the stream may answer for the numbers its own section's table frees, and is read before the revision `/Prev` points at | `PdfCrossReferencePrecedenceTests.Reads_an_object_a_hybrid_file_lists_as_free_in_its_table_and_describes_in_its_stream`, `.Reads_the_stream_of_a_hybrid_section_before_the_revision_it_follows` |
| An object stream answered for numbers the table places elsewhere: an object a later revision rewrote came back stale, and one it freed came back from the dead, depending on which object was resolved first | only the numbers the table places in a stream are taken from it, and the table is consulted before any cache | `.Resolves_the_object_the_table_names_not_the_copy_an_object_stream_still_holds`, `.Does_not_bring_back_an_object_the_table_lists_as_free` |
| A page that paints forms without end — six levels of forms, each painting the next 40 times, from a 3.6 KB document — never finished reading | `PdfEngineOptions.MaxFormPaintsPerPage` (100,000) and a per-page budget of 256 MiB of decoded form content, with a per-page cache | `PdfFormBudgetTests` |
| A check box or radio button's appearance stream had no `/Resources`, which the Arlington model of PDF 1.7 and later requires | the entry is stated, empty when the button draws no text | `PdfFormEditorTests.States_resources_on_the_appearance_of_a_button_that_draws_no_text` |
| The PDF/A conversion wrote documents that broke rules its analysis never looked at: annotation flags and types, the actions of widgets, fields, bookmarks and `/Next` chains, XFA, device colour under a mismatched output intent, undefined operators, graphics-state keys | see [the section below](#the-pdfa-conversion-against-the-verapdf-corpus) and chapter 24 | `PdfAArchivalChecksTests` (32 tests, 21 of which fail on the previous analysis) |

## Known limits

What the checks show and nothing here fixes, stated so that no claim goes further than the evidence:

| Limit | Where it shows | What a caller does |
| --- | --- | --- |
| A document that declares PDF 2.0 and draws with one of the 14 standard fonts omits `/FirstChar`, `/LastChar`, `/Widths` and `/FontDescriptor`, which PDF 2.0 withdrew the exemption from | the Arlington check, `FontType1-*-11` on `report.pdf`; accepted in `validation.yml` with the reason beside it | embed a font, or write the document as PDF 1.7 |
| The PDF/A conversion does not read every rule of ISO 19005: `Separation` and `DeviceN` colorant consistency (`6.2.4.4`), the font rules (`6.2.10` and `6.2.11`: `CIDToGIDMap`, `CIDSystemInfo`, embedded CMaps, widths, encodings, `.notdef`), rendering intent values (`6.2.6`), a halftone's transfer function (`6.2.5-6`), undefined operators inside Type 3 glyph procedures, metadata extension schemas (`6.6.2.3`), odd-length hexadecimal strings and integers beyond 32 bits (`6.1.5`, `6.1.13`), and the `Alternates`, `OPI` and reference-XObject keys (`6.2.7` to `6.2.9`) | the preflight measurement below, which lists them by clause | judge the result with veraPDF; the library says so in `PdfAProfileWritten` |
| A source whose own output intent is not a usable ICC profile, or whose content paints in a space no intent covers, is refused rather than converted | `PdfAConversionResult.UnsatisfiedRequirements` | supply an `OutputIntent` the content was prepared for |

## Corpus readings, 2026-11-16

| Corpus | Files | Read | Refused | Fault | Crash | Timeout |
| --- | --- | --- | --- | --- | --- | --- |
| veraPDF corpus, `PDF_A-2b` and `PDF_A-4` at `bb75f4f` | 1,471 | 1,471 | 0 | 0 | 0 | 0 |
| pdf.js `test/pdfs` at `v6.4.299` | 987 | 977 | 4 | 6 | 0 | 0 |
| synthesized hostile documents, before the fixes | 4 | 0 | 0 | 3 | 1 | 0 |

The six pdf.js faults were the empty-name, upside-down glyph and date-offset defects in the fixed table;
after those fixes all six are read, as is `jpeg-selector.pdf`.

## The PDF/A conversion against the veraPDF corpus

`validate-preflight-corpus.py` converts every file of the veraPDF corpus for its profile and asks veraPDF 1.30.4
whether the result conforms. Each file breaks one clause (`-fail-`) or passes one (`-pass-`), so the answer
separates three things: a conversion that writes a file veraPDF rejects (a *false success*), one that
refuses a source that already conformed (an *over-refusal*), and one that damages a conforming source.

**Before** (`robustness.yml` on `f529b02`, 2,074 files, five profiles): 395 false successes, of which 378 were
sources meant to fail and 17 were sources that conformed — the converter damaged them, chiefly by replacing a CMYK
output intent with sRGB. By profile: 1b 48, 2b 169, 2u 9, 4 169, 3b 0.

| Clause group | False successes | What veraPDF found |
| --- | --- | --- |
| Colour: `6.1.7`, `6.2.4.x`, `6.2.5`, `6.2.10` (transparency, part 2) | 88 | chiefly `6.2.4.3-3`: DeviceCMYK used with no CMYK output intent |
| Annotations and forms: `6.3.x`, `6.4.x` | 79 | `/F` missing (`6.3.2-1`), types the profile does not list, actions on widgets and fields, XFA |
| Actions: `6.5.x`, `6.6.1`, `6.6.3` | 28 | forbidden actions along `/Next` chains and on bookmarks; named actions |
| Fonts: `6.2.10.x` (part 4), `6.2.11.x` (part 2) | 88 | not looked at |
| The rest: output intent profile `6.2.3` (18), integers beyond 32 bits `6.1.13` (15), operators `6.2.2` (15), `6.1.12` (13), `Alternates`/`OPI`/reference XObjects `6.2.7` to `6.2.9` (26), strings, metadata | 112 | not looked at, except `6.2.2` |

**After**, on the same corpus and the same veraPDF (`robustness.yml` run on `a23fdc3`, 2,074 files): 229 false
successes, of which 227 were sources meant to fail and 2 sources that conformed (`6-6-2-3-2-t01-pass-b` and
`-d`, whose metadata extension schemas the conversion rewrites). By profile: 1b 33, 2b 95, 2u 9, 4 92, 3b 0. The
remaining 229 are mostly rules the analysis does not read: fonts `6.2.11` (43) and `6.2.10` (38), colour `6.2.4`
(34), integers `6.1.13` (15), `6.1.12` (13), `6.2.8` (13).

The ten clause groups that were changed, converted and judged locally file by file (150 fail-inputs and 51
pass-inputs of `PDF_A-2b`):

| | Refused | Conforms | Still fails |
| --- | --- | --- | --- |
| Inputs meant to fail (150) | 48 | 76 | 26 |
| Inputs meant to pass (51) | 3 | 39 | **0** |

No conforming source is damaged any more, which is the defect that mattered most: a caller who archives a
document that already conformed is told so by the result, not given a file that no longer does. The 26 inputs that
still fail break `6.2.4.4-1/-2` (colorant consistency, 7), the remaining `6.2.4.3` shapes (14), and a few single rules
the analysis does not read (`6.2.2-2`, `6.2.5-6`, `6.2.6-1`, `6.5.1-1`); they are the known limits above, by
clause. The three conforming sources refused are over-refusals: their colour use is allowed by an intent or a
`Default` space the analysis does not read.

`robustness.yml` repeats the full measurement weekly. Its `--max-false-success` is the ratchet, set to 229, the
figure above: the run fails when the count of false successes rises above it. Lower it in the change that lowers
the count.

## How the checks were calibrated

A check that never fails proves nothing, so each was shown to fail on purpose before it was trusted:

- **Rendering.** A plain pixel difference is mostly anti-aliasing: on the example output it reaches 1.4% of a
  page while a page with all its annotations dropped differs by 0.6%, so a threshold on it cannot separate
  the two. The comparison uses two measures instead: solid regions (the difference mask eroded by a 5 x 5
  minimum filter, which removes edge noise) and total darkness. At 96 dpi every agreeing page stays at 0
  solid pixels and at most 9.3% darkness; dropping the annotations of `annotated.pdf` gives 676 solid pixels
  and 50% darkness, and a missing watermark 53% darkness. The thresholds are 200 pixels and 20%. PDFium is
  given its form environment (`init_forms`) because without it it does not draw widgets, which first showed
  up as a false disagreement on the form pages.
- **Fonts.** Raising one width in a `/W` array by 50 units is reported as `CID 1: /W says 368, the program's
  advance is 317.9`.
- **Encryption.** pypdf answers a wrong password on a document with an empty user password with
  `NOT_DECRYPTED`, the same for a file qpdf encrypted, so the check expects that rather than a user open.
- **Incremental.** The `appends-only` check flags a revision that redeclares none of the earlier objects; a
  change to a page has to redeclare the page or the tree that points at it.
- **Robustness.** The four synthesized documents each produce the outcome named above, and a file listed as
  known that is read cleanly fails the run.
- **Fuzzing.** The JPEG selector input reproduces on the instrumented build through SharpFuzz's single-run
  path (`SCPDF_FUZZ_TARGET=dct dotnet SharpCorePDF.Validation.dll <file>`), with the stack ending in
  `JpegDecoder.DecodeBlock`; the seeds of `document`, `dct` and `ccitt` run clean.

## What runs where

On a Windows workstation the checks that need only Python, Node, .NET and a portable JRE run in a minute or
two, and veraPDF installs without a system change (`jdk-17` and `verapdf-greenfield-1.30.4` unpacked into a
scratch directory), which is how the per-clause measurement above was taken file by file. Two things do not run
there: libFuzzer, which needs Linux, and the Arlington service, which needs a Docker daemon. Both run in
`robustness.yml` and `validation.yml`, where the Arlington check is blocking and its accepted rules are listed
beside the step with the reason for each.
