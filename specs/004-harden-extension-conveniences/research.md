# Phase 0 Research: Extension Hardening

Four decisions. Each names what was rejected, because in this repository a decision with no rejected option is
a preference.

## D1 — How the materialising reads stop copying the payload

**Decision**: `ReadImageDataAsync` and `ReadEmbeddedFileDataAsync` return
`buffer.GetBuffer().AsMemory(0, (int)buffer.Length)` instead of `buffer.ToArray()`.

**Why**: the streams are created with `new MemoryStream(capacity)`, where `capacity` is `ByteLength` when it is
known and a positive `int`. A `MemoryStream` built that way is *publicly visible*: `GetBuffer()` is legal,
returns the array the copy route wrote into, and `Length` is the number of bytes actually written. Slicing to
`Length` rather than to `Capacity` keeps the result exact for a payload whose length was not known when the
stream was created — the stream then grows, and `Length` is still the payload. The array outlives the stream:
`MemoryStream.Dispose` closes the stream and leaves the buffer alone, so a `ReadOnlyMemory<byte>` over it is
valid after the `using` ends.

**Rejected**:

| Alternative | Why not |
| --- | --- |
| Keep `ToArray()` | Allocates a second array the size of the payload, on a path a caller reaches for precisely when the payload is large. |
| `TryGetBuffer(out var segment)` then `segment.AsMemory()` | Works, and is what `GetBuffer` does internally — but it returns a `bool` this call cannot use: the stream is constructed four lines above with a publicly visible buffer, so `false` is unreachable and the branch would be untested code (Principle VI, and the 90% new-code gate). |
| Return `buffer.GetBuffer().AsMemory()` without slicing | Returns the capacity, so a grown stream would report trailing zeros as payload. |
| Rent a pooled buffer and hand back `Memory<byte>` | Changes the return type, and a pooled buffer cannot be handed to a caller who keeps it — it would have to be returned to the pool, which is the copy again, later. |
| Change both members to `byte[]` and use `GetBuffer()` | `byte[]` cannot be a view of a longer array without `ArraySegment`, so this is a copy or a lie. |

**Consequence for evidence**: because `ToArray()` and `GetBuffer().AsMemory(...)` return the same bytes, no
assertion on content distinguishes them. The distinguishing fact is *aliasing*: the returned array is the one
the destination stream owns. The test supplies its own `IPdfContentReader` that writes into the `MemoryStream`
it is handed, keeps that stream's buffer, and asserts `MemoryMarshal.TryGetArray(...).Array` is that same
instance. Reverting to `ToArray()` makes it a different instance, and the test fails.

## D2 — Why the copy in `MergeToBytesAsync` stays

**Decision**: the member keeps returning `byte[]` and gains a `<remarks>` paragraph naming the copy.

**Why**: `MergeAsync` writes an unknown-length document into a stream, and `MemoryStream` doubles as it grows,
so `Capacity` is generally larger than `Length`. A `byte[]` whose length equals the document's length therefore
cannot be the buffer itself: one of the two has to give. `ToArray()` is that copy, and it is the exact cost of
the declared return type.

**Rejected**:

| Alternative | Why not |
| --- | --- |
| Return `ReadOnlyMemory<byte>` | A breaking change to a shipped member of a preview package, for a member whose whole purpose is "give me the bytes". SemVer says a minor release does not do that, and the review asked for hardening, not a re-shape. |
| Pre-size the stream | The merged length is only known after the merge; that is what the merge produces. |
| Add a second member returning `ReadOnlyMemory<byte>` | A public member that exists to say "the other one copies" is a second surface to keep in step for a caller who can already pass a `Stream` to `MergeAsync` and keep the buffer. |

## D3 — Why the destination path is documented rather than confined

**Decision**: no code guard. `SECURITY.md` gains a trust-boundary section, `docs/manual/src/09-error-handling.md`
repeats it in "Writing files safely", and the three file-writing members state it in their XML documentation.

**Why**: the finding describes a *perception* of path traversal, and the perception is wrong in this context. An
application that calls `SaveImageDataAsync` already holds a `FileStream`-capable process; the library opening
`../../x` on its behalf grants nothing the caller did not already have. A guard would have to answer "confined
to what?", and there is no answer: the library never chose a root, the caller's working directory is not the
caller's base directory, and a relative path with `..` is a legitimate way to name a sibling folder.

**Rejected**:

| Alternative | Why not |
| --- | --- |
| Refuse a path containing `..` | Breaks legitimate relative destinations; a `..` in a normalised path is not evidence of an attack. |
| Add a `rootPath` or `destinationRoot` option | A new public surface, a new option on every file-writing member, and a confinement the caller can bypass by calling `MergeAsync(plan, fileStream)` — solving nothing while claiming to solve something. |
| Write only under `Path.GetTempPath()` | Changes where files land for every existing caller. |
| Leave `Path.GetFullPath` out | It is what makes the behaviour statable: the path is resolved once, before the directory is created. |

**What is written instead**: the path is used as given — a relative path resolves against the process working
directory, `..` is followed, the destination directory is created when it does not exist, an existing file is
truncated — and a caller that takes a path from an untrusted source must confine it before the call. That is the
honest statement of Principle VII's boundary, and it is what a security reviewer needs to read.

## D4 — `await foreach` and what disposal it buys

**Decision**: `ReadPageDetailAsync` becomes

```csharp
await foreach (var detail in reader
    .ReadPageDetailsAsync(source, PageRange.Single(page), password, cancellationToken)
    .WithCancellation(cancellationToken)
    .ConfigureAwait(false))
{
    return detail;
}

return null;
```

**Why**: the compiler emits `GetAsyncEnumerator`/`MoveNextAsync` in a `try` with `DisposeAsync` in a `finally`
— the same shape the hand-written version had — so `return` inside the loop disposes the enumerator, which is
the only thing the explicit `try`/`finally` was protecting. `.ConfigureAwait(false)` keeps the continuation off
the captured context, which the hand-written `MoveNextAsync().ConfigureAwait(false)` also did.
`.WithCancellation(cancellationToken)` is redundant with the token already passed to `ReadPageDetailsAsync`, but
it is what makes the enumeration's own cancellability visible at the call site rather than three arguments
away; the repository's other enumerations pass the token to the producer.

**Rejected**:

| Alternative | Why not |
| --- | --- |
| Keep the manual enumerator | Six lines that a reader must check for a disposal bug before believing the method is correct. |
| `ReadPageDetailsAsync(...).FirstOrDefaultAsync()` | Needs `System.Linq.Async`, a runtime dependency (Principle II and the runtime-dependency constraint). |
| Materialise with `ToListAsync`-style collection | Reads every page of a one-page range twice over, and pulls in the same package. |
| `await using` plus a single `MoveNextAsync` | Warning-free and disposing on every path, but it is the same manual pattern in fewer lines — the construct the review asked about, kept. |

**The analyzer has to be answered too**: the loop trips S1751, "loops with at most one iteration should be
refactored", and the finding is accurate — the member answers one page by contract. The decision is recorded
where this repository records decisions about a rule it does not follow: `src/.editorconfig` gains a scoped
`S1751` entry with the reason, and the line carries `// NOSONAR` so the server side of the gate reaches the same
conclusion as the local build (Principle VI). Nothing else about the file's analysis changes.

## D5 — What the mutation run actually reported

**Measured, not predicted** — and the prediction was wrong: the two changed lines carry **no mutants at all**.
Stryker's `Standard` level does not mutate an integer literal and has no mutator for a method call, so
`GetBuffer().AsMemory(0, (int)Length)` is invisible to it. The reading cannot express "a view instead of a copy"
at any level, which is why the regression test pins the choice by array identity and the evidence for the change
is a demonstrated revert: with `ToArray()` back, both tests fail on `Assert.Same`.

What the run did find was worth more than the score. Before `PdfContentReaderExtensionsTests` existed, the
mutation inside `ReadPageDetailAsync` that removes `return detail;` came back **`NoCoverage`** — the member had
no test at all — and the four null guards of the two materialising reads were in the same state, which also said
nothing had ever asserted that the stream is *sized* to the payload. The new file closes both gaps, and the
second reading shows the `return detail;` block and the four guards killed. Of the pre-sizing conditional, eight
mutants were undetected by that reading — four at each site: `Conditional (true)`, `Logical`, `>= 0` and
`< int.MaxValue` — while the mutants that flip the condition the other way were already killed by
`Assert.Equal(PayloadLength, segment.Array.Length)`: the stream then grows, so its buffer is 524 288 bytes for a
400 000-byte payload.

`MergeToBytesAsync`'s `ToArray()` is untouched, so the fifteen survivors in `PdfMergerExtensions` are pre-existing
in the sense that this feature's behaviour change did not create them; the reading in `docs/mutation-testing.md`
records what they say. The documentation-only changes are not mutated at all — comments, XML documentation and
Markdown are outside Stryker's scope — which is why finding 2 is a documentation deliverable and not a behaviour
change.

## D6 — What closing the reading's gaps cost, and what it bought

The fourth record left three groups undetected — the two `Save*` members and `OpenDestination` with no test at
all, the eleven `PdfMergerExtensions` survivors read as equivalent, and the eight above. Reading them again as
*claims to check* rather than as findings to record was the whole of this follow-up, and three of the recorded
readings were wrong:

- **A guard is not redundant with the guard below it.** `PdfMergerExtensionsTests.Refuses_a_missing_argument`
  asserts `ArgumentNullException` for a null plan and passes with the overload's guard deleted, because the real
  merger raises the same exception itself. The extension's own promise only becomes observable over a port that
  answers anything it is given, which is what `RecordingMerger` is for: the two `ThrowIfNull(plan)` guards and
  `ThrowIfNull(destination)` turn on nothing else.
- **An exception's type can be too coarse to locate a refusal.** `Path.GetFullPath` refuses a whitespace path
  with `ArgumentException` too, so the one thing `ThrowIfNullOrWhiteSpace` adds is `ParamName`. Two mutants turn
  on asserting it.
- **"Equivalent" has to mean "computed the same value", not "looks like it cannot matter".** A stated length of
  minus one reaches `MemoryStream`'s constructor through the mutant that pins the pre-sizing conditional true and
  throws there, before the copy runs. Four mutants turn on a record whose length cannot be a size.

> **Superseded after `1.4.0`, in part.** The second bullet's remedy — asserting `ParamName` — was withdrawn once
> the release was out: the field is not part of the members' documented contract, so pinning it pinned an
> implementation choice, and the two mutants it killed are recorded as accepted survivors instead. The reading
> that moved is 68.42 % → 64.91 % over the same 57 scored mutants, measured both ways. `docs/mutation-testing.md`,
> "The sixth record", has the reasoning and both reports.

Production code did not move: the follow-up is tests and this record. What it bought is measurable — 28.07 % to
68.42 % over the same 57 scored mutants, with `NoCoverage` at zero in both files — and what is left is eighteen
mutants that are equivalent, out of reach without a two-gigabyte allocation, or a continuation.

Survivors were read rather than counted, and the run is not gated: `stryker-config.json` keeps `break` at 0 and
no threshold was moved to make the score look better.
