# Tasks: Extension Hardening

**Input**: Design documents from `/specs/004-harden-extension-conveniences/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [quickstart.md](./quickstart.md), `.specify/memory/constitution.md`

**Tests**: included and required. Constitution Principle IV makes a named test per behaviour part of the change;
the task below that changes a behaviour names the test that carries it. The documentation work has no test and
says so.

**Status of the boxes**: `[x]` means the task is in the working tree; the run that reproduced each one is quoted
in the closing section.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task delivers (`US1` … `US3`); absent in Setup and Polish
- Every task names the exact file it touches

## Path Conventions

Single project, library layout from `plan.md`: `src/<layer>/…`, `tests/SharpCorePDF.Tests/…`, `docs/…`,
`SECURITY.md`, `CHANGELOG.md`.

---

## Phase 1: Setup

**Purpose**: the two facts every later task depends on. No project, package or target framework is created.

- [x] T001 Confirm the surface that must not move: `src/SharpCorePDF.Application/PublicAPI.Shipped.txt` already
  lists every member this feature touches, so no `PublicAPI.Unshipped.txt` entry is expected and `RS0016`/
  `RS0017` stay silent. Confirm too that `PdfMergerExtensions.cs` has a `<remarks>` on `MergeToBytesAsync` to
  extend rather than one to invent.

**No Foundational phase**: nothing is shared between the three stories — each touches its own lines or its own
document — so US1, US2 and US3 can be delivered in any order after T001.

---

## Phase 2: User Story 1 — Materialise a payload at the cost of the payload (Priority: P1) 🎯 MVP

**Goal**: a materialised payload is the buffer the copy route filled, not a second array of the same size; the
one copy that stays (`MergeToBytesAsync`) says so where the caller reads it.

**Independent Test**: `PdfContentReaderExtensionsTests` — the returned memory's array is the destination stream's
buffer, its length is the payload's, its bytes are the payload's.

- [x] T002 [US1] In `src/SharpCorePDF.Application/Extensions/PdfContentReaderExtensions.cs`, replace
  `return buffer.ToArray();` in `ReadImageDataAsync` with `return buffer.GetBuffer().AsMemory(0, (int)buffer.Length);`
  and extend the member's `<remarks>`: the result is a view of the buffer the copy wrote into, valid after the
  stream is disposed, and `ToArray()` is not used because it would allocate the payload twice.
- [x] T003 [US1] The same two edits in `ReadEmbeddedFileDataAsync` in the same file.
- [x] T004 [US1] In `PdfMergerExtensions.cs`, extend the `<remarks>` of `MergeToBytesAsync` to name the copy:
  a `byte[]` of exactly the merged length, a second array on top of the one the merge filled, and the reason that
  is the return type rather than an oversight.
- [x] T005 [US1] Add `tests/SharpCorePDF.Tests/PdfContentReaderExtensionsTests.cs`: a test-local
  `IPdfContentReader` whose `CopyImageDataAsync`/`CopyEmbeddedFileDataAsync` write a known payload into the
  destination a bounded buffer at a time — the way the copy route writes — and keep the `MemoryStream`'s buffer.
  Tests assert `MemoryMarshal.TryGetArray` gives that same array, with the payload's `Count`, offset zero, the
  payload's bytes and its length, for a payload whose length is known and for one whose length is not, plus the
  two null guards. This is the test that fails on revert: restoring `ToArray()` fails both view tests.
  *(T011's first reading added the guards and the unknown-length case: the report showed them `NoCoverage`.)*

**Checkpoint**: a payload is materialised at its own cost, and the member that still copies says why.

---

## Phase 3: User Story 2 — Know where the library's responsibility for a path ends (Priority: P2)

**Goal**: the trust boundary around a destination path is stated once in `SECURITY.md`, once in the manual, and
three times in the XML documentation a caller actually reads. No behaviour changes, so no test carries it.

**Independent Test**: read the three places; each says that the path is used as given, that the directory is
created, that an existing file is truncated, and that confining the path is the caller's job.

- [x] T006 [US2] Add the trust-boundary section to `SECURITY.md`, beside the existing statement of what is in
  scope: a destination path is caller input, `Path.GetFullPath` normalises and nothing more, no root is enforced,
  the destination directory is created and an existing file truncated, and the library is not a sandbox — an
  application that takes a path from an untrusted source must confine it before the call.
- [x] T007 [US2] Repeat the boundary in `docs/manual/src/09-error-handling.md` under "Writing files safely", and
  extend the `<remarks>` of `MergeToFileAsync` (`PdfMergerExtensions.cs`), `SaveImageDataAsync` and
  `SaveEmbeddedFileAsync` (`PdfContentReaderExtensions.cs`) with the same facts.

**Checkpoint**: a reviewer can answer "what does the library promise about this path?" from three documents.

---

## Phase 4: User Story 3 — Read one page with the language's own construct (Priority: P3)

**Goal**: `ReadPageDetailAsync` says what it does in one loop.

**Independent Test**: `PdfContentReaderTests.Pages` still passes, and the method body names none of
`GetAsyncEnumerator`, `MoveNextAsync`, `DisposeAsync`.

- [x] T008 [US3] Rewrite `ReadPageDetailAsync` in `PdfContentReaderExtensions.cs` as an `await foreach` over
  `ReadPageDetailsAsync(...).WithCancellation(cancellationToken).ConfigureAwait(false)` that returns the first
  page and falls through to `null`; add a `<remarks>` line saying the enumeration is disposed as soon as that page
  arrives. Record S1751 — the loop runs once by contract — as a scoped decision in `src/.editorconfig` with
  `// NOSONAR` on the line, since the repository answers a rule it does not follow that way rather than by
  abandoning the construct. T011's first reading showed this member had **no test at all**, so T005's two
  page-detail tests are its evidence.

**Checkpoint**: the three findings are answered; nothing about a result has changed.

---

## Phase 5: Polish

- [x] T009 Add the `CHANGELOG.md` entry under `## [Unreleased]` → `### Changed`: the materialising reads return a
  view instead of a copy, and the destination-path boundary is documented. No public signature changed, so there
  is no `### Added` entry and no breaking-change note.
- [x] T010 Run the gates from `quickstart.md` 1, 2 and 4: `dotnet build .\SharpCorePDF.slnx -c Release` with zero
  warnings, the new tests on `net11.0`, and the full suite on `net11.0` and `net10.0 -r win-x64`.
- [x] T011 Run the scoped Stryker command from `quickstart.md` 5 over `PdfContentReaderExtensions.cs` and
  `PdfMergerExtensions.cs`, and read the survivors rather than the score. Any survivor on a line this feature
  changed is explained in the pull request; a survivor on `MergeToBytesAsync`'s `ToArray()` is pre-existing and
  recorded as such.

---

## Phase 6: Closing the gaps T011's reading found

**Purpose**: the reading came back with two things this change had written off — `SaveImageDataAsync`,
`SaveEmbeddedFileAsync` and `OpenDestination` with no test at all, and fifteen survivors in `PdfMergerExtensions`
recorded as equivalent without being tested. Both were checked rather than argued about. Every task here is a test
or a record; no production file moves.

**Independent Test**: both files' `NoCoverage` column is zero and the score over the same 57 scored mutants goes
from 28.07 % to 68.42 %, with every remaining survivor explained.

- [x] T012 [P] Add `tests/SharpCorePDF.Tests/PdfContentReaderExtensionsTests.Save.cs`, the second part of
  `PdfContentReaderExtensionsTests` (which becomes `partial`): an image and an embedded file written where the
  caller named them and read back byte for byte, a destination two folders deep, a file that is already there
  truncated, and a refused call that leaves no file — null source, null image or file, null path, empty path, and
  a whitespace path asserted down to its `ParamName`, because `Path.GetFullPath` refuses a whitespace path with
  the same exception type. Generalise the stub's `LastBuffer` to answer only a memory stream: a file has no buffer
  to keep.
- [x] T013 Extend `tests/SharpCorePDF.Tests/PdfMergerExtensionsTests.cs` with the same four file cases for
  `MergeToFileAsync`, and a `RecordingMerger` that writes a known payload and answers *any* plan — a null one
  included — with `Refuses_a_missing_plan_before_reaching_the_merger`,
  `Refuses_a_destination_path_that_is_not_a_path` and `Refuses_a_missing_list_or_destination` over it. The
  existing `Refuses_a_missing_argument` keeps its place and gains a remark saying it cannot tell the two layers
  apart, which is why the stub exists.
- [x] T014 Add `Materialises_a_payload_whose_stated_length_is_impossible` to
  `PdfContentReaderExtensionsTests.cs`, with `AssertGrewToHoldThePayload` shared with the unknown-length case: a
  `ByteLength` of minus one must fall back to a stream that grows rather than reach `MemoryStream`'s constructor.
- [x] T015 Assert `ParamName == "destinationPath"` on the whitespace refusal in both test files, where the guard's
  own contribution is the name of the argument rather than the exception type.
- [x] T016 Re-run the scoped Stryker command (`quickstart.md` 5) and read the eighteen survivors rather than the
  score: record the third and fourth readings in `docs/mutation-testing.md` as "The fifth record", correct the
  three readings the fourth record got wrong, and state the design consequence in `research.md` D6.

**Checkpoint**: every survivor over the two files is either killed or explained with a reason that names the
computation, and no production line changed to get there.

---

## Dependencies & Execution Order

- **T001** first: it fixes what "no API change" means for every later task.
- **US1 (T002–T005)**: T005 is a new file and can be written before T002/T003 so that it fails against the old
  `ToArray()` before the change lands; T002 and T003 each touch one member of one file and are ordered only by
  position.
- **US2 (T006–T007)**: independent of US1; T007 touches the same two source files as T002–T004, so it is done
  after them to keep the diff readable.
- **US3 (T008)**: same file as T002/T003/T007, so it is sequenced last among the source edits.
- **Polish (T009–T011)**: T010 and T011 depend on every source task.
- **Phase 6 (T012–T016)**: T012 and T013 are separate files and can be written in either order; T014 touches the
  first part of the class T012 declares `partial`; T015 depends on both test files existing; T016 depends on all
  four.

## Notes

- `[P]` is used once, on T012, which creates its own new file. Three of the four source tasks touch one file, and a
  diff of that file is easier to review in order than in parallel.
- No task adds a dependency, a target framework or a public member; a task that needed one would be the wrong
  task.
- Gate 6 of the Development Workflow applies to every task: no reordering, renaming or whitespace outside what
  the task needs.

---

## What the runs said

| Gate | Result |
| --- | --- |
| `dotnet build .\SharpCorePDF.slnx -c Release` | 0 warnings, 0 errors, both legs |
| `PdfContentReaderExtensionsTests`, `net11.0` | 6 passed |
| Full suite, `net11.0` (T010) | 997 passed, 0 failed |
| Full suite, `net10.0 -r win-x64` (T010) | 997 passed, 0 failed |
| Revert: `ToArray()` back in both materialising reads | both view tests failed, as intended; restored |
| Scoped Stryker, before the new tests | 72 mutants, 3 detected, 54 undetected, 4 compile errors, 11 ignored — 5.26 % |
| Scoped Stryker, after them | 72 mutants, 16 detected, 41 undetected, 4 compile errors, 11 ignored — 28.07 % |
| `PdfContentReaderExtensionsTests` + `PdfMergerExtensionsTests`, `net11.0` (T012–T015) | 22 passed, 0 failed |
| Full suite, `net11.0` (T016) | 1010 passed, 0 failed |
| Scoped Stryker, third reading (T012–T014) | 72 mutants, 37 detected, 20 undetected, 4 compile errors, 11 ignored — 64.91 % |
| Scoped Stryker, fourth reading (T015) | 72 mutants, 39 detected, 18 undetected, 4 compile errors, 11 ignored — 68.42 % |

The survivors, read rather than counted and recorded in `docs/mutation-testing.md` under "The fourth record" and
"The fifth record":

- **The changed lines carry no mutants at all.** Stryker's `Standard` level does not mutate an integer literal
  or a method call, so the reading cannot express the view-versus-copy choice; the revert row above is that
  evidence, and it is a demonstrated one.
- **`ReadPageDetailAsync` and the two members' null guards were `NoCoverage`** before T005 and are killed after
  it, which is what the second reading's six extra detections are.
- **The pre-sizing conditional**: the mutants that flip it one way are caught by asserting the buffer's length;
  `Conditional (true)` and `Logical` are killed by T014's impossible stated length; `>= 0` is equivalent, because
  its true branch computes the same zero, and `< int.MaxValue` differs only at a stated length that asks for a
  two-gigabyte buffer.
- **`ConfigureAwait(false)` → `true` survives**, as it does everywhere else in this repository: a continuation
  is not part of what a caller receives. It is now the largest of the eighteen.
- **`SaveImageDataAsync`, `SaveEmbeddedFileAsync` and `OpenDestination` were `NoCoverage`** in the second reading.
  T012 closed it: eleven of the fifteen are killed, and the four left are two `ConfigureAwait` flips and the two
  `FileStream` constructor arguments, neither of which a caller can observe.
- **The eleven `PdfMergerExtensions` survivors were not all equivalent.** Seven are killed by T013 over a merger
  that answers anything it is handed; the seven left are three `ConfigureAwait` flips, the same two constructor
  arguments, the `FlushAsync` that the `await using` disposal repeats, and `ThrowIfNull(sources)`, which
  `Enumerable.Select` masks with an `ArgumentNullException` of its own.
- **The four compile errors are not the suite's**: Stryker's "LINQ method" mutator rewrites `PageRange.Single` to
  `PageRange.SingleOrDefault`, which cannot compile against a static factory. They are the same in every reading.
