# Release checklist

What has to be true before a version number reaches nuget.org.

This is the gate list, not the manual. The mechanics — the trusted publishing policy's fields, the
tag rule, the dry-run handshake, the `--skip-duplicate` behaviour — are in the README under
[Publishing](https://github.com/MPCoreDeveloper/SharpCorePDF#publishing) and are deliberately not
repeated here. What follows is the part a person has to decide, and the part a checklist can prove.

Story and epic numbers below come from the project's internal plan, which is not in this repository. Every
one of them is described where it is cited — "the story that narrows the shipped leg", "the changelog and
symbols story" — so the gate reads without it, and nothing here depends on a document a reader cannot open.

## Where the repository stands, 2026-10-17

| Fact | State |
| --- | --- |
| Current `<Version>` | `1.0.0` (`src/Directory.Build.props`) |
| Published on nuget.org | **five packages at `1.0.0`**, listed since 2026-10-17: `SharpCorePDF` (the entry point) plus `SharpCorePDF.Domain`, `.Application`, `.Infrastructure`, `.DependencyInjection`. The four layers also still carry `1.0.0-preview.1` from 2026-10-15 |
| Tags | `v1.0.0-preview.1` (2026-10-15) and `v1.0.0` (2026-10-17), the source of the five `1.0.0` packages |
| Packages `dotnet pack` would produce | five at `1.0.0`: `SharpCorePDF` (the entry point, no assemblies) plus `SharpCorePDF.Domain`, `.Application`, `.Infrastructure`, `.DependencyInjection` |
| Build | `dotnet build SharpCorePDF.slnx -t:Rebuild` → 0 warnings, 0 errors, `net10.0` and `net11.0` |
| Tests | 549 pass on each leg |
| Coverage | 92% of the shipped assemblies, imported into SonarCloud by the scan; the 90% floor is a condition on the quality gate, where the suite's own 99% is not counted |
| External prerequisites | the trusted publishing policy, whose subject list must hold a line for the bare `SharpCorePDF` as well as the `SharpCorePDF.*` glob — the glob alone refuses the entry point with 403 — and the `NUGET_USERNAME` secret. Both were confirmed the hard way on the first `1.0.0` attempt; neither is visible from this repository. **Automatic Analysis being off** on sonarcloud.io belongs in this row for the same reason: no file here sets it, and both workflows stop while it is on |

**How to read the feed, because this page got it wrong once.** The earlier version of this table reported
"nothing published" on the strength of a 404 from
`api.nuget.org/v3/registration5-semver1/sharpcorepdf.domain/index.json`. That endpoint serves the SemVer1
view, and a package whose only versions are SemVer2 prereleases — `1.0.0-preview.1` has a dot in its
prerelease label, which is what makes it SemVer2 — is invisible there: the blob 404s while the package is
live, listed and downloadable. The endpoints that answer the question are
`api.nuget.org/v3-flatcontainer/<id>/index.json` and `.../registration5-gz-semver2/<id>/index.json`; a 404
from *those* is the evidence of an unpublished id. Four packages sat on nuget.org for two days behind a
false negative.

**What that costs, in version numbers.** `1.0.0-preview.1` is spent: it is on the feed, immutable, and the
four ids carry download history and a licence grant attached to it. So the next version for those four is
`1.0.0`, and the numbers this page previously called free — `preview.2` and `preview.3` — stay unused
because nothing needs them: a version nobody was served is a version nobody has to be told about. The
freedom that remains is the one that matters here — `SharpCorePDF` had never been pushed by anyone, so 1.0.0
is the first time the entry-point id is claimed, and G0 is what decided it. It was claimed on 2026-10-17.


## The gates

| # | Gate | Why it exists | True today? | Closed by |
| --- | --- | --- | --- | --- |
| G0 | The package id is the one we want to own forever | the first push claims the id; renaming later means a new id and no download history | **Yes — decided 2026-10-17**, see G0 below | — |
| G1 | The public surface has stopped moving | a preview is a promise a consumer builds against | **No** — 1 of 12 surface stories is in review; 1.0.0 was cut over it, see the sequence below | the remaining surface stories |
| G2 | One shipped leg, `net11.0` | 1.0 must not withdraw a target framework it once advertised | **No** — packages carry `lib/net10.0` and `lib/net11.0`, and `publish.yml` asserts both | the story that narrows the shipped leg |
| G3 | `CHANGELOG.md` exists and describes the version being published | a version with no notes is a version nobody can evaluate | **Yes, 2026-10-17** — the file holds a `[1.0.0]` entry, written in the same commit as the bump | keep the entry current in the commit that bumps `<Version>` |
| G4 | Symbols reach a debugger, and the build is proven reproducible | a stack trace from a consumer is useless without source mapping | **Partly** — `<Deterministic>true</Deterministic>` is set and Source Link rides on `RepositoryUrl`, but no symbol package is packed and no two builds are compared | the changelog and symbols story |
| G5 | Trusted publishing policy and `NUGET_USERNAME` exist | without them the workflow can publish nothing | **Yes, 2026-10-17** — the OIDC exchange minted a key that pushed all five (run `39025525848`); the first attempt was refused `403` on the bare id until its subject line was added to the policy | nuget.org → Trusted Publishing; repository secrets |
| G6 | A dry run is green on the commit that will be tagged | the local-feed handshake is the only rehearsal available | **Yes, 2026-10-17** — dispatch `39023732538` with `publish: false` pushed all five `1.0.0` packages into the runner's local feed on `a1d8f6a` | Actions → publish → `publish: false` |
| G7 | The tag equals `<Version>` | a tag must name the version, prerelease suffix included | **Yes** — `v1.0.0` names `<Version>` `1.0.0`, and `publish.yml` refuses a mismatch | `v` + the exact `<Version>` |
| G8 | The published packages install and open a document | the feed page is not the product | **Yes, 2026-10-17** — `1.0.0` installed from nuget.org into an empty `net11.0` console app; `AddSharpCorePDF()`, a merge of two sources (3 pages, 1,976 bytes, PDF 1.7, 0 warnings) and an inspection of the output that reported it readable with outlines | G8 below |
| G9 | The quality gate is green on the commit being released | a release is the one build nobody re-runs; the coverage and the analysis have to be true of the tree, not of a branch it came from | **Yes, enforced twice** — `ci.yml` fails on the gate, and `publish.yml` reads it again for the tagged commit before it packs anything; see G9 below | keep it green; tag the commit whose analysis is the newest on `main` |

## The 1.1 boundary amendment (CA-001)

Recorded on 2026-10-21, before any 1.1 phase branch is cut. CA-001 is the entry gate for 1.1, not a
change to the published 1.0.0 facts above.

| Item | State | Evidence |
| --- | --- | --- |
| Constitution | **2.0.0 ratified** — Principle VII defines a PDF creation, editing and reading library and keeps rendering, conversion, OCR, conformance validation, services and CLIs outside it | `.specify/memory/constitution.md` |
| ADR | **Accepted** — records the product decision, the PDFsharp 6.5 / PdfPig 0.1.x capability target, the exclusions and the consequences | `docs/adr/0001-pdf-platform-boundary.md` |
| Product statement | **Updated** — README, manual preface and chapter 1, chapter 2 boundary, chapter 14 status, `docs/README.md` and `CHANGELOG.md` agree with the amendment | the same commit as this record |
| Release version | **1.1.0 planned**; `<Version>` stays `1.0.0` until the release commit, and the 1.1 entry is written with the bump (G3) | `src/Directory.Build.props`, `CHANGELOG.md` |
| 1.0 baseline | **561 tests pass on `net10.0` and `net11.0`; Release build has 0 warnings and 0 errors** at `a5d816c` | CI and the local release run |
| Parity scorecard | **Phase 0 complete** — 21 rows (16 “Must”, 5 non-goal), each with an owner, a named test and a validation command; planned rows are closed by T038/T058/T079/T096 | `docs/parity.md` |
| Parity corpus | **Phase 0 complete** — 95 deterministic fixtures (60 born-digital, 5 labelled, 14 images, 5 forms, annotations, signatures, 3 PDF/A candidates) | `tests/SharpCorePDF.Tests/Assets/parity/`, `docs/parity/README.md` |
| API review | **Phase 0 complete** — placement, immutability, naming, no new package and no `System.Drawing`, `SkiaSharp` or `PdfSharp`/`PdfPig` reference | `docs/api/1.1-api-review.md` |
| Measurement baseline | **Phase 0 complete** — creator and editor scenarios added before implementation; §9 records 14.69 ms and 3.64 ms against SC-002/SC-003 | `docs/measurements.md` §9 |

**Before Phase 1 starts**, all of the following must be true:

- [x] Constitution 2.0.0 is ratified.
- [x] ADR-001 is accepted.
- [x] README, the manual and the changelog describe the expanded boundary and the remaining exclusions.
- [x] This amendment is recorded in the release checklist.
- [x] `docs/parity.md` has no unowned “Must” row.
- [x] The parity corpus is reproducible from `docs/tools/make-fixtures.py --parity`.
- [x] The Phase 0 API review is recorded in `docs/api/1.1-api-review.md`.
- [x] The 1.0 baseline measurements are re-read and the creator/editor budgets are recorded.

The 1.1 release still clears G1–G9. CA-001 only settles **what the product is**; it does not replace
the quality, determinism, performance, documentation or publishing gates.

## The 1.1 scope decision (D3, 2026-10-22)

Recorded before `1.1.0` is tagged, and it changes what that tag has to contain, not what the product is.

| Item | State | Evidence |
| --- | --- | --- |
| Scope | **D3** — `1.1.0` ships the capability implemented and evidenced on the branch; the remaining “Must” rows move to `1.2.0` with their task numbers | [`tasks.md`](../specs/003-pdf-platform-parity/tasks.md) Gate 0, [`spec.md`](../specs/003-pdf-platform-parity/spec.md) “Scope amendment” |
| Scorecard | **Rewritten in the tag commit** — twelve rows name `1.2` in their Release cell; no row is left `Planned`, and no `1.1` row lacks a test and a command | `docs/parity.md` “1.1 scope decision” |
| The `002-page-authoring` question | **Met** — page, text, 152 × 87 RGBA PNG, in-memory merge, byte-identical rebuild, qpdf/pypdf/PyMuPDF evidence | `docs/parity.md` rows 1 and 4, `docs/measurements.md` “Issue-closure re-run” |
| Evidence | **One command** — `docs/tools/verify-cover.py` runs C1–C3, asserts the recorded values and prints the transcript for the pull request; the rehearsal on 2026-10-23 broke none of them | `docs/measurements.md` §8, `tasks.md` C5 |
| Changelog | **G3 blocks the tag** until the 1.1 entry states both what ships and what is deferred | `CHANGELOG.md`, `docs/release-checklist.md` G3 |

Two gates carry the decision into the release run: **T105** refuses a “Must” row without evidence for the
release it names, and **G3** refuses a tag whose changelog does not say what the version does not do.

## G0 — the package id, decided before the first push, not after

`SharpCorePDF` is not on nuget.org. That is the last moment at which the name is still a decision
rather than a fact, and it is the gate most likely to be waved through because nothing about it fails
a build.

The name sits one transposition away from `PdfSharpCore`, a package with 38 million downloads and a
similar purpose. On a nuget.org search page the two ids will appear side by side, and a consumer
scanning results will not read the difference. Three consequences, in ascending order of cost:

- **Discoverability.** A search for "pdfsharp core" will surface this package at random moments and
  never for the reason its author intended.
- **Trademark.** PdfSharp is empira Software's name. The .NET Foundation's CLA asks a project to
  describe its trademarks, and a name that reads as a derivative of another product is a question the
  reviewer has to ask (see `docs/foundation-readiness.md`).
- **Permanence.** The id, once pushed, is the id. `SharpCorePDF.*` is also what the trusted publishing
  policy's glob names, so changing it later means editing the policy, the five project files, the
  README's five package links, and every `dotnet add package` line anyone has written down.

Deciding here costs an hour. Deciding after the first preview costs a version and a redirect nobody
will follow.

### Decision, 2026-10-17

**The name stays, and the id claimed first is `SharpCorePDF` itself.**

The second half is what makes the first half affordable. On nuget.org a consumer should not have to work
out which of four package names to install, so `SharpCorePDF` is published as a meta-package: no assembly
of its own, a dependency on the four layers, and the README and the icon on its page. The answer to
"which package do I need" becomes the product's name.

What that buys:

- `dotnet add package SharpCorePDF` is the whole install, and it is the id someone types when they
  already know the name.
- The four layers keep their own ids, pages and API snapshots. A caller who wants only the domain model
  still takes only the domain model, and the layering stays something the compiler enforces, because the
  assemblies stay separate.

What it does not buy: the transposition is still there. `PdfSharpCore` will appear next to it in a search,
which is why `NOTICE` states the non-affiliation in one paragraph and `docs/foundation-application.md`
states it again for the reviewer. Claiming the name early is the only part of that which a repository can
do anything about.

**The other way to get one search result, and why it is not taken:** unlisting the four layers. An
unlisted package still resolves as a dependency and still installs by exact version, so `SharpCorePDF`
would work and a search for the name would return exactly one entry. It is rejected because it buys the
cleaner result by hiding the thing that makes the layering worth publishing — `SharpCorePDF.Domain` with
no dependency at all — and because the entry point's description, icon and README on the first result are
enough to make the right name the obvious one without hiding the others.

Two consequences the first push had to respect. Both were exercised on 2026-10-17, the second the hard way:

1. `SharpCorePDF` goes in the **first batch**, not in a later one. Until a version of it exists, nobody
   owns the id, and an unclaimed id named after a product is exactly what gets registered by someone else.
   The five packages went out in one `dotnet nuget push`, so the id was claimed by the same run that
   published the layers it depends on.
2. The trusted publishing policy needs **both** `SharpCorePDF` and `SharpCorePDF.*`. The glob matches the
   four layers and not the bare id; a policy holding only the glob pushes four packages and refuses the
   fifth — which is exactly what the first attempt did, `403` on `SharpCorePDF.1.0.0.nupkg`, before any
   other file was sent. Nothing was published by that attempt and no version was consumed.

## G1 — what "the surface has stopped moving" means concretely

The surface did move this week, in a way worth naming: `MergeResult.Warnings` and
`PdfDocumentInfo.Warnings` changed from `IReadOnlyList<string>` to `IReadOnlyList<PdfWarning>`.
That was the right change, and it was made before a release, which is the only time it is
cheap. It is also evidence that the surface work is not finished: the remaining stories still rewrite what
inspection reports and what encryption refuses, and several of them add enum members or read-model
properties.

A preview published today would freeze that mid-state into a baseline that the API-compatibility check
would then hold the next build against.

## G2 — the leg that 1.0 removes

The repository currently ships `net10.0` and `net11.0` in every package, and the README promises both
("Every package targets `net10.0` and `net11.0` and carries both legs"). The narrowing story deletes the
`net10.0` leg outright, and `publish.yml` stops asserting it.

A preview published before that story therefore does two things at once: it advertises .NET 10 support,
and it creates an ApiCompat baseline that contains a `lib/net10.0` asset. The removal in 1.0 then
appears in the compatibility report as a lost target framework — a true statement, but a strange way
to introduce a first stable version.

**The rule this produces:** publish the preview series *after* the leg is narrowed, or publish it knowingly
as a two-leg preview and say so in the notes. Do not discover the difference after the fact.

**What 1.0.0 did:** took the second option. It ships both legs, and the changelog and the release notes on
every package page say so — the first option stopped being available on 2026-10-15, when `preview.1`
advertised `net10.0` and `net11.0` on a public feed.

## G3, G4 — the file a consumer reads and the symbols a debugger needs

`CHANGELOG.md` exists as of 2026-10-17, and it opens with the honest version of the situation: four layer
packages at `1.0.0-preview.1` are on the feed, the entry point is not, and the versions bumped in between
never reached one. From here the rule is the one G3 states — the entry is written in the same commit that
bumps `<Version>`, and `[1.0.0]` is that entry, including the two limits a consumer should know before
installing (no symbol package, no signing) and the framework legs it does and does not carry.

Symbols: nothing is packed for a debugger today. `IncludeSymbols` is absent, so no `.snupkg` is
produced — `dotnet pack` was run on 2026-10-17 and produced five packages and zero symbol packages — and
`<Deterministic>true</Deterministic>` is the SDK default rather than a proven fact. The reproducibility
requirement is that two builds be compared byte for byte; until that comparison runs, "reproducible" is a
setting rather than a result.

## G5, G6 — the parts that live outside the repository

The trusted publishing policy is owned by the nuget.org account, not by this repository. Its four
fields (owner, repository, workflow file name, environment) and the two subjects — `SharpCorePDF` and
`SharpCorePDF.*` — are recorded in the README; what the repository cannot tell you is whether the policy
exists, whether its scope includes *pushing new packages* as opposed to only *new versions of existing
ones*, or whether `NUGET_USERNAME` is set. A wrong answer makes the first push fail informatively — which
is why G6 exists: run the workflow with `publish` left false, see the local-feed handshake succeed, and
only then spend a version number.

## G8 — after the push

1. The five package pages render the README and the icon, and the version badge stops reading `unknown`.
   `SharpCorePDF` is the one to check first: its page must show the four dependencies and no download of
   its own, which is what a meta-package looks like on nuget.org.
2. A scratch project that targets `net11.0` (and `net10.0`, while that leg exists) restores
   `SharpCorePDF` — not the DependencyInjection package — and merges two fixture documents.
3. `docs/tools/validate-pdf.py` passes on the merged output, so the file is not merely readable by the
   library that wrote it.
4. The API-compatibility report is attached to the release notes.

## The sequence, and the version numbers it produces

1. The surface stops moving (G1). **Open when 1.0.0 was cut** — 1 of 12 surface stories is in review, so
   1.0.0 ships with the surface not formally settled. That is a decision recorded here rather than an
   oversight: the alternative was a `preview.4` whose only difference from `1.0.0` is the label on it.
2. Narrow the shipped leg (G2) and land the API snapshot. **Partly** — `PublicAPI.Shipped.txt` exists and
   RS0016/RS0017 are build errors, but the packages still carry both legs, which is what `preview.1`
   advertised; a 1.0.0 that dropped `net10.0` would withdraw a target the feed has already promised.
3. Create the trusted publishing policy and `NUGET_USERNAME` (G5), then run the dry dispatch (G6). The
   policy's subject list must now contain the bare `SharpCorePDF` as well as `SharpCorePDF.*`: the first
   publish of the entry point is this one, and a 403 on it fails the run after the layers are packed.
4. Bump `<Version>`, write the changelog entry in the same commit, and push it. Wait for that commit's
   `ci.yml` run: the analysis it uploads is the one the gate is read from, so the tag has to name the commit
   that run analysed and no later one (G9).
5. Tag `v` plus that exact `<Version>` and push the tag (G7). `publish.yml` re-reads the gate for that commit
   before it packs anything, so a red gate costs the release nothing but the push. Then verify (G8), including
   that the `SharpCorePDF` page shows four dependencies and no assemblies of its own.
6. Symbols, signing and the reproducible-build comparison are what `1.0.1` or `1.1.0` still owe (G4).

The preview numbers are settled by the feed rather than by intention: `preview.1` was published, so it is
history and its four ids carry the version forward to `1.0.0`; `preview.2` and `preview.3` never reached a
feed, so they are skipped rather than reused, and nothing needs to explain them to anyone.


## What `publish.yml` holds, and what it still does not

`publish.yml` asks SonarCloud about the commit a tag names, in a `sonar-gate` job that runs before the
build, before the pack and before the OIDC exchange. Each of the three questions has been answered wrongly
here at least once:

| Question | Read from | Publishing stops when |
| --- | --- | --- |
| Is Automatic Analysis off? | `api/settings/values` → `sonar.autoscan.enabled` | it is on — the snapshot it uploads carries no coverage report, so the coverage condition is never judged |
| Is the newest analysis of `main` of this commit? | `api/project_analyses/search?branch=main` → `revision` | it is not — the green gate belongs to a different tree |
| Is the gate green, coverage included? | `api/qualitygates/project_status?branch=main` | `status` is not `OK`, or the conditions hold no `new_coverage` |

- **The SonarCloud quality gate is a gate on a release now, not only on a run.** `ci.yml` still enforces it
  as it did — `sonar.qualitygate.wait` makes a red gate a red run — and what was missing was the link from
  that run to a tag: a tag could name any commit, and the badge says nothing about which one. The gate job
  closes it by asking for the verdict on the commit being published, and the freshness check is the part
  that makes the other two mean something, because the gate API answers about the newest snapshot of `main`
  and about nothing else.
- **The cost of that check is a rule for tagging:** tag the commit whose analysis is the newest on `main`,
  not a commit that four more have landed on since. A tag on an older commit is refused before anything is
  packed, which is the point — the alternative is publishing a version whose gate was decided by a tree it
  does not contain.
- **A green `ci.yml` run is still not a gate.** `publish.yml` builds and tests the commit itself, and does
  not wait for `ci.yml`, because the two workflows answer different questions and a manual dispatch may not
  have a `ci.yml` run to wait for. What the gate job adds is narrower: the verdict *for the tagged commit*,
  which is the one a release needs.

## G9 — the quality gate, and what "90%" counts

One service holds a number about this repository, and that number is a red-run gate in `ci.yml`:

| Gate | Target | Rules live in | Measured by |
| --- | --- | --- | --- |
| SonarCloud | the project's quality gate on new code as `Sonar way` configures it — `new_coverage` ≥ 80, no new bug, duplication ≤ 3% — plus the Sonar way profile over `src/` and `tests/` | the quality gate on sonarcloud.io; the exclusions in `sonar.exclusions` in `ci.yml`; local silences in `src/.editorconfig` | `dotnet-sonarscanner begin` before the build, `end` after the tests, reading `artifacts/coverage/net11.0.xml` |

The 90% floor is this repository's, not the service's: it is measured locally by
`docs/tools/coverage-on-new-code.py` and is not entered on the quality gate, which runs Sonar way's own 80.
The scan excludes the Spec Kit tooling, `docs/`, `tools/`, `examples/`, `bin/`, `obj/` and `artifacts/`, and
test code contributes no lines to a coverage metric, so the service's number describes the four assemblies the
packages ship plus the entry point. At 1.0.0 that was 92% (`SharpCorePDF.Infrastructure` 91%,
`SharpCorePDF.Domain` 96%, `SharpCorePDF.Application` 100%, `SharpCorePDF.DependencyInjection` 100%), while the
suite that measures them sat at 99%. Counting the tests would raise the number and tell a reader nothing.

A few consequences worth stating before a release:

1. **A gate that is green is a gate that ran.** The scan cannot be skipped silently: a missing
   `SONAR_TOKEN` skips its steps, and a run where they were skipped proves nothing about the analysis. The
   badge is the evidence that the scan ran, which is why it is worth checking the run rather than the diff.
2. **The target is a floor under refactoring, not a goal.** A change that adds a hundred uncovered lines
   and leaves the project at 91% fails the new-code condition, which is the condition that would have
   caught it.
3. **The 90% is a setting, not a property of the tree.** Nothing in this repository declares it: it is
   entered on the quality gate at sonarcloud.io. At the time of writing the project is bound to `Sonar way`,
   the default gate, whose only coverage condition is `new_coverage < 80` — so until 90% is entered there,
   `sonar.qualitygate.wait` enforces 80% faithfully, and a green gate is a true statement about the wrong
   number. The same gate asks for 100% of new security hotspots reviewed, which is a click in the UI and not
   something a build can do for itself. Nor is the 90% a click on the gate as it stands: `Sonar way` is built
   in and cannot be edited, so 90% means copying it into a gate of this project's own and binding the project
   to that copy.
4. **The analysis method is a setting of the same kind, and it can stop the run before the build.** SonarCloud
   will not accept a CI analysis while the project is on Automatic Analysis, and that switch is not writable
   from here or from the API — `api/settings/set` answers "Setting `sonar.autoscan.enabled` cannot be set on a
   Project". `ci.yml` reads it first and fails on the step named *Check that Automatic Analysis is off*, which
   means a run in that state has not built or tested anything; the fix is the toggle under the project's
   Administration → Analysis Method, and the error links to it. `publish.yml` reads the same switch and stops
   before it packs, so a project left on Automatic Analysis can neither be analysed by CI nor released — which
   is the right pair of failures, because a CI analysis is the only one that carries a coverage report.
5. **The snapshot a gate was read from can be replaced after the run that produced it.** `ci.yml` uploads an
   analysis of `main` with coverage; Automatic Analysis, while it is on, uploads another one of the same branch
   without coverage, and the project keeps the newer one. That is how a green 15:43 analysis became a red 16:14
   gate on 2026-10-17, on a finding that had already been fixed: the verdict was true of a snapshot that
   described the tree without its tests. So `publish.yml` reads neither a badge nor a previous run's
   conclusion — it asks for the newest analysis of `main`, checks that its revision is the commit the tag
   names, checks that its conditions include `new_coverage`, and only then reads the verdict.

### What T103 measured, before SonarCloud is asked

The gate above is enforced by a service. T103 asks the same question of the tree, locally, from the same
report `ci.yml` uploads, so the answer is known before a run is read:

```text
./.tools/dotnet-coverage collect -f coverage -o artifacts/coverage/net11.0-final.coverage `
  "dotnet test tests/SharpCorePDF.Tests/SharpCorePDF.Tests.csproj -c Release -p:TargetFramework=net11.0"
./.tools/dotnet-coverage merge -f xml -o artifacts/coverage/net11.0-final.xml artifacts/coverage/net11.0-final.coverage
python docs/tools/coverage-on-new-code.py artifacts/coverage/net11.0-final.xml
```

The tool takes the lines `git diff --unified=0 v1.0.0 -- src` adds, intersects them with the executable lines
in that report, and reports both the whole shipped surface and the new code.

| Measurement | Result |
| --- | --- |
| Whole shipped assemblies (module totals) | Infrastructure 87.07%, Application 88.47%, Domain 79.44%, DependencyInjection 93.65% |
| **New code, the gate** | **6,841 of 8,798 added executable lines, 77.76% — below 90%** |
| Tests on the leg measured | 748 passed, 0 failed, 0 skipped |

It has been measured three times on this tree, and every move came from a test rather than from a change of
definition: 6,718 of 8,798 (76.36%) at 743 tests, 6,817 (77.48%) after three inline-image tests, 6,841
(77.76%) at 748 tests after two container-level signing tests. The 1,957 uncovered lines are concentrated in
eight files — `TrueTypeFont` (94), `PdfTextScanner` (93), `SignatureValidator` (85), `PdfARequirements` (84),
`PdfContentScanner` (74), `PdfPageContentReader.Images` (71), `PdfImageFilters` (68) and `PdfTextCollector`
(67) — and they are the refusal and malformed-input branch of each parser and validator, not the behaviour a
parity row names.

The 92% quoted above is SonarCloud's own figure for the shipped assemblies at 1.0.0. For this release the
service reports `new_coverage` **83.5%** on the pull request's new code, against its own floor of 80 — so its
coverage condition is green while the local measurement of the same added lines says 77.76%. The two disagree
because the service builds its own period and line mapping from the SCM data and its own exclusions, and the
local script asks about every executable line `git diff v1.0.0 -- src` adds. Neither number is wrong; the local
one is the stricter, and it is the one this repository's 90% floor is applied to.

**The decision that figure carried is [`ADR-002`](./adr/0002-new-code-coverage-1.1.0.md)**: `1.1.0` ships with
77.76% recorded in this section, in that record and in the release notes; the floor stays at 90% and is
re-measured on 1.2's new code; no test is written whose only effect is to execute a line; and the parsers'
refusal branches become a named 1.2 task. The record is **Proposed** until the release owner signs it, and the
tag waits for that signature — an unsigned waiver is the one outcome this section is written to prevent.

## The 1.1.0 run of `quickstart.md` (T104)

The run book is `specs/003-pdf-platform-parity/quickstart.md`; running it end to end is the release's own
rehearsal, and its transcript is `artifacts/t104-release-transcript.md` — ignored by Git, like every other
build output, so it travels with the pull request rather than the tree. What it establishes:

| Item | Result | Where the evidence is |
| --- | --- | --- |
| Release build | 0 warnings, 0 errors | transcript, step 1 |
| Test legs, net10.0 and net11.0 | 748 passed, 0 failed, 0 skipped on each — 743 when the run was made, plus the five tests T103 added afterwards; no 1.0 test removed | transcript, step 1; `git diff v1.0.0 -- tests` |
| Examples | 15 of 15 `ok (…)`, exit 0 | transcript, step 1 |
| Creator phase | 34 tests, `author-page`, qpdf / pypdf / PyMuPDF, 0 expectations broken | transcript, phase 1; `artifacts/c5-transcript.md` |
| Editing phase | 37 tests; insert, rotate, delete and crop read back by pypdf | transcript, phase 2 |
| Annotations and forms | not runnable — 1.2 work, marked as such in the run book | transcript, phase 3 |
| Signatures and PDF/A | 12 tests, `archive-pdfa`, qpdf clean; **veraPDF absent**, so conformance rests on the recorded 1.30.3 `PASS`; `sign-and-archive` is 1.2 | transcript, phase 4 |
| Extraction, layout, object graph | 78 tests, `inspect-objects`; the scored corpus run is T079 | transcript, phase 5 |
| Determinism | `cover.pdf` byte-identical across processes; the merged file only with `ModificationDate` pinned | transcript, step 8 |
| Independence | no new package; no PDFsharp, PdfPig, SkiaSharp or System.Drawing; no network API in `src/` | transcript, step 8 |

Two of its findings changed the documents rather than the code: `quickstart.md` now runs only commands that
exist (phases 3 and 4 had named test classes, a scenario and tool flags that were never there), and step 8 hashes
`author-page` because the merge it used to hash is not byte-stable without a pinned `/ModDate`. The run itself
is not a pass on the coverage floor — that is the table above, and it is T103's measured decision
([`ADR-002`](./adr/0002-new-code-coverage-1.1.0.md)), not T104's.

## G1–G9 at `1.1.0` (T105, 2026-10-23)

The gate table above is the 1.0.0 state of each gate. This is the same nine, asked again of the tree that
`1.1.0` is cut from, with where the evidence lives. `<Version>` is now `1.1.0` in `src/Directory.Build.props`
(`AssemblyVersion` and `FileVersion` `1.1.0.0`), and the release notes on every package page are the 1.1 text
from the same file.

| Gate | State at `1.1.0` | Evidence |
| --- | --- | --- |
| G0 — the id | **Closed** | `SharpCorePDF` plus the four layers are published and immutable at `1.0.0`; `1.1.0` reuses the ids rather than claiming new ones |
| G1 — the surface has stopped moving | **Yes, for the 1.1 surface** | every addition is snapshotted in `PublicAPI.Unshipped.txt`, `RS0016`/`RS0017` are build errors, and `PublicApiSurfaceTests` pins the shape; the 1.2 rows will add members, which is why this is a per-release statement and not a permanent one |
| G2 — the shipped leg | **Unchanged: both legs ship** | `lib/net10.0` and `lib/net11.0` in every package; narrowing stays on the 1.2 backlog |
| G3 — notes a consumer can read | **Yes** | `CHANGELOG.md` holds `## [1.1.0] - 2026-10-23`, written in the same commit as the bump, with a *Known limits at 1.1.0* section that names the coverage figure and the two 1.0 limits that remain |
| G4 — symbols and reproducible binaries | **Still partly** | no `.snupkg` is packed and nothing is code-signed; `<Deterministic>true</Deterministic>` is set, and T102 proves the *output* is stable run to run, which is not the two-build comparison this gate asks for |
| G5 — trusted publishing and `NUGET_USERNAME` | **Unchanged, outside the tree** | the policy and the secret live on nuget.org and in the repository settings; the 1.0.0 push proved both, and `1.1.0` needs no new subject |
| G6 — a dry run on the commit to be tagged | **To be re-run** | `Actions → publish → publish: false` against the release commit; the 1.0.0 dispatch proved the handshake, not this commit |
| G7 — the tag equals `<Version>` | **To be cut** | the tag must read `v1.1.0`; `publish.yml` refuses any other value |
| G8 — the packages install and open a document | **After the push** | the same three checks as at 1.0.0, on `1.1.0` |
| G9 — the quality gate on the tagged commit | **Local number recorded, 77.76%; the gate passed on the release commit** | T103's measurement and [`ADR-002`](./adr/0002-new-code-coverage-1.1.0.md). SonarCloud's own `new_coverage` is 83.5% against its floor of 80, so coverage was never the failing condition; `new_reliability_rating` = 3 was, from one new bug in `PdfImageFilters`, fixed in the release commit — after which the run on `ea95c78` reported `QUALITY GATE STATUS: PASSED`. `publish.yml` still reads the gate on the commit the tag names |

Two things stand between this table and a tag, and neither of them is code:

1. **ADR-002 must be signed** by the release owner. It is `Proposed`; T103's checkbox closes on the signature,
   and T106 does not run without it.
2. **The tag must name the commit whose SonarCloud analysis is the newest on `main`** (G9, T106), which is why
   the analysis is read after the merge rather than before it.

The pull request that used to be on that list exists:
[#4](https://github.com/MPCoreDeveloper/SharpCorePDF/pull/4), `003-pdf-platform-parity` → `main`, twelve commits.
`artifacts/t104-release-transcript.md` is ignored by Git, so the transcript is in the body under a collapsed block
rather than attached as a file, and the body says why. It also says that the summary row reading 76.36% is the
run's own figure before the five tests the release commit adds — 77.76% is what the release ships with.

