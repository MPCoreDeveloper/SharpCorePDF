# Changelog

What changed, from the point of view of someone who has to decide whether to install this version.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and the version numbers
follow [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html). A version gets an entry here
before it is tagged, not after: `docs/release-checklist.md` G3 refuses a release whose notes do not exist,
and a version with no notes is a version a consumer cannot evaluate.

**`1.0.0-preview.1` was published to nuget.org on 2026-10-15**, as four packages — `SharpCorePDF.Domain`,
`.Application`, `.Infrastructure` and `.DependencyInjection` — pushed by `publish.yml` from tag
`v1.0.0-preview.1`. The `SharpCorePDF` entry point did not exist in that tree, so the only id on the feed
that a consumer could have installed was a layer. `1.0.0-preview.2` and `1.0.0-preview.3` were bumped here
and never pushed, which is why this file keeps no entries for them: there is no consumer of a version no
feed ever served.

`1.0.0` is therefore the first version that changes the *shape* of the install rather than only the
contents of the layers, and the entry below is written against the published `preview.1` rather than
against an empty feed.

## [Unreleased]

Nothing yet.

## [1.7.0] - 2026-11-14

### Added

- **Stationery: a background template per page range, with a default.** `PdfStationery` maps a template
  to a `PageRange` and can name a default for the pages no assignment covers (`Assign`,
  `AssignDefault`, `AssignNone`), and `PdfEditPlan.ApplyStationery(...)` records the whole plan as one
  operation. The template is recorded as the underlay layer the editor already writes — a form
  XObject — so the page's own content streams keep their bytes, vector graphics and selectable text
  survive, and nothing is rasterized. Assignments are tried last first, a one-page template repeats
  across its range, a multi-page template maps one page for one page, and a range that reaches past it
  is refused. `PDF/A-1b` refuses a translucent or blended stationery. The manual's chapter 14 has the
  section and `PdfDocumentEditorTests.Stationery*` has the tests, including one per acceptance
  criterion of #6.

## [1.6.0] - 2026-11-14

### Added

- **The archival output is validated by CI, against a pinned validator.** `compliance.yml` installs veraPDF
  1.30.4, writes the `archive-pdfa` documents and validates each one against the profile it was written for —
  the authored page and, through `SCPDF_ARCHIVE_SOURCE`, the six-page `SafeWebCore.pdf` fixture — then uploads
  the machine-readable reports. A validator below the pin exits 2 through `--require-version` instead of
  answering, so a stale rule set cannot report a pass this repository no longer stands behind. The run is
  repeated weekly because a validator upgrade changes the answer without this library changing a byte.
- **`validate-pdfa.py` reads the report instead of the word PASS.** `--format xml` takes the verdict from the
  `isCompliant` attribute of veraPDF's machine-readable report, `--report-dir` keeps that report per document,
  and `--require-version` refuses a validator older than the one the repository ships against. The text mode a
  person runs by hand is unchanged.
- **The `archive-pdfa` example can archive a document it did not write.** `SCPDF_ARCHIVE_SOURCE` names a file,
  and the example converts it for all three profiles beside the authored page — the same environment-variable
  pattern the signature example uses for its timestamp token, because no redistributable real-world PDF belongs
  in the repository. On 2026-11-13 all six documents reported `PASS` under veraPDF 1.30.4.
- **Two more implementations read the same documents.** `validate-pdfa-cross.py` puts every archival file
  through Apache PDFBox preflight and Ghostscript beside veraPDF. PDFBox shares no code with veraPDF and
  validates PDF/A-1 on its own, so `1b` gets a second verdict while `2b` and `3b` are reported as `parsed`,
  never as judged — PDFBox 3.0 does not implement those profiles and the tool does not pretend it does.
  Ghostscript rewrites the document as archival output from what it understood, the evidence that the file is
  not readable only by the tool that wrote it. `compliance.yml` runs the cross-check with `--strict-tools`, so
  a missing implementation fails the run rather than narrowing it silently, and a `2b` file judged as `1b` is
  now rejected by two independent implementations.
- **The archival output intent can be the caller's own ICC profile.** `PdfAConversionOptions.OutputIntent`
  takes a `PdfOutputIntent` — the profile bytes, the number of components and the condition identifiers —
  and the writer states it instead of the built-in sRGB one. The bytes are written verbatim, and they are
  checked first as what they claim to be: the ICC header's declared length, the `acsp` signature at offset 36
  and a data colour space that agrees with the declared component count, so a caller who passes a JPEG or a
  CMYK profile labelled as RGB is refused before a byte of the result exists. The pages are not converted
  into that colour space; the intent states what they are meant to be read in. A 6-page real-world document
  written with the sRGB program from a Windows workstation, under its own condition identifier, reported
  `PASS` under veraPDF 1.30.4, and the bytes in the result are exactly the bytes that went in.
- **PDF/A-3b output associates its embedded files, as ISO 19005-3 requires.** The catalog gains an `/AF`
  array, each entry of the `EmbeddedFiles` name tree and its association name the same file specification
  object (the tree is flattened so both point at one object), and every specification states an
  `/AFRelationship`. A file that already states a relationship keeps it, because the source's claim is
  evidence this writer does not have; one that states none takes
  `PdfAConversionOptions.AssociatedFileRelationship`, which defaults to `/Unspecified` rather than inventing
  a meaning, and may be set to `/Data`, `/Source`, `/Alternative` or `/Supplement`. A relationship set beside
  any other profile is refused, because those profiles forbid embedded files. A one-page document carrying a
  `text/plain` attachment, written with `/Alternative`, reported `PASS` under veraPDF 1.30.4.
- **PDF/A-2u and PDF/A-3u output.** A level-U document is a level-B one whose every glyph has a Unicode
  equivalent, and the writer already states a `ToUnicode` map for every font it subsets — so the level is a
  statement in the packet, not a different file. A measurement came before the code: a 2b and a 3b result
  whose `pdfaid:conformance` said `U` both reported `PASS` under veraPDF 1.30.4, for an authored page and for
  a six-page document written by "Microsoft: Print To PDF", which is the case that would have failed if the
  copied fonts had no mapping. Both profiles are now written end to end, `ArchiveExamples` writes all five,
  and `compliance.yml` validates and cross-validates every one of them. Level A, which needs tagged
  structure, is still out of scope.

- **A page can state an opacity and a blend mode for what it paints next.** `PdfPageCanvas.SetAlpha(alpha,
  blendMode)` writes the pair as one `/ExtGState` (`/ca`, `/CA`, `/BM`) and selects it with `gs`, so a
  stretch of text, an image or a whole group of shapes can fade or blend without giving each one an opacity
  of its own. The state is written once per distinct pair and restored by `SaveState`/`RestoreState`; a paint
  that states an alpha of its own still wins over the selection, which is the rule an explicit paint already
  followed for colour. Ten tests in `PdfPageCanvasTransparencyTests` pin the operator, the deduplication,
  the blend name, the restore, the guards and the interaction with the archival profiles: PDF/A-1b has no
  transparency and the existing pre-flight refuses the page for that profile, while PDF/A-2b keeps it. The
  manual's drawing chapter gained *Transparency and blend modes*, the `draw-shapes` example paints one panel
  through a multiplied state (`qpdf check: no problems`, `/ExtGState` with one entry per distinct pair per
  `docs/parity.md` row 2), and the new measurement rows live in `docs/measurements.md` §10.

- **A document's fonts can be read without opening them.** `IPdfFontInspector.ReadFontsAsync` answers which
  fonts a file uses and whether each program travels with it: one entry per font, keyed by the object its
  resource names, carrying the kind (`Type1` … `CIDFontType2`), where the program lives (`/FontFile`,
  `/FontFile2`, `/FontFile3` or a Type 3 font's own streams), the encoding the document states, whether a
  `/ToUnicode` map is present, whether the name carries the six-letter subset tag, and the one-based pages
  that select it. A font selected by two hundred pages is one entry with two hundred page numbers. A resource
  that does not resolve is reported as `FontUnreadable` and a font that states no descriptor as
  `FontDescriptorMissing` — the second is not a defect, because a standard font may legally rely on the one
  the reader already has. The walk follows a form XObject's own resources at depth, honours
  `PdfEngineOptions.MaxNestingDepth` and opens an encrypted document with its password instead of reporting
  it closed. The descriptor, kind, encoding and subset rules moved into `PdfFontDescriptorLookup`, which the
  PDF/A pre-flight now shares with the new inspector rather than keeping a second copy. Twenty tests in
  `PdfFontInspectorTests` cover every shape, the form case, the real-world case and both encryption
  outcomes; the manual gained chapter 26.

### Changed

- **A merge and an edit read each source once instead of twice.** The application layer inspected a source to
  resolve the page selection and then handed the same stream to the engine, which read the whole document a
  second time; on a 100 MB file that was 100 MB of extra garbage per operation, and it is what
  `docs/measurements.md` §1 attributed to "the output copy" — an explanation that could not hold for a partial
  selection, whose output here is 0.9 MB. `IPdfDocumentBytesReader` hands the inspected bytes on, the use cases
  prefer it when their reader implements it, and the engine's existing owned-bytes fast path turns the second
  read into an aliasing read. Measured on the same 100 MB document: a merge of ten of its pages allocated
  **204.2 MB → 104.2 MB** (2.04× → 1.04× input) with the same 931 273 B of output, and the process peak
  working set fell from 955.6 to 856.3 MB. `IPdfDocumentReader` is unchanged, so a third-party reader still
  compiles and still pays the second read; `SourceReadOnceTests` pins both behaviours with a source whose
  stream can be drained only once. The decision and the options that were rejected are recorded in
  [`docs/adr/0004-buffer-model.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/adr/0004-buffer-model.md).
- **Configuration registration is trim- and Native AOT-compatible.** `AddSharpCorePDF(IConfiguration)`
  binds `PdfEngineOptions` with ordinary property reads instead of the framework binder's reflection and
  runtime-generated converters, so the overload no longer carries `[RequiresUnreferencedCode]` or
  `[RequiresDynamicCode]`. An absent section still keeps the defaults; an unparsable value keeps that
  member's default. `examples/SharpCorePDF.AotSmoke` now exercises default, explicit and configuration
  registration and publishes with zero IL2026/IL3050 warnings. `docs/aot.md` records the evidence.

### Not in this version

Named here so the next version starts from a list rather than from memory. Each one is a capability, not a
defect: nothing below is claimed by the packages above.

- **Level A, and PDF/UA.** PDF/A-1a, -2a and -3a need a tagged structure tree and the accessibility semantics
  that go with it; PDF/UA is a different standard again. The writer states level B or U; a caller who needs a
  tag tree has to add one, and no part of this release pretends otherwise.
- **A PDF/A target on the merge path.** `MergeOutputOptions` has no profile, so an archive that merges first
  and converts second reads every source twice — once to merge, once to convert. The conversion is the only
  entry point that writes archival structures (`IPdfAWriter`), and a merge-time profile is a design of its
  own: the merge engine and the conversion share the writer but not the analysis.
- **A caller-supplied output intent on an ordinary edit.** The intent is a conversion option. A
  `PdfEditOutputOptions.OutputIntent` with a PDF/A profile works, because that route goes through the same
  writer, but a plain edit copies whatever the source carried and adds none.
- **Colour conversion.** The writer states the intent a caller supplies and never converts a page into that
  colour space. A CMYK profile on pages painted in device RGB is a finding a validator reports, not something
  the library repairs — by design, because a silent conversion changes what the page looks like.
- **Per-page and per-object associations.** PDF/A-3 allows an embedded file to be associated with a page or a
  structure element, not only with the document. This release writes the catalog-level `/AF` array and only
  that.
- **A corpus of real files.** The gate runs two documents through three implementations on every push: an
  authored page and one file a producer wrote. The next step is breadth — customer files, one per profile and
  per producer — because a document that fails is worth more than a rule that passes.
- **PDF/X.** Prepress exchange is a separate standard with its own colour and box rules. It stays out.
- **Ghostscript locally.** `validate-pdfa-cross.py` reports it as `n/a` when it is not installed and only
  fails under `--strict-tools`, which is what CI passes. A workstation without it gets two implementations
  instead of three, and the run says so.

## [1.5.0] - 2026-11-06

### Performance

- **The scan allocates less per letter, and two more bulk scans are vectorised.** A word's text is built
  once at its final length with `string.Create` instead of a `StringBuilder` per word; the glyph list is
  sized from the first page's count so it stops doubling; the search for `endstream` walks the stream a
  vector at a time through the same `SimdByteScan` kernel the inline-image search uses; and the PNG `Up`
  predictor is a vector add. Measured on the 200-page fixture under server GC in an isolated process:
  warm extraction 58.8 → 38.3 ms and allocations 33 183 → 29 557 KiB; on the locked 13-document external
  corpus the median open+extract fell from 36.1 to 21.9 ms with the same 1 852 569 letters on both
  engines. A new `extract-1-inline-image-1mb` row — the first scenario that contains a `BI`/`EI` image —
  reads a 1 MiB inline image in 0.7 ms against PdfPig's 8.2 ms.

- **A session keeps the glyphs of its two most recent page ranges.** A caller that asks the letters and
  then the words of the same pages now walks the content once and groups it twice; the second answer costs
  a grouping pass instead of a page walk. On the new `text-200-two-questions` row a session reads in
  31.3 ms and allocates 41 MB against PdfPig's 89.3 ms and 109 MB, and the same build with the cache
  bypassed allocated 7.9 MB more — exactly one glyph list.

- **The scan allocates less and runs faster on the work a reader does.** Seven changes, each measured
  on the same 200-page fixture with `DOTNET_gcServer=1` in an isolated process: a number is parsed in
  place with `Utf8Parser` instead of being copied into a string first (the three scanners and the
  token lexer); a literal string that needs no decoding is a slice of the stream rather than a list and
  a copy, and one that does is decoded into a pooled buffer; a hexadecimal string is one array of the
  right size rather than a growing list and an array copied out of it; an operator keyword comes from a
  cached set keyed by its packed bytes instead of a fresh string per operator; a `TJ` item is a struct
  rather than a boxed `object`; the two base-encoding tables are built once and copied only by the one
  caller that changes them (`/Differences`); and the word grouping reads a range of the glyph list
  instead of copying a page at a time. Extract allocations fell from 35 028 to 33 183 KiB (−5.3 %),
  median cold extract from 75.2 to 57.8 ms (−23 %) and warm from 68.8 to 58.3 ms (−15 %) over five
  runs; the external corpus still reads **1 852 569 letters on both engines**, so none of it moved a
  byte of text.
- **The vector byte scan is used where a run is bulk, and was measured out of where it is not.** A new
  internal `SimdByteScan` finds a byte with AVX2 (32 bytes), SSE2/NEON (16 bytes) or a scalar loop, in
  the shape this team uses elsewhere: a platform check, one kernel per instruction set, a scalar tail.
  Applied to the whitespace and keyword scan that runs before every token it was **slower** — 75.2 to
  97.6 ms — because those runs are one to three bytes and a 32-byte compare of six to sixteen values
  costs more than the three byte branches it replaces. That use was reverted, the set-shaped kernels
  written for it lost their last caller and were removed with it, and the single-byte kernel stays for
  the inline-image `EI` search, where the run between `ID` and `EI` is hundreds of kilobytes. The lesson
  is recorded in `docs/mutation-testing.md` rather than remembered: vectorising a per-token decision is
  a loss.

### Changed

- **The external corpus the scoreboard cites is larger, and the numbers are fresh.** Two documents join it: a
  linearised copy of one arXiv paper (`qpdf --linearize` through `pikepdf`) and an image-only A4 page the
  library writes itself, standing in for a scan — the two shapes the earlier sample did not contain. Both
  engines read 13 of 13 files, the letters are still identical to the character (1 852 569 on each), the
  linearised copy reads the same 53 921 letters as its original with no warning, and the image-only page
  yields no text on either engine and raises nothing, so "this page has no text" stays distinct from "this
  page could not be read". `docs/competitive-scoreboard.md` carries the new lock hash
  (`fca6dd17…5dab5b`), the refreshed medians and the release-cite table re-measured with
  `DOTNET_gcServer=1` in isolated per-engine processes.

### Fixed

- **A font that states `MacRomanEncoding` is read.** The reader knew `WinAnsiEncoding` and
  `StandardEncoding` and reported any other set as unreadable rather than guess; a Mac-written PDF found by
  the wider external corpus therefore read as no text at all. The Mac OS Roman table is now built once
  beside the other two — including the `ﬁ`/`ﬂ` ligatures, the typographic quotes and the dashes — and the
  file that found the gap reads 2 432 letters, the same count PdfPig reports. `MacExpertEncoding` remains
  outside the claim and is still reported rather than approximated.

- **A PAGE XML export is schema-valid.** The first run of the schema check the parity matrix asks for
  (ALTO 4.2 and PAGE XML 2019-07-15 against the published `.xsd` files with `lxml`) rejected the PAGE
  document: its `<Metadata>` stated a creator and neither of the two times the schema requires. `Created`
  and `LastChange` are now written at a fixed instant - the export stays byte-for-byte reproducible, which
  another test already pins - and both documents validate. `docs/parity.md` row 16 moves to Evidenced with
  the run recorded and the new test named.

### Added

- **The packages are trim- and Native AOT-compatible, and one overload says when it is not.** Every
  assembly declares `IsTrimmable` and `IsAotCompatible`, and the build is clean under the trim, AOT and
  single-file analyzers: the reader, the writer and the merge engine use no reflection, no dynamic code
  and no serialization of their own types. `examples/SharpCorePDF.AotSmoke` proves it — it authors a page,
  merges two documents through the container and reads the text back, and its `expect-aot` switch fails the
  run when the published binary still supports dynamic code. The configuration-binding overload
  (`AddSharpCorePDF(IConfiguration)`) uses the framework binder and is annotated
  `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`, so a trimmed or AOT publish reports it at build
  time; an AOT application passes `PdfEngineOptions` in code. `docs/aot.md` records the evidence.

- **A page can paint in a colour space the caller names.** `PdfPageCanvas.SetFillColor` and `SetStrokeColor`
  write the selection set — `cs`/`CS` then `scn`/`SCN` — and `FillPath`/`StrokePath` paint with what they
  selected, writing no colour operator of their own; that pair is what makes a separation, a device-N space or
  a pattern reachable, because a `PdfPaint` carries a device colour and every other drawing call states its
  own paint. An explicit paint still wins over a selection, a name a PDF name cannot state (`#`, a delimiter,
  whitespace, or nothing after the slash) is refused at the call site, and so is a colour with no component
  or one that is not finite. This closes the last `Partial` row of the creation parity matrix: `docs/parity.md`
  row 2 is now Evidenced with six new tests and a `validate-pdf.py` run over a page whose content stream is
  `/DeviceCMYK cs`, `scn`, `f`, `/DeviceGray CS`, `SCN`, `S` (`qpdf check: no problems`).

- **The harness reports informational notes apart from warnings, so a decrypted document no longer looks
  damaged.** A file that is encrypted with an empty user password is read in full; the remark saying so was
  being counted as a warning, which put the external corpus's last remaining "warning" in the wrong column.
  `ExtractionObservation` and `RobustnessResult` now carry a note count (the encryption remark), perf rows
  print `notes=` when there are any, and the robustness table has its own Notes column. The eleven-document
  external leg therefore reads **0 warnings** for SharpCorePDF with one note, and 1 798 648 letters on both
  engines.

- **A Type1 font's own encoding is read when the document states none, so LaTeX formulas are no longer
  lost.** A pdfTeX subset of a Computer Modern math or symbol font states neither `/Encoding` nor
  `/ToUnicode`: what its codes mean exists only in the clear-text header of the embedded Type1 program, as
  a run of `dup <code> /<glyph> put` lines or a `StandardEncoding def` statement. That header is now read
  (the encrypted part of the program still never is), and the glyph table now knows the mathematical and
  Greek names these fonts use. The gap was found by the external-corpus leg — 16 glyphs and eight
  `TextUnreadable` warnings on one arXiv paper — and closed to zero: both engines now read 1 798 648
  letters on the same eleven documents. The manual's claim that the reader never consults an embedded font
  program is corrected accordingly, and a fixture pins the precedence: a dictionary that states an
  encoding keeps it.

- **The first external-corpus leg is recorded, not just the synthetic one.** Eleven public-domain documents
  (IRS forms and Publication 15, USCIS AcroForms, two arXiv LaTeX papers, NIST SP 800-53r5) were fetched
  locally, pinned by a SHA-256 manifest and scored through the hash-locked robustness leg by both engines:
  11/11 completed for each, no errors or timeouts, median open+extract 36.5 ms (SharpCorePDF) against
  157.2 ms (PdfPig), and identical letter counts on ten of eleven files — the exception being 16 glyphs
  of a 54 000-letter paper where SharpCorePDF reports 8 warnings. Word counts stay non-comparable across
  engines, as they are everywhere else. The corpus and its manifest live under gitignored `artifacts/`;
  the harness still downloads nothing.

- **Row-band order and the segmentation interplay it lives in are pinned by tests.** A three-row
  two-column fixture shows the row-band read (`Alpha, Beta` before `Delta, Charlie`), the same page read
  column by column under the default, and a raw segmentation that keeps each drawn string a block; a
  touching row whose left edge contradicts the row above proves the band comparison is strict. The
  fixture also documents that the default segmentation merges tightly-led rows of a column into one block
  by leading, which is why the boundary needs the raw segmentation to be observable at all.

- **A row-band reading order, for documents known to be tables.** `PdfReadingOrderStrategy.RowBands` reads
  a page as horizontal bands — the blocks that overlap vertically, left to right, before the band below
  them — which is the order a table's cells are read in. The labelled table now reads `Item, Quantity,
  Price` then `Widget, 2, 10.00` under it, and a corpus test pins that. The default stays `ColumnAware`:
  the same order applied to a two-column page would interleave its columns line by line, so the previous
  characterisation of the default remains correct and is kept as the record of what the default does.

- **Block segmentation is a measured row, and one engine per process is the cite recipe.** The harness now
  compares each engine's Docstrum as a block producer (`text-200-all-blocks-docstrum`: equal output of
  200 blocks and 16 400 words on the fixture) and accepts `--engine <name>` so a release-cite run puts one
  adapter in a process of its own. Under `DOTNET_gcServer=1` with that isolation the warm extraction rows
  sit within a few milliseconds of each other while cold extraction and block segmentation lead. A
  deterministic local robustness sample can be rebuilt from repository assets with
  `scripts/sample-corpus.ps1`, which writes its SHA-256 lock before scoring it (synthetic by design, never
  presented as an external corpus). Harness and docs only — no product package changed. See
  `docs/competitive-scoreboard.md`.

- **A convention-free box overlap column in the competitive scorer.** The labelled scoring now reports
  intersection-over-union per matched character beside the label-convention agreement, so a coordinate
  convention and a miss are distinguishable: on the current corpus PdfPig scores 0% against the label
  baseline boxes but 40.83% under IoU while SharpCorePDF scores 100% on both. Alternates are measurable
  too: `text-200-all-nearest-neighbour-alt` is a named opt-in row per adapter, and the external
  robustness leg accepts a SHA-256 manifest (`--robustness-lock`, written once with
  `--write-robustness-lock`) that reports a changed file as `LockMismatch` without opening it. Harness
  only — no product package changed. See `docs/competitive-scoreboard.md`.

- **A reusable layout session.** `IPdfLayoutReader.OpenAsync` returns an `IPdfLayoutSession` that keeps one
  parse open across letters, words, blocks, hyperlinks and exports. One-shot members are unchanged. The
  competitive harness measures cold extract and a named `text-200-all-warm` row so session reuse is visible
  beside PdfPig's single open.

- **A competitive measurement harness, outside the product packages.**
  `benchmarks/SharpCorePDF.Competitive` restores pinned PdfPig 0.1.16 and PDFsharp 6.5.0 behind a
  separate solution and lock file, never referenced by `SharpCorePDF.slnx`, `src/` or the unit tests.
  It scores labelled extraction, timed text/create/merge scenarios with equal-work summaries, optional
  local-corpus robustness, and captured writer output for `validate-pdf.py`. Methodology and the first
  local baseline live in `docs/competitive-scoreboard.md`; licence notes are in
  `benchmarks/SharpCorePDF.Competitive/THIRD-PARTY.md`. Reports are gitignored under
  `artifacts/competitive/`.

### Changed

- **The competitive harness measures steady state, and it now has a row for the vector scan.** Every
  scenario is executed once per engine before any warm-up is taken, because the first call of a method
  pays tiered compilation: PdfPig's cold and warm rows run the identical code path and read 181.6 and
  47.8 ms in the previous report. The per-letter observation the harness builds is a struct rather than a
  class, so the allocation column is closer to the engine's own number, and the report's method limits say
  what it still includes. `extract-1-inline-image-1mb` is the first scenario with a `BI`/`EI` image, so the
  vector byte scan is measured rather than assumed.

- **Text advance is computed once per glyph, and character/word spacing and Tz are now asserted.** The
  text walk derives the em-scaled width once and shares it between the glyph box and the advance, and
  tests a space without a string comparison; the measured effect is nil on the competitive fixture
  (allocation medians byte-identical, wall time inside its noise band), so this is a clarity change, not
  a speed one. What it surfaced is real: no test pinned <c>Tc</c>/<c>Tw</c>/<c>Tz</c>, so the collector
  scored 41.9% under mutation; a spacing fixture and test (`Moves_letters_by_the_character_word_spacing_and_scaling_it_is_drawn_with`)
  now pin the arithmetic at 100/104/110 points and the collector scores 46.6%.

- **The glyph walk no longer copies its own output.** Pages append straight into the read's glyph list
  (no per-page list plus `AddRange`), and the result of a glyph walk is handed over as built instead of
  being copied into a defensive list. Extract allocations fell again (36 → 34 MB on the competitive
  fixture, ~36% below PdfPig); warm full-document extract is within ~1.2× of PdfPig in the latest run —
  `docs/competitive-scoreboard.md` keeps the run-to-run variance note.

- **Word grouping no longer materialises a letter and a rectangle per glyph on the way to a word.**
  `WordExtractor` groups on glyph geometry and reads as a letter only what lands in a word; the word box
  comes straight off the baseline points. One-byte fonts resolve widths through a table lookup instead of
  a dictionary hit per code. Competitive cold extract: ~146 → ~110 ms median; allocations on extract are
  now ~33% below PdfPig while PdfPig still leads that wall-time row — see `docs/competitive-scoreboard.md`.

- **Font state and the text scan are no longer rebuilt per operator or per page.** The fonts a resource
  dictionary names are read once per read — a 200-page document with one shared resources entry decodes its
  /ToUnicode maps and width tables once instead of 200 times — and `PdfTextScanner` yields a single reused
  operation instance instead of allocating one object and three lists per operator. Median cold extract time
  on the competitive fixture moved again (467 → ~146 ms across slices); PdfPig still leads that row.

- **Cold text extraction no longer copies an in-memory source before the page walk.** When the stream is a
  `MemoryStream` that already owns the whole document array (the shape `PdfByteArraySource` hands in),
  `PdfByteReader` uses those bytes as they are. `WordExtractor` groups pages with a linear scan and an
  in-place page-order sort instead of LINQ `GroupBy`/`OrderBy`, and builds each word without per-glyph
  `Select` allocations. On the competitive 200-page cold extract (2 warm-ups / 5 iterations, this machine)
  median wall time dropped about 3× versus the prior smoke baseline while work counts stayed equal;
  PdfPig still leads that full-document row, and the warm session row still trails — see
  `docs/competitive-scoreboard.md`.

- **The README authoring summary no longer denies JPEG.** PNG, JPEG, BMP and GIF are the image formats
  the surface accepts, with the refusals already named in `docs/parity.md`.

## [1.4.0] - 2026-11-04


### Added

- **An image's colour space is read, not guessed from its name, so the spaces and bit depths that used to
  stay payloads now yield pixels.** `DecodedPixels` was `null` for every image whose samples were not already
  one byte per channel of `DeviceGray` or `DeviceRGB` — a bilevel scan, a palette image, a CMYK photograph,
  an `ICCBased` one — even though the payload had been decoded and nothing about it was unknown. The read
  now resolves `/ColorSpace` through the document (`PdfSampleSpaces`), following a resource name, an
  `[/Indexed base hival lookup]` array to the palette it points at — a stream or a string, decoded through
  the same filters as any payload — and an `[/ICCBased profile]` array to the component count its `/N`
  states, and a sample that is narrower than a byte is widened instead of a row being read as a continuous
  bit stream. `PixelFormat` gains a third value, `Cmyk32`: four device samples per pixel with no ink
  conversion, because turning cyan, magenta, yellow and black into light is a rendering decision rather
  than a fact of the file, and neither an ICC profile nor a tint transform is applied anywhere on this
  route. A `Separation`, `DeviceN` or `Lab` space, sixteen bits per sample, and a palette image whose
  indices run past its own table stay payloads, and the record states which one it is. The expansion is
  bounded by the same `MaxInlineContentBytes` budget that governs `Data`, so widening a large image cannot
  allocate past it.

- **An image compressed with LZW is decoded instead of being handed back compressed.** `LZWDecode` sat in
  the list of filters the reader named and passed through, so a scan stored the way TIFF stores one was
  reported with `IsDecodable` cleared and `DecodedPixels` `null` even though the library could already read
  everything about it but the samples. `PdfFilters` now carries a decoder for the coding PDF inherits from
  TIFF — nine-bit codes growing to twelve, the table built as the stream is read, `EarlyChange` honoured —
  so such an image yields its samples on both the read and the copy route, and the
  `ImageFilterUnsupported` warning it used to raise is gone. The same decoder serves object streams, so a
  document that stores one behind LZW now reads instead of raising `PdfUnsupportedFeatureException`, and
  the abbreviated name `/LZW` is accepted wherever `/LZWDecode` is — which it was not before. The one
  convention a decoder can get wrong on its own account is the code at which the field widens, so the tests
  decode a strip libtiff wrote as well as one this repository's own test encoder wrote: a round trip cannot
  catch a rule that is wrong on both sides, and `EarlyChange` exists precisely because a decoder's table is
  one entry behind an encoder's.

### Changed

- **A materialised payload is handed back as a view of the buffer the copy route filled, not as a second array
  of the same size.** `ReadImageDataAsync` and `ReadEmbeddedFileDataAsync` returned `ReadOnlyMemory<byte>` that
  already was the payload, and then copied it with `ToArray()`; both now return the stream's own buffer, sliced
  to its length, so reading a large attachment into memory costs one copy of it rather than two. Nothing
  observable changes but the allocation: the bytes, the length and the lifetime after the call are the same.
  `MergeToBytesAsync` keeps its copy — a `byte[]` of exactly the merged length cannot be the buffer of a stream
  that grew by doubling — and its documentation now says so, instead of leaving the copy to be found in a
  profiler. `ReadPageDetailAsync` is written with `await foreach` and reads exactly as it did. The same pass
  documents the destination path of `MergeToFileAsync`, `SaveImageDataAsync` and `SaveEmbeddedFileAsync`: it is
  used as given, no root is enforced, and confining a path that came from an untrusted source is the caller's
  job. That boundary is stated once in `SECURITY.md`, once in the manual's chapter on error handling, and in
  each member's XML documentation.


### Fixed

- **A filter name this library has never seen was reported as a damaged payload rather than as an
  unsupported filter.** `PdfImageFilters` recognised five names it does not decode — `JPXDecode`,
  `JBIG2Decode`, `CCITTFaxDecode`, `RunLengthDecode` and `Crypt` — and let every other name fall through to
  the arm that reports data a filter refused. A producer that invents a filter name therefore had its image
  reported as `ImageDataUnreadable` — "the payload could not be decoded" — while
  `docs/manual/src/08-inspection.md` and `specs/001-pdf-content-inspection/contracts/warnings.md` both describe
  such an image as `ImageFilterUnsupported`: nothing was decoded because the decoder is not there, not because
  the bytes are damaged, and the stored bytes are still what the read carries. The pass-through decision is
  now the complement of the four filters the library decodes, so the two cases stay apart however many names a
  document states.
- **An image with no filter never yielded pixels, so the simplest image of all was the one shape the read
  model could not state.** `ImagePixels` treated a `null` filter chain as "nothing here to widen" and
  returned before it looked at the samples, which meant an unfiltered image — and every inline image
  written without a filter — was reported with `DecodedPixels` `null` while the same bytes behind
  `/FlateDecode` were exposed, and `docs/manual/src/08-inspection.md` had always described the unfiltered
  image as the simplest case rather than an exception. The condition now asks whether the chain contains
  `DCTDecode` — the one payload whose samples are not bytes yet — instead of whether there is a chain at
  all.
- **A form whose field tree was too deep lost fields without saying so.** `ReadFormFieldsAsync` followed
  the tree to a hardcoded 32 levels and, past that point — or at a reference that repeated an object it
  had already read — returned without a word, so a truncated form read exactly like a shorter one. The
  walk now uses `PdfEngineOptions.MaxNestingDepth`, the same guard the page tree and the content reads
  already use, and reports the cut with the new `PdfWarningKind.FormFieldDepthExceeded`; a repeated
  object is reported as `FormFieldUnreadable`. The fields read above the cut are still returned, which is
  what makes "this form has five fields" and "this form was too deep to finish" two different answers.
  Adding the enum member is additive: the set is open, so an existing `switch` keeps working through its
  `default` arm.
- **The signature search had a depth of its own, and it was silent too.** `SignatureValidator` carried a
  private `MaxFieldDepth = 32` and returned past it without a word, so a document whose form nests deeper
  than that answered "these are its signatures" with nothing to suggest the list could be short. The walk
  now uses `PdfEngineOptions.MaxNestingDepth`, the same limit as everything else, and records
  `PdfWarningKind.FormFieldDepthExceeded` on the reader. A signature answer has no warnings channel, so the
  cut is also reported as the new `PdfSignatureProblemKind.FormFieldTreeTooDeep` on every validation that
  was returned: `Integrity` and `Trust` still describe the signature that is named, and the new problem says
  the answer is narrower than the document rather than that a signature is wrong. The page annotations are
  still scanned as well, which is where a signature a viewer shows carries its widget, so the cut costs the
  fields between the root and the widget and not the signature itself.

## [1.3.0] - 2026-10-24

`1.3.0` is on the feed: the five packages were pushed by `publish.yml` from tag `v1.3.0` on 2026-10-24.

### Added

- **Chapter 25 describes how this manual is built.** `docs/manual/src/25-how-this-manual-is-built.md`
  walks the four stages — Markdown to HTML, HTML to pages, measure, finish — and states what each one
  refuses: the converter's subset of Markdown (no raw HTML, no nested list, no image), the six class names
  the highlighter emits so that the stylesheet stays the contract, `--no-pdf-header-footer` and why the
  browser prints no header of its own, the forward search that locates a heading without pulling the
  cursor back into an earlier chapter, the second pass that replaces the placeholders with measured
  numbers and the re-render that follows a number which moved a heading, and the overlay, outline and
  metadata SharpCorePDF writes. It records the coordinate trap that put the footer at the top of the page,
  and the command line with its five switches. The preface no longer asks for Python to build the book.

### Changed

- **The manual is generated by the library it documents.** `tools/SharpCorePDF.Manual` replaces
  `docs/manual/build-manual.py`, and `dotnet run --project .\tools\SharpCorePDF.Manual` is the build: it
  writes `docs/SharpCorePDF-Manual-<version>.pdf` (the `1.3.0` edition unless `--version` says otherwise)
  and there is no second route. The tool is a console project in `SharpCorePDF.slnx` under `/tools/` and
  needs nothing beyond the .NET SDK and Chrome or Edge — `SHARPCOREPDF_CHROME` points it at a browser
  installed elsewhere, and a missing browser is an error message that says so rather than a stack trace.
  The Markdown conversion, the highlighting, the contents page, the two passes that measure the heading
  pages, the running footer, the bookmarks and the document metadata are all written with SharpCorePDF's
  own reader, authoring surface and edit plan, so no pypdf and no Pygments take part. The rebuilt edition
  is **26 chapters, 118 pages and 222 bookmarks**; the `1.2.0` entry below records the edition as it was
  first released, at 25 chapters and 113 pages.

### Fixed

- **The quickstart promised one package and then built a container.** `SharpCorePDF` brings every
  SharpCorePDF assembly, but `ServiceCollection` and `BuildServiceProvider()` live in
  `Microsoft.Extensions.DependencyInjection`, which the registration package does not reference — it takes
  `...Abstractions`, the assembly `AddSharpCorePDF()` extends. A project that followed Chapter 3 and
  Chapter 10 as they were written did not compile until it added that package itself. Both chapters now say
  so, where the first provider is built. G8 found it: the published `1.2.0` packages, installed into an empty
  `net11.0` console app, failed on the first build with `CS1061` on `IServiceCollection`.
- **The manual builder printed the running footer under the top edge.** The Python builder wrote the
  footer's text matrix 30 points above the bottom edge; the port passed the same 30 to a canvas that counts
  down from the top-left corner, so every page carried the footer in its top margin, where it reads as a
  header. `ManualFinisher` now converts the anchor (`page.HeightPoints - 30`, the rule 12 points closer to
  the text), which puts the footer back inside the 18mm bottom margin the stylesheet declares. Reading the
  printed page's content stream — `1 0 0 1 48.2 30 Tm` after the fix, `811.92` before it — is what caught
  it; a screenshot of the page did not.
- **A layer was drawn under the state the page left open.** The streams of a page reach a reader as one
  stream, so a producer that states a transform outside a `q` — Chromium's print output opens every page
  with `.24 0 0 -.24 0 841.92 cm` — had that transform still in force where `PdfEditPlan.Overlay` drew its
  form: the layer landed at a quarter of its size, in the corner of the page instead of where it was made.
  The plan now frames the original content in `q`/`Q` whenever it draws a layer, as it already did for a
  resized page, so what a page states cannot reach what the plan appends — which is what `docs/parity.md`
  row 6 asked for in the first place. The page's own bytes are still copied unchanged.
  `PdfDocumentEditorTests.Places_a_layer_where_it_was_made_when_the_page_leaves_its_state_open` is the test;
  the manual's footer is how the fault surfaced, as a short dash of purple above the first line of every page.

## [1.2.0] - 2026-10-24

### Added

- **A document is annotated without touching its content.** `PdfAnnotations` creates the ten kinds a
  viewer draws — a link to a URL or to a page, a sticky note, free text, a stamp, a highlight, an
  underline, a strike-out, a square, a circle, a line and ink — and `PdfEditPlan.AddAnnotation`,
  `UpdateAnnotation` and `RemoveAnnotation` write them through the same immutable plan as every other
  edit. The page's `/Annots` array is rebuilt and the page's content streams are never rewritten, so a
  page the plan does not name keeps every object it had; an annotation removed stops being reachable and
  is therefore not written at all. Every annotation the engine creates carries an appearance stream of
  its own — a note with a folded corner, free text auto-sized to its box, a centred stamp caption, filled
  quads, elliptical arcs, lines and ink — drawn in the annotation's own space and compressed, so a
  viewer that does not synthesise appearances and a printer, which never does, still show it; a caller
  that wants its own operators supplies `PdfAnnotationAppearance`. An annotation without an identifier
  is named `scpdf-N`, which is what a later operation of the same plan names it by. The refusals happen
  before a byte is written: a link with both a page and a URL, a markup without a quad, a line with the
  same point at both ends, an unknown flag bit, an opacity outside 0–1, a translucent annotation for
  PDF/A-1b and an identifier the page does not carry. A rectangle that reaches past the page is clipped
  with an `AnnotationClipped` warning, and a link to a page the same plan deletes is dropped with an
  `AnnotationDestinationUnresolved` warning.
- **A form is described, filled and flattened through the same plan.** `PdfFormFields` creates the five
  fields a form is built from — text, check box, radio button, choice (list box or combo box) and push
  button — and `PdfForm` groups them with the style a field that states none is drawn in.
  `PdfEditPlan.SetForm`, `SetFormField`, `ResetForm` and `FlattenForm` (whole document or one page) carry
  them, and a field is addressed by its name, dotted when it is a leaf of a tree: writing a name the
  document already has fills that field in place and leaves every other field alone. The flags a document
  stores are derived from the properties (`IsReadOnly`, `IsRequired`, `IsMultiline`, `IsCombo` and the
  rest), so no bit the editor cannot honour can be asked for. The refusals happen before a byte is
  written: a name with an empty part, a choice value that is not an option unless the combo box is
  editable, repeated or empty options, a button that states a value, a value longer than the maximum
  length, a text field that is multiline and a password field at once, options or a length on a kind that
  holds neither, two fields that share a name, and a plan that writes one name twice.
- **The form writer fills, resets and flattens.** `PdfEditEngine` now writes the fields a plan states, so
  `SetFormField` on a document that already carries the name fills that field where it stands — its
  rectangle, its ancestors and its widgets survive, and only `/V`, `/AS` and `/AP` change — while a name it
  does not carry becomes a new field, a dotted name written as a tree. Every widget gets an appearance the
  engine draws (a wrapped or shrunk value, a masked password, a highlighted list selection, a tick, a dot,
  a caption), `NeedAppearances` is written `false`, and the form carries a `/DR` with the fonts it names.
  `ResetForm` restores each field's `/DV`; `FlattenForm` draws the widgets into the page content so a reader
  without form support still shows the values, then takes them off the page and drops the form. A read-only
  field and a signature field are refused with the field's name, an XFA packet is removed with the new
  `XfaFormRemoved` warning, and an action on a field is copied inert with `FormFieldActionPreserved`. The
  `fill-form` example writes, fills and flattens a form, and the three files pass qpdf, pypdf and PyMuPDF.
- **An authored page draws a PNG, a JPEG, a BMP or a GIF image.** `PdfImage.Open` reads a file once —
  `FromPng`, `FromJpeg`, `FromBmp` and `FromGif` are the same read with the format stated, and refuse a
  file whose signature disagrees — and reports `Format`, `WidthPixels`, `HeightPixels`, `ColorSpace`,
  `BitsPerComponent`, `HasAlpha`, `NaturalWidth`, `NaturalHeight`, `FrameCount` and a `Key` that makes two
  opens of one file a single image object. `PdfPageCanvas.DrawImage` takes either the bytes or the
  `PdfImage`, at an explicit size or at the size the file's resolution states. A JPEG is passed through
  untouched under `DCTDecode`, with `DeviceGray`, `DeviceRGB` or — when an Adobe `APP14` segment states the
  transform — `DeviceCMYK` and the `/Decode` array that flips its inverted samples, and a file it cannot
  read (an EXIF orientation, arithmetic coding, a precision other than eight bits, four components with no
  transform) is refused by name. A BMP is decoded from its palette or its twenty-four and thirty-two bit
  pixels in either row order, and a GIF's first frame is LZW-decoded, de-interlaced, and given a soft mask
  from its transparent palette entry; the frames after the first are counted by `FrameCount` and not drawn,
  because PDF has no animation.
- **A PNG that states its own colour profile keeps it.** An `iCCP` chunk becomes an `/ICCBased` colour
  space with the profile in its own Flate stream — `/N` from the profile's signature, `/Alternate` the
  device space to fall back to — instead of being dropped, which would show the picture through whatever
  profile the viewer happens to use. `PdfImage.ColorSpace` reports `IccBased` for such a file, and a
  profile that does not describe the samples (a `GRAY` profile on an RGB image) is refused by name.
- **An authored document carries an XMP packet, bookmarks, and pages copied from another file.**
  `PdfDocumentBuilder.WithXmp` writes an XMP packet into the catalog exactly as it was given and
  uncompressed, from bytes or from the `PdfXmpMetadata` a reader returned. `WithOutline` writes the
  bookmark tree — nesting, open or closed state, page destinations, URLs, and the `/XYZ` corner a stated
  area names — and refuses a bookmark that points at a page the document does not have rather than writing
  a dangling one. `ImportPage` and `ImportPages` copy pages out of another document with the merge engine's
  own copier, so an imported page keeps its content, resources, annotations, rotation and geometry, and the
  attributes it inherited from its own page tree are stated on the copy.
- **A typeface says what its metrics are, and measures text without a page.** `PdfTypeface.Metrics` returns
  a `PdfTextMetrics` in the face's own font units — em size, ascent, descent, cap height, x-height and the
  line height `LineHeight(size)` derives from them — for a standard font out of metrics generated from the
  AFMs, and for an embedded TrueType face out of its `head`, `hhea`, `maxp`, `hmtx` and `cmap` tables, with
  the cap and x height falling back to the outline of `H` and `x` when `OS/2` states neither.
  `PdfTypeface.Measure(text, size)` returns the advance width of a string, with optional character and word
  spacing, so a caller can centre a title or size a column before anything is built. A standard font
  measures through WinAnsi and refuses with `ArgumentException` a character it cannot encode rather than
  guessing a width; an embedded face refuses a character its `cmap` does not map, and a size that is not a
  positive finite number is refused by both.
- **Text wraps into a box and is drawn line by line.** `PdfTextLayouter.Layout` takes a string, a
  `PdfTextStyle` and a `PdfTextLayoutOptions` — width, alignment, line height, `BreakWords`, `Overflow` and
  direction — and returns the `PdfTextLayout` that `PdfPageCanvas.DrawTextLayout` draws: a paragraph per
  line break, greedy wrapping at spaces by measured word width, `Left`, `Center`, `Right` and `Justify`
  placement (a justified line stretches the gaps between its words, not the words themselves), a leading
  that is the option, then the style's, then the typeface's own line height, and `Refuse`, `BreakWords`,
  `Clip` or `Continue` for a word that does not fit the width. The layout's coordinates are its own: the
  origin is the top-left corner of the box and y grows downwards, so the first baseline is one ascent below
  the point the layout is drawn at. A right-to-left paragraph is refused with
  `PdfUnsupportedFeatureException` rather than drawn backwards.
- **An edit records one page of another document over or under a page of the source.**
  `PdfEditPlan.Overlay` and `Underlay` take a one-page document, a page number and a `PdfOverlayOptions` —
  placement (`Fit`, `Stretch`, `OriginalSize`), opacity, `Clip` and the six separable blend modes — and write
  the layer as a form XObject that keeps the resources of the document it came from, so a font or image name
  in the layer cannot collide with a name the page already means something else by. The page's own streams
  are copied unchanged: an overlay appends a stream that draws the form, an underlay prepends one, and the
  opacity and blend mode of the layer travel in an `ExtGState` of its own. The layer follows its page through
  a move, a duplicate shares it, and a deletion removes it with the page. A translucent layer is refused on a
  PDF/A-1b output, whose format has no transparency.
- **An edit replaces the bookmarks, the page labels, the viewer preferences and the XMP packet.**
  `PdfEditPlan.SetOutline` writes the bookmark tree as stated, with page destinations numbered in the order
  the edit leaves the pages in, and `PdfOutline.Empty` removes the panel the source carried; `SetPageLabels`
  writes ranges in any of the six styles the standard names, and `PdfPageLabeling.None` removes the labels
  instead of reporting them as dropped; `SetViewerPreferences` writes the window, panel, layout, binding and
  print-scaling preferences in place of the ones the source had; and `SetXmp` writes the packet as given,
  uncompressed. A bookmark or a label range that names a page the result does not have is refused before the
  source is opened, and a later operation of the same kind replaces an earlier one.
- **An edit appends a revision to the source instead of rewriting it.** `PdfEditOutputOptions.UseIncrementalUpdate`
  copies the bytes of the source into the result unchanged and writes the edited objects, the pages and the
  catalog after them as one more revision, whose cross-reference section chains back to the one the source
  ended with through `/Prev`. That is what keeps a signature: the bytes it covers are still the bytes it
  covered, so the outcome reports `PriorSignaturePreserved` for the signature and `IncrementalUpdateAppended`
  for the revision, while the same edit as a complete rewrite reports `PriorSignatureInvalidated`. New objects
  are numbered above every object the source used, because a reused number would make an earlier revision
  ambiguous to a reader that resolves through the newest section first. The version of the source is kept —
  an appended revision cannot change the header it follows — and a plan that asks for another one is written
  anyway, with `IncrementalUpdateVersionKept`. The path refuses what it cannot honour: a source that cannot
  seek, an encrypted source, whose appended objects would need the file key of the revision they follow, a
  cross-reference table the reader had to rebuild by scanning, a destination that already holds bytes, and a
  plan that also asks for encryption or a PDF/A profile.
- **The examples draw a page of shapes.** The `draw-shapes` scenario
  (`examples/SharpCorePDF.Examples/DrawingExamples.cs`) writes one page of vector content with the authoring
  surface: a round-capped dashed rule with a dash phase, a two-subpath path whose even-odd fill leaves a hole,
  a clipped column of stripes drawn past its own clip, and a group of shapes turned and moved by a single
  `SetTransform`. It is the artifact the drawing row of `docs/parity.md` is validated against.
- **The examples watermark a document.** A new `watermark` scenario in
  `examples/SharpCorePDF.Examples/PdfEditExamples.cs` builds a stamp as a document of its own — one A4 page
  whose `DRAFT` is turned forty-five degrees — and places it on every page of an existing document as an
  overlay at 18 % opacity in `Multiply`, then prints the operations and the warnings the edit reports and
  reads its own output back, the page's sentence whole and the stamp a letter per block because each turned
  glyph has its own baseline. It is the artifact the overlay row of `docs/parity.md` is validated against,
  beside `edit-pages`, which `specs/003-pdf-platform-parity/quickstart.md` step 4 names.
- **An image read states its pixels, not only its bytes.** `PdfImageInfo` carries `DecodedPixels` and
  `PixelFormat`, so a caller that wants to look at a picture no longer writes a decoder first: the samples
  arrive tightly packed, top row first, one byte per sample, as `Gray8` or `Rgb24`, and their length is
  exactly the geometry times the channels. They are present only when nothing is left to interpret — the
  library decoded the payload itself, the width, height and bit depth are stated, every sample is eight bits,
  and the colour space is one whose samples are its channels (`DeviceGray`, `CalGray`, `G`, `DeviceRGB`,
  `CalRGB`, `RGB`) — so a palette image, a sixteen-bit image, a `CMYK` or `ICCBased` one, and any payload the
  reader passed through keep `null` and are described by `Filter`, `ColorSpace` and `IsDecodable` instead. A
  JPEG stays a JPEG: its bytes are already a format the caller can read, so `PixelFormat` is `null` for a
  `DCTDecode` image rather than a second decoder being shipped for it.
- **The examples archive a page and then sign it.** The `sign-and-archive` scenario
  (`examples/SharpCorePDF.Examples/ArchiveExamples.cs`) writes one authored page as PDF/A-3b, signs that file
  with a certificate it makes for the run and never writes down, and reports the integrity and the trust of
  what it produced as two separate verdicts — `integrity Valid, trust Trusted` with the run's own certificate
  as the anchor — with a timestamp token only when `SCPDF_TIMESTAMP_TOKEN` supplies one. The order is the
  promise: the conversion writes the archival bytes and the signer appends a revision, so the signature covers
  the whole file (coverage `ENTIRE_FILE`, as pyHanko reports it) while the structures the conversion wrote are
  still the ones a validator reads. Converting a signed file would be the reverse and is not offered, because
  it rewrites the bytes the signature covers.

### Fixed

- **A drawn text layout landed a box height above its origin.** `PdfPageCanvas.DrawTextLayout` took the top
  of `PdfTextLayout.Bounds` off every baseline, and that top is the height of the box, so a five-line block
  asked for at 100 points from the top of an A4 page was drawn at 37.5 — 71 points too high, with its first
  line almost off the page. Each run is now placed at its own start point added to the origin, and
  `PdfDocumentBuilderTests.Draws_the_layout_from_the_top_left_of_its_box` pins the result against the same
  baselines drawn one at a time with `DrawText`.
- **A caller-supplied annotation appearance is written, with the face it names.** `PdfAnnotationAppearance`
  was validated, its bounding box became the `/BBox` of the form XObject, its font decided that a `/Helv`
  resource was written — and its `Content`, the operator stream the record exists for, was dropped: the
  writer drew its own appearance and a caller who stated one got the engine's drawing instead. The operators
  are now the appearance, verbatim, wrapped in the same form XObject; and the face written as `/Helv` is the
  one the record names rather than the annotation's text style, which is what the record's own documentation
  promised. `PdfDocumentEditorTests.Writes_the_operators_of_a_supplied_annotation_appearance` writes a square
  with its own stream in `Courier-Bold` and asserts both.

### Changed

- **The manual is the `1.2.0` edition.** `docs/SharpCorePDF-Manual-1.2.0.pdf` is rebuilt from the sources —
  25 chapters, 113 pages, bookmarks and contents page numbers measured — and it is the first edition whose
  chapters describe the surface without dating it: the availability notes on chapters 16 to 19 used to say
  which part of their subject the `1.1.0` package did not carry, and they now say what `1.2` adds instead.
  The preface and the version note name `1.2.0`, the installation chapter installs `1.2.0`, and the
  `1.1.0` edition stays beside the new one as the record of what that release shipped. `docs/README.md` and
  the repository README point at the new file.
- **The manual documents the layers and the document-level edits.** `docs/manual/src/14-editing.md` gains
  the six operation rows, a section on overlays and underlays with every option they take, and a section on
  the four edits that state a value of the document; it now also states that a value the plan states
  replaces the one an edit would otherwise drop or report, and that PDF/A-1b refuses a translucent layer.
- **The manual documents text layout.** `docs/manual/src/16-text-layout.md` is new: measuring with
  `PdfTypeface.Measure` and the metrics in font units, `PdfTextLayouter.Layout` and every option it takes,
  the wrap rules, alignment and justification, the overflow policies, the `DrawTextLayout` coordinate space,
  the result records, determinism and what a layout deliberately does not do. Chapter 12 now points at it
  instead of stating that nothing measures or wraps, the cookbook gains a sub-recipe for a paragraph that
  comes from a template, and the preface explains why chapter 16 is no longer reserved.
  `docs/SharpCorePDF-Manual-1.1.0.pdf` was rebuilt from the sources — 21 chapters, 92 pages, bookmarks and
  contents page numbers measured. The chapter states at its head that the `1.1.0` package does not carry the
  surface it describes.
- **The manual documents drawing.** `docs/manual/src/15-drawing.md` is new and chapter 15 is no longer
  reserved: coordinates and the trap in `PdfRectangle`'s edge names, lines, rectangles, ellipses, paths and
  the two fill rules, `PdfPaint` and `PdfStroke` with the operator each member writes, alpha as an
  `/ExtGState`, dashes in points, clipping, transforms (including that a positive `Rotate` turns content
  clockwise on a canvas whose y axis points down, and how to compose a group so the rotation does not turn the
  placement), the saved graphics state, and what the surface deliberately does not do. Chapter 12 points at it
  for shapes, the preface reserves only chapters 17–19, and `docs/README.md` lists it.
- **Every `dotnet add package` line names the version that exists.** The installation chapter and the README
  asked for `1.1.0`'s predecessor — `--version 1.0.0` in three places — and the producer examples said a merged
  document is stamped `SharpCorePDF 1.0.0`. Both now say `1.1.0`, which is what the feed serves and what the
  domain assembly's version produces.

### Known limits at 1.2.0

- **PDF/A conformance is what a validator says it is, not what this library says.** `PdfAConversionResult`
  reports what a conversion could or could not meet; the verdict comes from running veraPDF against the
  output (chapter 24, `docs/tools/validate-pdfa.py`). The archival files this release is evidenced on were
  validated before the signature was appended, so a validator run on the signed copy is the caller's step —
  chapter 23 says why the order cannot be the other way round.
- **A timestamp is only as good as the token the caller supplies.** There is no network call: `PdfSignatureOptions.TimestampToken`
  signs what the caller hands over, and without one the signature states its own `signingTime` and nothing
  more. Revocation checking, trust lists and certificate chain building are a verifier's job here, which is
  why `PdfSignatureTrust` reports what the file carries rather than what a PKI would decide.
- **`PdfImageInfo.DecodedPixels` is present for the images whose samples need no interpretation.** A JPEG is
  a payload, a palette or sixteen-bit image is described but not expanded, and a `CMYK` or `ICCBased`
  image keeps its declared colour space — the reader states `Filter`, `ColorSpace` and `IsDecodable` for
  those rather than guessing at a conversion (chapter 8).
- **Layout analysis reports what it measured, not a guarantee.** Blocks, reading order and the extracted
  text are scored against `docs/tools/score-layout.py` and the fixture in `docs/measurements.md`; a table is
  a block of text with the rows the heuristics found, not a parsed table.
- **The remaining gates are the ones the release checklist still calls open.** There is no symbol package
  yet, so a debugger shows no source for a stack trace, and nothing is code-signed. The 90% new-code floor
  of G9 is enforced by `ci.yml` and read again by `publish.yml` for the tagged commit; the measurement for
  this version's code is in `docs/measurements.md`. Rendering, format conversion, OCR and conformance
  validation stay outside the product, as [ADR-001](docs/adr/0001-pdf-platform-boundary.md) states.

## [1.1.0] - 2026-10-23

### Added

- **An existing document can be edited into a new one.** `IPdfDocumentEditor`, resolved by
  `AddSharpCorePDF()`, applies an immutable `PdfEditPlan` to one source: `RotatePage` adds a quarter
  turn to a page, `DeletePage` removes it, `MovePage` changes its position, `DuplicatePage` copies it
  to another position, and `SetMetadata` overrides document information. Page numbers are one-based
  and are resolved against the state produced by the preceding operations, so a plan reads like a
  sequence of edits. Each operation is a value; the plan is validated before the source is read, and
  an operation that reaches past the last page fails with `PdfEditPlanValidationException` before any
  output is written. `PdfEditResult` reports the page count, bytes written, output version, elapsed
  time, the operations applied and the warnings the edit produced.
- **Pages can be cropped and resized without re-rendering their content.** `CropPage` sets an
  explicit `/CropBox` inside the media box: a box that reaches outside it is clamped and reported as
  `PdfWarningKind.PageGeometryNormalized`, and a box with no overlap is refused before any output is
  written. `ResizePage` changes `/MediaBox` and wraps the untouched original content streams in a
  graphics state (`q`, a `cm` transform, then `Q`), so `PdfPageResizeMode.Scale` scales the content
  to the new size and `PdfPageResizeMode.Preserve` keeps it at its original size and centres it. A
  crop box set before a resize is carried through the same transform.
- **Editing copies pages, it does not re-render them.** The engine writes a complete new document and
  copies every surviving page's content streams, resources, fonts, images and annotations verbatim;
  inherited `MediaBox`, `CropBox`, `Resources` and `Rotate` are materialised onto each page. Bookmarks,
  named destinations and form widgets that name a source page are redirected to the first surviving
  occurrence, or to the first page of the result when the named page did not survive. Page labels are
  dropped when an operation changes the page order or count, and an
  XMP packet is dropped when document information is overridden; both decisions are reported through
  `PdfWarningKind.PageLabelsDropped` and `PdfWarningKind.XmpPacketDropped` rather than silently
  producing a document whose metadata contradicts itself.
- **An edit is reproducible when its inputs are fixed.** `PdfEditOutputOptions` chooses the output
  version, metadata overrides, producer, modification date and document identifier. The file
  identifier is derived from the version, date, page count, identifier and applied operations, so a
  fixed `ModificationDate` and `DocumentId` make repeated edits of the same source byte-identical.
  The editing release reads encrypted sources through the existing standard security handler; the
  result is written in the clear unless `Encryption` is set (see below).
- **A page written from nothing.** `PdfDocumentBuilder` creates an in-memory document — one or more
  pages of any `PdfPageSize` — whose pages are drawn through `PdfPageCanvas`: `DrawText` sets a line in a
  standard or an embedded font at a baseline, in a top-left coordinate system with `y` downwards, and
  `DrawImage` places a PNG at an explicit size or at the size its `pHYs` chunk states. `Build()` returns
  a `PdfByteArraySource`, so the page can lead a `MergePlan` without a temporary file; `ToArray()` and
  `SaveAsync(Stream)` are the other two routes. `WithMetadata` writes the document information and
  `PdfAuthoringOptions` fixes the creation date and document identifier, which makes repeated builds
  byte-identical.
- **Two levels of font, neither of them a system font.** `PdfTypeface.Standard` selects one of the twelve
  standard PDF faces with `WinAnsiEncoding` and no font file; `PdfTypeface.FromTrueType` embeds a TrueType
  program the caller supplies, subsetted to the glyphs the pages draw, with a `/ToUnicode` map so the text
  — `é`, `è`, `à`, `ç`, `ô`, `ï` included — extracts again. Glyph numbers are preserved, unused outlines
  are left out, and the program is never looked up in the machine's font folder. A character the face
  cannot encode is refused with the character and code point in the message, rather than replaced.
- **PNG images without an imaging dependency.** Eight bit, non-interlaced PNGs of colour types 0, 2, 3,
  4 and 6 are embedded: an alpha channel becomes an `/SMask`, a palette with `tRNS` is expanded to RGB and
  a soft mask, a colour key becomes `/Mask`, and an opaque image keeps its original `IDAT` bytes with a
  PNG predictor instead of being recompressed. Interlaced and sixteen bit images are refused with an
  explanation; JPEG and other formats are out of scope. One image drawn on several pages is one object.
- **Reading what a document already contains.** `IPdfContentReader`, resolved by `AddSharpCorePDF()` and
  constructible by hand, answers the questions a caller asks before drawing anything: `ReadImagesAsync`
  returns every image a page range paints — one entry per object, carrying every page that draws it, with
  the bytes the file holds (a `DCTDecode` image is never re-encoded) and the area it occupies;
  `ReadAnnotationsAsync` returns comments, highlights, stamps and links with their geometry, their text
  and their target, resolving direct and named destinations; `ReadFormFieldsAsync` returns the field tree
  with fully-qualified names, types, values and every widget of each field; `ReadMetadataAsync` returns the
  XMP packet byte-exact with the `dc:`, `pdf:` and `xmp:` values parsed beside it; `ReadEmbeddedFilesAsync`
  returns the attachments with a distinct identifier per entry; `ReadOutlineAsync` returns the bookmark
  tree with its destinations resolved; and `ReadPageDetailsAsync` yields everything known about one page at
  a time, so a 200-page index never holds a report for the whole file.
- **One payload rule for every read.** A record carries its bytes inline only within
  `PdfEngineOptions.MaxInlineContentBytes` (new, default 256 KiB, `0` for metadata only); above it `Data`
  is `null`, `ByteLength` stays truthful and `IsBuffered` says so, with no warning because nothing failed.
  `CopyImageDataAsync`, `CopyImageMaskDataAsync` and `CopyEmbeddedFileDataAsync` write such a payload to a
  stream the caller supplies through a pooled 64 KiB buffer, and `ReadImageDataAsync` / `SaveImageDataAsync`
  and their embedded-file twins are the convenience members beside them. A `ReadOnlyMemory<byte>` that
  escapes a call never aliases a rented buffer.
- **Reading the text the pages draw.** `IPdfContentReader.ReadTextAsync` returns `PdfTextBlock` records in
  the order a reader would take them — down the page, then along the line — rather than in the order the
  content stream happens to draw them. A block carries its `Text`, `LineCount`, page and `BoundingBox`, and
  holds the `PdfWord` records it is built from, each with a baseline, a font size and a font name and the
  `PdfGlyph` records behind it, so a caller that needs a coordinate never parses the page a second time.
  Words end where the document draws a space and where a `TJ` adjustment is wide enough to stand in for
  one; `Tc`, `Tw`, `Tz`, `TL`, `Ts`, `Td`, `TD`, `Tm`, `T*` and `q`/`Q` are all honoured, so a line inside
  a scaled or rotated form lands where a viewer draws it. A simple font is read through its `/Encoding`,
  its `/Differences` and the standard glyph names, a composite font through its `/ToUnicode` map, and the
  width of a code comes from `/Widths` or `/W` with the fallbacks the specification states. One page
  overload, `ReadTextAsync(source, page)`, reads a single page without a range.
- **Text is read or reported, never guessed.** A glyph whose code no font entry maps is skipped and its
  font named in one `TextUnreadable` warning per page, so a document whose encoding this library cannot
  follow reads as fewer words plus a remark instead of a page of plausible nonsense, and a page whose font
  is unreadable still leaves the pages around it readable.
- **Eleven new `PdfWarningKind` members** — `ContentStreamUnreadable`, `ImageFilterUnsupported`,
  `ImageDataUnreadable`, `XObjectResourceMissing`, `NamedDestinationUnresolved`, `EmbeddedFileUnreadable`,
  `EmbeddedFileNameCollision`, `XmpPacketMalformed`, `FormFieldUnreadable`, `AnnotationUnreadable` and
  `TextUnreadable` — so a filter the library does not decode, a damaged image, a name that does not
  resolve, a malformed XMP packet or a font it cannot read is reported as data instead of being skipped in
  silence.
- **The layout comes out with the text.** `IPdfLayoutReader`, resolved by `AddSharpCorePDF()` and
  constructible by hand, reads a page the way a search or an analytics pipeline needs it:
  `ReadLettersAsync` returns one `PdfLetter` per glyph in content order, with its Unicode value, its font
  size, the font the document declares, its box, its baseline start and end and the direction that
  baseline runs in; `ReadWordsAsync` groups those letters into `PdfWord` records that keep the letters
  they were grouped from, with the order of the page as the default and `PdfWordExtractionStrategy.Raw`
  for a caller that needs the order of the producer; and `ReadHyperlinksAsync` returns the clickable area
  of every link with either the URI it opens or the destination it jumps to. `PdfWordExtractionOptions`
  states both rules in ems — a quarter em as a space, half an em as one line — and refuses a negative
  distance or a strategy this release does not implement when it is created, before a document is
  opened. Whitespace letters can be kept in the word they follow, so the words of a page concatenate back
  to the text it draws, and a letter whose baseline runs towards the left is read in its own direction
  with a box that covers the area between its two baseline points.
- **A page's blocks, and the order a reader takes them in.** `IPdfLayoutReader.ReadBlocksAsync` groups the
  words of a page into `PdfTextBlock` records — the text, the words it keeps, the area it covers and its
  one-based position on the page — by the segmentation `PdfLayoutOptions` selects: `Raw`, which reproduces
  the producer's drawing order and is the baseline a comparison starts from; `NearestNeighbour`, which
  groups the lines of a paragraph; `RecursiveXYCut`, the default, which splits a region along the widest
  vertical gutter and then the widest horizontal one, so two columns and a heading are found without
  knowing what a column is; and `Docstrum`, which measures the page's own leading instead of assuming it.
  The reading order is chosen separately, because a page can be segmented correctly and still be listed in
  the wrong order: `ColumnAware`, the default, reports the columns of a page left to right and the blocks
  of a column top to bottom, and `Raw` keeps the order the segmentation produced. The segmentation and the
  order travel with the result, so a page that yielded no block is not the same answer as a page that was
  never segmented; `PdfLayoutOptions.IncludeDecorations` decides whether a block that carries no letter and
  no digit — a rule drawn as a run of hyphens, a lone bullet — is reported; and a strategy only groups the
  letters and words a read produced, never changing what one of them says.
- **The reading of a page written down for another tool.** `IPdfLayoutReader.ExportAsync` writes what a block
  read produced as `PdfLayoutFormat.Alto`, `PageXml`, `Hocr` or `SvgText`, straight into a stream the caller
  supplies: ALTO 4 with a `Page` per page, a `TextBlock`, a `TextLine` and a `String` per word; PAGE XML with
  a `TextRegion` per block, a `TextLine` per line and the reading order that names them; hOCR as XHTML with
  `ocr_page`, `ocr_carea`, `ocr_par`, `ocr_line` and `ocrx_word` elements, each carrying its `bbox`; and SVG
  with one `text` element per word on the baseline it is drawn on. Coordinates are the page's own numbers
  with the origin moved to the top left corner and the y axis turned down, which is the convention of every
  one of the four formats; text is escaped for the document it lands in, and a character XML cannot carry
  becomes the replacement character instead of aborting the write. A page that draws no text still produces
  its page element, a range is written in ascending page order, and no document states a timestamp or a
  random identifier, so the same read produces the same bytes on every run. The call returns the blocks it
  wrote, with the segmentation and the order that produced them and the warnings the read made, so an export
  of a page whose font could not be mapped is not silent. A destination that cannot be written and a format
  this release does not produce are both refused before the document is opened, and the exports describe text
  only: `PdfLayoutOptions.IncludeImages` is reserved for the image regions a later release places beside it.
- **A document can be signed without rewriting a byte that is already signed.** `IPdfDocumentSigner` —
  resolved by `AddSharpCorePDF()` and constructible by hand like every other engine port — appends one
  revision to a source and returns a `PdfSignatureInfo` read back from the file it wrote: the field, the
  filter and sub-filter, the digest, the signer, the byte range and the revision the signature covers.
  The CMS is built here rather than by `SignedCms`, because a detached signature over a reserved byte
  range needs the digest of everything except its own placeholder, the signed attributes CAdES names, and
  a size known before the value exists: the writer reserves `ByteRange` and `Contents`, hashes the
  revision with the gap removed, patches the range, and only then writes the signature into it. The
  certificate's key decides the algorithm — RSA PKCS#1 v1.5 or ECDSA — and the options decide the digest.
  A certificate with no private key, an encrypted document and a revision too small for the chain are
  refused before anything is written, so a half-signed file cannot exist; a visible appearance writes a
  form XObject, a widget and an `/Annots` entry in that same revision, and `/AcroForm` is added when the
  document had none.
- **A signature can carry a timestamp the caller obtained.** `PdfSignatureOptions.TimestampProvider` is
  the whole of it: the library hands the caller's `IPdfTimestampProvider` the digest of the signature
  value and embeds the RFC 3161 token it answers with as the `signatureTimeStampToken` unsigned
  attribute. Nothing here opens a socket, and a provider that throws stops the signature instead of
  leaving a document that only looks signed. The token travels unverified, and says so: a token that
  cannot be read is a `TimestampInvalid` problem rather than a claim about its time, and
  `PdfTrustOptions.CheckTimestampTrust` is reserved for the release that checks the authority's own
  signature.
- **Every signature in a document is checked, and the two questions are answered apart.**
  `IPdfDocumentSignatureValidator` finds each signature field in the form tree and on the page widgets,
  reads its byte range and its CMS, and reports one `PdfSignatureValidation` per signature: `Integrity` —
  the digest the CMS carries matches the bytes the range covers — and, only when the caller names anchors
  through `PdfTrustOptions`, `Trust` — the chain leads to one of them. They are separate results because
  they are separate failures: a document changed after it was signed is not the same defect as a
  certificate that leads nowhere, and `IsValid` speaks only about integrity, which is the half this
  library can decide on its own. No machine trust store is consulted, no certificate is fetched and no
  authority is asked whether one was revoked; a placeholder signature, an unreadable structure or a range
  that does not reach the end of the file is `Unknown`, never `Valid`.
- **A document can be written for archival delivery.** `IPdfAWriter`, resolved by `AddSharpCorePDF()`
  and constructible by hand, writes a complete PDF/A-1b, -2b or -3b document: the version the profile
  names, an uncompressed UTF-8 XMP packet carrying `pdfaid:part` and `pdfaid:conformance` with the
  document information mirrored into it, an `/N 3` output intent built from a generated sRGB colour
  profile, and `/NeedAppearances false` on a form. `PdfEditOutputOptions.PdfAProfile` routes an ordinary
  edit through the same writer. The analysis runs before a byte is written: a font that is not embedded,
  a CID subset that states no `/CIDSet` for 1b, transparency, `JPXDecode`/`LZWDecode`, optional content or
  an attachment the profile forbids, an annotation without an appearance stream, a JavaScript/`Launch`
  and similar action, or an external file stream is refused with `IsWritten = false`, a named
  requirement and an untouched destination — never written with a label it does not deserve. What can be
  removed without changing a page is removed and reported as `PdfWarningKind.PdfAFeatureRemoved`.
  `docs/tools/validate-pdfa.py` wraps veraPDF; the release is recorded in `docs/parity.md` on veraPDF
  1.30.3 reporting `PASS` for all three profiles of both an authored page and a real
  "Microsoft: Print To PDF" document.
- **An edit can write an encrypted result.** `PdfEditOutputOptions.Encryption` applies the same standard
  security handler a merge uses — AES-256 by default, AES-128 and RC4 available — and the identifier of
  the result keys it. The engine refuses a plan that asks for a PDF/A profile and encryption together,
  before the source is read, and with `VerifyOutput` on it reads its own encrypted output back with the
  password it was written with.
- **Embedded TrueType subsets now state a `/CIDSet`.** ISO 19005-1 requires the set of CIDs a subset
  describes, and veraPDF failed a PDF/A-1b conversion without it. The writer emits one that names every
  CID the preserved numbering makes present, which also satisfies the PDF/A-2 rule for subsets that carry
  a set.

### Fixed

- **Images decode to the samples their filter names, and a mask can be fetched.** A `/Predictor` image is
  now inflated and undone one row at a time instead of into a single buffer, so an image whose stored form
  is small but whose samples are large is counted rather than materialised and the copy route writes rows
  as it produces them. An inline image's data ends where its header says it does — the separator after `ID`
  is consumed once and `EI` is found even when a delimiter follows it — so a sample stream containing a
  space followed by `EI` is no longer cut short, and an image with no `EI` at all keeps what it read and
  raises `ImageDataUnreadable` instead of ending in silence, and a page's streams are separated only
  *between* streams, so an unterminated image ends at the last byte the page holds. An inline dictionary
  whose value is a name (`/CS /G`) keeps its colour space instead of losing it and misreading the entry
  after it. Three smaller corrections ride with them: `CopyImageMaskDataAsync` fetches a soft or explicit
  mask by the `(ImageId, Kind)` its record carries and refuses a colour key mask, which has no payload, by
  name; `PdfImageInfo.IsInline` tells an inline image from an object; and every image warning names the
  source and the page, plus the image wherever the failure belongs to one, so a 200-page report can be
  triaged.
- **The examples no longer contain a password.** The encryption example in `examples/` wrote a literal user
  password and a literal owner password into the document it demonstrates — the kind of constant a static
  analysis scanner reports as a hardcoded credential, and the kind a reader copies into their own code. Both
  are now made per run, the user password is printed so the document can still be opened, and the example
  opens it with the value it just wrote. No package changes with this; the finding is what turned the
  project's security rating red.

### Changed

- **What 1.1 delivers, and what it defers to 1.2.** The capability described in this entry is what
  `1.1.0` ships: page creation with standard and embedded fonts, PNG images, the path writer, every page
  operation of the editor (rotate, delete, move, duplicate, reorder, crop, resize, extract, split, import,
  insert, append and metadata overrides), reading with layout analysis and the four text exporters,
  digital signatures and PDF/A-1b, -2b and -3b output. Text measurement and word wrap,
  JPEG/BMP/GIF, overlays, annotation and form authoring, and incremental updates are **not** in `1.1.0`.
  They stay in the parity matrix as `1.2` rows with a named task rather than disappearing, so a caller can
  see the gap instead of discovering it: [`docs/parity.md`](docs/parity.md) names the release of every
  row, and the specification carries the dated scope amendment behind that split.
- **The product boundary now includes creation and editing.** Constitution 2.0.0 and
  [ADR-001](docs/adr/0001-pdf-platform-boundary.md) define SharpCorePDF as a dependency-free PDF
  creation, editing and reading library. The 1.1 target is capability parity with PDFsharp 6.5 for
  creation and processing and PdfPig 0.1.x for reading and layout analysis; rendering, conversion,
  OCR, conformance validation, services and CLIs stay outside the product. This release implements
  page-assembly editing only — page content, annotations and forms are still copied, not rewritten.
- **A release waits for the quality gate, on the commit being released.** `publish.yml` reads the SonarCloud
  verdict in a `sonar-gate` job that runs before the build, before the pack and before the OIDC exchange, and
  stops when the gate is red, when the snapshot it judged carried no coverage report, or when the newest
  analysis of `main` is of a different commit than the tag. It changes when a version appears on the feed,
  not what is in it; the rule it adds is that a tag goes on the commit whose analysis is the newest on `main`.

### Known limits at 1.1.0

One shortfall is stated rather than left for a reader to find: **77.76% of the executable lines this version
adds are covered by the suite, against the 90% new-code floor** in `docs/release-checklist.md` G9 —
[ADR-002](docs/adr/0002-new-code-coverage-1.1.0.md) records the measurement, the eight parser and validator
files the uncovered lines sit in, and the decision to ship with the number stated rather than to move it with
tests that only execute lines. The floor itself is unchanged and is re-measured on 1.2's new code. What the
suite does instead is evidence behaviour: 748 tests on both legs, 21 parity rows each naming a test and an
external command, and pyHanko, qpdf, veraPDF 1.30.3, pypdf and PyMuPDF run against the output.

Two things are as they were at 1.0.0: there is no symbol package yet, so a debugger shows no source for a
stack trace, and nothing is code-signed. Both remain gates in the release checklist rather than intentions, and
neither changes what the packages contain.

## [1.0.0] - 2026-10-17

### Added

- **`SharpCorePDF`, the package to install.** The four layers stay separate assemblies with separate API
  snapshots, but a consumer no longer has to work out which of them to reference: `SharpCorePDF` is a
  meta-package whose only content is a dependency on `SharpCorePDF.Domain`, `.Application`,
  `.Infrastructure` and `.DependencyInjection`, and `dotnet add package SharpCorePDF` is the whole
  install. The four remain installable on their own.
- **`NOTICE`**, stating in one page who holds the copyright, what third-party material exists (no
  third-party source code; `Microsoft.Extensions.*` in `SharpCorePDF.DependencyInjection` only; test
  fixtures generated by `docs/tools/make-fixtures.py`) and what the name is not.
- **`SECURITY.md`**, `CODE_OF_CONDUCT.md` and `CONTRIBUTING.md` at the repository root: a private
  reporting route with a 7-day acknowledgement, Contributor Covenant 2.1 with two enforcement channels,
  and the build, test and review bar a change has to clear.
- **`.github/`**: issue forms for a defect and a feature request, a pull request template, and a
  Dependabot configuration for the NuGet packages and the workflow actions.
- **`SonarAnalyzer.CSharp` in the build** (`src/Directory.Build.props`, dev-time only). The rules
  SonarCloud applies now run in `dotnet build`, at the line that causes them. The three rules this
  repository answers with a decision rather than a change (`S101`, `S1694`, `S4790`) are silenced per file
  in `src/.editorconfig` with the reason, and their lines carry `// NOSONAR`.
- **The public surface is an approved snapshot** (`Microsoft.CodeAnalysis.PublicApiAnalyzers`, dev-time only).
  Each project declares what it exports in `PublicAPI.Shipped.txt` and queues additions in
  `PublicAPI.Unshipped.txt`, and RS0016/RS0017 are build errors: a public member the snapshot does not list,
  or an entry that outlives its member, stops the build rather than the next release.

### Changed

- **`publish.yml` checks the entry point differently from the layers.** Every package still has to carry
  `lib/net10.0` and `lib/net11.0` and the README nuget.org renders; `SharpCorePDF` carries no assemblies
  by design, so it is held to the opposite rule instead: it must depend on all four layers.
- **The trusted publishing policy needs two subjects, not one.** `SharpCorePDF.*` matches the four layers
  and does not match `SharpCorePDF` itself, which has no dot after the product name; a policy holding only
  the glob pushes four packages and refuses the fifth.
- **`ci.yml` enforces the SonarCloud quality gate instead of reporting it.** The scan passes
  `sonar.qualitygate.wait` to `begin` — the `end` phase rejects every property but the token — so a failing
  gate fails the run, and the gate's coverage condition reads the report this run produced:
  `dotnet-coverage collect` measures the net11.0 leg, and the Visual Studio XML
  it yields is named exactly (`artifacts/coverage/net11.0.xml`) instead of by a glob. Coverage is measured
  on every run rather than only when a SonarCloud token exists, and every action is pinned to a commit hash
  rather than a tag.
- **Codecov is gone, and with it the second coverage report.** One service measuring the number is the
  arrangement that cannot disagree with itself: `codecov.yml` asked for 90% on the project and on the
  change, while SonarCloud was already importing the same measurement, so the gate holds the number and
  the cobertura conversion that only Codecov read is no longer produced. The 90% is a condition to enter on
  the quality gate on sonarcloud.io — above Sonar's 80% default on new code, and the only place it can be
  enforced once the file that declared it is gone — and `README.md`, `CONTRIBUTING.md` and
  `docs/release-checklist.md` say so instead of pointing at a file in the tree.
- **The SonarCloud findings are fixed in the source, not silenced**: loops that read better as
  `Where`/`Select`/`Distinct`, a nested ternary in the AES object-key derivation, two helpers moved into
  the walker that uses them, and `PdfReference` now implements `IEquatable<PdfReference>`, so the
  `Equals` overload it already carried is the one a generic comparer calls.

### Known limits at 1.0.0

Two things are known and deliberately not promised: there is no symbol package yet, so a debugger shows no
source for a stack trace, and nothing is code-signed. Both are gates in the release checklist rather than
intentions. Neither affects what the packages contain or what the assemblies do.

The shipped leg is two, not one: `lib/net10.0` and `lib/net11.0` carry the same API, as `preview.1`
already advertised, because a 1.0.0 that narrowed to a single framework would withdraw a target the feed
has promised since 2026-10-15 (`docs/release-checklist.md` G2). The public surface is the one frozen by
`PublicAPI.Shipped.txt` — additions after this version go through `PublicAPI.Unshipped.txt` and a minor
version, not through a silent change to a shipped signature.

## [1.0.0-preview.3], [1.0.0-preview.2]

Never pushed. Both names exist only as values the repository's `<Version>` passed through, so no feed ever
served them, nothing was downloaded from them, and skipping them broke no promise to anyone.

## [1.0.0-preview.1] - 2026-10-15

The first and — until 1.0.0 — only published version: `SharpCorePDF.Domain`, `.Application`,
`.Infrastructure` and `.DependencyInjection` at `1.0.0-preview.1`, from tag `v1.0.0-preview.1`. No entry
was written here for it at the time, and this heading does not invent one after the fact; it records that
the four ids are live and immutable, which is why their next version is `1.0.0` and not a re-cut of
`preview.1`.

