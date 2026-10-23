# Quickstart: Validating PDF Platform Parity

**Feature**: `003-pdf-platform-parity` | **Date**: 2026-10-21

This guide is the run order for proving the feature end to end. Commands marked **existing** work in
the 1.0 tree today. Commands marked **after Phase N** become runnable when that phase lands; they are
the acceptance commands for the phase, not claims that the code already exists.

## Prerequisites

- .NET SDKs for `net10.0` and `net11.0` (the versions in `global.json`).
- PowerShell 7 (`pwsh`) on Windows; a POSIX shell on Linux/macOS.
- Python 3.11+ with `pikepdf`, `pypdf`, `PyMuPDF`, `fontTools`, `pyhanko` and `verapdf` available for
  the external checks. These are validation tools, not package dependencies.
- `qpdf` on `PATH` (the `pikepdf` wheel can supply its library; the CLI is still the reference).
- A Java runtime for veraPDF if the standalone installer is used.
- No network is required for the library or the test suite. The signature/timestamp fixtures use local
  certificates and a locally supplied timestamp token.

## 1. Baseline: the 1.0 tree is still green (existing)

```powershell
dotnet build .\SharpCorePDF.slnx -c Release
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0
dotnet run --project .\examples\SharpCorePDF.Examples -c Release
```

**Expected**: Release build reports 0 warnings and 0 errors; both legs pass; every example prints
`--- ok`; the example runner exits 0. Record the test count and the two framework results; they are the
1.0 baseline that later phases must not regress.

## 2. Phase 0: boundary and evidence

```powershell
# Review the amendment and the parity matrix.
Get-Content .specify\memory\constitution.md | Select-String 'Version.*2.0.0'
Get-Content docs\parity.md | Select-String 'Must'
Get-Content CHANGELOG.md | Select-String '1.1.0'

# Regenerate the corpus from its generator; the PDFs stay binary in Git.
python docs\tools\make-fixtures.py --parity
git status --short -- tests\SharpCorePDF.Tests\Assets
```

**Expected**: constitution 2.0.0 is ratified; every “Must” row in `docs/parity.md` has an owner, a test
name and a validation command; the generator produces the same fixtures (no unexpected diff); the
1.0 baseline from step 1 is unchanged.

If CA-001 is not merged, **stop here**. Phase 1 is blocked by design.

## 3. Phase 1: creator parity

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfDocumentBuilderTests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- author-page
uv run --with pikepdf --with pypdf --with pymupdf python docs\tools\verify-cover.py <output>\cover.pdf <output>\cover-and-summary.pdf
uv run --with pikepdf python docs\tools\validate-pdf.py <output>\cover.pdf <output>\cover-and-summary.pdf
```

**Expected**: the page, text and image tests pass; `author-page` writes the A4 cover — six lines and a
152 × 87 RGBA PNG — and merges it in front of a real document in memory; `verify-cover.py` asks qpdf,
`pypdf` and PyMuPDF the three questions (syntax, accented text and image XObject with its soft mask,
72 dpi pixels inside and outside the logo) and exits non-zero if any recorded value moves;
`validate-pdf.py` reports no qpdf problem and decodes every stream. The `creator-parity` scenario this
phase is planned around is T036's ten-page document with vector paths, wrapped text, a JPEG and a PNG,
which ships in 1.2 — on `1.1.0` this phase runs `author-page`, which is the same cover the question asks
for.

## 4. Phase 2: editing parity

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfDocumentEditorTests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- edit-pages
python docs\tools\validate-pdf.py <output>\edited.pdf
```

**Expected**: page order, rotation, crop and size read back exactly (`edit-pages` prints 3 pages, page 2
`Clockwise90`, page 3 cropped to `[36 36 806 559]`, and the one warning is its own `OutputVerified`
success signal); `qpdf --check` is clean; a second reader reports the same page count and geometry —
`pypdf` reads the third page's `cropbox` as `[36, 36, 806, 559]`; an incremental update leaves the
original byte range unchanged.

## 5. Phase 3: annotations and forms

**Nothing to run on `1.1.0`.** Annotations and form fields are 1.2 work — the manual's chapters 15–19 are
reserved for them and the tree carries no annotation or form API — so `PdfAnnotationTests`,
`PdfFormTests` and the `annotate-and-fill` scenario are the names 1.2 will create. The block below is that
phase's acceptance run and T104 records it as not runnable on the release commit.

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfAnnotationTests|FullyQualifiedName~PdfFormTests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- annotate-and-fill
python docs\tools\validate-pdf.py <output>\annotated.pdf
```

**Expected**: every annotation has a readable rectangle and appearance; `pypdf` reads the field values;
the flattened page renders the values without a form viewer; the original page content stream is
byte-identical to the source.

## 6. Phase 4: signatures and PDF/A

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfDocumentSignerTests|FullyQualifiedName~PdfDocumentSignatureValidatorTests|FullyQualifiedName~PdfATests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- archive-pdfa
python docs\tools\validate-pdf.py <output>\archive-1b.pdf <output>\archive-2b.pdf <output>\archive-3b.pdf
python docs\tools\validate-pdfa.py <output>\archive-2b.pdf --profile 2b
```

**Expected**: the twelve signature and PDF/A tests pass; `archive-pdfa` writes 1b, 2b and 3b with zero
unsatisfied requirements and refuses a page whose Helvetica is not embedded, writing 0 bytes;
`validate-pdf.py` finds no qpdf problem in any of the three and decodes every stream; `validate-pdfa.py`
reports `PASS` for a profile where veraPDF is installed and exits 2 (“cannot run”) where it is not — the
check needs veraPDF and a Java runtime, and no report is invented in their absence. veraPDF 1.30.3's
`PASS` for all three profiles is recorded under “Phase 4 progress — archival output”. The `sign-and-archive`
scenario (T094) and `validate-signatures.py` against its `signed.pdf` are 1.2 work; pyHanko's evidence for
the signature surface `1.1.0` ships is recorded under “Phase 4 progress — signatures”.

## 7. Phase 5: reading and analysis

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfLayoutReaderTests|FullyQualifiedName~PdfLayoutExportTests|FullyQualifiedName~PdfLayoutCorpusTests|FullyQualifiedName~PdfObjectInspectorTests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- extract-layout inspect-objects

# Score a reading externally. <exports> holds one <name>.alto.xml per labelled <name>.json in
# tests\SharpCorePDF.Tests\Assets\parity\labelled; the example writes that format for its own sample,
# in <output>, and the same call over the five labelled PDFs writes the folder this scores.
python docs\tools\score-layout.py <exports> --known-order-mismatch table
```

**Expected**: `PdfLayoutCorpusTests` scores the five labelled documents in process — character recall at
least 99.5%, box agreement at least 95% within 0.5 pt, and reading-order agreement at least 95% — and
`score-layout.py` measures the same three numbers from a folder of ALTO exports, one line per threshold.
Both report 100% recall and 100% box agreement on all five documents, and 100% reading order on four of
them: `table` is passed to `--known-order-mismatch` because a table is symmetric under the XY cut — the gap
between its columns is as wide as the gap between its rows — so a cell becomes a block and the page is read
column by column rather than row by row. That is a recorded limitation and not an agreement:
`PdfLayoutCorpusTests.Table_page_is_still_read_column_by_column` fails if it ever stops holding, so the
exception cannot outlive the behaviour. The four export formats validate and preserve coordinates — today
`PdfLayoutExportTests` proves the four documents are well-formed, escape their text, place every box in page
space with the origin at the top left and repeat byte for byte, and T079 adds the external schema run — and
the object graph cannot be mutated and a dangling reference is reported, not hidden.

## 8. Determinism and independence

```powershell
# The authored page, twice, in two processes: the same bytes.
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- author-page
Get-FileHash <output>\cover.pdf -Algorithm SHA256
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- author-page
Get-FileHash <output>\cover.pdf -Algorithm SHA256

# A merge is reproducible once its date is: `options` pins ModificationDate, merges twice and compares.
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- options
Get-FileHash <output>\report.pdf -Algorithm SHA256

# No new package dependency.
dotnet list .\SharpCorePDF.slnx package --include-transitive | Select-String 'PdfSharp|PdfPig|SkiaSharp|System.Drawing'

# No network path in the library.
Select-String -Path (Get-ChildItem src -Recurse -Filter '*.cs').FullName -Pattern '\b(HttpClient|WebRequest|WebClient|Socket|TcpClient|UdpClient|Dns)\b' |
  Where-Object { $_.Line -notmatch '^\s*(//|///|\*)' }
```

**Expected**: the two `cover.pdf` hashes are identical (`499520B9…`); `options` prints
`identical True` for its two in-process merges and writes a `report.pdf` whose hash is the same on a second
process (`D32E1E6E…`), because a merge stamps `ModDate` with the current time unless
`MergeOutputOptions.ModificationDate` pins it — and the file identifier is derived from the file, so a
pinned date pins the bytes. The `creator-parity` scenario this step is planned around is 1.2 work (T036);
`author-page` is the 1.1 document. The package search returns no PDFsharp/PdfPig/SkiaSharp/System.Drawing
package, and no `/PackageReference` was added to `src/` since `v1.0.0`; the source search returns nothing —
`Socket` and `WebRequest` appear only in doc comments, which the `Where-Object` drops, and it returns hits
if the filter is removed, which is what an unfiltered search reporting a network path is.

## 9. Evidence to Record

For each phase, attach to the pull request:

- the exact commands and their output;
- the test names that would fail if the behaviour were reverted;
- the external validator versions;
- the performance row from `docs/measurements.md`;
- the parity rows closed, with links to the test and the validation command.

A phase is not complete because the code compiles. It is complete when the evidence above exists for
the release commit.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `dotnet test` cannot find the framework | Name `-p:TargetFramework=net10.0` or `net11.0`; the project multi-targets. |
| The example cannot find a font | The library never reads system fonts; pass the DejaVu subset or another caller-supplied program. |
| JPEG output differs from the source bytes | EXIF orientation or a CMYK/Adobe transform was applied; the warning names the reason. |
| `qpdf --check` reports a missing object | A page or annotation reference survived a delete; the editor’s warning list should name it. |
| pyHanko rejects a signature | Check the byte range, the detached CMS, and whether the timestamp token matches the digest. |
| veraPDF fails a profile | Read `UnsatisfiedRequirements`; a font, colour space or action is usually the cause. |
| Reading order differs on a two-column page | The selected strategy is in the result; raw order is the baseline, not the expected answer. |
