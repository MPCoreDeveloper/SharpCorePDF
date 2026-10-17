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
| Tests | 447 pass on each leg |
| Coverage | 92% of the shipped assemblies, imported into SonarCloud by the scan; the 90% floor is a condition on the quality gate, where the suite's own 99% is not counted |
| External prerequisites | the trusted publishing policy, whose subject list must hold a line for the bare `SharpCorePDF` as well as the `SharpCorePDF.*` glob — the glob alone refuses the entry point with 403 — and the `NUGET_USERNAME` secret. Both were confirmed the hard way on the first `1.0.0` attempt; neither is visible from this repository |

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
| G9 | The quality gate is green on the commit being released | a release is the one build nobody re-runs; the coverage and the analysis have to be true of the tree, not of a branch it came from | **Yes, by construction** — `ci.yml` fails on the gate; see G9 below | keep it green; never tag a commit whose `ci.yml` run is red |

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
4. Bump `<Version>`, write the changelog entry in the same commit, tag `v` plus that exact version, push
   the tag (G7). Then verify (G8), including that the `SharpCorePDF` page shows four dependencies and no
   assemblies of its own.
5. Symbols, signing and the reproducible-build comparison are what `1.0.1` or `1.1.0` still owe (G4).

The preview numbers are settled by the feed rather than by intention: `preview.1` was published, so it is
history and its four ids carry the version forward to `1.0.0`; `preview.2` and `preview.3` never reached a
feed, so they are skipped rather than reused, and nothing needs to explain them to anyone.


## Two things people assume are gates and are not

- **The SonarCloud quality gate, in `publish.yml`.** `ci.yml` enforces it — the scan's `end` step passes
  `sonar.qualitygate.wait`, so a failing gate is a failing run, coverage condition included. What is *not*
  enforced is the link between that run and a release: `publish.yml` never asks SonarCloud whether the
  commit it is packing was green. A tag is therefore trusted on its own merits, which is an argument for
  tagging a commit whose `ci.yml` run was already green, not a mechanism that enforces it.
- **A green `ci.yml` run.** `publish.yml` builds and tests the commit itself, but it does not wait for
  `ci.yml`. The two workflows answer different questions and are deliberately independent.

## G9 — the quality gate, and what "90%" counts

One service holds a number about this repository, and that number is a red-run gate in `ci.yml`:

| Gate | Target | Rules live in | Measured by |
| --- | --- | --- | --- |
| SonarCloud | the project's quality gate on new code — the coverage floor this repository wants there is 90%, above Sonar's 80% default — plus the Sonar way profile over `src/` and `tests/` | the quality gate on sonarcloud.io; the exclusions in `sonar.exclusions` in `ci.yml`; local silences in `src/.editorconfig` | `dotnet-sonarscanner begin` before the build, `end` after the tests, reading `artifacts/coverage/net11.0.xml` |

The 90% is deliberately *not* the whole tree. The scan excludes the BMad bundle, `docs/`, `tools/`,
`examples/`, `bin/`, `obj/` and `artifacts/`, and test code contributes no lines to a coverage metric, so
the number describes the four assemblies the packages ship plus the entry point. At the time of writing
that is 92% (`SharpCorePDF.Infrastructure` 91%, `SharpCorePDF.Domain` 96%,
`SharpCorePDF.Application` 100%, `SharpCorePDF.DependencyInjection` 100%), while the suite that measures
them sits at 99%. Counting the tests would raise the number and tell a reader nothing.

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
   Administration → Analysis Method, and the error links to it.


