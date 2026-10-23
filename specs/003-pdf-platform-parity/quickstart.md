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
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- creator-parity
python docs\tools\validate-pdf.py --check-syntax --text --render --images <output>\creator-parity.pdf
```

**Expected**: the path, text, image and layout tests pass; the creator example writes a ten-page
document with vector paths, wrapped text, a JPEG and a PNG; `pikepdf`/`qpdf` report no syntax problems;
`pypdf` extracts the accented text; PyMuPDF renders the paths and images at the expected positions; the
same inputs produce identical bytes on a second run.

## 4. Phase 2: editing parity

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfDocumentEditorTests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- edit-pages
python docs\tools\validate-pdf.py --check-syntax --pages --geometry <output>\edited.pdf
```

**Expected**: page order, rotation, crop and size read back exactly; the watermark is present in the
requested layer; `qpdf --check` is clean; a second reader reports the same page count and geometry; an
incremental update leaves the original byte range unchanged.

## 5. Phase 3: annotations and forms

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfAnnotationTests|FullyQualifiedName~PdfFormTests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- annotate-and-fill
python docs\tools\validate-pdf.py --annotations --form-values <output>\annotated.pdf
```

**Expected**: every annotation has a readable rectangle and appearance; `pypdf` reads the field values;
the flattened page renders the values without a form viewer; the original page content stream is
byte-identical to the source.

## 6. Phase 4: signatures and PDF/A

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 `
  --filter "FullyQualifiedName~PdfSignatureTests|FullyQualifiedName~PdfATests"
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- sign-and-archive

python docs\tools\validate-signatures.py <output>\signed.pdf
python docs\tools\validate-pdfa.py <output>\archive-2b.pdf --profile 2b
```

**Expected**: pyHanko reports a valid signature and a trusted chain for the local test root; a
one-byte mutation is rejected; an annotation added afterwards leaves the first signature valid; veraPDF
reports PDF/A-2b conformance, and the same for 1b and 3b fixtures.

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
# Same inputs, fixed dates, twice.
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- creator-parity
Get-FileHash <output>\creator-parity.pdf -Algorithm SHA256
dotnet run --project .\examples\SharpCorePDF.Examples -c Release -- creator-parity
Get-FileHash <output>\creator-parity.pdf -Algorithm SHA256

# No new package dependency.
dotnet list .\SharpCorePDF.slnx package --include-transitive | Select-String 'PdfSharp|PdfPig|SkiaSharp|System.Drawing'

# No network path in the library.
Select-String -Path (Get-ChildItem src -Recurse -Filter '*.cs').FullName -Pattern 'HttpClient|WebRequest|Socket'
```

**Expected**: both hashes are identical; the package search returns no PDFsharp/PdfPig/SkiaSharp/
System.Drawing package; the source search returns no network API in `src/`.

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


