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

```powershell
dotnet tool install --tool-path ./.tools dotnet-stryker --version 5.0.0

# One layer. The five projects under src/ are four layers and a meta package: SharpCorePDF itself has no
# code to mutate.
./.tools/dotnet-stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj --output artifacts/mutation/Domain

# One file, when the question is about one file
./.tools/dotnet-stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj --mutate "**/PageRange.cs"

# Only the lines a branch changes against main
./.tools/dotnet-stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj --since:main

# The reading, out of the report the run leaves behind
python ./docs/tools/mutation-summary.py ./artifacts/mutation/Domain/reports/mutation-report.json --detail
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

### The score moves by about two points

**The same command printed 92.11% before the two tests and 92.11% after them, with different findings.** The
three pre-test reports that were kept recorded eight, seven and seven undetected: 86, 124, 129, 135, 196 and
199 survived in all three, and 106 and 109 in two of the three. Add that up and the pre-test reading of
92.11% — six undetected — is the one run in which neither 106 nor 109 survived; the run above reaches the
same six without 86 or 135, which the two tests now kill.

Since nothing at 106 or 109 changes behaviour, a run that records one of them as detected is a run where a
test failed or hung for a reason of its own: a flaky assertion, or a timeout on a machine that was busy. The
test responsible has not been identified, and it is a finding in its own right — a survivor that disappears
in some runs is a question about the suite rather than about the mutant.

The arithmetic for a floor follows from that: four readings of one command, spread over about two points, so
a threshold set at the last number seen would go red on a busy runner. That is why `break` stays 0 until a
layer has been swept more than once, and why the report is the thing to read.

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
