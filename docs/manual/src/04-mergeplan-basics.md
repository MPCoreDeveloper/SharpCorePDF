# 4. The Merge Plan

Everything the library does with several documents is described by a `MergePlan`: an ordered list of
sources and one set of output options. A plan is a value. It is not a builder that gets mutated, not a
job that gets executed, and not a handle that has to be disposed.

```csharp
var plan = MergePlan.Create()
    .AddFile("cover.pdf")
    .AddFile("chapter-1.pdf", pages: "1-")
    .AddFile("chapter-2.pdf", pages: "1-")
    .WithOutput(options => options with { TargetVersion = PdfVersion.V1_7 });
```

That single expression states the order of the merge, what each source contributes and how the output
must be written. The next chapters unpack the three parts; this one explains how they fit together.

## Building a plan

`MergePlan.Create()` starts an empty plan. Every method that changes it returns a new plan, so the usual style is to
chain calls and keep the result.

| Member | Purpose |
| --- | --- |
| `Create(MergeOutputOptions? output = null)` | Starts a plan, optionally with output options |
| `For(params PdfSource[] sources)` | Starts a plan from sources you built yourself |
| `AddSource(PdfSource source)` | Appends a source entity |
| `AddSources(IEnumerable<PdfSource> sources)` | Appends several sources, in enumeration order |
| `RemoveSource(PdfSourceId id)` | Returns a plan without the source with that identity |
| `WithOutput(MergeOutputOptions output)` | Replaces the output options |
| `WithOutput(Func<MergeOutputOptions, MergeOutputOptions> mutate)` | Adjusts them with a `with` expression |
| `WithPrimarySource(PdfSourceId? id)` | Sets, or clears, the primary source |
| `FindSource(PdfSourceId id)` | Returns the source, or `null` |
| `Validate()` | Returns every problem that can be found without reading a document |
| `EnsureValid()` | Throws `MergePlanValidationException` if there is one |
| `Sources` | The sources, in merge order |
| `PrimarySource` | The source that supplies document-level features |
| `SourceCount` | The number of sources |

The convenience methods `AddFile`, `AddBytes`, `AddStream` and `AddContent` are extension methods from
`SharpCorePDF.Infrastructure.Extensions`. They create the matching content source and call
`AddSource`, and they accept the page selection in its textual notation. The core `AddSource` is enough
when you already have a `PdfSource`.

## Sources and identity

A `PdfSource` has four pieces of state:

- **`Content`** — an `IPdfContentSource`, the object that can open the document for reading.
- **`DisplayName`** — the name used in messages and in `MergeResult`. It defaults to the content
  source's own name and can be changed with `Rename`.
- **`Pages`** — a `PageSelection`; `PageSelection.All` when nothing was specified.
- **`Password`** — the user or owner password for an encrypted document, or `null`.

It also has an **`Id`**, a `PdfSourceId` generated when the source is created. The identity is what
lets a plan refer to a source later — for example as the primary source — without relying on its
position, and it is what validation uses to detect an accidental duplicate.

Two `AddFile` calls for the same path produce two sources with two identities. That is legal and is how
you repeat a document:

```csharp
var plan = MergePlan.Create()
    .AddFile("terms.pdf", displayName: "Terms (before)")
    .AddFile("body.pdf")
    .AddFile("terms.pdf", displayName: "Terms (after)");
```

The duplicate that SharpCorePDF rejects is the same *identity* appearing twice, which can only happen if
you pass one `PdfSource` instance to `AddSource` twice or supply an explicit identity through
`PdfSource.Create(id, content, …)`. `AddSource` throws `MergePlanValidationException` immediately; it is
a programming error rather than a use case, and it fails before any document is read.

Sources also support non-mutating edits of their own:

```csharp
var source = PdfSource.Create(new PdfFileSource("report.pdf"))
    .WithPages(PageSelection.FromTo(2, 5))
    .Rename("Report, pages 2 to 5");
```

`WithPages`, `WithPassword` and `Rename` keep the identity, so a plan that already refers to the source
as its primary source keeps working after the edit.

## Order is the merge

The order of `Sources` is the order of the output. There is no sorting, no grouping by path and no
implicit title page. Pages contributed by the first source come first, then those of the second, and so
on. Within one source, the page selection decides the order, which is why `"3,1"` is a legal selection
and produces page 3 before page 1.

This makes a plan readable as a table of contents. If you need a different order, build a different
plan; reordering is intentional, not an optimisation the library performs behind your back.

## The primary source

Some things can only exist once in a PDF: the document information dictionary, viewer preferences, page
layout and mode, the interactive form, named destinations, embedded files. When sources disagree, the
merge must choose one of them. It chooses the **primary source**.

By default the primary source is the first source in the plan. `WithPrimarySource` changes it:

```csharp
var cover = plan.Sources[0];
var body = plan.Sources[1];

var withBodyMetadata = plan.WithPrimarySource(body.Id);
```

The primary source is usually the one whose identity matters most — the cover whose title should
survive, or the document whose form fields the user expects to keep working. It does not have to be the
first source, and setting it does not change the order of the pages. Chapter 6 lists exactly which
output options follow the primary source and which can override it.

`PrimarySource` throws `MergePlanValidationException` for an empty plan, and for an id that is not part
of the plan. Both are also caught by `Validate`.

## Validation happens twice

`Validate` checks what can be known without opening a document:

- the plan has at least one source;
- no two sources share an identity;
- every source has a non-empty display name;
- the output options are internally consistent — a supported target version, a known encryption
  algorithm, a primary source that belongs to the plan.

`MergeAsync` calls `EnsureValid` itself, so a plan that was never validated still cannot reach the
engine. Validation then continues against the real documents: each source is opened, its page count is
known, and a selection such as `"9-12"` against a four-page document fails with
`MergePlanValidationException` before the output is written.

Calling `EnsureValid` yourself is still worthwhile when you want the failure to happen before you
create a destination file, or when you want to report all problems at once:

```csharp
var problems = plan.Validate();

if (problems.Count > 0)
{
    throw new MergePlanValidationException(problems);
}
```

The exception's `Errors` property preserves the individual messages. They are written for humans and
are not part of the compatibility contract; do not parse them.

## Executing a plan

There are four ways to run a plan, all of them thin wrappers over the same engine:

| Call | Destination | Returns |
| --- | --- | --- |
| `merger.MergeAsync(plan, stream, ct)` | A writable stream you own | `MergeResult` |
| `merger.MergeToFileAsync(plan, path, ct)` | A file, created or overwritten | `MergeResult` |
| `merger.MergeToBytesAsync(plan, ct)` | An in-memory buffer | `byte[]` |
| `merger.MergeAsync(sources, stream, ct)` | A stream, from a list of content sources | `MergeResult` |

`MergeToFileAsync` creates the destination directory when it does not exist, opens the file with
`FileMode.Create` and exclusive sharing, and lets the merge write into it. The stream overload flushes
the destination but does not close it: the caller owns the stream. `MergeToBytesAsync` is convenient
for an HTTP response, but it holds the whole result in memory; use a file or a stream for large
documents.

The list overload is a shorthand when every page of every source is wanted:

```csharp
var result = await merger.MergeAsync(
    new IPdfContentSource[]
    {
        new PdfFileSource("a.pdf"),
        new PdfFileSource("b.pdf"),
    },
    destination);
```

It builds a plan with default options. As soon as you need a page selection, a primary source or
non-default output options, build the plan explicitly; the shorthand does not grow parameters for
those.

> **Cancellation leaves a partial destination.** A cancelled `MergeToFileAsync` throws
> `OperationCanceledException`, and the file it was writing may be incomplete. If the destination must
> never be observed half-written, merge to a temporary file in the same directory and move it into
> place after the call succeeds. Chapter 13 shows the pattern.

## Reuse and thread safety

A plan never changes after it is built, so it can be stored, compared, cached and shared. The built-in
content sources open a fresh read for every execution: `PdfFileSource` opens the file again,
`PdfByteArraySource` returns a new read-only `MemoryStream` over the same bytes, and `PdfStreamSource`
captures the stream once, under a lock, and serves every later read from the captured bytes. A plan
whose sources are one of these can be executed concurrently.

A custom `IPdfContentSource` must honour the same contract — every call to `OpenReadAsync` returns an
independent readable stream — if you intend to reuse or share its plan. If it cannot, create a new
source, and therefore a new plan, per execution.

The merger itself is thread safe. The container registers it as a singleton, and the engine keeps no
per-merge state on the instance.

