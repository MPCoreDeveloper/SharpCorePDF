# Plan: Roadmap Intake — Performance Core, Spec Expansion and Diagnostic Tooling

**Type**: program plan (intake). It changes no product code by itself; it decides what each later
feature is allowed to be.

**Audience**: the next AI or maintainer who implements the roadmap feedback titled
*"SharpCorePDF — Architecture & Feature Roadmap"* (categories 1–3, its text is truncated inside 3.1).

**Date**: 2026-11-06, against `1.5.0` (`src/Directory.Build.props`).

**Status (2026-11-07)**: **P0, P1, P2 and P3 are done — P3's ADR-004 is Accepted and implemented.** The gap
analysis is [`docs/roadmap-gap-analysis.md`](../../docs/roadmap-gap-analysis.md); `PdfPageCanvas.SetAlpha`
and `IPdfFontInspector` are in the tree with their tests, manual chapters, changelog entries, parity row and
mutation record. P3 measured the 100 MB read path (`--large` in `tools/SharpCorePDF.Measure`, recorded in
`docs/measurements.md` §10), wrote [`docs/adr/0004-buffer-model.md`](../../docs/adr/0004-buffer-model.md) —
which **rejects `System.IO.Pipelines` and the `ReadOnlySequence<byte>` seam for 1.x** — and implemented the
option it recommended: a merge and an edit now read each source once, measured at **2.04× → 1.04×** input on
a 100 MB document with identical output. `P4` (one-call diagnostics) is still waiting for the remainder of the
roadmap text, and `P5` (PDF/A beyond the current claim) for a decision.

**Baseline**: this was written against the **working tree**, not the last commit. That tree carries the
pending `[Unreleased]` change (configuration binding without reflection, the AOT smoke app) and three new
Domain value objects — `PdfFontKind`, `PdfEmbeddedFontProgramKind`, `PdfARequirementKind` — which are not
yet committed. Re-run P0 once that work lands, and correct any line/file reference this document states if
the merge moved it.

---

## 0. The rule that decides every task in this document

The feedback was written as if none of it existed. **Most of it ships already**, and the parts that do
ship were built under the repository's constitution (`.specify/memory/constitution.md` 2.0.0), the parity
matrix (`docs/parity.md`) and the release gates. So the first job is not to write code — it is to refuse
to rewrite what exists, and to spend the effort on the three genuine gaps.

Read this before touching anything:

1. `.specify/memory/constitution.md` — principles I–VII. Principle II (layers point inward) and
   principle V (the public surface is snapshotted) decide where code may go.
2. `docs/parity.md` — rows 10 (incremental updates), 11 (signatures), 12 (PDF/A) and 13 (layout) are
   already **Must / Evidenced**, each with a named test and an external validator command.
3. `src/Directory.Build.props` — `IsTrimmable`, `IsAotCompatible`, `WarningsAsErrors`, the
   `PublicAPI.*.txt` analyzer and the Sonar rule set are already wired. There is nothing to "add" for
   trimming or AOT correctness.
4. `CHANGELOG.md` `[1.5.0]` — the performance work the feedback asks for was already measured and
   recorded; the release even documents a *rejected* vectorisation ("vectorising a per-token decision is
   a loss"). Do not undo that lesson.

**Hard rule for the implementing AI**: a task marked **SHIPPED → verify only** must not produce a new
type, a new name or a new file under `src/`. If verification fails, the task becomes a bug fix with a
named test — never a re-implementation. The same rule applies to a name the feedback invents: if the
capability exists under another name, the plan uses the existing name and says so (§2).


---

## 1. Gap analysis (verified, not assumed)

Every "evidence" cell below was checked in this tree on 2026-11-06.

| Feedback item | What the tree already has | Verdict | Work left |
| --- | --- | --- | --- |
| 1.1 vector/SIMD token search (`obj`, `endobj`, `stream`, `xref`) | `src/SharpCorePDF.Infrastructure/Pdf/SimdByteScan.cs` plus `.Avx2`, `.Sse2`, `.AdvSimd`, `.Scalar` legs; applied to `endstream`, inline images and the PNG `Up` predictor (`CHANGELOG.md` `[1.5.0]` → Performance) | **SHIPPED** | nothing |
| 1.1 `System.IO.Pipelines` / `ReadOnlySequence<byte>` while parsing | zero occurrences of either name under `src/`; `PdfFileReader` holds the document as one `byte[]` (`src/SharpCorePDF.Infrastructure/Pdf/PdfFileReader.cs:15`) because a cross-reference table makes the read random-access | **MISSING**, and partly mis-specified (§2) | **P3**, behind an ADR and a measurement |
| 1.2 `[assembly: IsTrimmable(true)]` | `IsTrimmable` + `IsAotCompatible` in `src/Directory.Build.props:46-47` emit that assembly metadata themselves; runnable proof in `examples/SharpCorePDF.AotSmoke`; evidence in `docs/aot.md` | **SHIPPED** | nothing — the attribute would be duplicate metadata |
| 1.2 no reflection / no dynamic code | `AddSharpCorePDF(IConfiguration)` binds `PdfEngineOptions` by property reads (`SharpCorePDFServiceCollectionExtensions.BindEngineOptions`), so the `RequiresUnreferencedCode`/`RequiresDynamicCode` annotations are gone | **SHIPPED** | nothing |
| 1.2 source generator for `AddSharpCorePdf()` | nothing is reflective any more, so there is nothing for it to replace | **REJECTED** (§2), unless the maintainer wants generator-based *options validation* | — |
| 2.1 `PdfAConformanceLevel` on `PdfEditOutputOptions` / builder | the same concept ships as `PdfAProfile` (PDF/A-1b, -2b, -3b) in `src/SharpCorePDF.Domain/ValueObjects/PdfAProfile.cs`, carried by `PdfEditOutputOptions.PdfAProfile`, `PdfAConversionOptions` and `IPdfAWriter` | **SHIPPED under another name** | nothing — a rename breaks the frozen snapshot |
| 2.1 mandatory font embedding, `/OutputIntents`, pre-flight warnings | `Authoring/PdfARequirements.cs` (font program, `/CIDSet`, ExtGState transparency at 234–245, forbidden filters, actions, external files), `PdfARequirementKind`, `PdfAProfileRules`, output intents in `PdfEditEngine.Document.cs:204-224` and `PdfMergeEngine.Document.cs:39`, XMP in `Authoring/XmpPacket.cs`; `PdfATests`; external check `docs/tools/validate-pdfa.py` | **SHIPPED** | nothing for the ask; `-2u`/`-3u`/`-4` is new scope (**P5**, decision gate) |
| 2.2 incremental saving | `PdfEditOutputOptions.UseIncrementalUpdate`, `Pdf/PdfIncrementalWriter.cs`, `PdfEditEngine.Incremental.cs`, `PdfIncrementalWriterTests`; parity row 10 | **SHIPPED** | nothing |
| 2.2 `PdfSignaturePlan`, PKCS#7/CMS, `/ByteRange` | the same capability ships as `PdfSignatureOptions` + `IPdfSigner`/`IPdfSignatureValidator`, implemented by `Signatures/CmsBuilder.cs`, `SignatureFieldWriter.cs`, `CmsReader.cs`, `SignatureValidator.cs`, `TrustEvaluator.cs`; pyHanko evidence in `docs/tools/validate-signatures.py`; parity row 11 | **SHIPPED under another name** | nothing — do not add a second plan type |
| 2.3 `DrawRectangle` / `DrawLine` / `DrawPath` | `PdfPageCanvas.DrawLine:160`, `DrawRectangle:177`, `DrawEllipse:209`, `DrawPath:260`, plus `FillPath`, `StrokePath`, `SetClip`, `SetTransform`, `SetFillColor`, `SetStrokeColor`, `SaveState`/`RestoreState` | **SHIPPED** | nothing |
| 2.3 alpha channels / `/ExtGState` transparency on the canvas | the machinery exists and is tested for annotation appearances — `ContentStreamBuilder.SetAlpha:168`, `Authoring/GraphicsState.SetAlpha:57`, `ContentResourceRegistry` (dedupes states, writes `/ca`, `/CA`, `/BM`), `PageResources.cs:191` — but `PdfPageCanvas` exposes **no** member and there is no operation record for it | **PARTIAL** | **P1** (three small edits) |
| 3.1 `PdfInspector.InspectAsync(stream)` → page count, encrypted, version | `IPdfInspector.InspectAsync` → `PdfDocumentInfo` (`Version`, `IsEncrypted`, `RequiresPassword`, `PageCount`, `Pages`, `Metadata`, `Warnings`) | **SHIPPED** | nothing |
| 3.1 `inspector.GetEmbeddedFonts()` → `IReadOnlyList<PdfFontInfo>` | no `PdfFontInfo` and no `GetEmbeddedFonts` anywhere; the pieces exist (`PdfFontKind`, `PdfEmbeddedFontProgramKind`, the reader-side `PdfFont`, and the descriptor walk in `PdfARequirements.FindDescriptor`) | **MISSING** | **P2** |
| 3.1 "diagnostic inspection API" beyond that | `IPdfObjectInspector` already returns the whole object graph and the operations of a page; `IPdfLayoutReader` (with `IPdfLayoutSession`) returns letters, words, blocks and exports | **SHIPPED, richer than the sketch** | **P4** adds one summarising call on top |

The roadmap text arrives truncated immediately after the `GetEmbeddedFonts()` snippet. Everything in
category 3 after that point is unknown; **P4** is planned from what the repository can already answer,
and the maintainer should paste the remainder of the document before P4's spec is written.

---

## 2. Asks this plan refuses, and why

Each is a decision, recorded so a later reviewer does not reopen it by accident.

1. **Do not rename `PdfAProfile` to `PdfAConformanceLevel`, or `PdfSignatureOptions` to
   `PdfSignaturePlan`.** Both are in `PublicAPI.Shipped.txt`, and principle V freezes that surface. The
   capability the feedback describes is what those members already do.
2. **Do not add `[assembly: IsTrimmable(true)]`.** The property in `src/Directory.Build.props` already
   puts the metadata in every assembly and turns the analyzers on; an attribute would be a second,
   drifting statement of the same claim.
3. **Do not let `System.IO.Pipelines` own the document.** A pipe is forward-only and its memory does not
   survive `Advance`; a PDF is addressed by absolute offset from a cross-reference table. Pipelines may
   only be used where the engine truly consumes bytes forwards (P3 below). For the record:
   `System.IO.Pipelines` is in-box on both legs (`Microsoft.NETCore.App.Ref/10.0.14/ref/net10.0` and
   `11.0.0/ref/net11.0`), so using it adds **no package reference** — the objection is about
   semantics, not dependencies.
4. **Do not add a source generator for `AddSharpCorePDF()`.** It would generate what `BindEngineOptions`
   already does by hand, and add an analyzer package to the build for no caller-visible gain.
5. **Do not quote "&lt;10 ms startup" or "&lt;30 MB image" in any document until measured** on
   `examples/SharpCorePDF.AotSmoke` and on a published binary (P0-T005). This repository publishes no
   number it has not run.
6. **Do not vectorise a per-token decision.** `CHANGELOG.md` `[1.5.0]` records the measurement that made
   that a rule.

---

## 3. The phases

### 3.0 How a phase becomes a change (applies to P1–P5)

A phase is not implemented straight from this document. Each one is delivered the way `001`–`004` were:

```text
specs/00N-<phase-slug>/
├── spec.md        # /speckit-specify: what the phase promises, with acceptance scenarios
├── plan.md        # /speckit-plan: technical context + constitution check (copy the shape of 004/plan.md)
├── research.md    # only where a decision exists (P3 requires one; P1 and P2 do not)
├── quickstart.md  # how a reviewer sees the change and sees the test fail on revert
└── tasks.md       # /speckit-tasks: T001…, each naming the exact file
```

Every phase, without exception, ends with the same seven items:

1. a **named test that fails on revert** (principle IV) for every behaviour it adds;
2. `PublicAPI.Unshipped.txt` updated **in the same commit** as the surface change (RS0016/RS0017 are
   build errors), refreshed with `docs/tools/update-public-api.ps1`;
3. zero warnings on `net10.0` **and** `net11.0` in `Release`;
4. the manual chapter that describes the feature (a chapter, not a paragraph in the README);
5. a `CHANGELOG.md` entry under `[Unreleased]`;
6. a scoped Stryker run over the touched files, with the survivors **read**, per `.clinerules/tooling.md`;
7. the docs/parity row (new row, or the existing row's evidence cell) updated with the test name and the
   external command that was actually run.

### P0 — Intake verification (no product code, half a day)

**Goal**: turn this document's "SHIPPED" column into evidence a reviewer can click, so nothing downstream
re-implements a shipped feature.

| Task | What | Done when |
| --- | --- | --- |
| P0-T001 | `dotnet build .\SharpCorePDF.slnx -c Release` | zero warnings, five packages |
| P0-T002 | `dotnet test .\SharpCorePDF.slnx` on both legs (`-p:TargetFramework=net11.0`, `-p:TargetFramework=net10.0 -r win-x64`) | green, test count recorded (README says 1076 per leg) |
| P0-T003 | run the three external validators on the checked-in candidates: `docs/tools/validate-pdfa.py`, `docs/tools/validate-signatures.py` (pyHanko), `docs/tools/validate-pdf.py` (qpdf) | each command and its output quoted in the phase's `quickstart.md` |
| P0-T004 | publish and run `examples/SharpCorePDF.AotSmoke` with `expect-aot` | zero IL2026/IL3050, exit 0 |
| P0-T005 | **measure** cold start of the published smoke binary and the size of a self-contained publish; compare with the feedback's `<10 ms` / `<30 MB` | a number in `docs/aot.md`; the feedback's claim is corrected or dropped |
| P0-T006 | record the `1.5.0` performance baseline that P3 must not regress: the `extract-200`, `text-200-two-questions` and `merge` rows in `docs/measurements.md` | the numbers are pasted into P3's spec as the budget |

**Outcome**: `docs/roadmap-gap-analysis.md` — one page, the table from §1 with the commands' real output
beside each "SHIPPED" cell. This is the artefact that stops the next contributor from rebuilding PDF/A.

### P1 — Canvas transparency and blend modes (roadmap 2.3, the real half) → `1.6.0`

**Why**: the machinery is complete but unreachable. `ContentStreamBuilder.SetAlpha`, `GraphicsState` and
`ContentResourceRegistry` are exercised by annotation appearances and covered by
`PdfContentStreamBuilderTests.Registers_an_identical_extgstate_once`; only the page canvas has no door to
them. This is the smallest task in the feedback with the largest visible payoff ("draw a translucent
watermark", "multiply a logo"), which is why it goes first.

**Design constraints**

- The public member mirrors `GraphicsState.SetAlpha(double alpha, PdfBlendMode blendMode)` semantics:
  both the non-stroking and the stroking alpha, one value for each. Do **not** invent separate
  fill/stroke members in this phase; `PdfPaint.Alpha` already carries per-paint alpha on a fill.
- Validation at the canvas boundary must match the internal guard: finite, `0.0 ≤ alpha ≤ 1.0`,
  `Enum.IsDefined(blendMode)`; otherwise `ArgumentOutOfRangeException` naming the parameter.
- Over PDF/A-1b the result must be refused by the existing pre-flight, not by the canvas: the check at
  `Authoring/PdfARequirements.cs:234-245` (`UsesTransparency`) already reports an ExtGState that blends or
  fades. The phase **adds a test for that pairing**, it does not add the rule.
- Unlike a draw call, `SetAlpha` is state, not content: it must follow `SaveState`/`RestoreState`
  correctly. Two translucent shapes under one `SaveState` must share one `/ExtGState` name (the registry
  dedupes), and a `RestoreState` must not resurrect the alpha of an outer state.

**Tasks**

| Task | File | Change |
| --- | --- | --- |
| P1-T001 | `src/SharpCorePDF.Infrastructure/PdfPageCanvas.cs` | add `AlphaOperation(double Alpha, PdfBlendMode BlendMode) : IPageOperation` beside the other operation records (the block that starts at `internal interface IPageOperation` around line 547) |
| P1-T002 | same file | add `public PdfPageCanvas SetAlpha(double alpha, PdfBlendMode blendMode = PdfBlendMode.Normal)`: validate, `operations.Add(new AlphaOperation(alpha, blendMode))`, `return this`. XML docs must state that it applies to everything painted after it until the next call or restore, and that PDF/A-1b forbids it |
| P1-T003 | `src/SharpCorePDF.Infrastructure/Authoring/PageContentWriter.cs` | add `case AlphaOperation alpha: content.SetAlpha(alpha.Alpha, alpha.BlendMode); break;` inside the existing switch (the `resources.AddGraphicsStates(content.Resources)` call after the loop already publishes the `/ExtGState` dictionary — no other change is needed) |
| P1-T004 | `src/SharpCorePDF.Infrastructure/PublicAPI.Unshipped.txt` | one line for `PdfPageCanvas.SetAlpha` (`docs/tools/update-public-api.ps1`) |
| P1-T005 | `tests/SharpCorePDF.Tests/PdfPageCanvasTransparencyTests.cs` (new) | see below |

**Tests (P1-T005)** — the file must contain at least these, each one failing on revert of T001–T003:

1. `SetAlpha` writes `/GS1 gs` into the page's content stream and `/ExtGState << /GS1 << /Type
   /ExtGState /ca 0.5 /CA 0.5 >> >>` into the page resources — read the produced document back with
   `PdfDocumentReader`, do not string-match a builder buffer.
2. two identical `SetAlpha` calls write **one** `/ExtGState` entry and two `gs` operators.
3. `SetAlpha(0.5, PdfBlendMode.Multiply)` writes `/BM /Multiply`.
4. `SaveState(); SetAlpha(0.2); RestoreState();` leaves the second shape opaque — assert the emitted
   order (`q`, `gs`, `Q`) and that the trailing operations carry no further `gs`.
5. `-0.1`, `1.1`, `double.NaN` and `(PdfBlendMode)99` each raise `ArgumentOutOfRangeException`.
6. authoring an alpha page with `PdfAProfile = PdfAProfile.PdfA1B` makes the conversion refuse and name
   the graphics state (reuses `PdfATests`'s `ConvertAsync` helper shape).

**Evidence**: `dotnet test` with the new file; the produced PDF through `docs/tools/validate-pdf.py`
(`qpdf --check` plus decoded streams) so a third implementation confirms the `/ExtGState` is well formed.

**Docs**: `docs/manual/src/15-drawing.md` gains "Transparency and blend modes" (one example, the PDF/A-1b
caveat, and the note that `SetAlpha` is state, so it pairs with `SaveState`); `CHANGELOG.md`
`[Unreleased] → Added`; `docs/parity.md` row 9 or a new row carries the test name.

**Stryker**: `dotnet stryker --config-file stryker-config.json --project
src\SharpCorePDF.Infrastructure\SharpCorePDF.Infrastructure.csproj --mutate "**/PdfPageCanvas.cs"
--mutate "**/Authoring/PageContentWriter.cs"` (with the `MSBUILD_EXE_PATH` workaround from
`.clinerules/tooling.md`). Survivors to expect and to judge: the guard clauses of `SetAlpha` are the ones
a missing test leaves alive — tests 5 close them.

### P2 — Font and embedded-font inspection (roadmap 3.1, the missing half) → `1.6.0`

**Why**: the one call in the feedback that does not exist at all. It is also the call a support engineer
needs first: "why does this document print blanks?" is answered by "this font is not embedded" or "this
CID font has no `/ToUnicode`, so nothing can be extracted from it".

**Shape** — follow the object-inspector trio exactly, because that shape already exists twice and a
reviewer should not have to learn a third:

| Layer | Type | Notes |
| --- | --- | --- |
| Domain | `ReadModels/PdfFontInfo.cs` (`PdfFontInfo`) | reuse `PdfFontKind` and `PdfEmbeddedFontProgramKind`; do **not** add a parallel enum |
| Domain | `Ports/IPdfFontInspector.cs` | `Task<PdfReadResult<PdfFontInfo>> ReadFontsAsync(IPdfContentSource source, string? password = null, CancellationToken cancellationToken = default)` — mirrors `IPdfObjectInspector` |
| Application | `Ports/IPdfDocumentFontInspector.cs` | engine-facing: `(string sourceName, Stream content, string? password, CancellationToken)`, mirrors `IPdfDocumentObjectInspector` |
| Application | `Services/PdfFontInspectorService.cs` | opens the source once per call, disposes the stream, refuses nothing but a null source — mirrors `PdfObjectInspectorService` |
| Infrastructure | `Reading/PdfFontInspector.cs` | the engine; walks the page tree, then each page's `/Resources /Font` |
| DependencyInjection | `AddSharpCorePDFCore` | two registrations (`TryAddSingleton` for the engine, then the port), same ordering as the object inspector |

`PdfFontInfo` fields, each one justified by a question someone actually asks:

- `ResourceName` (`/F1`), `BaseFont` (`/BaseFont`), `Kind` (`PdfFontKind` from `/Subtype`, `Type0`
  resolved to its descendant's `CIDFontType0`/`CIDFontType2`);
- `Program` (`PdfEmbeddedFontProgramKind` from `/FontDescriptor /FontFile|FontFile2|FontFile3`),
  `IsEmbedded` (`Program != None || Kind == Type3`), `IsSubset` (the six-uppercase-letter prefix before
  `+` in `/BaseFont`, the same test `PdfARequirements` uses at line 538);
- `Encoding` as the document states it (`WinAnsiEncoding`, `MacRomanEncoding`, or "custom" for a
  `/Differences` array), and `HasToUnicode`;
- `PageNumbers` — the one-based pages whose resources select this font, deduplicated by object number, so
  a document that reuses one font on 200 pages yields one entry with 200 numbers;
- warnings ride on `PdfReadResult<PdfFontInfo>`, not on each record.

**Reuse, do not duplicate**: the `/Type0` → descendant → `/FontDescriptor` walk already exists as
`PdfARequirements.FindDescriptor` (private, `Authoring/PdfARequirements.cs:555-570`). Extract it into an
internal helper (proposal: `src/SharpCorePDF.Infrastructure/Pdf/PdfFontDescriptorLookup.cs`, one static
`Find(PdfFileReader reader, PdfDictionary font)`) and call it from both places. That is a refactor with a
test, not a copy — Sonar's duplicate-member rule would reject the copy anyway.

**New warning kinds** (additive to the public `PdfWarningKind` enum, each with XML docs): `FontUnreadable`
(a font resource could not be resolved) and `FontDescriptorMissing` (a font states no descriptor, so
neither its program nor its subset can be reported). Reuse `DanglingReference` where a reference simply
does not resolve, and `TextUnreadable` where the *text* could not be read — that distinction is the point.

**Tasks**

| Task | File | Change |
| --- | --- | --- |
| P2-T001 | `src/SharpCorePDF.Domain/ReadModels/PdfFontInfo.cs` | new record with the fields above and a `ToString()` in the house style |
| P2-T002 | `src/SharpCorePDF.Domain/Ports/IPdfFontInspector.cs` | new port |
| P2-T003 | `src/SharpCorePDF.Domain/ValueObjects/PdfWarningKind.cs` | the two new kinds, documented |
| P2-T004 | `src/SharpCorePDF.Application/Ports/IPdfDocumentFontInspector.cs`, `Services/PdfFontInspectorService.cs` | new port + service |
| P2-T005 | `src/SharpCorePDF.Infrastructure/Pdf/PdfFontDescriptorLookup.cs` | extracted from `PdfARequirements`; `PdfARequirements` then calls it |
| P2-T006 | `src/SharpCorePDF.Infrastructure/Reading/PdfFontInspector.cs` | the walk, the dedupe and the warnings; a form XObject's resources are walked at depth, mirroring `PdfARequirements.CheckResources` and honouring `PdfEngineOptions.MaxNestingDepth` |
| P2-T007 | `src/SharpCorePDF.DependencyInjection/SharpCorePDFServiceCollectionExtensions.cs` | two registrations inside `AddSharpCorePDFCore` |
| P2-T008 | the four `PublicAPI.Unshipped.txt` files | Domain, Application, Infrastructure, DependencyInjection |
| P2-T009 | `tests/SharpCorePDF.Tests/PdfFontInspectorTests.cs` + `Assets/parity/fonts/*.pdf` | fixtures: an embedded TrueType subset, a standard Type1 with `/WinAnsiEncoding`, a `Type0` with `/Identity-H` and no `/ToUnicode`, a Mac-written font, and an encrypted one |

**Tests (P2-T009)** — named, and each one fails on revert:

1. the embedded TrueType fixture reports `Kind = TrueType`, `Program = FontFile2`, `IsEmbedded = true`,
   `IsSubset = true`, and the page numbers that select it;
2. the standard-font fixture reports `Program = None`, `IsEmbedded = false`,
   `Encoding = "WinAnsiEncoding"`;
3. the `Type0` fixture resolves to `CIDFontType2` and reports `HasToUnicode = false` — the case that
   explains "no text came out";
4. one font selected by many pages yields one entry carrying every page number, ascending;
5. an encrypted document opened with its password reports what its decrypted twin reports; without the
   password it returns no font plus the `EncryptionNotOpened` warning, never an exception;
6. a font reference that dangles yields `FontUnreadable` and does not fail the read;
7. a font with no `/FontDescriptor` yields `FontDescriptorMissing` and `IsEmbedded = false`.

**Docs**: a new chapter rather than an edit — `docs/manual/src/26-fonts.md` (appending keeps every
existing cross-reference valid), then rebuild the manual with `tools/SharpCorePDF.Manual` so the contents
page, footer and bookmarks stay in step; a `docs/parity.md` row; one `README.md` bullet under inspection;
`CHANGELOG.md` `[Unreleased] → Added`.

**Fixtures**: if they are generated rather than authored by hand, add them to
`docs/tools/make_parity_fixtures.py` and record provenance and licence in `docs/parity/README.md`, which
the corpus integrity test reads. A hand-written fixture is allowed and is often clearer for a font
dictionary, but it must be added to `Assets/parity/manifest.json` like the others.

### P3 — The 100 MB read path: measure, then decide (roadmap 1.1) → `1.7.0`

**Why this phase exists, and why it is last of the technical work**: the feedback asks for
`System.IO.Pipelines` and `ReadOnlySequence<byte>`, but the engine's read is *random access by offset* —
the cross-reference table names byte offsets and object streams are decoded from ranges. A pipeline is
forward-only and its memory is invalidated by `Advance`. Adopting it wholesale would mean buffering
everything anyway, which is the cost the feedback wants removed. So this phase measures the real envelope,
removes the gratuitous copies, and lets a written decision — not a preference — choose the buffer model.

**P3-T001 — measure first.** Build a 100 MB fixture (repeat a 1 MB document, or give
`docs/tools/make-fixtures.py` a `--large` switch) and record in `docs/measurements.md`, with
`DOTNET_gcServer=1` in an isolated process the way every other figure there was taken:

| Scenario | Numbers to record |
| --- | --- |
| open + `InspectAsync` of the 100 MB file | wall time, **total allocated bytes**, **peak working set**, retained heap |
| merge of 10 pages out of it | the same four |
| `IPdfLayoutReader` extraction of page 1 | the same four |

The gap between "source size" and "allocated bytes" is the number the roadmap is actually about. That gap
becomes P3's budget: no `1.5.0` row may regress by more than 10%, and the 100 MB allocation figure must
fall by a stated fraction.

**P3-T002 — enumerate the copies that exist.** The read path is `IPdfContentSource.OpenReadAsync` → the
document bytes (`byte[]` in `PdfFileReader`), with `PooledByteBuffer` and `PdfOutputBuffer` on the write
side. List each one with its size and whether it is avoidable:

- `PdfFileSource` / `PdfMemorySources` — does a stream source copy into a `byte[]`, and is that
  `GetBuffer()`-shaped or `ToArray()`-shaped? (Feature `004` established the pattern: alias the buffer
  instead of copying it.)
- `PdfDocumentReader` between the source and `PdfFileReader`;
- `PdfOutputBuffer` growth during a merge;
- `PdfIncrementalWriter`'s copy of the prior revision — the one genuinely append-only candidate.

**P3-T003 — write `docs/adr/0004-buffer-model.md`** with three options, the measurement beside each, and a
recommendation:

- **A. Status quo plus targeted copy removal** — keep `byte[]`, stop materialising where a stream already
  holds the bytes. Cheapest, no new abstraction, and it addresses most of the allocation.
- **B. A `ReadOnlySequence<byte>` seam** — an internal `PdfByteSource` (multi-segment, pooled) with the
  lexer and scanners reading through `ReadOnlySequence<byte>`/`ReadOnlySpan<byte>`, keeping random access
  by mapping an offset to a segment. This delivers the feedback's intent without lying about the access
  pattern; it is also the expensive option and must be justified by P3-T001, not by the roadmap's wording.
- **C. `System.IO.Pipelines` for the append-only paths only** — the incremental writer's byte copy and a
  pre-pass that locates `startxref`/`%%EOF` before the random-access read begins. No package is added
  (in-box on both legs), and the risk stays confined.

The ADR states which option the repository will do and what would make it revisit the choice.
**Do not implement B or C before the ADR is merged.**

**P3-T004 — implement what the ADR chose**, as its own numbered tasks, each with a test that fails on
revert. If the answer is A, the phase is a short series of copy removals plus one allocation test per
removal (`GC.GetAllocatedBytesForCurrentThread` around a read, the technique `docs/measurements.md`
already describes).

**Guardrails for this phase**

- No new package, no native code, no change to `PdfEngineOptions.MaxSourceBytes` semantics (principle I).
- Reader behaviour does not move: same `PdfDocumentInfo`, same warnings, same `PdfSourceFormatException`
  cases, and the determinism gate (byte-identical output at a fixed date) still holds afterwards.
- **Do not touch `SimdByteScan`** — the vectorised search the feedback asks for is the code that already
  exists, and `CHANGELOG.md` `[1.5.0]` records which vectorisations lost.
- The 100 MB fixture is never committed (`.gitattributes` keeps PDFs binary, and a 100 MB blob is a
  repository problem). Generate it in the run and record the generation command.

### P4 — One-call diagnostics (roadmap 3.1, remainder) → `1.7.0`

**Why**: the feedback's DX ask is "inspect before you edit". Inspection, the object graph, layout and
signature validation already answer it in five calls; what a support engineer wants is one report.

**Shape** — a read-only summary assembled from the existing use cases, so it cannot drift from them:

```csharp
public sealed record PdfDiagnosticsReport
{
    public required PdfDocumentInfo Document { get; init; }
    public IReadOnlyList<PdfFontInfo> Fonts { get; init; }                  // P2
    public IReadOnlyList<PdfSignatureValidation> Signatures { get; init; }   // existing validator
    public PdfAConversionResult? Archival { get; init; }                     // existing pre-flight
    public IReadOnlyList<PdfWarning> Warnings { get; init; }                 // merged, de-duplicated
}
```

**Design constraints**

- `IPdfInspector` gains **nothing**: a new `IPdfDiagnoser` (Domain port) + `PdfDiagnoserService`
  (Application, depending on `IPdfInspector`, `IPdfFontInspector`, `IPdfSignatureValidator` and the
  archival pre-flight) keeps every existing implementor compiling — the same reasoning as
  `PdfContentReaderExtensions`' extension-block comment.
- Read-only and redaction-explicit: the report names sources and describes structure, and it never
  returns stream contents. Record that in the record's XML docs.
- A console that prints it (text and JSON) lives in `tools/`, never in `src/` — the constitution excludes
  a CLI *inside* the library. `tools/SharpCorePDF.Measure` is the precedent for a tool project.

**Tests**: a clean document yields no findings; a damaged one surfaces `CrossReferenceRebuilt`; an
encrypted one opened without a password surfaces `IsReadable = false` plus `RequiresPassword` and does not
throw; a PDF/A candidate surfaces the archival requirements; a null source is refused before the source is
opened. One test asserts the merged warning list carries no duplicates when two sub-reads report the same
kind.

### P5 — PDF/A beyond the current claim (decision gate, not scheduled)

Only if the maintainer asks for it. `PDF/A-2u`/`-3u` add a Unicode requirement (`/ToUnicode` on every
font) and `PDF/A-4` moves the version base; each needs its own member in `PdfAProfile`, its own rules in
`PdfAProfileRules`/`PdfARequirements`, a veraPDF run recorded in `docs/parity.md`, and a manual section.
Principle III and the parity matrix are explicit that the library never claims conformance itself — so the
deliverable is *a file veraPDF accepts*, not a claim. Do not start without that evidence path agreed.

---

## 4. Gates every phase clears (the repository's own, restated)

Copied from `specs/003-pdf-platform-parity/plan.md` §Release Gates and trimmed to what a phase touches:

| Gate | Test |
| --- | --- |
| G1 Boundary | no fifth assembly, no new package, no CLI in `src/`; Domain still references only the BCL |
| G2 Surface | every new public member is documented, in `PublicAPI.Unshipped.txt`, and covered by a contract test; no shipped signature changes |
| G3 Evidence | each new behaviour has a named test **and**, where an external authority exists, a command that was actually run (`qpdf`/`pikepdf`, pyHanko, veraPDF) |
| G4 Quality | zero warnings on both legs; full suite green on both legs; new code at the 90% condition of `docs/adr/0002-new-code-coverage-1.1.0.md` |
| G5 Determinism | fixed-date output byte-identical across repeated runs; P3 must not disturb this |
| G6 Performance | no `1.5.0` row in `docs/measurements.md` regresses by more than 10%; new scenarios get a recorded budget before the code is written |
| G7 Documentation | the manual chapter, the example and `docs/parity.md` are updated together; example code compiles and runs |
| G8 Release | `CHANGELOG.md` entry exists before the tag; `ci.yml` is green on the analysed commit; `publish.yml` re-reads that gate |
| G9 Mutation | a scoped Stryker run over the touched files, survivors read and explained in `docs/mutation-testing.md`'s next record |

## 5. The exact commands this plan was written against

```powershell
# build (both legs, zero warnings is the bar)
dotnet build .\SharpCorePDF.slnx -c Release

# tests, one leg at a time (the net10.0 leg needs a RID)
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64

# the AOT claim, with the assert-the-absence switch
dotnet publish .\examples\SharpCorePDF.AotSmoke -c Release -r win-x64
# then run the binary with expect-aot

# external authorities (run through uv, as the README shows)
uv run --with pikepdf  python .\docs\tools\validate-pdf.py <file.pdf>
uv run --with pyhanko  python .\docs\tools\validate-signatures.py <signed.pdf>
# veraPDF for the archival profiles, command recorded in docs/parity.md

# the public surface snapshot
pwsh .\docs\tools\update-public-api.ps1

# mutation testing, scoped to the files a phase touched
$sdk = (& dotnet --version).Trim()
$env:MSBUILD_EXE_PATH = "$env:ProgramFiles\dotnet\sdk\$sdk\MSBuild.dll"
dotnet stryker --config-file stryker-config.json `
  --project .\src\SharpCorePDF.Infrastructure\SharpCorePDF.Infrastructure.csproj `
  --mutate "**/PdfPageCanvas.cs"
```

`StrykerOutput/` and `artifacts/` are ignored; a report is never committed.

---

## 6. Risks and open questions

| # | Risk / question | Mitigation or owner decision |
| --- | --- | --- |
| R1 | A future contributor reads the roadmap as a to-do list and rebuilds PDF/A or signatures | P0's `docs/roadmap-gap-analysis.md` plus this document's §1 table, linked from the top of the roadmap issue |
| R2 | P3 turns into a multi-week rewrite of the reader for a number nobody measures | The ADR is a merge gate; option B is only allowed with P3-T001's figures and a stated target |
| R3 | Font inspection duplicates the PDF/A font walk, and the two drift | P2-T005 extracts the lookup before the inspector is written, and the same helper feeds both |
| R4 | New `PdfWarningKind` members are matched with a `switch` somewhere that has no `default` | The enum's own XML docs already say the set is open and a `switch` needs a `default`; the new kinds are additive only |
| R5 | A canvas `SetAlpha` is mistaken for the PDF/A "transparency" fix | P1's test 6 pins the refusal, and the manual says PDF/A-1b forbids transparency |
| R6 | Manual chapter order drift breaks the built PDF | `docs/README.md` and the constitution both require a rebuild after a chapter is added; the chapter is appended as `26-…`, not inserted |
| Q1 | The roadmap's category 3 is truncated after `GetEmbeddedFonts()` | Maintainer to paste the remainder; P4 is scoped from what exists today |
| Q2 | Is PDF/A-2u/-3u/-4 wanted at all? | Maintainer decision; P5 is a gate, not a commitment |
| Q3 | Does "100 MB+" mean a single document or a batch of documents? | If a batch, P3's budget is about concurrency and pooling and the spec changes shape |

## 7. Ordering, and the first thing the next AI does

Ship order, with the release each phase belongs to:

```text
1.6.0   P0 (evidence)  →  P1 (canvas transparency)  →  P2 (font inspection)
1.7.0   P3 (measure → ADR → implement)  →  P4 (one-call diagnostics)
2.0     P5, only on a maintainer decision, and only with veraPDF evidence
```

P1 and P2 are independent of each other and of P3, both are additive, and together they answer every item
in the feedback that is genuinely missing — which is why they are the cheapest credible 1.6.0.

**Step 1 (ten minutes)**: `dotnet build .\SharpCorePDF.slnx -c Release`, then
`dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0`. If either is
not green on a clean tree, stop and fix that first; every task in this plan assumes a green baseline.

**Step 2**: copy the §1 table into `docs/roadmap-gap-analysis.md` and fill the evidence cells with P0's
real output. Commit that alone — it is the document that keeps the next reader from rebuilding the
library.

**Step 3**: write `specs/006-canvas-transparency/spec.md` from §P1 and implement only P1. Do not merge P1
and P2 into one pull request: they touch different layers and a reviewer needs to hold one constitution
check in their head at a time.
