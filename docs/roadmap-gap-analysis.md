# Roadmap intake: what the feedback asked for, and what was already here

The roadmap document *"SharpCorePDF — Architecture & Feature Roadmap"* arrived as a list of nine
capabilities. Read against this tree on 2026-11-06, **six of the nine already shipped**, two were the same
capability under another name, and three were genuinely missing. This page records that comparison with the
command output that supports it, so the next contributor does not rebuild a feature that exists.

The plan that came out of it is [`specs/005-roadmap-intake/plan.md`](../specs/005-roadmap-intake/plan.md).

## The comparison

| Feedback item | What was already in the tree | Outcome |
| --- | --- | --- |
| 1.1 vectorised token search | `Pdf/SimdByteScan.{Avx2,Sse2,AdvSimd,Scalar}.cs`, used for `endstream`, inline images and the PNG `Up` predictor | already shipped — nothing to do |
| 1.1 `System.IO.Pipelines` / `ReadOnlySequence<byte>` | nothing; the reader is random-access over one `byte[]` (`PdfFileReader`) because a cross-reference table names byte offsets | **decided against for 1.x** in [`adr/0004-buffer-model.md`](adr/0004-buffer-model.md), on a 100 MB measurement: the read path already allocates 1.00× input, and a pipe cannot address a document by offset. The allocation that *is* there — 2.04× on a file-backed merge — is a second read, not a buffer model, and it was **removed** under ADR-004: a merge and an edit read each source once, 2.04× → 1.04× with identical output |
| 1.2 `[assembly: IsTrimmable(true)]` | `IsTrimmable`/`IsAotCompatible` in `src/Directory.Build.props`, which emits that metadata for every assembly | already shipped — an attribute would duplicate it |
| 1.2 no reflection / no dynamic code | `AddSharpCorePDF(IConfiguration)` binds by property reads; no `RequiresUnreferencedCode` anywhere | already shipped |
| 1.2 source generator for `AddSharpCorePDF()` | nothing left to generate | refused (plan §2) |
| 2.1 `PdfAConformanceLevel` | ships as `PdfAProfile` (1b, 2b, 3b) with a pre-flight, `/OutputIntents` and XMP | already shipped under another name — a rename breaks the frozen surface |
| 2.2 incremental updates and PKCS#7 signatures | `PdfEditOutputOptions.UseIncrementalUpdate`, `PdfIncrementalWriter`, `PdfSignatureOptions`, `Signatures/CmsBuilder` | already shipped (`PdfSignaturePlan` is `PdfSignatureOptions`) |
| 2.3 `DrawRectangle` / `DrawLine` / `DrawPath` | `PdfPageCanvas` had all of them, plus `DrawEllipse`, `SetClip`, `SetTransform`, `FillPath`, `StrokePath`, `SaveState`/`RestoreState` | already shipped |
| 2.3 alpha channels and `/ExtGState` transparency | the machinery existed (`ContentStreamBuilder.SetAlpha`, `GraphicsState`, `ContentResourceRegistry`) but the canvas exposed no door to it | **done in this increment** |
| 3.1 `PdfInspector.InspectAsync` | `IPdfInspector.InspectAsync` → `PdfDocumentInfo` | already shipped |
| 3.1 `GetEmbeddedFonts()` → `PdfFontInfo` | nothing | **done in this increment** |

## What this increment built

Two features, both additive, both in `[Unreleased]`:

1. **`PdfPageCanvas.SetAlpha(alpha, blendMode)`** — the opacity and blend mode of what is painted next,
   written as one `/ExtGState` and selected with `gs`, restored by `SaveState`/`RestoreState`, refused
   outside 0–1. `docs/manual/src/15-drawing.md` has the section; `PdfPageCanvasTransparencyTests` has the
   ten tests, including the PDF/A-1b refusal and the PDF/A-2b acceptance.
2. **`IPdfFontInspector.ReadFontsAsync`** — one entry per font with its kind, program, encoding,
   `/ToUnicode` flag, subset flag and the pages that select it, plus the `FontUnreadable` and
   `FontDescriptorMissing` remarks. `docs/manual/src/26-fonts.md` is the chapter;
   `PdfFontInspectorTests` has the twenty tests.

## The evidence, as it was run

### Build and tests

```text
dotnet build .\src\SharpCorePDF.DependencyInjection\SharpCorePDF.DependencyInjection.csproj -c Release
  → Build succeeded. 0 Warning(s) 0 Error(s)

dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -c Debug -p:TargetFramework=net11.0
  → Passed! total: 1139, failed: 0, succeeded: 1139

dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64
  → Passed! total: 1139, failed: 0, succeeded: 1139

dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -c Release -p:TargetFramework=net11.0
  → Passed! total: 1139, failed: 0, succeeded: 1139
```

The suite grew from 1076 tests per leg (the count in `README.md`) to 1139.

A **stale test host** from an earlier Release run held `tests/.../bin/Release/net11.0` open for the whole
session, so the Release leg above was run with `-p:BaseOutputPath=artifacts\lockfree\`; the ordinary path
could not be written to. That is an environment artefact, not a repository one — the folder is ignored, and
a fresh checkout builds it. One full-suite run during that window also failed
`PdfDocumentEditorTests.Writes_the_same_bytes_for_the_same_input` with a one-byte difference inside a
number; two full runs after the stale host stopped holding the assembly were green, so it is recorded here
as observed once, unexplained, and not reproducible.

### Native AOT and trimming

```text
dotnet publish .\examples\SharpCorePDF.AotSmoke -c Release -r win-x64 -p:PublishAot=true
  → no warnings (no IL2026, no IL3050)
.\examples\SharpCorePDF.AotSmoke\bin\Release\net11.0\win-x64\publish\SharpCorePDF.AotSmoke.exe --expect-aot
  → aot=True pages=2 chars=26 bytes=1348
  → AOT smoke: ok        (exit 0)
```

Measured, because the roadmap quotes figures:

| What | Value |
| --- | --- |
| Published executable | 3 524 096 bytes (3.36 MiB) |
| Whole publish folder | 21.61 MiB across 10 files |
| End-to-end process wall time, five runs | 19.3 / 18.4 / 16.9 / 15.4 / 17.4 ms, median **17.4 ms** |

So a published AOT process that authors a page, merges two documents through the container, reads the text
back and asserts that it is running without dynamic code completes in about **17 ms** on this machine. The
roadmap's "&lt;10 ms startup" is not supported by that measurement and is not repeated anywhere in this
repository; "&lt;30 MB image" is consistent with the 21.61 MiB publish folder, which is where the claim
belongs.

### External validation

The font inspector's real-world leg is pinned on `tests/SharpCorePDF.Tests/Assets/parity/labelled/table.pdf`,
a document this repository did not write. Two independent tools were run against the same file:

```text
uv run --with pikepdf --with pypdf python .\docs\tools\validate-pdf.py tests\SharpCorePDF.Tests\Assets\parity\labelled\table.pdf
  → qpdf check: no problems
  → version : 1.3, pages: 1, objects: 7
  → page 1  : [0.0, 0.0, 612.0, 792.0] rotate=0 content=320 bytes, annots=0
  → streams : 1 decoded, 0 failed

uv run --with pikepdf python -c "…"      # list every /Font entry of the same file
  → fonts: 3
  → ('/F1', '/Type1', '/Helvetica',   '/WinAnsiEncoding', False)
  → ('/F2', '/Type1', '/Courier',     '/WinAnsiEncoding', False)
  → ('/F3', '/Type1', '/Times-Roman', '/WinAnsiEncoding', False)
```

`PdfFontInspectorTests.Reports_the_fonts_of_a_real_world_document` asserts exactly those three names, their
kind, their encoding and that none carries a program, so the library's answer and pikepdf's answer agree on
the file. The in-memory fixture that pins every other branch is built by `PdfContentFixtures.Fonts()`;
writing it to disk for a validator run is the remaining gap named in `docs/parity.md` row 22.

### Mutation testing

Scoped runs over the files this increment touched:

| File | Score | Killed | Survived | No coverage | Compile errors | Mutants |
| --- | --- | --- | --- | --- | --- | --- |
| `Pdf/PdfFontDescriptorLookup.cs` | **97.44%** | 38 | 0 | 1 | 28 | 69 |
| `Reading/FontAccumulator.cs` | **94.44%** | 34 | 1 | 1 | 28 | 73 |
| `Reading/PdfFontInspector.cs` | **90.91%** | 10 | 1 | 0 | 2 | 16 |
| `PdfPageCanvas.cs` | 42.04% | 66 | 67 | 24 | 1 | 201 |

The canvas figure is dominated by drawing code this increment did not write; **every mutant inside
`SetAlpha` and its operation is killed** (`PdfPageCanvas.cs` lines 316–337: one `CompileError`, three
`Ignored` block removals, everything else `Killed`). The two survivors in the new font code are read and
explained in `docs/mutation-testing.md`: a `ConfigureAwait(false)` argument and a dictionary key whose only
job is to be unique, both unobservable.

Reading the survivors is what shaped the fixture. The first run scored `FontAccumulator.cs` at 69.44% and
`PdfFontDescriptorLookup.cs` at 42.55%, and each survivor named a shape the fixture did not carry — a Type 3
font that states no `/CharProcs`, a `/FontFile` and a `/FontFile3` program, an encoding dictionary that names
a base encoding, a composite font with a `CIDFontType0` descendant, a subset tag of exactly eight characters,
a nested form, and a page whose resources are inherited from a branch of the page tree. Two statements were
**removed** because a survivor said so: the redundant `?? page.Inherited…` fallback in `PdfFontInspector`
(the page tree already merges a page's own attributes into `Inherited`), and eight
`ArgumentNullException.ThrowIfNull` guards in an internal helper no caller can hand a null to.

