<!--
Sync Impact Report
==================
Version change: 1.0.0 -> 2.0.0
Modified principles: VII (creation and editing removed from the excluded surface; the library may
  author and edit PDFs. Rendering, conversion, OCR, conformance validation, service and CLI
  exclusions remain).
Added sections: none.
Removed sections: none.
Follow-up TODOs: none. Every placeholder was resolved from README.md, CONTRIBUTING.md,
  SECURITY.md, docs/release-checklist.md, docs/measurements.md, docs/manual/src/11-architecture.md,
  src/Directory.Build.props and src/.editorconfig.
-->

# SharpCorePDF Constitution

## Core Principles

### I. Every PDF Byte Is Ours

SharpCorePDF reads, decrypts, copies and writes every PDF byte itself. The reader, the parser, the
standard security handler, the object copier and the writer are code in this repository.

- No shipped package MUST depend on a third-party PDF engine, a native library or a
  platform-specific binary.
- A change that would need one is an architectural change, not a fix: it is discussed in an issue
  before a pull request exists.
- The engine MUST remain able to explain a failure in terms of the file it was given, because a bug
  in this repository is a bug in the file a caller hands to someone else.

**Rationale**: the absence of a foreign engine is the product, not a preference. It is also what
keeps the allocation and throughput numbers in `docs/measurements.md` meaningful and keeps the
packages free of deployment prerequisites.

### II. Dependencies Point Inward, and Domain Knows Nothing

The four layers are the architecture, and their edges are one-directional:

- `SharpCorePDF.Domain` depends on nothing but the BCL and defines plans, selections, policies,
  read models, warnings and ports.
- `SharpCorePDF.Application` depends on Domain and orchestrates without parsing PDF syntax.
- `SharpCorePDF.Infrastructure` depends on Domain and Application and is the only place that
  understands file structure.
- `SharpCorePDF.DependencyInjection` depends on all three and is the only composition root.
- `SharpCorePDF` is a meta-package carrying no assembly of its own.

A project reference that points outward from Domain, or a concrete engine type named outside the
infrastructure and dependency-injection projects, MUST NOT be merged. A change to this graph is an
architectural change and needs an issue first.

**Rationale**: a plan can be built, validated and tested without touching a disk only while the
domain stays ignorant of what a file is.

### III. Copy Bytes, Never Re-encode What You Cannot Reproduce

The merge copies the bytes a document already holds; it does not regenerate them.

- Stream bytes MUST be copied verbatim. Nothing is re-compressed on the way through.
- Inheritable page attributes MUST be materialised onto each page so that partial selections and
  reordering stay possible.
- Every decision that loses information MUST be reported as a `PdfWarning` whose `Kind` names the
  condition; the message text is prose, the `Kind` is the part a caller may match on.
- With a fixed `ModificationDate`, the same plan over the same inputs MUST produce byte-identical
  output.
- PDF fixtures stay binary in Git (`*.pdf binary` in `.gitattributes`): a checkout that rewrites
  line endings moves every cross-reference offset in them.

**Rationale**: a silently re-encoded stream is a document that changed without a warning, and a
warning whose identity cannot be matched is a string a caller cannot act on.

### IV. A Test That Fails on Revert (NON-NEGOTIABLE)

Evidence, not assertion, decides whether a behaviour change is done.

- Every behaviour change MUST ship with a test that fails when the change is reverted, and the pull
  request MUST name that test.
- New code under `src/` MUST be exercised by the suite: the SonarCloud gate requires 90% coverage
  on new code, above Sonar's 80% default, and `ci.yml` waits for that verdict instead of reporting
  it afterwards.
- Tests run on both legs (`net10.0` and `net11.0`), and test code contributes no lines to the
  coverage number, so a test does not raise the metric it exists to justify.
- A defect found in conversation or in the tracker MUST become a test in
  `tests/SharpCorePDF.Tests` once it is understood.

**Rationale**: a suite that exists to catch regressions is measured by the regression it catches,
not by the count of tests it contains.

### V. The Public Surface Is Snapshotted, and SemVer Says What Changed

- `PublicAPI.Shipped.txt` is the frozen surface; additions go into `PublicAPI.Unshipped.txt` in the
  same commit as the member they describe.
- `RS0016` (a public member the snapshot does not list) and `RS0017` (an entry that outlives its
  member) are build errors, so a forgotten entry fails the build rather than review.
- A snapshot entry that changes shape MUST be called out in the pull request as breaking or not.
  The repository follows Semantic Versioning 2.0.0, and `CHANGELOG.md` carries the entry before the
  tag exists.
- The snapshot analyzer and `SonarAnalyzer.CSharp` stay development-time only
  (`PrivateAssets="all"`) and MUST NOT reach a package or a consumer's build.

**Rationale**: the snapshot diff is the clearest signal a release is a breaking one, and it is
cheaper to read than the diff of the API itself.

### VI. Zero Warnings, or a Recorded Decision

- `dotnet build .\SharpCorePDF.slnx -c Release` MUST be clean, with zero warnings, on both legs.
  "No new warnings" is not the bar; zero is.
- A new warning is a regression like any other. The analyzers run in the build
  (`latest-recommended`, `SonarAnalyzer.CSharp`, the same rule set SonarCloud applies), so a finding
  appears at the line that causes it instead of after a push.
- A finding MUST be fixed rather than configured away. A rule this repository deliberately does not
  follow is silenced per file in `src/.editorconfig`, with the reason written beside it, and the
  line carries `// NOSONAR` so the server reaches the same conclusion as the local build. It MUST
  NOT be turned off repository-wide.

**Rationale**: a warning that is tolerated once is a warning the next contributor has to guess
whether to take seriously.

### VII. The Boundary Is Part of the API

What the library refuses to do is documented, and the documentation is governed like code.

- SharpCorePDF is a PDF creation, editing and reading library. It is not a renderer, a converter,
  an OCR engine, a PDF/A or PDF/UA validator, a general-purpose mutable PDF object-graph parser, a
  service or a CLI. Adding one of those to the surface is a change of product, and it MUST NOT
  happen inside a fix.
- A change that makes a sentence in `README.md`, `docs/` or the manual false MUST update that
  sentence in the same pull request.
- Published claims MUST stay tied to a measurement or to a test. A differentiator that
  `docs/measurements.md` does not back is a claim the repository does not make.

**Rationale**: a caller decides whether to trust this library by reading what it says it will not
do, so an inaccurate boundary is a defect in the product rather than in the prose.

## Architectural and Technical Constraints

**Supported frameworks.** Every layer multi-targets `net10.0` (C# 14) and `net11.0` (C# 15, with
`runtime-async=on`), and both legs ship in every package. `net10.0` is first-class rather than a
leftover: it is what keeps the packages installable by applications that have not moved to .NET 11.
Adding a target framework, or dropping one, is an architectural change with a build-matrix cost and
MUST be raised in an issue first.

**Runtime dependencies.** The four layer packages have none beyond `Microsoft.Extensions.*`, and
`SharpCorePDF.Domain` has none at all. A new runtime dependency is an architectural change, not a
convenience.

**What the library never does at runtime.** It opens no socket and reaches no network, writes no
log, and stores no secret. A password is used for one operation and is not kept.

**Cost is measured, not estimated.** Allocations, wall time, throughput, retained heap and output
size are recorded in `docs/measurements.md`, measured with `tools/SharpCorePDF.Measure`, and read
against the budgets stated there. A change that moves a number outside its budget MUST record a new
measurement in the same pull request; a claim of an improvement without a measurement is not one.

**Reproducibility.** The build sets `Deterministic` to true, and a merge with a fixed
`ModificationDate` is byte-identical across runs. A change that breaks either is a regression.

**Fixtures and independent validation.** The fixture corpus under `tests/SharpCorePDF.Tests/Assets`
is generated by `docs/tools/make-fixtures.py`, never edited by hand, and stays binary in Git. A
change that alters what a merge writes is validated with an independent reader
(`docs/tools/validate-pdf.py`, which drives `qpdf` through `pikepdf`) before the result is believed.
Neither Python tool runs in CI; their output quoted in a pull request is worth more than a
description of it.

**Diagnostics over exceptions where the document is merely damaged.** A damaged cross-reference
table, a lying stream length or a reference to a page that no longer exists is reported through the
warning list, not by throwing. Throwing is reserved for a caller's mistake and for a document that
cannot be opened at all.

## Development Workflow and Quality Gates

**Issue first.** These are architectural rather than incremental, and a pull request that arrives
without the conversation is declined on grounds that have nothing to do with its quality: a runtime
dependency, a native library or platform-specific binary, another target framework, and any change
to the shape of the public API that the pull request does not declare.

**What a pull request has to clear.**

1. A test that fails when the behaviour is reverted, named in the description.
2. The public API snapshot, in the same commit.
3. A build with zero warnings on both legs.
4. New code at or above the 90% coverage condition of the SonarCloud quality gate.
5. PDF fixtures still binary, and unchanged unless the change is about them.
6. No drive-by reformatting: no reordering, renaming or whitespace outside what the change needs.
7. Prose updated wherever the change makes a documented claim false.
8. Nothing from a gitignored planning document, and no internal story, epic or requirement
   identifier, in a comment, a commit message or a pull request description.

**Local gates before a push.** `dotnet build .\SharpCorePDF.slnx -c Release`, `dotnet test` over
both legs, and `dotnet run --project .\examples\SharpCorePDF.Examples -c Release` — the examples are
code that compiles and executes, so documentation snippets do not drift.

**Continuous integration is the gate, not a badge.** `ci.yml` builds the solution in `Release`, runs
the `net11.0` leg under `dotnet-coverage collect` and the `net10.0` leg plainly, analyses with
SonarCloud and waits for the quality gate verdict (`sonar.qualitygate.wait`, 600 s). A run in which
the analysis steps were skipped — no `SONAR_TOKEN` — proves nothing about the analysis and MUST NOT
be cited as if it did.

**The order a release follows.** Write the changelog entry, push, wait for `ci.yml` to be green, tag
the commit that run analysed, and let `publish.yml` re-read the gate for that commit before it
packs. A tag never inherits a verdict about a different tree, and a version with no changelog entry
is not released. The tag produces five packages: the four layers and the `SharpCorePDF` entry point.

**One supported line.** A fix lands on `main` and ships in the next patch of the current version; it
is not back-ported, because there is nothing behind the current line to port to.

**Commits and reviews.** The subject says what the change does, in the imperative; the body says
what breaks without it. One maintainer reviews, with a first answer within 7 days, and rights are
granted on the published ladder in `CONTRIBUTING.md` rather than assumed.

**Documentation is built, not hand-edited.** The manual's chapters live in `docs/manual/src` as
Markdown and the PDF beside them is generated by `docs/manual/build-manual.py`; a chapter added or
renamed is followed by a rebuild so the contents page, footer and bookmarks stay in step.

## Governance

**Authority.** This constitution states the rules a change is held to. Where it conflicts with
`CONTRIBUTING.md`, `README.md` or a document under `docs/`, this file wins and the other document is
corrected in the same pull request. Where it is silent, the repository's own practice decides, and a
practice that hardens into a rule is written here rather than left in a reviewer's memory.

**Amendments.** An amendment is a pull request against `.specify/memory/constitution.md` that states
the change and why the old text was wrong or incomplete. It updates the version line, the Sync
Impact Report and any dependent document in the same commit. Ratification requires the maintainer's
approval; there is no other path to a change.

**Versioning policy.** The constitution follows Semantic Versioning:

- MAJOR: a principle is removed or redefined in a way that makes existing practice non-compliant.
- MINOR: a principle or section is added, or existing guidance is materially expanded.
- PATCH: clarification, wording or a typo, with no change in what is required.

**Compliance review.** Every pull request review verifies compliance and says which principles it
checked. CI enforces what a machine can: the build and its zero-warning bar, the API snapshot, both
test legs and the coverage gate. Principles I, II, III and VII are not machine-checked and are the
reviewer's charge; a principle that cannot be evidenced in a pull request is a finding, not a
formality.

**Exceptions.** A rule this repository decides not to follow is silenced where it occurs, with the
reason recorded — a per-file analyzer entry with `// NOSONAR`, or a named exception in the pull
request. Nothing is exempted repository-wide, and a rule waived twice is a candidate for an
amendment rather than a habit.

**Ratification.** Adopted 2026-10-20 as version 1.0.0, replacing the unmodified template scaffold.

**Amendment 2.0.0 (2026-10-21).** Creation and editing are part of the product. Rendering,
conversion, OCR, conformance validation, a service and a CLI remain outside it.

**Version**: 2.0.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-21
