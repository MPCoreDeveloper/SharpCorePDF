# SharpCorePDF examples

Eight runnable examples that show how the library is used, and the console app that runs them. Everything
here is code that compiles and executes against the four packages, so nothing in the documentation is a
snippet that was never run.

```powershell
# every example
dotnet run --project .\examples\SharpCorePDF.Examples -c Release

# what there is to run
dotnet run --project .\examples\SharpCorePDF.Examples -- list

# one example, or several, by name
dotnet run --project .\examples\SharpCorePDF.Examples -- select-pages
dotnet run --project .\examples\SharpCorePDF.Examples -- errors options
```

The examples write their documents to `%TEMP%\SharpCorePDF.Examples` (printed at the start of every run) and
leave them there, so the merged files can be opened in a viewer afterwards. The exit code is `0` when every
example ran and `1` when one failed.

## What each example shows

| Name | Shows |
| --- | --- |
| `merge-files` | Three documents from disk into one file, and what the merge result says about each source. |
| `select-pages` | The page selection notation (`3,1`, `2-`, `-2`, `2,2,1`), that it is an order and not a set, and how to build a selection in code. |
| `merge-streams` | A source that is an upload stream and a source that is already a `byte[]`, with the merged document handed back as bytes. |
| `no-container` | The same merge without a dependency injection container, and the proof that the result is the same. |
| `inspect-first` | Describing a folder of documents before merging, which is how an encrypted or unusable file is found without an exception. |
| `options` | Metadata policy and override, outline and page-label policy, the target version, and a reproducible result (byte for byte). |
| `warnings` | The two channels: what inspection reports about a document, and what the merge reports about its own work. |
| `errors` | Every refusal a caller can meet, with the exception type and the message the library produces. |

## The documents the examples merge

The samples are built byte by byte by [`SampleDocuments.cs`](SharpCorePDF.Examples/SampleDocuments.cs),
including a correct cross-reference table, so the examples need no fixture files and run from any folder.
They are not part of the library; they exist so that running the examples has something to merge.

| File | What it is |
| --- | --- |
| `Summary.pdf` | Two portrait pages (595 x 842 pt). |
| `Appendix.pdf` | Three landscape pages (842 x 595 pt), an author and a creation date. |
| `Annex.pdf` | One portrait page. |
| `Broken.pdf` | A text file that someone renamed to `.pdf`. |
| `Encrypted.pdf` | A document whose trailer declares `/Encrypt`. |
| `NamedDests.pdf` | A first page with no declared page size, and named destinations in its catalog. |

Every page of a document is ten points narrower than the one before it. That is what lets an example name a
page by printing its size, which is how `select-pages` shows that `3,1` really did put page 3 first:

```text
--- Appendix, page 3 before page 1: "3,1"  ->  2 page(s)
    page 1: 822 x 595 pt      <- page 3 of the appendix
    page 2: 842 x 595 pt      <- page 1 of the appendix
```

## The five moves the examples are built from

```csharp
// 1. Build a plan: which pages, from which documents, in which order.
var plan = MergePlan.Create()
    .AddFile(@"C:\reports\summary.pdf")                  // every page
    .AddFile(@"C:\reports\appendix.pdf", pages: "1-3")   // one-based: "5", "2-7", "3-", "-4", comma separated
    .AddFile(@"C:\reports\annex.pdf", pages: "3,1");     // in this order

// 2. Merge it, to a file, to a stream or to bytes.
var result = await merger.MergeToFileAsync(plan, @"C:\reports\combined.pdf");
var bytes  = await merger.MergeToBytesAsync(plan);

// 3. Find out what a document contains before merging it. An encrypted document is reported, not thrown.
var info = await inspector.InspectAsync(PdfFileSource.From(path));

if (info.IsEncrypted) { /* impossible to merge, and the merge would say so too */ }

// 4. Control what the result carries besides the pages.
var report = plan.WithOutput(options => options with
{
    TargetVersion = PdfVersion.V2_0,
    MetadataPolicy = MetadataPolicy.Merge,
    MetadataOverride = new PdfMetadata { Title = "Quarterly report 2026" },
    OutlinePolicy = OutlinePolicy.Drop,
    ModificationDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),   // reproducible output
});

// 5. Register it once, in a container, and inject the two ports.
services.AddSharpCorePDF();          // default engine options
services.AddSharpCorePDF(config);    // or from the "SharpCorePDF" configuration section
```

## How the examples are put together

| File | Contents |
| --- | --- |
| `Program.cs` | The runner: argument handling, Ctrl+C cancellation, and the exit code. |
| `Scenarios.cs` | The example registry and the command line handling. |
| `ExampleContext.cs` | The sample documents, the container (`AddSharpCorePDF` from `appsettings.json`), the two ports, and the report helper. |
| `SampleDocuments.cs` | The fixture generator described above. |
| `MergeExamples.cs` | `merge-files`, `select-pages`. |
| `SourceExamples.cs` | `merge-streams`, `no-container`. |
| `InspectionExamples.cs` | `inspect-first`. |
| `OptionExamples.cs` | `options`, `warnings`. |
| `ErrorHandlingExamples.cs` | `errors`. |
| `appsettings.json` | The engine options the container binds, including `VerifyOutput`, so every example also proves its output can be read back. |

The app targets `net11.0`. The packages multi-target `net10.0;net11.0`, and changing `TargetFramework` in the
project file to `net10.0` runs exactly the same examples on the older target.

## Where to go next

* [`docs/measurements.md`](../docs/measurements.md) — what a merge costs in time, allocations and peak memory,
  and the method behind those numbers.
* [`tests/SharpCorePDF.Tests`](../tests/SharpCorePDF.Tests) — the specification: domain invariants, the reader
  against real-world documents, the merge path through the container.
* [`docs/tools/validate-pdf.py`](../docs/tools/validate-pdf.py) — check any PDF (including one these examples
  wrote) with an independent implementation:

  ```powershell
  uv run --with pikepdf python .\docs\tools\validate-pdf.py "$env:TEMP\SharpCorePDF.Examples\output\combined.pdf"
  ```

