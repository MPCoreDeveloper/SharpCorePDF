# ADR-004: The buffer model for reading a large document

- **Status**: **Accepted** (2026-11-07) — option **B1c** implemented; `System.IO.Pipelines` and
  `ReadOnlySequence<byte>` rejected for 1.x
- **Date**: 2026-11-06 (proposed), 2026-11-07 (accepted)
- **Related**: [`docs/roadmap-gap-analysis.md`](../roadmap-gap-analysis.md),
  [`specs/005-roadmap-intake/plan.md`](../../specs/005-roadmap-intake/plan.md) (P3),
  [`docs/measurements.md`](../measurements.md) §10

## Context

A roadmap asked for `System.IO.Pipelines` and `ReadOnlySequence<byte>` so that 100 MB documents stop
allocating. That ask was written without a measurement, and this repository does not change a buffer model
on an ask. Three facts had to be established first, and `--large` in `tools/SharpCorePDF.Measure` now
measures them: one 100 MB document (a padded `SafeWebCore.pdf`), server GC, one process, median of three
rounds, the file re-read from disk in every round.

| Row | Allocated | × input | Wall | Effective rate |
| --- | --- | --- | --- | --- |
| bytes read from disk, no parsing (`File.ReadAllBytes`) | 100.0 MB | 1.00 | 34.8 ms | 2 873 MB/s |
| open + inspect | 100.0 MB | **1.00** | 235.5 ms | 425 MB/s |
| merge 10 of its pages | 204.2 MB | **2.04** | 450.7 ms | 222 MB/s |
| text of page 1 | 103.7 MB | 1.04 | 240.5 ms | 416 MB/s |

1. **The read path materialises a document exactly once.** Opening and inspecting a 100 MB file allocates
   1.00× its size — the array the bytes are read into, and nothing else worth a line in the table. There is
   no hidden second copy to remove, and the `byte[]` that `PdfFileReader` holds is the whole cost.
2. **A merge of a file-backed source allocates the document twice.** The output of that row is 931 273 B, so
   the second 100 MB cannot be the output buffer. It is a second *read*: `PdfMergerService` opens the
   source, asks `IPdfDocumentReader` to inspect it (one materialisation, to resolve the page selection),
   rewinds the stream, and hands it to the engine, which reads it again (`ReadSourcesAsync` →
   `PdfByteReader`). Both arrays are garbage by the time the merge finishes.
3. **Time is a bigger cost than memory at this size.** Reading the file is 35 ms; opening and inspecting it
   is 235 ms. The 200 ms difference is parsing, not copying, and it did not move when the read chunk was
   raised from 80 KiB to 1 MiB (1 MiB chunks: 210.8 ms, allocations unchanged) — so the chunk constant is
   not where the time is, and it was left alone.

For contrast, a **stream-backed** source already allocates 1.00× however often it is read:
`PdfStreamSource` captures once and hands out `MemoryStream`s over the captured array, and
`PdfByteReader.TryGetOwnedDocumentBytes` uses a `MemoryStream`'s own array as the document instead of
copying it. That fast path exists and works; a file-backed source simply cannot reach it, because every
`OpenReadAsync` returns a fresh `FileStream`.

## Decision

**Accepted: fix the double read, keep the `byte[]` model, and do not adopt `System.IO.Pipelines` or a
`ReadOnlySequence<byte>` seam in 1.x.** Option **B1c** — a second port — is what was implemented; the
alternatives are recorded below because they were weighed, not because they are still open.

### The implementation (B1c, accepted)

- `Domain.ReadModels.PdfDocumentBytes` carries the description and the bytes together, and `OpenRead()`
  returns a `MemoryStream` view of the same array — no copy.
- `Application.Ports.IPdfDocumentBytesReader.ReadWithBytesAsync` is a second, narrower port.
  `IPdfDocumentReader` is unchanged, so an implementor of it still compiles.
- `PdfDocumentReader` implements both. `ReadAsync` is now a delegation to `ReadWithBytesAsync`, so there is
  one read path in the engine and not two.
- `PdfMergerService` and `PdfDocumentEditorService` prefer the bytes port when the reader they were given
  implements it, and fall back to `ReadAsync` plus a rewind when it does not. No constructor changed, so
  every existing caller and test still compiles; `AddSharpCorePDF()` registers the second port to the same
  singleton, so the container takes the fast path.
- The engine needed nothing: `PdfByteReader.TryGetOwnedDocumentBytes` already recognises a `MemoryStream`
  that owns whole-array bytes, so the second read became an aliasing read.

**Measured result** (`--large`, same document, same machine, server GC, median of three):

| Row | Before | After |
| --- | --- | --- |
| merge 10 of its pages | 204.2 MB, 2.04×, 450.7 ms | **104.2 MB, 1.04×, 452.7 ms** |
| output of that merge | 931 273 B | 931 273 B (identical) |
| process peak working set | 955.6 MB | **856.3 MB** |

The pinning test is `SourceReadOnceTests`: a source whose stream can be drained once and is not seekable
makes the merge and the edit fail if the source is read twice, and a third case shows that a reader
implementing only `IPdfDocumentReader` still works and still reads its source twice — the fallback is a
feature, not a leftover.

### A. Status quo (rejected as the destination, kept as the fallback for a foreign reader)

Do nothing; record the 2.04× as the cost of a file-backed merge. It is honest, and it was already close to
what `docs/measurements.md` §1 recorded (2.05–2.29× across the large rows) — but §1 attributed that multiple
to "the input is read into a buffer, copied into the output buffer". The 10-of-100 MB row showed that
explanation is incomplete: a partial selection has a 0.9 MB output and still allocated 2×. That is why the
fallback was kept only for readers this library does not ship.

### B. The alternatives that were weighed

- **B1a / B1b — a member on `IPdfDocumentReader`.** b) breaks every implementor; a) needs a default
  implementation that either throws or silently re-reads, which is a worse API than a second port. Both were
  rejected in favour of B1c.
- **B2 — the service captures the stream once.** It must not copy a stream that already owns its bytes, or
  every `PdfStreamSource` gains a copy it does not pay today, and the code that tells the two apart is
  internal to Infrastructure. Application would have had to duplicate that test; rejected.
- **B3 — the engine resolves the selections.** It removes a read but changes a public port's contract, and
  it moves the "refuse an encrypted source before doing any work" decision out of the use case. Rejected as
  the larger change for the same allocation win.

### C. `ReadOnlySequence<byte>` / `System.IO.Pipelines` (rejected for 1.x)

Neither would help, and one of them cannot work:

- `System.IO.Pipelines` is forward-only and invalidates read memory on `Advance`, while this reader addresses
  a document by absolute offset from its cross-reference table. A pipeline can only *feed* a buffer the
  engine keeps — which is the `byte[]` it already keeps.
- A `ReadOnlySequence<byte>` seam would let a document live in several segments and avoid one contiguous
  copy. That is real value, but only for a source that must not be materialised at once, and this engine has
  to hold the whole document to resolve an object by offset at any moment. Its allocation would be the same
  bytes in more, smaller arrays, and its complexity (offset→segment mapping in the lexer, the parsers and
  every scanner) would be paid by every read, including the 1.00× one measured above. It becomes interesting
  the day a read path exists that never needs random access — a streaming validator, say — and not before.

## Consequences

- The `byte[]` document model, and `PdfEngineOptions.MaxSourceBytes` with it, stay as they are.
- A merge and an edit of a file-backed source now allocate one document instead of two. `docs/measurements.md`
  §10 carries the before/after rows, and §1's explanation of the 2.x multiple now names the second read and
  points here.
- A third-party `IPdfDocumentReader` keeps working; it simply pays the second read, and
  `SourceReadOnceTests` pins both behaviours.
- Revisit this record if a caller needs a document larger than `MaxSourceBytes`, or if a read path appears
  that does not need random access; those are the two conditions under which C becomes interesting.

### A. Status quo, documented (accepted as the fallback, not as the destination)

Do nothing; record the 2.04× as the cost of a file-backed merge. It is honest, and it is already close to
what `docs/measurements.md` §1 records (2.05–2.29× across the large rows) — but §1 attributes that multiple
to "the input is read into a buffer, copied into the output buffer". The 10-of-100 MB row shows that
explanation is incomplete: a partial selection has a 0.9 MB output and still allocates 2×. Leaving a wrong
explanation in the measurement record is worse than leaving the allocation.

### B. Remove the second read (recommended)

The application layer holds the inspected bytes for a moment and then throws them away. Three ways to keep
them, in order of how much they change:

- **B1 — the reader hands them back.** An additive member on `IPdfDocumentReader` (for example
  `ReadWithBytesAsync`, returning the bytes together with the info), implemented by `PdfDocumentReader` from
  the array `PdfByteReader` already produced. `PdfMergerService` then gives the engine a `MemoryStream` over
  that array, `TryGetOwnedDocumentBytes` recognises it, and the engine copies nothing. Cost: one public
  interface member (implementors break unless it carries a default implementation), no new retention — the
  array the engine needs during the merge is the array that already exists — and the row should fall from
  2.04× to about 1.00× plus the output.
- **B2 — the service captures the stream once.** `PdfMergerService` copies the opened stream into a
  pre-sized `MemoryStream` (it knows `source.Content.Length`) and uses that for both reads. No port change,
  BCL only — but it must *not* copy a stream that already owns its bytes, or every `PdfStreamSource` gains a
  copy it does not pay today, and the code that tells the two apart lives in Infrastructure
  (`PdfByteReader.TryGetOwnedDocumentBytes`, internal). Application would have to duplicate that test.
- **B3 — the engine resolves the selections.** Drop the inspecting read from the service and let the engine
  resolve `PageRange` against the page count it computes anyway. It removes a whole document read and the
  service's encryption pre-check with it, and it changes a public port's contract — the largest change for
  the same allocation win.

**B1 is the recommendation**: the smallest change that keeps one materialisation per merge, no new
retention, and it reuses the fast path that already exists instead of adding a third way to read a document.
It needs this record and a maintainer's approval, because it adds a member to a public port.

### C. `ReadOnlySequence<byte>` / `System.IO.Pipelines` (rejected for 1.x)

Neither would help, and one of them cannot work:

- `System.IO.Pipelines` is forward-only and invalidates read memory on `Advance`, while this reader addresses
  a document by absolute offset from its cross-reference table. A pipeline can only *feed* a buffer the
  engine keeps — which is the `byte[]` it already keeps.
- A `ReadOnlySequence<byte>` seam would let a document live in several segments and avoid one contiguous
  copy. That is real value, but only for a source that must not be materialised at once, and this engine has
  to hold the whole document to resolve an object by offset at any moment. Its allocation would be the same
  bytes in more, smaller arrays, and its complexity (offset→segment mapping in the lexer, the parsers and
  every scanner) would be paid by every read, including the 1.00× one measured above. It becomes interesting
  the day a read path exists that never needs random access — a streaming validator, say — and not before.

## Consequences

- The `byte[]` document model, and `PdfEngineOptions.MaxSourceBytes` with it, stay as they are. Nothing in
  `src/` changes until the maintainer accepts B1.
- `docs/measurements.md` §10 keeps the four rows and the explanation of the 2.04× that this record
  established; §1's "copied into the output buffer" sentence now also names the second read.
- Whichever option is accepted, the acceptance test is the same: the `merge 10 of its pages` row of
  `--large` must fall to about 1.00× input while the bytes of the result stay identical, and the §1 rows
  must not regress.
- Revisit this record if a caller needs a document larger than `MaxSourceBytes`, or if a read path appears
  that does not need random access; those are the two conditions under which C becomes interesting.

