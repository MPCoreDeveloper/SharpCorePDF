# What the tests would notice — mutation testing

`ci.yml` measures **coverage**, and coverage answers exactly one question: was this line executed. It
cannot answer the question behind it — would a test have failed had that line done something else. A line
can be covered by an assertion that passes either way, and a suite can execute 92% of a tree while letting
an operator change, a boundary move or a statement disappear with every test green.

Mutation testing asks the second question. Stryker changes one thing at a time in a compiled assembly —
`+` for `-`, `<=` for `<`, a constant for another constant, a statement removed — rebuilds, and runs the
tests that cover the change. If a test fails, the mutant is **killed** and the suite is doing its job
there. If every test still passes, the mutant has **survived**, and the line it changed is a line no test
pins down.

This is the record: what the configuration is, how to run it, how to read a report, and what has been
measured so far.

## Running it

| Where | What runs |
| --- | --- |
| [`mutation.yml`](../.github/workflows/mutation.yml) | the four layers, Mondays at 03:00 UTC, or on dispatch with one layer, a mutation level and a `--since` switch |
| locally | the same configuration file, one layer at a time, from the repository root |

Stryker is a local tool of this repository, not something a clone has to install: `.config/dotnet-tools.json`
pins `dotnet-stryker` to the version `stryker-config.json` is written for, so `dotnet tool restore` is the
whole setup and `dotnet stryker` is the command. The workflow restores the same manifest, which is why the
pin is stated in one place.

```powershell
dotnet tool restore

# One layer. The five projects under src/ are four layers and a meta package: SharpCorePDF itself has no
# code to mutate.
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj --output artifacts/mutation/Domain

# One file, when the question is about one file
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj --mutate "**/PageRange.cs"

# Only the lines a branch changes against main
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj --since:main

# The reading, out of the report the run leaves behind
python ./docs/tools/mutation-summary.py ./artifacts/mutation/Domain/reports/mutation-report.json --detail

# What moved between two readings: the verdicts, not only the score
python ./docs/tools/mutation-summary.py ./artifacts/mutation/Domain/reports/mutation-report.json `
    --compare ./artifacts/mutation/Domain-earlier/reports/mutation-report.json
```

`artifacts/mutation/<layer>/reports/` keeps one reading three ways: `mutation-report.html` opens in a
browser and shows each mutated line with the mutation in place, which is the one to read when a survivor
has to be understood; `mutation-report.json` is what a tool reads, and what `mutation-summary.py` reads;
`mutation-report.md` is the per-file table a run summary shows.

### On a machine with Visual Studio installed

Stryker's project analysis runs an MSBuild it discovers, and on a workstation that can be Visual Studio's
rather than the SDK's. The symptom is a run that finds the test project but reports every layer under
`src/` as `simulated build failed`, then stops with `error CS0518: Predefined type 'System.Attribute' is
not defined or imported`, because the mutants were compiled with no framework references at all. Name the
SDK's own copy:

```powershell
$sdk = (& dotnet --version).Trim()
$env:MSBUILD_EXE_PATH = "$env:ProgramFiles\dotnet\sdk\$sdk\MSBuild.dll"
```

The hosted runners carry no Visual Studio, so `mutation.yml` needs none of this.

## The configuration

[`stryker-config.json`](../stryker-config.json) holds every setting, so that the workflow and a local run
ask the same question. It names no solution: Stryker finds `SharpCorePDF.slnx` in the directory it is run
from and builds it first, which is what keeps the other layers' assemblies current while one is mutated.

| Key | Value | Why |
| --- | --- | --- |
| `test-projects` | `tests/SharpCorePDF.Tests` | one suite covers all four layers; Stryker can mutate one assembly against it at a time, which is what the workflow's matrix is for |
| `test-runner` | `mtp` | `global.json` pins Microsoft.Testing.Platform, and Stryker's default runner is the VSTest one — the same reason `ci.yml` collects coverage with `dotnet-coverage` instead of `dotnet test --collect` |
| `target-framework` | `net11.0` | every project multi-targets. It is the leg `ci.yml` instruments; the net10.0 leg needs a runtime identifier (README → Building and testing) that Stryker's build has no switch for |
| `configuration` | `Release` | the mutants the suite is asked about are the ones that ship |
| `mutation-level` | `Standard` | the default set of mutators: enough to be useful on a real operator, boundary and statement without the `Advanced` and `Complete` levels' noise |
| `coverage-analysis` | `perTest` | each mutant runs only the tests that reach it, which is what makes a sweep of a suite this size finite |
| `thresholds` | `high` 90, `low` 80, `break` 0 | `break` is 0: see "Why nothing gates on it" below. The first two only colour the report |
| `reporters` | `ClearText`, `Html`, `Json`, `Markdown` | the console table, the report a person reads, the report a tool reads and the table a run summary takes |
| `report-file-name` | `mutation-report` | the name the workflow's summary step reads back (`reports/mutation-report.json`); the two have to agree |
| `mutate` | `**/*` | everything in the assembly under mutation. A narrower question is a command-line `--mutate`, not a check-in |
| `break-on-initial-test-failure` | `true` | a suite that is already red cannot tell a killed mutant from a broken build, so the run stops instead of measuring nothing |

`language-version` is deliberately absent: Stryker reads the project's own, which is C# 14 on `net10.0` and
`preview` on `net11.0`. Nothing is silenced with `ignore-mutations` or `ignore-methods` either — the score
is about the suite as it stands. `concurrency` is absent too, so Stryker starts one test process per core,
which is right on a 4-core runner and on a workstation alike.

## Reading a report

| Status | What it means | In the score |
| --- | --- | --- |
| `Killed` | a test failed on the mutant, so the suite noticed the change | detected |
| `Timeout` | the suite hung on the mutant, which is also noticing it | detected |
| `Survived` | every test passed with the mutant in place: the finding | undetected |
| `NoCoverage` | no test reaches the line, so nothing could notice | undetected |
| `Ignored` | outside the `--mutate` filter, or silent by configuration | neither |
| `CompileError`, `RuntimeError` | the mutant could not be compiled or run, so no test was asked | neither |

The score is `detected / (detected + undetected)`, which is Stryker's own arithmetic and the one
`docs/tools/mutation-summary.py` recomputes from the JSON so that a summary cannot drift from the report it
summarises. Ignored and uncompilable mutants take part in neither half: they measure the filter and the
compiler, not the suite, and counting them as survivors would turn the score into a report about
configuration.

Two things a single number hides, so read them beside it. A layer at 90% with every survivor in the reader
is not the same finding as one at 90% with its survivors spread through the domain. And the report is per
line, not per assertion: a survivor next to a test that exercises the line usually means the test asserts
the *type* of the result rather than its *value*.

## The first record

Recorded: 2026-10-29, Windows 11 x64, .NET SDK 11.0.100-rc.2.26504.105, `dotnet-stryker` 5.0.0,
`Release`, `Standard`, `net11.0`. **A scoped run, not a baseline**: `--mutate "**/PageRange.cs"` on
`SharpCorePDF.Domain`, which is one value object out of one layer, taken to prove the pipeline end to end.
No sweep of a whole layer has been recorded yet; the first scheduled run of `mutation.yml` is what a
baseline would come from.

```powershell
./.tools/dotnet-stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj `
    --mutate "**/PageRange.cs" --output artifacts/mutation/PageRange
```

The path is the command that was run: that first sweep predates `.config/dotnet-tools.json`, and installed the
tool with `--tool-path .tools`. The manifest and `.tools/` hold the same 5.0.0, so `dotnet stryker` after
`dotnet tool restore` is the same tool from one pin — which is what the commands above and `mutation.yml` use,
and what a clone has without any install step.

| Mutants | Detected | Undetected | Compile errors | Ignored | Score |
| --- | --- | --- | --- | --- | --- |
| 105 | 70 | 6 | 9 | 20 | 92.11% |

Of the 105, 102 are in `PageRange.cs` and 3 are in `ProductInfo.cs`, which the `--mutate` filter dropped and
the report keeps as `Ignored`. The MTP runner found 908 test cases, and the whole command — solution build,
initial suite, 102 mutants — took 1 minute 27 seconds.

### The two tests the first reading produced

The first sweep left survivors that were gaps rather than equivalents, and each became a test in
`tests/SharpCorePDF.Tests/PageRangeTests.cs`:

- **The refusal said nothing a caller could use.** `Parse` threw a `FormatException` naming the text it
  refused and the four notations that would have been accepted, and no test read the message: it could be
  blanked and the suite stayed green. `Says_what_a_range_looks_like_when_it_refuses` asserts it whole —
  `'first' is not a valid page range. Expected 5, 2-7, 3- or -4.` The message is the part of a refusal a
  user acts on, so it is part of what the method promises.
- **`3-3` was never parsed.** Every closed range in the file ended above where it began, which is why
  `end >= start` could be weakened to `end > start` with nothing failing: the one spelling that tells the
  two comparisons apart was missing. `Parses_the_notation` carries `[InlineData("3-3", 3, 3)]` now, and the
  range it produces formats back to `3` — `Parse("3")` and `Parse("3-3")` were always the same range, and
  only the dash spelling was unproven.

Both are `Killed` in the run above, and the suite went from 906 test cases to 908. What the six survivors
left say is narrower than what the first eight did, which is the point of reading the table rather than the
number.

### What survives now

All six are equivalent mutations or unreachable code: the guard they delete is restated by the code a moment
later, so no test can tell the mutant from the original without asserting the order two guards are written
in.

| Line | Mutation | Reading |
| --- | --- | --- |
| 106 | `single >= 1` dropped from the single-page branch | equivalent: `TryCreate(single, single)` refuses the same values a moment later, so the guard restates a rule rather than enforcing one |
| 109 | `trimmed.IndexOf('-', separator + 1) >= 0` narrowed to `> 0` | equivalent: the search starts at index 1 or later, so the two comparisons cannot disagree |
| 124, 129 | the `>= 1` guard dropped from a `TryParse` chain | equivalent: the same values are refused by `TryCreate` |
| 196 | `from < 1 \|\| to < from` weakened to `&&` | equivalent from `Parse`: `end >= start` has already been checked, so the second half cannot be true when `TryCreate` is reached |
| 199 (not covered) | `return false` replaced with `true` | unreachable: the guards above refuse first, so no test can reach the line. The one survivor that is about the shape of the code rather than about the tests |

None of them is worth a test. An assertion that pinned one down would assert the order two guards are
written in, and would break on the day the guard moved rather than on the day the behaviour changed. What
they say is that `TryParse` and `TryCreate` overlap — the guards are a second statement of a rule `TryCreate`
enforces — and that overlap is where the guards could go, not where the suite is thin.

### Two of the seventy were caught by the clock

`Detected` is not all assertions. Two mutants hang instead of failing:

| Line | Mutation | Why it does not return |
| --- | --- | --- |
| 152 | `IsOpenEnded ? documentPageCount : To` with the condition forced `false` | an open-ended range resolves to `int.MaxValue`, so `Enumerate` has two billion pages to hand out |
| 175 | `page++` in `Enumerate` decremented | the loop counts down from 1, wraps at `int.MinValue` and keeps going |

Stryker records `Timeout` and counts it as detected, which is right: an enumeration that never terminates is a
failure a caller would live through, and the suite noticed it by not returning. But a hang is a weaker witness
than a failing assertion — it says the harness ran out of patience, not which test was watching — so the two are
named here rather than folded silently into the seventy. Neither is worth a test that asserts on a duration:
that test would be slower and less reliable than the timeout that already catches them.

### Two reports, two trees, and where the two points came from

Both reports were kept, and diffing them by mutant says what changed where a score cannot:

| Report | Detected | Undetected | Score | Survivors |
| --- | --- | --- | --- | --- |
| before the two tests | 68 | 8 | 89.47% | 86, 106, 109, 124, 129, 135, 196, 199 |
| after them | 70 | 6 | 92.11% | 106, 109, 124, 129, 196, 199 |

Two mutants changed verdict and no other one did: line 86, where the refusal's message could be blanked, and
line 135, where `end >= start` could be weakened. Both are `Killed` now because a test reads them. The two
points between the scores are those two tests, and nothing else in the file moved.

A third reading printed 90.79%, which is 69 detected of 76: one more than the run before the two tests and one
fewer than the run after them, which is the count a tree with one of the two tests in place would produce. Its
report was deleted before it could be diffed, so that number is quoted from the run rather than from a file;
the two reports above are the evidence.

The run recorded on this page was then repeated against the same tree: all 102 verdicts identical, 1 minute 27
seconds the first time and 1 minute 34 the second. Between two runs of one tree the clock varies and the
verdicts do not, so the comparison worth making is between reports rather than between scores:

```powershell
python .\docs\tools\mutation-summary.py .\artifacts\mutation\PageRange\reports\mutation-report.json `
    --compare .\artifacts\mutation\SharpCorePDF.Domain\reports\mutation-report.json
```

It prints both tallies and every verdict that moved, by file, line, mutator and replacement. That is how the
paragraph above is known rather than remembered: an earlier draft of this page claimed the score drifted by two
points between runs of one tree, which the reports do not support.

## Why nothing gates on it

`thresholds.break` is 0, so a red run means Stryker could not finish — never "the score dropped". The
reason is not modesty about the suite: a floor has to be a number that was measured and then agreed to be
held, and no layer has been swept yet. `docs/adr/0002-new-code-coverage-1.1.0.md` records the same lesson
from the other side, where a 90% floor on new code existed before a tree met it and `1.1.0` shipped under a
signed exception instead. `high: 90` and `low: 80` colour the report; they fail nothing.

When a baseline exists, raising `break` is one line in `stryker-config.json` and one deliberate commit, and
the run summary `mutation.yml` writes is what it would be derived from.

## Mutants that do not compile

Where a mutation leaves the C# uncompilable — a removed assignment to an `out var`, a `return` the flow
analysis needs — Stryker records `CompileError`, drops the mutations in that method (its log calls this
Safe Mode) and carries on. They take part in neither half of the score because no test was ever asked about
them: they cost a compile each and they say nothing about the suite. The scoped run above recorded 9 in
`PageRange.cs`, and the report of a run narrowed to that one file still records several hundred across the
layer, which is the shape of this codebase's guard clauses rather than a fault in the tests.

## The second record: the image code

Recorded: 2026-10-31, Windows 11 x64, .NET SDK 11.0.100-rc.3.26522.148, `dotnet-stryker` 5.0.0, `Release`,
`Standard`, `net11.0`. Three files of `SharpCorePDF.Infrastructure` — the colour space an image states, the
pixels its payload becomes, and the filters it is decoded through — taken because they are the newest code
in the tree and because the first of them had no suite of its own before this reading:

```powershell
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Infrastructure.csproj `
    --mutate "**/PdfSampleSpaces.cs" --mutate "**/ImagePixels.cs" --mutate "**/PdfImageFilters.cs" `
    --output artifacts/mutation/images
```

Four readings of the same three files, each taken after the tests the reading before it produced:

| Reading | `PdfSampleSpaces.cs` | `ImagePixels.cs` | `PdfImageFilters.cs` | Scoped total |
| --- | --- | --- | --- | --- |
| before the tests | 48.84% (21/43) | 86.49% (64/74) | 59.40% (158/266) | 63.45% |
| after the resolution tests | 88.24% (30/34) | 86.67% (65/75) | 59.02% (157/266) | 67.20% |
| after the profile and empty-table tests | 97.06% (33/34) | 86.67% (65/75) | 59.02% (157/266) | 68.00% |
| after the equivalences removed and the pixel tests | 100.00% (32/32) | 95.89% (70/73) | 59.02% (157/266) | 69.81% |

The denominator moves between rows because the code did: the last reading removed a guard and a conditional,
so two mutants in each of those files stopped existing rather than started being killed. That is the honest
way to read a score that rises by removing code — the page states what changed, not the number.

### The survivor that was a bug, not a gap

The first reading's most interesting survivor was not an unasserted line but a wrong one: `ImagePixels`
returned no pixels for an image whose `filter` was `null`, on the reasoning that a payload with nothing to
undo had nothing to read. Every image a test covered carried `/FlateDecode` or `/DCTDecode`, so the branch
was covered and the suite stayed green — and the case it refused was the simplest one there is, an
unfiltered image, which is also how an inline image is written when nobody compresses it. The manual had
described that image as the ordinary case all along; the test that found it was written for inline images
and failed for a reason nobody was looking for.

`Exposes_the_samples_of_an_image_that_declares_no_filter` is the test the reading asked for, and the
condition it pins is `filter?.Contains("DCTDecode", …) == true`: what makes a payload unreadable as samples
is a JPEG, not the absence of a chain.

### The survivors that were missing tests

Each of these was a real path no test walked, which is what a survivor of this shape means:

- **A profile that states one component, and one that states four.** Only `/N 3` was ever exercised, so
  `ICCBased` grey and `ICCBased` ink could have been dropped from the switch with nothing failing.
  `Exposes_the_samples_of_a_profile_by_the_components_it_states` reads both.
- **A lookup table that holds nothing.** `when text.Bytes.Length > 0` was never exercised in either
  direction: an empty palette string had never been read. `Does_not_read_a_palette_whose_table_is_empty`
  covers it.
- **A palette whose expansion crosses the budget.** Every budget test was on a single-channel image, where
  the byte count and the pixel count are the same number, so `columns * rows * channels` could be divided by
  `channels` with nothing failing. `Keeps_an_expansion_that_fits_the_budget_exactly` reads a three-channel
  palette at the budget and one byte under it.
- **A sample narrower than a byte in more than one channel.** All the sub-byte tests were grey, where
  `(column * channels) + component` collapses to `column`. `Exposes_four_bit_samples_of_a_three_channel_image`
  reads four-bit RGB, which is the only shape in which the offset arithmetic can be wrong.
- **An image that states no extent.** `width is not > 0` could be loosened to `>= 0` because no fixture
  stated a zero width or height. `Does_not_expose_an_image_that_states_no_extent` reads both.

### The two survivors that were redundant statements

Neither is a missing test, so neither got one:

| Where | Mutation | Why no test can tell |
| --- | --- | --- |
| `PdfSampleSpaces.ReadLookup` | `when text.Bytes.Length > 0` narrowed to `>= 0` | the table becomes entries a few lines later, and `entries == 0` refuses an empty one — the guard restated a rule the code already enforced, so it was removed rather than tested |
| `ImagePixels.Of` | `colorSpace.IsIndexed ? PaletteStride : Components` forced `true` | for every non-indexed kind the two properties hold the same number — one, three or four — so the conditional stated a difference that cannot exist; it is `PaletteStride` now |

### What survives in `ImagePixels`, and why it is not a test

Three mutants, and all three are equivalent rather than unasserted:

| Line | Mutation | Reading |
| --- | --- | --- |
| 179 | the `bits == 8` fast path in `Read` removed | equivalent: the general path computes `shift = 0` and `offset >> 3 == index` for eight-bit samples, so removing the shortcut changes the work and not the answer |
| 186 | `>>` widened to `>>>` | equivalent: `offset` is `index * bits` with both non-negative, and an unsigned shift of a non-negative value is that value |
| 186 | `offset >> 3` widened to `offset >>> 3` | equivalent for the same reason |

`PdfSampleSpaces` has no survivors. Not because its tests are clever: the file is small, its branches are
the spaces it names, and the two statements no test could pin down were deleted instead of asserted.

### The third reading: the capped route

`PdfImageFilters` is the file the two readings above left unexplained: 109 undetected of 266 across four
runs, in the loops that exist so a payload above the inline budget is counted rather than held. The tests
that existed walked those loops through `Decode` with a megabyte of budget, which keeps every route on its
buffered side, and through the copy route only for payloads that fit one chunk. A mutant that moves a budget
comparison is invisible there by construction: the payload fits, so the comparison never decides anything.

Written against the capped route instead — a payload of exactly the budget, one byte under it and one byte
over; a prediction that ends mid-row; a chain whose first filter dropped what the second one needed; a
predicted row wider than a chunk — the same file reads differently. Six readings: the baseline the second
record left, and five runs of this file alone, each taken after the tests the run before it produced:

```powershell
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Infrastructure.csproj `
    --mutate "**/PdfImageFilters.cs" --output artifacts/mutation/image-filters-5
```

| Reading | Detected | Undetected | Score |
| --- | --- | --- | --- |
| with the suite the second record left | 157 | 109 | 59.02 % |
| after the decoder and budget tests | 192 | 69 | 73.56 % |
| after the prediction, LZW and copy tests | 206 | 55 | 78.93 % |
| after the raw-Deflate and crossed-budget chain tests | 208 | 53 | 79.69 % |
| after the wide-row test and the primed buffer | 209 | 52 | 80.08 % |

The denominator moves from 266 to 261 for the same reason it moved in the table above: the code changed
underneath the reading, not the tests. The duplicated predictor question in `CopyAsync` — `HasPredictor`
first, then a null test on the decoder, with a fallback no document could reach — is gone, and the
pass-through decision is now the complement of the four filters this class decodes rather than a list of five
names, so five `or` mutations on that list stopped existing and four took their place.

One run in that table is worth its own sentence, because it is the case the comparing mode of
`docs/tools/mutation-summary.py` exists for: the run after the wide-row test scored the same 79.69 % as the
run before it. Two verdicts had moved — a mutant killed, and a survivor that a *different* test had been
masking — so the score agreed while the tree did not, and reading the survivor lists rather than the number
is what said so.

### The second survivor that was a bug, not a gap

The survivor worth following in that list sat on `TryDecodeOne`'s `default` arm, which answers "not decoded"
for a name the switch does not know. A mutant that turned that answer into `true` survived, which says the
arm was never taken; asking why turned up the reason. `Decode` tested a filter against a list of five names —
`JPXDecode`, `JBIG2Decode`, `CCITTFaxDecode`, `RunLengthDecode`, `Crypt` — and let every *other* unknown name
reach that arm, which reports `Failed`. `Failed` is the signal the page reader turns into
`ImageDataUnreadable`, "the payload could not be decoded", while both
`specs/001-pdf-content-inspection/contracts/warnings.md` and `docs/manual/src/08-inspection.md` say a name the
library does not know is an unsupported filter: nothing was decoded because the decoder is not there, not
because the bytes are damaged.

The manual and the contract were right and the code was the outlier, which is the shape a mutation reading
finds and a coverage reading cannot. `IsPassThrough` is the complement of `IsDecodable` now, so a document
that invents a filter name is reported as `ImageFilterUnsupported` with its stored bytes whatever the name
is. `Reports_a_filter_name_it_has_never_seen_as_unsupported` pins the invented name and
`Reports_a_filter_it_passes_through` keeps the named one, because a fix that widens a condition can just as
easily swallow the case that already worked.

### What survives in `PdfImageFilters`, and why it is not a test

52 mutants, and none of them is a path a test could walk. Reading them one at a time is what says so; they
fall into four groups, and the table is that reading rather than a tally.

| Group | Mutants | Reading |
| --- | --- | --- |
| pooled memory | 13: every `ArrayPool.Return` and `Dispose` in a `finally`, and the four `output.Dispose()` statements that sit in front of `output = null` | the bytes are copied out of the buffer before it goes back, so returning it changes working set and not the answer; `output = null` is what stops the capturing |
| a budget the code asks twice | 13: the four `cap > 0 ? new PooledByteBuffer(Math.Min(cap, CopyChunkSize)) : null` allocations with their `Math.Min`, and `Keep`'s `(true ? data : null)` | a buffer over-sized by a mutant is caught by the guard that follows, which compares the running total with the same `cap`, and `Decode` applies `Buffer` once more at the end — so keeping more than the budget allows is dropped by the second question |
| a branch no document reaches | 9: `buffered ? … : null` in the two early returns, the `default` arm of `TryDecodeOne`, its `catch (InvalidDataException)`, the two `catch (IOException)` arms, and the two predictor null tests in the capped routes | the loop returns *before* those arms whenever capturing stopped; `Decode` passes only the names `IsDecodable` lists; a `MemoryStream` cannot throw `IOException`; and a capped predictor route is entered only when `HasPredictor` was already true |
| work, not bytes | 17: six `ConfigureAwait(false)` flips, `written > 0` loosened to `>= 0`, `PendingRow.Length > 0` loosened to `>= 0`, the three parts of the zlib header test, the two `Normalize` arms for names that pass through either way, and `OpenInput`'s copy of a memory that is always array-backed | a continuation, a zero-byte write and an allocation size are not what a caller receives |

The two `Normalize` arms are worth naming: `CCF` and `RL` are expanded for a reader that would refuse them a
line later either way, so a mutant that blanks those strings is one no document can tell apart — the name
only has to arrive at `IsPassThrough` intact. The header test is the same kind of thing: `(b0 << 8) & b1` is
zero for every pair of bytes, so the checksum term stops discriminating, and a *valid* raw Deflate block can
never begin with a byte whose low nibble is eight, which is the only input that mutant would send down the
wrong branch.

The suite this record produced is `tests/SharpCorePDF.Tests/PdfImageFilterTests.cs`: 45 cases over the three
routes — `Decode` within the budget, `Decode` past it, and `CopyAsync` — with each boundary stated in the name
of the test that reads it, because a budget that is off by one is exactly what a mutation of `>` to `>=` is
looking for.

## The fourth record: the extension conveniences

A review of the two extension files raised three findings — a copy a `ReadOnlyMemory<byte>` return type did not
need, a destination path the library does not confine, and a hand-driven async enumerator — and only the first
is a change a test can see. The run was scoped to the two files:

```powershell
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Application.csproj `
  --mutate "**/PdfContentReaderExtensions.cs" --mutate "**/PdfMergerExtensions.cs" `
  --output artifacts/mutation/extensions
```

| Reading | Mutants | Detected | Undetected | Compile errors | Ignored | Score |
| --- | --- | --- | --- | --- | --- | --- |
| before the new tests | 72 | 3 | 54 | 4 | 11 | 5.26 % |
| after them | 72 | 16 | 41 | 4 | 11 | 28.07 % |

The first reading is the one worth keeping. `ReadPageDetailAsync` — the member whose enumerator the review
asked about — had **no test at all**: the mutation that removes its `return detail;` came back `NoCoverage`, so
the suite had never run the line, let alone asserted it. The four null guards of the two materialising reads
were in the same state, which also said that nothing had ever asserted that the stream is *sized* to the
payload. `PdfContentReaderExtensionsTests` was written against that reading: two tests that pin the returned
memory to the buffer the copy wrote into, one for a payload of unknown length, one for the guards, and two for
the page detail — and the second reading shows the `return detail;` block killed and the four guards killed
with it.

### What survives in the two files, and why it is not a test

41 mutants survive the second reading, and they fall into five groups.

| Group | Mutants | Reading |
| --- | --- | --- |
| the pre-sizing conditional, both halves | 8: four at the image's `ByteLength is > 0 and <= int.MaxValue ? … : 0` and four at the file's | only the four that flip the condition *false* are killed, by `Assert.Equal(PayloadLength, segment.Array.Length)`: they leave the stream growing, so its buffer is 524 288 bytes for a 400 000-byte payload. The four that pin it *true* — and `>= 0`, and `< int.MaxValue` — ask for exactly the capacity the original asks for, so no payload can tell them apart |
| `ConfigureAwait(false)` flipped to `true` | 3: the two materialising reads and the page detail | a continuation, not a byte a caller receives; the test host installs no synchronization context, so both continuations run on the pool |
| `SaveImageDataAsync`, `SaveEmbeddedFileAsync` and `OpenDestination` | 15, every one `NoCoverage` | these three have no test in the suite: the two `Save*` members and the helper they share are reached by no case at all. That is a gap the reading found and this change does not close — it touches those members' documentation, not their behaviour, and gate 6 says a fix does not grow a scope |
| `MergeToFileAsync`'s guards and file handling | 11: `ThrowIfNull(plan)`, `ThrowIfNullOrWhiteSpace`, the `string.IsNullOrEmpty(directory)` test, `CreateDirectory`, `bufferSize: 64 * 1024`, `FileShare`, `useAsync` and the `FlushAsync` | none of them changes the bytes that land on disk: the guard clauses are redundant with the `ArgumentNullException` the merger itself raises, `Path.GetFullPath` never yields a null directory for a path it just made absolute, and a destination that already exists needs no `CreateDirectory` |
| `MergeToBytesAsync` and `MergeAsync` guards and statements | 4: `ThrowIfNull(plan)`, `ThrowIfNull(sources)`' neighbours, and the two statements that build the plan | `ThrowIfNull(sources)` is this file's one kill, because `sources.Select(…)` would raise `NullReferenceException` where the test expects `ArgumentNullException` — that difference is what makes it observable. `ThrowIfNull(plan)` is not: the merger raises the same exception itself for a null plan. The plan construction the mutants remove is then done again by the call below |

**Correction, from the fifth record below.** Three of those readings did not survive being tested, and each was
the same mistake in a different dress: a statement looks unobservable while the only test of it cannot tell
which layer acted.

- The fifteen `NoCoverage` mutants over `SaveImageDataAsync`, `SaveEmbeddedFileAsync` and `OpenDestination` were
  a real gap. The fifth record closes it, and **eleven of the fifteen** are now killed — every one the two
  members' behaviour actually decides.
- "The guard clauses are redundant with the `ArgumentNullException` the merger itself raises" was the wrong thing
  to conclude from `PdfMergerExtensionsTests.Refuses_a_missing_argument`. That test uses the real merger, which
  refuses a null plan of its own accord, so it passes whether the overload guards or not. Put a merger in front
  of the overload that answers anything it is given, and **eight of those fifteen** are killed — the two
  `ThrowIfNull(plan)` guards and `ThrowIfNull(destination)` on nothing but that stub.
- `ThrowIfNull(sources)` was **not** that file's one kill — the statement inside `MergeToBytesAsync` was. It
  survives for the same masking reason: `Enumerable.Select` raises `ArgumentNullException` for a null source
  before `MergePlan` ever sees the call.
- The `string.IsNullOrEmpty(directory)` tests were not unobservable either. `Path.GetFullPath` does yield a
  non-null directory for every path it is given here, but the mutation is not "the directory is empty": it is
  the negation inside a `!`, so the mutant skips `CreateDirectory` and the write fails with
  `DirectoryNotFoundException`. All three are killed by a destination two folders deep.

The `ConfigureAwait` group is the repository's largest single answer and the same one recorded in the third
reading: a continuation is not part of what a caller receives.

### The mutant this run cannot express

The change itself — `ToArray()` replaced by `GetBuffer().AsMemory(0, (int)Length)` — has **no mutant**. Stryker's
`Standard` level mutates operators, string literals and statements; it does not mutate an integer literal, and
neither `ToArray` nor `AsMemory` is a construct it can substitute. So the reading cannot carry this decision,
and the suite carries it instead: `Materialises_an_image_as_a_view_of_the_buffer` asserts that the returned
memory's array *is* the destination stream's buffer (`Assert.Same`), which a copy cannot satisfy. Restoring
`ToArray()` in either member fails both tests — run, observed, restored — so the revert that Principle IV asks
about is a demonstrated one rather than an assumed one.

## The fifth record: closing the gaps the fourth found

The fourth record ended with three things it had not fixed: fifteen `NoCoverage` mutants over the two `Save*`
members and the `OpenDestination` helper they share, eleven survivors in `PdfMergerExtensions` that had been read
as equivalent, and eight over the pre-sizing conditional. Each was then tested rather than argued about. Same
scope, same command:

```powershell
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Application.csproj `
  --mutate "**/PdfContentReaderExtensions.cs" --mutate "**/PdfMergerExtensions.cs" `
  --output artifacts/mutation/extensions3
```

| Reading | Mutants | Detected | Undetected | Compile errors | Ignored | Score |
| --- | --- | --- | --- | --- | --- | --- |
| second, as the fourth record left it | 72 | 16 | 41 | 4 | 11 | 28.07 % |
| third, after the save tests and the merger stub | 72 | 37 | 20 | 4 | 11 | 64.91 % |
| fourth, after the whitespace path is asserted to its `ParamName` | 72 | 39 | 18 | 4 | 11 | 68.42 % |

In the last reading `PdfContentReaderExtensions.cs` kills 30 of its 41 scored mutants (73.17 %) and
`PdfMergerExtensions.cs` 9 of 16 (56.25 %), and both files' `NoCoverage` column is zero.

### The tests the reading asked for

- `PdfContentReaderExtensionsTests.Save.cs`, the second part of the class: an image and an embedded file are
  written where the caller named them and read back byte for byte, a destination two folders deep is created, a
  file that is already there is truncated, and a refused call leaves no file behind.
- `PdfMergerExtensionsTests` gains the same four for `MergeToFileAsync`, and a `RecordingMerger` — a merger that
  writes a known payload and answers *any* plan, a null one included — with the three refusals over it: a missing
  plan, a path that is not a path, a missing list or destination.
- `Materialises_a_payload_whose_stated_length_is_impossible` hands `ByteLength = -1` to both materialising reads.
  The original falls back to a stream that grows; the mutant that pins the conditional true would pass minus one
  to `MemoryStream`'s constructor and throw before the copy ran. That is what kills the `Conditional (true)` and
  the `Logical` mutant per site, which the fourth record had written off as "asking for the capacity the original
  asks for".

### Two guards whose exception type was not enough

`ArgumentException.ThrowIfNullOrWhiteSpace` and `Path.GetFullPath` refuse a whitespace path with the same
exception, so deleting the guard changes nothing a caller can see — except the name of the argument. The refusal
tests now assert `ParamName == "destinationPath"` for that one case: the difference between "the overflow refused
this" and "the framework underneath did". It is the only assertion in the suite that reaches into an exception's
fields, and it is there because the alternative was a survivor explained as equivalent when it was not.

### What is left, and why each of the eighteen is not a test

| Survivors | Where | Reading |
| --- | --- | --- |
| `ByteLength is > 0` → `>= 0` | 2, one per materialising read | equivalent: the true branch computes `(int)ByteLength`, which for zero is the same zero the false branch returns |
| `<= int.MaxValue` → `< int.MaxValue` | 2, the same two lines | differs only at a stated length of exactly `int.MaxValue`, where the original asks for a two-gigabyte buffer. A test that killed it would have to allocate one |
| `ConfigureAwait(false)` → `true` | 5 in the reader, 3 in the merger | a continuation is not a byte a caller receives, and the test host installs no synchronization context |
| `bufferSize: 64 * 1024` → `64 / 1024`, and `useAsync: true` → `false` | 2 + 2, the two `FileStream` constructors | neither is observable: a zero buffer means no buffering, and a synchronous stream still serves `WriteAsync` |
| `await file.FlushAsync(cancellationToken)` deleted | 1 | the `await using` disposal flushes before the method returns. It stays because it flushes *under the caller's token*, before success is reported — a cancellation arriving after the merge has a defined place to surface |
| `ThrowIfNull(sources)` deleted | 1 | equivalent through `Enumerable.Select`, which raises `ArgumentNullException` for a null source before the plan is ever built |

The four compile errors are unchanged and are still not the suite's: Stryker's LINQ mutator rewrites
`PageRange.Single` to `PageRange.SingleOrDefault`, which cannot compile against a static factory.

### What this record did not change

Production code is untouched: the two extension files are the ones the fourth reading measured, and every new line
is a test. `MergeToBytesAsync`'s `ToArray()` still copies, on purpose (research D2); no threshold moved, `break`
stays at 0, and no `ignore` was added to the configuration. The score went from 28.07 % to 68.42 % because
eighteen mutants stopped being invisible — not because the number was flattered.

