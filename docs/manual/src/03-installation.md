# 3. Installation and First Merge

This chapter takes an empty console project to a merged file and an inspection report. It should take
about five minutes.

## Install the package

```bash
dotnet add package SharpCorePDF --version 1.2.0
```

One package is enough. `SharpCorePDF` is a meta-package that brings the domain model, the application
services, the engine and the dependency injection registration together. The four layer packages
remain installable on their own — see Chapter 1 for when that is useful — but an application that
merges documents does not need to choose.

The container is the one thing that package leaves to you. `SharpCorePDF.DependencyInjection` references
`Microsoft.Extensions.DependencyInjection.Abstractions` — the assembly `AddSharpCorePDF()` extends and the
one `IServiceCollection` lives in — while `ServiceCollection` and `BuildServiceProvider()` are in
`Microsoft.Extensions.DependencyInjection`. An application that builds a provider itself adds that package:

```bash
dotnet add package Microsoft.Extensions.DependencyInjection
```

An application that runs inside an ASP.NET Core or Worker host already has it, which is why no line below
mentions it again. Nothing else in this manual needs a second package, and Chapter 10 shows the manual
wiring for a caller who wants no container at all.

The library targets **.NET 10** and **.NET 11**. A project on either target framework can reference
it; the package selects the matching assembly.

## Create the project

```bash
dotnet new console -n MergeDemo
cd MergeDemo
dotnet add package SharpCorePDF --version 1.2.0
```

Put two or more PDFs beside the project, or change the paths below. The example uses `cover.pdf`,
`body.pdf` and `appendix.pdf`.

## Register the services

The shortest correct wiring is a service collection. It costs four lines and gives you the merger, the
inspector and the engine behind their ports, all as singletons.

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Ports;

var services = new ServiceCollection()
    .AddSharpCorePDF()
    .BuildServiceProvider();

var merger = services.GetRequiredService<IPdfMerger>();
var inspector = services.GetRequiredService<IPdfInspector>();
```

`AddSharpCorePDF()` has three overloads: no arguments (default engine options), an explicit
`PdfEngineOptions`, and an `IConfiguration` plus an optional section name. Chapter 10 covers all three
and the options themselves.

You do not have to use dependency injection. The infrastructure types have public constructors, and
Chapter 10 shows the manual wiring. The rest of this manual uses the container because it is the
supported path for an application.

## Merge

Add the following to `Program.cs`. The merge writes the selected pages of three documents, in the
order they are added, to `merged.pdf`.

```csharp
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Infrastructure.Extensions;

var plan = MergePlan.Create()
    .AddFile("cover.pdf")
    .AddFile("body.pdf", pages: "1-3,7")
    .AddFile("appendix.pdf", displayName: "Appendix", pages: "2-");

var result = await merger.MergeToFileAsync(plan, "merged.pdf");

Console.WriteLine($"{result.PageCount} pages, {result.OutputBytes} bytes, {result.Duration.TotalMilliseconds:F0} ms");
Console.WriteLine($"Primary source: {plan.FindSource(result.PrimarySourceId)?.DisplayName}");
```

Run it:

```bash
dotnet run
```

Expected output, with different timings and sizes:

```text
6 pages, 184213 bytes, 37 ms
Primary source: cover.pdf
```

Three things are worth noticing in that snippet.

**The page selection is a string.** `"1-3,7"` means pages 1, 2, 3 and 7, in that order. `"2-"` means
page 2 to the end. The notation is explained in Chapter 5; `null` or an omitted argument means every
page.

**The display name is for humans.** It defaults to the file name and appears in `MergeResult`, in the
source summaries and in messages. It does not have to be unique and it does not affect the output.

**The extension methods come from two namespaces.** `AddFile`, `AddBytes` and `AddStream` live in
`SharpCorePDF.Infrastructure.Extensions`; `MergeToFileAsync` and `MergeToBytesAsync` live in
`SharpCorePDF.Application.Extensions`. A missing `using` for either produces a compiler error that
mentions the receiver type, not the namespace.

## Read the result

`MergeResult` is the record of what happened. The properties used most often are:

| Property | Meaning |
| --- | --- |
| `PageCount` | Pages written to the output |
| `SourceCount` | Number of sources that contributed at least one page |
| `OutputBytes` | Size of the merged document |
| `OutputVersion` | The PDF version written in the header |
| `Duration` | Wall-clock time spent in the merge |
| `PrimarySourceId` | The source that supplied document-level features |
| `Sources` | One `MergedSourceSummary` per input, in plan order, with contributed page count |
| `Warnings` | Every warning recorded while reading, merging or writing |

The destination is not repeated in the result: the caller already has the path or the stream it
passed in. Use `plan.FindSource(result.PrimarySourceId)` when you need the display name of the primary
source, as in the example above.

If `Warnings` is empty, nothing had to be repaired or dropped. If it is not empty, the merge still
succeeded; Chapter 9 tells you how to triage the warning kinds.

## Inspect before you merge

Inspection uses the same reader. Use it to check a document before adding it to a plan, or to display
a summary to a user.

```csharp
using SharpCorePDF.Infrastructure.Sources;

var info = await inspector.InspectAsync(new PdfFileSource("body.pdf"));

Console.WriteLine($"{info.PageCount} pages, PDF {info.Version}, encrypted: {info.IsEncrypted}");
Console.WriteLine($"Readable: {info.IsReadable}, warnings: {info.Warnings.Count}");

foreach (var page in info.Pages.Take(3))
{
    Console.WriteLine(
        $"  page {page.Number}: {page.Size.WidthPoints:F0} x {page.Size.HeightPoints:F0} pt, " +
        $"rotation {page.Rotation.Degrees}");
}
```

`PdfFileSource` is a small `IPdfContentSource` that opens the file when it is read. `InspectAsync` has
an optional password parameter — `InspectAsync(source, password, cancellationToken)` — which Chapter 8
covers together with the rest of the read models.

## A complete `Program.cs`

The pieces above, assembled and made a little more careful: it validates the plan before doing any
work, prints warnings, and returns a non-zero exit code if the merge fails.

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Infrastructure.Extensions;

var services = new ServiceCollection().AddSharpCorePDF().BuildServiceProvider();
var merger = services.GetRequiredService<IPdfMerger>();

var plan = MergePlan.Create()
    .AddFile("cover.pdf")
    .AddFile("body.pdf", pages: "1-3,7")
    .AddFile("appendix.pdf", displayName: "Appendix");

try
{
    plan.EnsureValid();

    var result = await merger.MergeToFileAsync(plan, "merged.pdf");
    Console.WriteLine($"Wrote {result.PageCount} pages, {result.OutputVersion}.");

    foreach (var warning in result.Warnings)
    {
        Console.WriteLine($"warning {warning.Kind}: {warning.Message}");
    }

    return 0;
}
catch (PdfException exception)
{
    Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
    return 1;
}
```

Two details in that listing are deliberate. `EnsureValid()` runs before the destination file is
created, so an invalid plan cannot leave a truncated file behind. And the `catch` is for `PdfException`,
the base type of every library failure, rather than `Exception`; an `OperationCanceledException` or an
`OutOfMemoryException` is not a PDF problem and should not be reported as one.

## Where to go next

- To control which pages come from which document, read Chapter 5.
- To set metadata, version, bookmarks or encryption, read Chapters 6 and 7.
- To decide what to do when a source is damaged or password-protected, read Chapters 8 and 9.
- To wire the library into an existing application, read Chapter 10.
- To write a cover page from nothing and merge it in front, read Chapter 12.
