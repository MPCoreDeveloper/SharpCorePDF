# 1. What SharpCorePDF Is

SharpCorePDF is a PDF **merging** and **inspection** library for .NET. It answers two questions:

1. *What is in this document?* — page count, page sizes, rotation, metadata, outlines, annotations,
   form fields, embedded files, encryption and the warnings the reader had to record.
2. *How do I produce one document from several?* — in the order I choose, with the pages I choose,
   under the output policies I choose.

Everything else in the API exists to make those two operations predictable: an immutable plan, value
objects for the inputs, read models for the outputs, typed exceptions for the failures, and warnings
for the decisions that are not failures.

## Two use cases, one engine

Merging and inspection share the same reader. A merge inspects every source anyway — it has to know
the page count before it can resolve `"2-5"` — so inspection is not a separate code path with its own
interpretation of a damaged file. If `InspectAsync` reports four pages, the merge that follows sees the
same four pages and the same warnings.

```csharp
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Infrastructure.Extensions;

var plan = MergePlan.Create()
    .AddFile("quarterly-cover.pdf")
    .AddFile("quarterly-body.pdf", pages: "1-12")
    .AddFile("quarterly-appendix.pdf", displayName: "Appendix");

var result = await merger.MergeToFileAsync(plan, "quarterly-report.pdf");
```

The call above produces a new file and returns a `MergeResult`:

```text
18 pages from 3 sources, written as PDF 1.7, 421,338 bytes, in 41 ms
Primary source: quarterly-cover.pdf
Warnings: none
```

The result is a *summary*, not the document. It tells you how many pages were written, how long it
took, which source led the merge, what each source contributed and which warnings the operation
produced. That is enough for a log line, an audit record or a UI that says "18 pages, 3 documents".

## What a merge actually does

A merge is not a concatenation of files. The engine:

- reads each source, following the page tree to its leaves and resolving indirect objects;
- copies every object the selected pages need into a new document, rewriting references so that the
  copies refer to each other and not to the source file;
- builds a new page tree in source order, preserving each page's size, rotation, content, resources
  and annotations;
- merges bookmarks into one outline whose top-level entries follow source order;
- applies the output policies: document information, page labels, form fields, embedded files,
  encryption and the PDF version written in the header;
- writes a complete file with a cross-reference table and a trailer.

The source files are only ever read. The merge does not modify them, does not need write access to
them, and does not leave temporary files beside them.

## The guarantees you can build on

**The plan is immutable.** Every method that changes a plan returns a new plan; nothing mutates an existing
one. A plan can be built at start-up, validated once, shared between requests and executed
concurrently without a lock. Two plans with the same sources and options are equal.

**Plans are validated before work starts.** `Validate()` reports every problem it can see without
opening a document — no sources, a duplicate identity, an unsupported target version, an encryption
algorithm that the target version cannot carry. `EnsureValid()` turns those problems into a single
`MergePlanValidationException`. A bad plan fails before any output file is created.

**Failures are typed.** A malformed source, a missing password, an unsupported feature and an invalid
plan each have their own exception type, all derived from `PdfException`. You can catch the base type
for a coarse handler or the derived types for a precise one.

**Recoverable problems are warnings.** The reader rebuilds a missing cross-reference table, skips an
unreadable bookmark and assumes a page size when the page tree does not state one. Each decision is
recorded as a `PdfWarning` with a stable `Kind`, so a caller can decide whether the document is good
enough without parsing error text.

**Cancellation is honoured.** Every asynchronous operation takes a `CancellationToken`. Cancellation
throws `OperationCanceledException`, the .NET convention, and does not masquerade as a PDF error.

**No native dependency.** The package is managed code. There is no `pdfium`, no `qpdf`, no executable
to deploy, no platform-specific runtime to select and nothing to install on the host beyond .NET.

## The packages

The installable package is **`SharpCorePDF`**. It is a meta-package: it carries no assemblies of its
own and depends on the four layers, so one `dotnet add package SharpCorePDF` is the whole install.

| Package | Contains | Reference it when |
| --- | --- | --- |
| `SharpCorePDF.Domain` | Plan, sources, value objects, read models, ports, exceptions, warnings | You build plans in a domain project that must not depend on the engine |
| `SharpCorePDF.Application` | Merge and inspection use cases, convenience extensions | You need the services without the concrete engine |
| `SharpCorePDF.Infrastructure` | Reader, engine, file and memory sources, engine options | You construct the engine yourself instead of using DI |
| `SharpCorePDF.DependencyInjection` | `AddSharpCorePDF()` and configuration binding | You register the library in a `IServiceCollection` |
| `SharpCorePDF` | Dependencies on all four | You just want the library |

The layers exist so that a solution can keep its domain free of infrastructure, not so that every
consumer has to think about them. If you are unsure, install `SharpCorePDF`.

## What "no third-party engine" means in practice

The PDF parser, the object copier, the outline merger, the encryption writer and the cross-reference
writer are implemented in this repository. The runtime dependencies of the library projects are the
`Microsoft.Extensions.*` abstractions used by the dependency injection package; there is no PDF
library underneath.

That has consequences worth knowing before you adopt it:

- The feature set is deliberately bounded. Chapter 2 lists the boundary.
- The writer emits classic cross-reference tables and requires PDF 1.4 or newer. It does not write
  incremental updates or linearised files.
- The reader is written to accept the shapes real producers emit — object streams, cross-reference
  streams, damaged tables, the six revisions of the standard security handler — and to say so in a
  warning when it has to repair something.

If your requirement is "merge these documents and tell me what you had to fix", that trade is a good
one. If your requirement is a general PDF toolkit, Chapter 2 will tell you quickly whether this is the
wrong library.
