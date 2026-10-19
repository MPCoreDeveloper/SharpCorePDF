<!--
  The logo and the badges are absolute URLs on purpose: nuget.org refuses to render a relative image
  path, and this README is packed into every package (PackageReadmeFile in src/Directory.Build.props).
  Logo.jpg is the 2048 x 2048 source; Logo-256.png is the icon the packages carry. Both image hosts
  are on nuget.org's trusted list (raw.githubusercontent.com and img.shields.io), and its renderer
  keeps <p align>, the <img> tag and the align/width/src/alt attributes.

  Every badge is an img.shields.io URL for that same reason: a workflow badge served from
  github.com/.../badge.svg is not on nuget.org's trusted list, while shields.io answers the same
  question from a host that is. Read them as:

    nuget.org version  the vpre endpoint for the entry-point id, which read "unknown" until 1.0.0 was
                       pushed on 2026-10-17, and reads 1.0.0 now; the four layer ids resolve too, at
                       1.0.0 and 1.0.0-preview.1
    tests              the ci.yml runs: the build, both test legs and the SonarCloud analysis
    quality gate       the SonarCloud project MPCoreDeveloper_SharpCorePDF, which is also where the
                       coverage number is read; it reads "unknown" until the first analysis lands, the way
                       the version badge did before the first push
    licence            LICENSE, MIT
    Sponsor            the funding page, which .github/FUNDING.yml puts on the repository page too
-->
<p align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCorePDF/main/Logo.jpg" alt="SharpCorePDF" width="260" />
</p>
<p align="center">
  <a href="https://www.nuget.org/packages/SharpCorePDF">
    <img src="https://img.shields.io/nuget/vpre/SharpCorePDF?label=nuget.org" alt="nuget.org version" />
  </a>
  <a href="https://github.com/MPCoreDeveloper/SharpCorePDF/actions/workflows/ci.yml">
    <img src="https://img.shields.io/github/actions/workflow/status/MPCoreDeveloper/SharpCorePDF/ci.yml?label=tests&amp;logo=githubactions&amp;logoColor=white" alt="tests" />
  </a>
  <a href="https://sonarcloud.io/dashboard?id=MPCoreDeveloper_SharpCorePDF">
    <img src="https://img.shields.io/sonar/quality_gate/MPCoreDeveloper_SharpCorePDF?server=https%3A%2F%2Fsonarcloud.io&amp;logo=sonarcloud&amp;logoColor=white" alt="SonarCloud quality gate" />
  </a>
  <a href="https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/LICENSE">
    <img src="https://img.shields.io/badge/licence-MIT-blue" alt="licence: MIT" />
  </a>
  <a href="https://github.com/sponsors/MPCoreDeveloper">
    <img src="https://img.shields.io/badge/Sponsor-%E2%9D%A4-ff69b4" alt="Sponsor" />
  </a>
</p>

# SharpCorePDF

A dependency-injection first, domain-driven PDF library for .NET 10 and .NET 11, built around one
capability done properly: **merging PDF documents** — whole documents, selected pages, in any order.

Every PDF byte is read and written by this library. There is no dependency on a third-party PDF
engine, no native binary and no licence to accept: the reader, the writer and the merge engine are
part of the package, and they are covered by tests against real-world documents and validated with
independent implementations (`qpdf` via `pikepdf`, and `pypdf`).

## What it does

```csharp
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure.Extensions;

var plan = MergePlan.Create()
    .AddFile(@"C:\reports\summary.pdf")                // every page
    .AddFile(@"C:\reports\appendix.pdf", pages: "1-3") // one-based, "1,3-5,8-"
    .AddFile(@"C:\reports\annex.pdf", pages: "3,1");   // in this order

await merger.MergeToFileAsync(plan, @"C:\reports\combined.pdf");
```

* **Page selections** in the notation people write: `5`, `2-7`, `3-`, `-4`, comma separated, in the
  order they are given. Page numbers are always one-based.
* **Everything of every source travels with its pages**: content streams, images, fonts, resources,
  annotations, bookmarks and form widgets. Stream bytes are copied verbatim — nothing is re-compressed.
* **One flat page tree** in the result, with the inheritable page attributes (`Resources`, `MediaBox`,
  `CropBox`, `Rotate`) materialised onto each page, which is what makes partial selections and
  reordering possible.
* **Bookmarks of every source** are merged into one tree whose top level items follow the source order.
* **Document level features** — viewer preferences, page layout and mode, the interactive form, named
  destinations and embedded files — are carried over from the primary source, because they cannot
  sensibly exist twice in one document; page labels are dropped by default and XMP metadata is kept
  unless `MetadataPolicy` says otherwise.
* **Document information** is taken from the primary source, merged from all sources, or suppressed;
  an explicit `MetadataOverride` wins for the fields it can set. `Producer` and `ModificationDate` are
  output options, not override fields, and fixing the date makes the output reproducible byte for byte.
* **Encrypted sources are opened, not refused.** The standard security handler is implemented for
  revisions 2 to 6 — RC4, AES-128 and AES-256, including the hardened hash revision 6 introduced — so a
  source is merged with its user or owner password and every string and stream of it is read in the
  clear. A document that opens with no password at all, which is the shape of a file that is only
  protected against editing, needs to be told nothing.
* **The result can be encrypted, and is written in the clear unless you ask otherwise.** Supplying
  `MergeOutputOptions.Encryption` protects the merged document under passwords you choose — RC4-40,
  RC4-128, AES-128 or AES-256 (the default), with the permission bits the format defines — and the
  strings and streams of the result are sealed as they are written:

  ```csharp
  var plan = MergePlan.Create()
      .AddFile(@"C:\reports\summary.pdf")
      .AddFile(@"C:\reports\appendix.pdf")
      .WithOutput(options => options with
      {
          Encryption = new PdfEncryptionOptions
          {
              Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,
              UserPassword = "opens-it",
              OwnerPassword = "owns-it",
              Permissions = PdfPermissions.PrintOnly,
          },
      });
  ```

  A merge of encrypted sources under new passwords needs nothing from the old ones, and a source that was
  only protected against editing can be merged and re-protected in one step.
* **Diagnostics instead of surprises**: damaged cross-reference tables are rebuilt by scanning the
  file, unselected pages that something still points at are folded onto a page that exists, and
  anything the merge had to change is reported in `MergeResult.Warnings` as a `PdfWarning` whose
  `Kind` names the condition — the part a caller may match on, while its message is only text.

## Getting it into a project

The dependency injection package wires everything behind its ports:

```csharp
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Ports;

services.AddSharpCorePDF();                                      // default engine options
services.AddSharpCorePDF(PdfEngineOptions.Default with { ... });  // explicit options
services.AddSharpCorePDF(configuration);                         // from the "SharpCorePDF" section
services.AddSharpCorePDF(configuration, "My:Pdf:Section");       // from a section of your choosing

var merger = provider.GetRequiredService<IPdfMerger>();
var inspector = provider.GetRequiredService<IPdfInspector>();
```

Every package targets `net10.0` and `net11.0` and carries both legs: NuGet hands a `net10.0` app the
`lib/net10.0` assembly and a `net11.0` app the `lib/net11.0` one, with exactly the same API on both.
Nothing else is supported yet — there is no `netstandard2.0` or `net8.0` leg, so a project that targets
.NET 8 or .NET 9 cannot install these packages.

`IPdfInspector.InspectAsync` describes a document — version, page count and sizes, rotation, metadata,
bookmarks, embedded files, form presence, warnings — and opens an encrypted one with the password it is
given instead of throwing. A document whose encryption stays closed is reported with
`PdfDocumentInfo.IsReadable` cleared and `PdfDocumentInfo.RequiresPassword` set when a password is what
stands in the way, so a caller can ask for one instead of being told the document is simply unusable.

The package to install is `SharpCorePDF`. It is the product name and the only name to remember: it
carries no code of its own and depends on the four layers below, so one `dotnet add package` pulls in the
whole library:

```powershell
dotnet add package SharpCorePDF
```

If you would rather take one layer on its own — `SharpCorePDF.Domain` has no dependency at all — the four
are published separately under the same version, so they install the same way and stay in step with the
entry point. Before 1.0.0 the only ids on the feed were the layers, at `1.0.0-preview.1`; that is why an
older instruction may tell you to add `SharpCorePDF.DependencyInjection` by name, and why `--prerelease`
was needed then. `dotnet add package SharpCorePDF` replaces both.


The XML documentation ships inside the packages, so every public type and member is described in
IntelliSense without leaving the editor.

To work against this source tree instead of the feed, reference the project
(`dotnet add reference .\src\SharpCorePDF.DependencyInjection\SharpCorePDF.DependencyInjection.csproj`),
or pack all five into a local feed and install them from there:

```powershell
dotnet pack .\SharpCorePDF.slnx -c Release -o $env:TEMP\SharpCorePDF-feed
dotnet nuget add source $env:TEMP\SharpCorePDF-feed --name SharpCorePDF
dotnet add package SharpCorePDF --version 1.0.0
```

Every package carries the logo as its package icon and embeds this README, so the page a feed shows for
it is the introduction you are reading. nuget.org renders images only from a list of trusted hosts and
cannot resolve relative paths, which is why the logo and the links in this file are absolute URLs.

## Examples

[`examples/SharpCorePDF.Examples`](https://github.com/MPCoreDeveloper/SharpCorePDF/tree/main/examples/SharpCorePDF.Examples)
is a console app with nine runnable examples. Every snippet on this page is code that compiles and
executes there:

```powershell
dotnet run --project .\examples\SharpCorePDF.Examples -c Release          # all of them
dotnet run --project .\examples\SharpCorePDF.Examples -- select-pages    # one of them
dotnet run --project .\examples\SharpCorePDF.Examples -- errors options  # several, in this order
```

| Example | Shows |
| --- | --- |
| `merge-files` | Three documents from disk into one file, and what each source contributed. |
| `select-pages` | `3,1`, `2-`, `-2` and `2,2,1`: a selection is an order rather than a set, and it can be built in code too. |
| `merge-streams` | A source that is an upload stream, and one that is already a `byte[]`. |
| `no-container` | The same merge without a container, and the proof that both routes write the same bytes. |
| `inspect-first` | Describing a folder of documents before merging: a file that is unusable, or encrypted and waiting for its password, is reported, not thrown. |
| `options` | Metadata policy and override, outline and page-label policy, the target version, reproducible output. |
| `protect-result` | A result that needs a password: AES-256, a user and an owner password, the permission bits, and a merge of an encrypted source into a document sealed again under new passwords. |
| `warnings` | What inspection reports about a document, versus what a merge reports about its own work. |
| `errors` | Every refusal a caller can meet, with the exception type and the message the library produces. |

The examples generate the documents they merge, so nothing else is needed to run them, and they leave the
results in a folder under the temporary directory for you to open.
[`examples/README.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/examples/README.md)
walks through each one.

## Documentation

The [manual](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/README.md) is the long-form
guide to the 1.0.0 API: thirteen chapters, from the first merge to encryption, inspection, dependency
injection, the architecture and a cookbook of complete recipes. Read it as
[a PDF](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/SharpCorePDF-Manual-1.0.0.pdf)
or chapter by chapter as Markdown, starting with
[Installation and First Merge](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/manual/src/03-installation.md).
The [documentation index](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/README.md)
links every chapter and the supporting measurement, release and governance documents.

This README remains the short tour: what the library does, how to install it, and how the repository is
built, tested and published. The manual is where the behaviour of each option, the warning kinds and the
recipes live.

## How it is put together

Five packages: one to install, and four that do the work, one per layer. Each name links to its page on
nuget.org.

| Package | Contents |
| --- | --- |
| [`SharpCorePDF`](https://www.nuget.org/packages/SharpCorePDF) | The one to install. Carries no assembly of its own: its only content is a dependency on the four below, so `dotnet add package SharpCorePDF` is the whole install. |
| [`SharpCorePDF.Domain`](https://www.nuget.org/packages/SharpCorePDF.Domain) | The `MergePlan` aggregate, the `PdfSource` entity, value objects (`PageSelection`, `PageRange`, `PdfMetadata`, `PdfVersion`, `PdfPageSize`, `PageRotation`, the policies), the read models and the ports (`IPdfMerger`, `IPdfInspector`, `IPdfContentSource`). No I/O, no third-party code. |
| [`SharpCorePDF.Application`](https://www.nuget.org/packages/SharpCorePDF.Application) | The use cases: `PdfMergerService` (validate, open, inspect, resolve selections, hand over to the engine, release every stream) and `PdfInspectorService`, plus the extension methods callers use. |
| [`SharpCorePDF.Infrastructure`](https://www.nuget.org/packages/SharpCorePDF.Infrastructure) | The engine: the object model, the lexer, the object parser, the filter decoders, cross-reference tables, cross-reference streams, object streams, the page tree walk, the object copier, `PdfMergeEngine`, `PdfDocumentReader`, the writer and the content sources. |
| [`SharpCorePDF.DependencyInjection`](https://www.nuget.org/packages/SharpCorePDF.DependencyInjection) | `AddSharpCorePDF()` registration. |

The domain and the application layer know nothing about PDF syntax; the engine implements their ports.
That is what keeps the merge rules testable without a single PDF file, and what would let a different
engine be swapped in.

### Why the engine looks the way it does

* A source is read into memory once and its objects are resolved on demand, so a damaged object that no
  page needs never stops a merge.
* Objects are copied in two phases: first every reachable object is *reserved* a target number, then
  the bodies are written. That is what makes the cyclic graphs real files contain — a form field that
  points at its widget which points back at the field — terminate.
* Only objects reachable from the kept pages and features are copied, so the result does not inherit
  the dead weight of its sources.
* Object streams and cross-reference streams are decoded (their contents have to be understood); every
  other stream is copied byte for byte. The result uses a classic cross-reference table, which every
  reader in existence supports.

## Known limits (deliberate, for version 1)

* **Encryption covers the standard security handler, revisions 2 to 6.** A document that uses a public
  key handler, or a crypt filter that is not `RC4`, `AESV2`, `AESV3` or `None`, stays closed: inspection
  reports it with `PdfDocumentInfo.IsReadable` cleared and `PdfDocumentInfo.RequiresPassword` still
  `false`, because no password would help, and a merge refuses it with `PdfEncryptionException`.
* **The merged document is written in the clear unless `MergeOutputOptions.Encryption` asks for more.**
  Encryption is opt in, and a result has its own passwords and permissions: what a source declared is
  never carried into it.
* **Passwords are taken exactly as they are given**, without SASLprep normalization, so a password whose
  normalized form differs from the form its producer used must be supplied as the producer wrote it.
  Revisions 2 to 4 predate any agreement on how the characters of one become bytes — Adobe, qpdf and
  pypdf all hash the Latin-1 bytes of the text, which is what the engine reads and writes, and text with
  no Latin-1 form is hashed as UTF-8 — while revisions 5 and 6 are UTF-8 by definition and use at most
  127 bytes.
* **An encrypted result is reproducible byte for byte only when it is written with RC4.** AES seals every
  value with a fresh initialization vector and AES-256 draws a random file key, so two runs of the same
  merge differ; RC4 keeps no such state, so fixing `ModificationDate` fixes the output.
* **Named destinations are merged from the primary source only.** A bookmark of another source that
  used a named destination keeps its title and loses its destination, and the merge says so in its
  warnings.
* **The result is written as PDF 1.4 – 2.0 with a classic cross-reference table** and no object
  streams; `MergeOutputOptions.TargetVersion` (default PDF 1.7) decides the header.
* A reference to a page that was **not selected** is folded onto the first page that source
  contributed, so a link stays valid instead of dangling.
* A bookmark that is stored inline in the page tree (not as an indirect object) cannot be referenced
  and is skipped, with a warning.

## Building and testing

```powershell
# Everything at once, through the solution: the five packages, the tests,
# the examples and the measurement harness.
dotnet build .\SharpCorePDF.slnx -c Release
dotnet test  .\SharpCorePDF.slnx                      # 447 tests on net10.0, 447 on net11.0
dotnet run --project .\examples\SharpCorePDF.Examples -c Release

# Or one project at a time:
dotnet build .\src\SharpCorePDF.DependencyInjection\SharpCorePDF.DependencyInjection.csproj

# The test project must build an app host, which the RID-less net10.0 leg of some SDK builds skips:
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64
```

The tests cover the domain invariants, the serialisation details, the reader against a real-world
document, a document that uses object streams and cross-reference streams, a document with bookmarks,
page labels, annotations and a rotated page, one encrypted document per revision of the standard
security handler — including one whose password is not ASCII — the merge path through the container,
and the encryption of a merged result, which every algorithm is checked against by reading the document
back with the password it was written with.

### Measuring what a merge costs

```powershell
dotnet run -c Release --project .\tools\SharpCorePDF.Measure
dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --single large

# GC mode is part of the measurement, and server mode is the deployment envelope:
$env:DOTNET_gcServer = "1"; dotnet run -c Release --project .\tools\SharpCorePDF.Measure
```

The harness reports the bytes a merge allocates, its wall time, the same allocations as a multiple of
the input, throughput, the heap still held after a full collection, and the bytes the merge wrote — that
last column is a fingerprint, because two runs that write the same bytes ran the same merge.
`--single <all|large|one|half>` samples the managed heap during one isolated merge, which is what "peak"
means. The recorded numbers, and what they say about the performance and memory budgets, are in
[`docs/measurements.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/measurements.md).

### Validating the output with an independent implementation

```powershell
uv run --with pikepdf python .\docs\tools\validate-pdf.py <file.pdf>
uv run --with pikepdf python .\docs\tools\validate-pdf.py --password <password> <encrypted.pdf>
uv run --with pikepdf python .\docs\tools\make-fixtures.py .\tests\SharpCorePDF.Tests\Assets
```

`validate-pdf.py` runs `qpdf --check`, decodes every stream in the file, and prints the geometry,
rotation and annotations of every page; with `--password` it does the same for an encrypted document and
prints what qpdf makes of its encryption dictionary. It is the check that caught the one bug this library
has had so far: a hexadecimal string written with name escapes, which `qpdf` rejected as damage. The
encrypted documents the merge engine writes were checked the same way with qpdf, pikepdf and pypdf.

## Continuous integration

[`ci.yml`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/.github/workflows/ci.yml) runs on
every push to `main` and on every pull request: it builds the solution, runs both test legs under the
coverage collector, scans with SonarCloud, and fails the run when the quality gate fails. It is
deliberately not the publishing workflow, so a pull request is checked without anything reaching a feed.

| What | Workflow | When |
| --- | --- | --- |
| Build, both test legs, coverage, static analysis, the gate | `ci.yml` | a push to `main`, a pull request, a manual dispatch |
| The gate read for the tagged commit, the pack, the package check, the push to nuget.org | `publish.yml` | a `v*` tag, a manual dispatch with `publish: true` |

The analysis runs in the runner rather than on SonarCloud's side, which is what lets it carry coverage:
the scanner session opens before the build, the measured test leg runs inside it, and it closes after it,
so the report describes the assemblies that run produced. Coverage comes from `dotnet-coverage collect`,
because `global.json` pins Microsoft.Testing.Platform as the test runner and the VSTest-only switches
(`--collect "XPlat Code Coverage"`) are rejected there. The collector's binary report is converted once, to
the Visual Studio XML the scanner reads; the same file is kept as a build artifact, so the number can be
read without opening SonarCloud.

What the scan reports on is `src/` and `tests/`, with the BMad Method bundle (`_bmad`, `_bmad-output`,
`.cline`), the Python tooling under `docs/`, the examples and the measurement harness excluded, because the
gate is about what the packages ship rather than about everything in the tree. The coverage condition
counts the four layered assemblies and the entry point only: test code contributes no lines to it, so the
number describes the shipped surface. It was 92% when this paragraph was written; the suite that measures
it sits at 99%, and a suite covering itself is not coverage of anything.

The gate fails the run:

| Gate | Where its rules live | What a red run means |
| --- | --- | --- |
| SonarCloud quality gate | the project on sonarcloud.io: the conditions are entered there — the coverage floor this repository wants on new code is 90%, above Sonar's 80% default — plus the exclusions in `sonar.exclusions` | the analysis was uploaded and the gate failed |

`sonar.qualitygate.wait`, passed to the scanner's `begin` step, is what turns it from a badge into a gate:
the scan waits for the analysis to be processed and fails when the gate fails, so a green badge is a run
that passed rather than a run that is not finished yet. It goes on `begin` because the `end` phase rejects
every property but the token. The 90% is a setting on that gate, not a property of this tree:
until it is entered there, the step enforces Sonar's 80% and is telling the truth about the wrong number.

`publish.yml` does not take that verdict on trust, and a green badge is not enough to publish with. Before it
builds or packs anything, a `sonar-gate` job asks SonarCloud three questions about the commit the tag names:
whether Automatic Analysis is off, whether the newest analysis of `main` is of that commit, and whether the
gate is green with a `new_coverage` condition among the ones it judged. The middle question is the one this
repository learned the hard way — an Automatic Analysis run replaced a CI analysis that carried coverage, and
the gate went red again on a finding that had already been fixed, because the snapshot it judged was a tree
without its tests.

Two things switch the analysis on, and both live outside this repository:

1. **A project on SonarCloud** under the key `MPCoreDeveloper_SharpCorePDF` in the organisation
   `mpcoredeveloper`, with **Automatic Analysis switched off** (Administration → Analysis Method). A
   repository cannot be analysed both ways at once: with it on, the scanner's `end` step fails with "You
   are running CI analysis while Automatic Analysis is enabled", and Automatic Analysis cannot import a
   coverage report anyway — it never sees the test run.
2. **The repository secret `SONAR_TOKEN`** (settings → Secrets and variables → Actions), a token from
   that SonarCloud account.

Until the secret exists, the scanning steps are skipped and the workflow still builds, tests and measures
coverage, because the presence of the token is decided in a small first job — a job-level `if` cannot read
a secret itself. That is what keeps a fork, or this repository before the project was created, from showing
a red run that says nothing about the code.

### The same rules, locally

`src/Directory.Build.props` references `SonarAnalyzer.CSharp`, so `dotnet build` runs the rules the
SonarCloud profile runs and reports a finding at the line that causes it instead of after a push. The
build is clean on both legs with the analyzer enabled. Three findings this repository answers with a
decision rather than a change — `S101` for the product's capitalised name, `S1694` for the closed
`PdfObject` hierarchy, `S4790` for the MD5 that the PDF encryption algorithms specify — are silenced per
file in `src/.editorconfig`, each with the reason, and the affected lines carry `// NOSONAR` so the server
reaches the same conclusion. Everything else the analyzer reports is meant to be fixed.

## Publishing

Packages go to nuget.org from
[`.github/workflows/publish.yml`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/.github/workflows/publish.yml),
and no API key is stored anywhere. The workflow asks GitHub for an OIDC token, exchanges it with
nuget.org for a temporary API key that lives for one hour, and pushes. nuget.org performs that
exchange only when the token matches a **trusted publishing policy** for this repository, so the
workflow can publish nothing until that policy exists.

A tag publishes nothing before SonarCloud says the quality gate is green for the commit it names either:
`publish.yml` reads that verdict in a `sonar-gate` job that runs before the build, before the pack and before
the OIDC exchange. See [Continuous integration](#continuous-integration) above and G9 in
[`docs/release-checklist.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/release-checklist.md).

The policy lives at nuget.org → your username → **Trusted Publishing**, and its fields are the ones
the workflow's header comment records:

| Field | Value |
| --- | --- |
| Repository Owner | `MPCoreDeveloper` |
| Repository | `SharpCorePDF` |
| Workflow File | `publish.yml` — the file name only, not the `.github/workflows/` path |
| Environment | empty, because the job declares no environment |

The Create form then asks which scopes the policy grants — pushing new packages, pushing new versions
of existing packages and unlisting are offered separately — and which packages those scopes apply to.
That box is the one the portal labels *Glob Patterns and Packages (Separate by a new line)*, and it
takes one glob or exact package ID per line:

```
SharpCorePDF
SharpCorePDF.*
```

An empty box is refused with *At least one valid glob pattern or package must be specified*. A line may
hold only `*`, letters, digits, `_`, `.` and `-`, which is why both lines are accepted — and both are
needed. `SharpCorePDF.*` matches the four layers, which all have a dot after the product name, and does
not match `SharpCorePDF` itself, which has not. A policy holding only the glob publishes four packages and
refuses the entry point with 403 Forbidden; five lines holding the five IDs cover the same ground. The
temporary API key is scoped to the subjects written there, so a package that no subject matches cannot be
pushed.

The policy is owned by a nuget.org user or organization and applies to every package that owner owns;
its scopes decide whether it may publish new packages, new versions of existing ones, or both. The
`user:` input the workflow passes to `NuGet/login` is that account's username, kept as the repository
secret `NUGET_USERNAME` (settings → Secrets and variables → Actions).

Publishing then happens one of two ways:

* **Actions → publish → Run workflow.** With `publish` left false it stops after packing: both test
  legs run, every package is checked, and the packages are pushed into a throwaway local feed as a
  handshake. Set `publish` to true to carry on to nuget.org.
* **Push a `v*` tag** —
  `git tag v1.0.0 && git push origin v1.0.0`. The tag is a `v` followed by the exact `<Version>` in
  [`src/Directory.Build.props`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/src/Directory.Build.props),
  because a tagged run refuses to publish a version the tag does not name: `v1.0.0-preview.1` against a
  `<Version>` of `1.0.0` fails the check instead of publishing. `v1.0.0-preview.1` put the four layers on
  the feed on 2026-10-15; `v1.0.0` added the entry point on 2026-10-17, in the same push as the layers'
  first stable version.

Nothing reaches a feed until the packages pass that check: every `.nupkg` must carry both `lib/net10.0`
and `lib/net11.0`, which is the promise made in `src/Directory.Build.props`.

A version on nuget.org is immutable, so bump `<Version>` before publishing; `--skip-duplicate` makes a
re-run of an already-published version a no-op instead of an error. Publishing from a workstation still
means a long-lived API key, because the temporary ones exist only inside a CI run. GitHub Packages is a
separate mechanism that does not use any of this: it wants a token with `packages: write`, either
`GITHUB_TOKEN` inside a workflow or a personal access token from a workstation.

What has to be true *before* any of that is spent on a version number — the package id, the settled
surface, the single target framework, the changelog, the symbols, and the dry run — is written down as a
gate list in
[`docs/release-checklist.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/release-checklist.md).
The [.NET Foundation's eligibility criteria](https://github.com/dotnet-foundation/projects#eligibility-criteria),
adopted here as the project's public bar rather than as an application tactic, are mapped criterion by
criterion in
[`docs/foundation-readiness.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/foundation-readiness.md).
The eight conditions that decide *when* an application is filed, and the answers it will contain, are in
[`docs/foundation-application.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/docs/foundation-application.md).

## Contributing

Read
[`CONTRIBUTING.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/CONTRIBUTING.md) before the
first pull request: it names the two SDKs the build needs, the per-framework test invocations, the bar a
change has to clear, and what a merge buys you in rights. Participation falls under the
[Code of Conduct](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/CODE_OF_CONDUCT.md), and a
suspected vulnerability goes through
[`SECURITY.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/SECURITY.md) rather than through
the issue tracker — a public issue describing a crash in a PDF parser is a working exploit by the time it
is indexed.

## Feedback

Bugs, questions and feature requests go to the
[issue tracker](https://github.com/MPCoreDeveloper/SharpCorePDF/issues). The most valuable report of
all is a merge that produced a file something else cannot open: attach the sources if you can, and the
warning list the merge returned, and it becomes a test.

