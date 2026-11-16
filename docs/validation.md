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
python docs/tools/validate-incremental.py $env:TEMP\corpus --known-defect id-first --known-defect appends-only
python docs/tools/validate-signatures.py --manifest $env:TEMP\corpus\manifest.json --known-defect signed-then-edited.pdf:FirstSignature
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
| An incremental update writes a new identifier into both elements of `/ID`; ISO 32000-1 §14.4 keeps the first | #10 | `PdfIncrementalInvariantTests.Keeps_the_first_identifier_element_across_an_incremental_update` (skipped); `validate-incremental.py` `id-first` |
| `UseIncrementalUpdate` appends a renumbered copy of the whole document rather than the objects it changed: every byte of the earlier revision is kept, but the file grows by its own size per edit, and the signature dictionary is written again after signing, so pyHanko places a signature followed by an edit as `CONTIGUOUS_BLOCK_FROM_START` with modification level `OTHER` instead of `ENTIRE_REVISION` | #10, #3 | `validate-incremental.py` `appends-only`; `validate-signatures.py` on `signed-then-edited.pdf` |
| A merge copies a page that states no `/MediaBox` (and inherits none) without one, so the result breaks the rule that every page has a media box; qpdf repairs it by assuming Letter | #2 | `validate-pdf.py` on `inspected.pdf` and `with-warnings.pdf` (both merge a page of `NamedDests.pdf`) |
| The JPEG decoder indexes its four Huffman tables with a scan's 4-bit selector unchecked | #12, #1 | `jpeg-selector.pdf`: `IndexOutOfRangeException` from `JpegDecoder.DecodeBlock` |
| A dictionary key that is the empty name (`/` alone, which PDF syntax allows) makes `PdfDictionary.Set` throw `ArgumentException` while parsing, so the document does not open at all | #12 | pdf.js corpus: `issue3188.pdf`, `issue5896.pdf`, `pdfjs_wikipedia.pdf`, `poppler-742-0-fuzzed.pdf` |
| A glyph whose box comes out upside down (a negative font size or a flipped font matrix) makes `PdfGlyph.BoundingBox` construct a `PdfRectangle` that throws, so text extraction faults | #12 | pdf.js corpus: `issue14415.pdf` |
| `PdfDate.TryParse` throws `ArgumentOutOfRangeException` for a time-zone offset beyond 14 hours instead of answering false, so one malformed `/CreationDate` faults the whole inspection | #12 | pdf.js corpus: `TAMReview.pdf` |

Fixed since the list was first drawn up, each with the test that fails without its fix:

| Defect | Fixed by | Test |
| --- | --- | --- |
| The signature validator accepted a `/ByteRange` gap holding more than the `/Contents` string (signature wrapping) | the gap must be exactly one hex string, equal to the dictionary's `/Contents` | `PdfSignatureAttackTests.Does_not_report_a_gap_that_holds_more_than_the_signature_value_as_covering_the_document` |
| No limit on the decoded size of a Flate, LZW or RunLength stream | `PdfEngineOptions.MaxDecodedStreamBytes` (128 MiB) | `PdfFilterTests.Refuses_a_flate_stream_that_decodes_past_the_limit` and its LZW, RunLength and content-stream companions |
| A chain of indirect `/Length` references overflowed the stack | the chain is followed no deeper than `MaxNestingDepth`, then the stream is found by its `endstream` | `PdfReaderRecoveryTests.Reads_a_stream_whose_length_is_a_chain_of_sixty_thousand_streams` |
| An object stream's `/N`, and a JPEG frame's width and height, sized allocations unchecked | `/N` is bounded by what the stream can hold; a JPEG above the caller's budget is measured from its header, and working sizes are computed in 64 bits | `PdfReaderRecoveryTests.Reads_an_object_stream_whose_count_its_header_cannot_hold`, `PdfJpegFilterTests.Measures_a_frame_too_large_for_the_budget_without_allocating_it` |

## Corpus readings, 2026-11-16

| Corpus | Files | Read | Refused | Fault | Crash | Timeout |
| --- | --- | --- | --- | --- | --- | --- |
| veraPDF corpus, `PDF_A-2b` and `PDF_A-4` at `bb75f4f` | 1,471 | 1,471 | 0 | 0 | 0 | 0 |
| pdf.js `test/pdfs` at `v6.4.299` | 987 | 977 | 4 | 6 | 0 | 0 |
| synthesized hostile documents, before the fixes | 4 | 0 | 0 | 3 | 1 | 0 |

The six pdf.js faults are the three defects above. A sample of the pre-flight measurement (the first 60
files of `PDF_A-2b` and of `PDF_A-4`) wrote 108 results, refused 9 non-conforming inputs and refused 3 that
already conformed; whether the 108 conform is veraPDF's to say, which `robustness.yml` asks.

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

## What has not been run here

The Arlington check (#4) and the judged half of the pre-flight measurement (#7) need the Arlington service
and veraPDF, and the fuzzing (#1) needs Linux; they were written against the documented interfaces and run
first on a runner. Both report-only checks say so in their workflow, with what to change once the first run
has set the baseline.
