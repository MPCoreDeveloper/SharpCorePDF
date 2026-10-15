<!--
  The logo and the badges are absolute URLs on purpose: nuget.org refuses to render a relative image
  path, and this README is packed into every package (PackageReadmeFile in src/Directory.Build.props).
  Logo.jpg is the 2048 x 2048 source; Logo-256.png is the icon the packages carry. Both image hosts
  are on nuget.org's trusted list (raw.githubusercontent.com and img.shields.io), and its renderer
  keeps <p align>, the <img> tag and the align/width/src/alt attributes. The version badge asks for
  vpre, so it reads 1.0.0-preview.1 now and 1.0.0 once that ships.
-->
<p align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCorePDF/main/Logo.jpg" alt="SharpCorePDF" width="260" />
</p>
<p align="center">
  <a href="https://www.nuget.org/packages/SharpCorePDF.DependencyInjection">
    <img src="https://img.shields.io/nuget/vpre/SharpCorePDF.DependencyInjection?label=nuget.org" alt="nuget.org version" />
  </a>
  <a href="https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/LICENSE">
    <img src="https://img.shields.io/badge/licence-MIT-blue" alt="licence: MIT" />
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
  destinations, embedded files, page labels, XMP metadata — are carried over from the primary source,
  because they cannot sensibly exist twice in one document.
* **Document information** is taken from the primary source, merged from all sources, or suppressed;
  an explicit override always wins. Fix `ModificationDate` and the output is reproducible byte for byte.
* **Diagnostics instead of surprises**: damaged cross-reference tables are rebuilt by scanning the
  file, unselected pages that something still points at are folded onto a page that exists, and
  anything the merge had to change is reported in `MergeResult.Warnings`.

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
bookmarks, embedded files, form presence, warnings — and reports an encrypted document instead of
throwing, so a caller can decide what to do before merging anything.

The package to install is `SharpCorePDF.DependencyInjection`: it depends on the other three, and its
`AddSharpCorePDF()` call is the whole wiring. The version is still a preview, so `--prerelease` is
required until 1.0.0 ships:

```powershell
dotnet add package SharpCorePDF.DependencyInjection --prerelease
```

The XML documentation ships inside the packages, so every public type and member is described in
IntelliSense without leaving the editor.

To work against this source tree instead of the feed, reference the project
(`dotnet add reference .\src\SharpCorePDF.DependencyInjection\SharpCorePDF.DependencyInjection.csproj`),
or pack all four into a local feed and install them from there:

```powershell
dotnet pack .\SharpCorePDF.slnx -c Release -o $env:TEMP\SharpCorePDF-feed
dotnet nuget add source $env:TEMP\SharpCorePDF-feed --name SharpCorePDF
dotnet add package SharpCorePDF.DependencyInjection --prerelease
```

Every package carries the logo as its package icon and embeds this README, so the page a feed shows for
it is the introduction you are reading. nuget.org renders images only from a list of trusted hosts and
cannot resolve relative paths, which is why the logo and the links in this file are absolute URLs.

## Examples

[`examples/SharpCorePDF.Examples`](https://github.com/MPCoreDeveloper/SharpCorePDF/tree/main/examples/SharpCorePDF.Examples)
is a console app with eight runnable examples. Every snippet on this page is code that compiles and
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
| `inspect-first` | Describing a folder of documents before merging: an encrypted or unusable file is reported, not thrown. |
| `options` | Metadata policy and override, outline and page-label policy, the target version, reproducible output. |
| `warnings` | What inspection reports about a document, versus what a merge reports about its own work. |
| `errors` | Every refusal a caller can meet, with the exception type and the message the library produces. |

The examples generate the documents they merge, so nothing else is needed to run them, and they leave the
results in a folder under the temporary directory for you to open.
[`examples/README.md`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/examples/README.md)
walks through each one.

## How it is put together

Four packages, one per layer. Each name links to its page on nuget.org.

| Package | Contents |
| --- | --- |
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

* **Encrypted documents are refused** with `PdfEncryptionException`; no decryption is attempted, so
  `PdfDocumentInfo.RequiresPassword` is always `false` — the engine cannot know whether an empty user
  password would have opened the file.
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
# Everything at once, through the solution: the four packages, the tests,
# the examples and the measurement harness.
dotnet build .\SharpCorePDF.slnx -c Release
dotnet test  .\SharpCorePDF.slnx                      # 53 tests on net10.0, 53 on net11.0
dotnet run --project .\examples\SharpCorePDF.Examples -c Release

# Or one project at a time:
dotnet build .\src\SharpCorePDF.DependencyInjection\SharpCorePDF.DependencyInjection.csproj

# The test project must build an app host, which the RID-less net10.0 leg of some SDK builds skips:
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64
```

The tests cover the domain invariants, the serialisation details, the reader against a real-world
document, a document that uses object streams and cross-reference streams, a document with bookmarks,
page labels, annotations and a rotated page, an encrypted document, and the whole merge path through
the container.

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
uv run --with pikepdf python .\docs\tools\make-fixtures.py .\tests\SharpCorePDF.Tests\Assets
```

`validate-pdf.py` runs `qpdf --check`, decodes every stream in the file, and prints the geometry,
rotation and annotations of every page. It is the check that caught the one bug this library has had so
far: a hexadecimal string written with name escapes, which `qpdf` rejected as damage.

## Publishing

Packages go to nuget.org from
[`.github/workflows/publish.yml`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/.github/workflows/publish.yml),
and no API key is stored anywhere. The workflow asks GitHub for an OIDC token, exchanges it with
nuget.org for a temporary API key that lives for one hour, and pushes. nuget.org performs that
exchange only when the token matches a **trusted publishing policy** for this repository, so the
workflow can publish nothing until that policy exists.

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
SharpCorePDF.*
```

An empty box is refused with *At least one valid glob pattern or package must be specified*. A line may
hold only `*`, letters, digits, `_`, `.` and `-`, which is why `SharpCorePDF.*` is accepted and why it
covers all four packages here; four lines holding the four IDs cover the same ground. The temporary API
key is scoped to the subjects written there, so a package that no subject matches cannot be pushed.

The policy is owned by a nuget.org user or organization and applies to every package that owner owns;
its scopes decide whether it may publish new packages, new versions of existing ones, or both. The
`user:` input the workflow passes to `NuGet/login` is that account's username, kept as the repository
secret `NUGET_USERNAME` (settings → Secrets and variables → Actions).

Publishing then happens one of two ways:

* **Actions → publish → Run workflow.** With `publish` left false it stops after packing: both test
  legs run, every package is checked, and the packages are pushed into a throwaway local feed as a
  handshake. Set `publish` to true to carry on to nuget.org.
* **Push a `v*` tag** —
  `git tag v1.0.0-preview.1 && git push origin v1.0.0-preview.1`. The tag is a `v` followed by the
  exact `<Version>` in
  [`src/Directory.Build.props`](https://github.com/MPCoreDeveloper/SharpCorePDF/blob/main/src/Directory.Build.props),
  prerelease suffix included, because a tagged run refuses to publish a version the tag does not name:
  `v1.0.0` against the current `1.0.0-preview.1` fails the check instead of publishing.

Nothing reaches a feed until the packages pass that check: every `.nupkg` must carry both `lib/net10.0`
and `lib/net11.0`, which is the promise made in `src/Directory.Build.props`.

A version on nuget.org is immutable, so bump `<Version>` before publishing; `--skip-duplicate` makes a
re-run of an already-published version a no-op instead of an error. Publishing from a workstation still
means a long-lived API key, because the temporary ones exist only inside a CI run. GitHub Packages is a
separate mechanism that does not use any of this: it wants a token with `packages: write`, either
`GITHUB_TOKEN` inside a workflow or a personal access token from a workstation.

## Feedback

Bugs, questions and feature requests go to the
[issue tracker](https://github.com/MPCoreDeveloper/SharpCorePDF/issues). The most valuable report of
all is a merge that produced a file something else cannot open: attach the sources if you can, and the
warning list the merge returned, and it becomes a test.

