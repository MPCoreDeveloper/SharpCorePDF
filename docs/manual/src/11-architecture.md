# 11. Architecture

SharpCorePDF is built as four layers around a pure domain model. This chapter explains the shape and
the reasons behind it, so that you can judge where a change belongs and what a failure means.

## The layers

| Project | Contains | Depends on |
| --- | --- | --- |
| `SharpCorePDF.Domain` | `MergePlan`, `PdfSource`, value objects, policies, read models, warnings, exceptions, `IPdfContentSource` | Nothing but the BCL |
| `SharpCorePDF.Application` | `PdfMergerService`, `PdfInspectorService`, `IPdfDocumentReader`, `IPdfMergeEngine`, extension methods | Domain |
| `SharpCorePDF.Infrastructure` | The PDF reader, writer, object model, filters, copier, engine and content sources | Domain, Application |
| `SharpCorePDF.DependencyInjection` | `AddSharpCorePDF()` and the service registrations | Domain, Application, Infrastructure |
| `SharpCorePDF` (meta-package) | No assembly; depends on the four above | — |

Dependencies point inward:

```text
SharpCorePDF (meta)
        │
        ├── DependencyInjection ──┬── Infrastructure ──┐
        │                         ├── Application ─────┤
        │                         └── Domain ◄─────────┘
        └── (transitively, all four)
```

The domain does not know what a file is. It defines plans, page selections, output options and results,
but the only way it reaches the outside world is `IPdfContentSource`, which is a stream factory with a
display name. That is why a plan can be built, validated and tested without touching the disk.

The application layer orchestrates but does not parse PDF syntax. It validates a plan, opens each
source, asks `IPdfDocumentReader` what is inside, resolves the page selections against the real page
counts, and hands a `MergeEngineRequest` to `IPdfMergeEngine`. It also owns the lifetime of every stream
it opens, so a failure in the middle does not leak a handle.

The infrastructure layer is the PDF engine. It reads bytes, parses objects, decrypts, walks page trees,
copies object graphs, writes a cross-reference table and encrypts the result. It has no opinion about
where a document came from or which pages the caller wanted; the request states both.

The dependency injection project is the composition root. It knows the concrete classes, and nothing
else does.

## The life of a merge

A call to `IPdfMerger.MergeAsync` passes through these stages:

1. **Argument checks.** A null plan or destination is a programmer error.
2. **Plan validation.** `EnsureValid()` checks the invariants that do not need a document: at least one
   source, unique identities, a primary source that belongs to the plan, defined enum values, usable
   encryption settings.
3. **Opening.** Each `IPdfContentSource` is asked for a readable stream. The streams stay open until the
   merge ends.
4. **Inspection.** `IPdfDocumentReader` reads each source, opens its encryption if a password was given,
   walks its page tree and reports `PdfDocumentInfo`. The application layer turns an unreadable
   encrypted source into `PdfEncryptionException` here, while it still knows which source it was.
5. **Resolution.** `PageSelection.Resolve` maps the one-based ranges onto real page numbers, collapses
   duplicates and preserves order; the engine skips a closed range's tail past the last page.
6. **Writing.** The engine receives the resolved sources and the output options. It copies the objects
   that the selected pages and retained features reach, renumbers them, builds one page tree, merges
   catalog-level features according to the policies, and writes the result to the destination.
7. **Release.** The application disposes every source stream in a `finally`, including on cancellation
   and on failure.

The engine never mutates the plan or the request. Everything it needs while running lives in
`PdfMergeContext`, a per-merge object; that is what makes the registered services safe to share.

## Why the plan is immutable

`MergePlan` is an immutable record. `AddSource`, `WithOutput` and the rest return new instances, and a
source identity is created once and preserved by every operation on `PdfSource`. Three consequences are
deliberate:

- A plan can be validated and then executed without a window in which another thread changes it.
- `MergePlanValidationException.Errors` describes a specific plan value, not a transient state.
- A plan is safe to log, cache or pass across layers. Its `ToString()` is stable and excludes passwords.

The cost is allocation on every edit. Building a plan of a hundred sources allocates a hundred plans;
that is negligible next to reading the sources, and it buys predictable behaviour.

## What the engine writes

The result is a new PDF, not an incremental update of a source. In particular:

- The merged document uses a classic cross-reference table, even when a source used cross-reference
  streams or object streams. Objects stored in object streams are written as ordinary objects.
- Only reachable objects are copied. A source's unused fonts, unused images or long-deleted revisions do
  not travel into the result.
- The object graph of each source is copied in two phases — reserve, then write — so cycles such as a
  form field pointing at its widget and back are handled without recursion loops.
- A single page tree node lists the selected pages in merge order. Page labels, outlines, annotations,
  form fields and attachments are handled by the output policies described in Chapter 6.
- The document is written as it is produced; there is no temporary full copy in the engine unless
  `VerifyOutput` is enabled, which captures the whole output so it can be re-read and checked.

## The memory model

Two facts determine the memory profile, and both are worth knowing before processing large files:

1. **Each source is read completely into memory.** `PdfByteReader` reads a stream into a pooled buffer,
   and parsing resolves objects from those bytes on demand. `MaxSourceBytes` is the guard.
2. **Every source is held until the merge finishes.** The application inspects each source once, then
   the engine reads it again, and the engine keeps a reader per source while it copies objects from all
   of them. Peak memory is therefore roughly the sum of all source sizes, plus the destination's own
   buffering.

The destination is written in a streaming fashion, so a large result does not need a second full copy in
memory. Verification is the exception: `VerifyOutput` deliberately captures the whole output.

For very large jobs, split the work into separate output documents, or merge subsets to temporary files
and keep the intermediates on disk rather than in byte arrays. Staging reduces the work of each step, but
the final merge still holds every intermediate it is given; it cannot lower the peak below the total size
of all sources for one output document.

## Boundaries and extension points

The ports are the seams:

| Port | Implemented by | Replace it when |
| --- | --- | --- |
| `IPdfContentSource` | `PdfFileSource`, byte and stream sources | Documents come from a database, blob store or network |
| `IPdfDocumentReader` | `PdfDocumentReader` | You need a different inspection strategy in tests |
| `IPdfMergeEngine` | `PdfMergeEngine` | You want to stub the writer while testing orchestration |
| `IPdfMerger` | `PdfMergerService` | You want to test a caller without any PDF work |
| `IPdfInspector` | `PdfInspectorService` | Same, for inspection |

A content source is small enough to write for any backing store. Each call must return an independent,
readable stream positioned at the start, and `DisplayName` should identify the document in errors:

```csharp
using SharpCorePDF.Domain.Ports;

public sealed class StreamFactorySource(
    string displayName,
    Func<CancellationToken, ValueTask<Stream>> open) : IPdfContentSource
{
    public string DisplayName { get; } = displayName;

    public long? Length => null;

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        open(cancellationToken);
}
```

```csharp
var source = PdfSource.Create(new StreamFactorySource(
    "report.pdf",
    cancellationToken => storage.OpenReadAsync("report.pdf", cancellationToken)));
```

The merger takes ownership of the returned stream and disposes it, so the factory must return a fresh
stream on every call. If the backing store can report a length cheaply, return it from `Length`; the
current reader buffers from the stream's own `Length` when it is seekable, so the property is a
declaration of what you know rather than something the engine relies on today.

What is *not* pluggable is equally deliberate: PDF filters, encryption handlers and the object model
live inside the infrastructure layer. Adding a filter means changing the engine, not registering a
service. The supported feature set is a compatibility promise, and a plugin point would make it
impossible to say what the library can read.

## Concurrency

Every registered service is a singleton and stateless. Concurrent merges share the services but not the
data: each call has its own request, context, readers and writer. Two rules follow for callers:

- **Do not share a destination stream between concurrent merges.** The stream itself is not
  synchronised, and interleaved PDF output is corrupt by construction.
- **Do share plans.** An immutable plan can be executed repeatedly and concurrently; the sources open a
  fresh stream per merge.

The content source is the only collaborator a caller supplies, and the contract requires
`OpenReadAsync` to be safe to call more than once. A source backed by a non-seekable one-shot network
stream cannot honour that contract; buffer it first, as the upload example in Chapter 10 does.


