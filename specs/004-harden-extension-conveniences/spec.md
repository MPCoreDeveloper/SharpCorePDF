# Feature Specification: Extension Hardening

**Feature Branch**: `004-harden-extension-conveniences`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "A code review of `PdfContentReaderExtensions` and `PdfMergerExtensions` raised three
points: the byte-returning conveniences call `ToArray()` on a `MemoryStream` that already holds the bytes, the
file-writing conveniences resolve a destination path with `Path.GetFullPath` and nothing else, and
`ReadPageDetailAsync` drives `IAsyncEnumerable` by hand. Address them without changing the public surface."

## What the review found

The three findings are not defects in the sense SECURITY.md defines one — no document produces a wrong result,
no caller is surprised by an exception — but each is a place where the surface states less than it could about
what it costs or what it assumes. This feature answers them the way the repository answers anything: by naming
the behaviour, pinning it with evidence, and writing down the boundary rather than tightening it into a lie.

| Finding | Where | What is actually wrong |
| --- | --- | --- |
| A second, full-size array per materialised payload | `ReadImageDataAsync`, `ReadEmbeddedFileDataAsync`, `MergeToBytesAsync` | The stream already holds the bytes; `ToArray()` allocates the payload again. |
| A destination path the library does not confine | `MergeToFileAsync`, `SaveImageDataAsync`, `SaveEmbeddedFileAsync` | `Path.GetFullPath` normalises and nothing more, so `..` is followed and no root is enforced. |
| A hand-driven async enumerator | `ReadPageDetailAsync` | `GetAsyncEnumerator`/`MoveNextAsync`/`DisposeAsync` says what `await foreach` says, in six lines instead of one. |

The third finding is a readability question with a factual answer: the manual enumerator bought nothing
(`await foreach` disposes on `return`, `break` and throw exactly as the `finally` block did), so the answer is
to say it plainly rather than to keep the ceremony.

The second finding needs the most care, because the obvious "fix" is wrong. A library that refuses `..` would
break every caller that builds a relative path on purpose, and it would still not be a sandbox: the caller can
write the same file itself. There is no root to confine to, because the library never chose one. So the
boundary is not moved; it is stated.

The first finding splits in two, and only one half is a defect. `ReadImageDataAsync` and
`ReadEmbeddedFileDataAsync` return `ReadOnlyMemory<byte>`, and the buffer they hand back is already exactly
the payload, so the copy is pure waste. `MergeToBytesAsync` returns `byte[]`, and an exact `byte[]` cannot be
produced from a growing `MemoryStream` without copying; the copy is the return type, and the honest change is
to say so next to the member that makes the caller pay it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Materialise a payload at the cost of the payload (Priority: P1)

A service reads the images and attachments of documents it inspects and hands the bytes to a downstream
consumer. Every materialised payload costs two arrays today: one the copy route filled, one `ToArray()`
returned. For an inspected average the difference is noise; for the largest payload a caller is willing to
materialise it is the difference between holding one copy and two.

**Why this priority**: this is the only finding that changes what the code does rather than what it says, and
it is the only one a test can pin. The other two are documentation and readability, and they are worth less if
this is wrong.

**Independent Test**: read an image and an embedded file through the materialising conveniences and assert the
returned memory is a view of the buffer the copy route wrote into — the same array, the payload's length —
with no second array handed back.

**Acceptance Scenarios**:

1. **Given** an image whose payload is larger than the inline budget, **When** the caller materialises it with
   `ReadImageDataAsync`, **Then** the returned memory is the buffer the copy route filled, its length equals
   `ByteLength`, and its content equals the bytes the copy route writes.
2. **Given** an embedded file, **When** the caller materialises it with `ReadEmbeddedFileDataAsync`, **Then**
   the same holds: one array, the payload's length, byte-identical to the copy route.
3. **Given** a payload whose length was not known when the stream was created, **When** it is materialised,
   **Then** the returned memory is still exactly the bytes written and no longer.
4. **Given** `MergeToBytesAsync`, **When** it is called, **Then** it still returns a `byte[]` of exactly the
   merged length, and the documentation states that this copy is the price of the return type.

---

### User Story 2 - Know where the library's responsibility for a path ends (Priority: P2)

A reviewer of an application that embeds this library asks what happens when a path from a web request reaches
`SaveImageDataAsync`. The answer has to be readable without reading the source: the library writes where it is
told, it creates the directory, it truncates an existing file, and confining the path is the application's job.

**Why this priority**: nothing in the code changes, so it cannot break a caller — but a security reviewer who
has to guess the boundary is a reviewer who writes a finding. Principle VII makes the boundary part of the API.

**Independent Test**: read SECURITY.md, the manual's chapter on error handling and the XML documentation of the
three file-writing members, and find the same statement in each: the path is used as given, and a caller that
takes it from an untrusted source must confine it first.

**Acceptance Scenarios**:

1. **Given** `SECURITY.md`, **When** a reader looks for the trust boundary around file destinations, **Then**
   a section states that destination paths are caller input, that no root is enforced, and that the library is
   not a sandbox.
2. **Given** the manual's "Writing files safely" section, **When** a reader reaches it, **Then** it repeats the
   boundary next to the advice it already gives about temporary files.
3. **Given** the XML documentation of `MergeToFileAsync`, `SaveImageDataAsync` and `SaveEmbeddedFileAsync`,
   **Then** each says that a relative path resolves against the working directory, that `..` is followed, and
   that the destination directory is created and an existing file truncated.

---

### User Story 3 - Read one page with the language's own construct (Priority: P3)

A maintainer reading `ReadPageDetailAsync` should not have to reason about enumerator disposal to learn that
the method returns the first page. The same method written with `await foreach` says that in one line, and the
compiler still disposes the enumerator on every exit.

**Why this priority**: it cannot change a result, only the number of lines a reader has to hold in their head.

**Independent Test**: two tests — a page that exists in a two-page document, and a page number past the last
page — cover the rewritten member, which the mutation reading showed had **no test at all** before them; the
method body no longer names `GetAsyncEnumerator`, `MoveNextAsync` or `DisposeAsync`.

**Acceptance Scenarios**:

1. **Given** a page that exists, **When** `ReadPageDetailAsync` is called, **Then** it returns that page's
   detail.
2. **Given** a page number past the last page, **When** `ReadPageDetailAsync` is called, **Then** it returns
   `null` and the enumeration is still disposed.

---

### Edge Cases

- A payload whose `ByteLength` is zero or larger than `int.MaxValue` is materialised through a stream that
  grows rather than a pre-sized one; the returned view is still trimmed to the bytes actually written.
- The returned memory outlives the `using` that disposes the stream on return: the array is not cleared on
  dispose, and the `ReadOnlyMemory<byte>` keeps it alive.
- A relative destination resolves against the process working directory, which is not the caller's directory
  and can differ between hosts. The documentation says so; the code does not guess a different one.
- A destination that names a directory, or a path whose directory can be created but whose file cannot, still
  fails with the exception the file system raises; this feature does not wrap it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `ReadImageDataAsync` MUST return the payload as a view of the buffer the copy route wrote into,
  allocating no second array the size of the payload.
- **FR-002**: `ReadEmbeddedFileDataAsync` MUST do the same.
- **FR-003**: The memory returned by both MUST be exactly the bytes written — not the stream's capacity — and
  MUST remain valid after the method returns and the stream is disposed.
- **FR-004**: `MergeToBytesAsync` MUST keep returning `byte[]` and MUST document that the array is a copy of
  the merged output, which is the cost of the return type rather than an oversight.
- **FR-005**: No public signature changes: `PublicAPI.Shipped.txt` MUST NOT change and nothing is added to
  `PublicAPI.Unshipped.txt`.
- **FR-006**: The XML documentation of `MergeToFileAsync`, `SaveImageDataAsync` and `SaveEmbeddedFileAsync`
  MUST state that the destination path is used as given — a relative path against the working directory, `..`
  followed — that the destination directory is created, and that an existing file is truncated.
- **FR-007**: `SECURITY.md` MUST state the trust boundary: destination paths are caller input, no root is
  enforced, and the library is not a sandbox.
- **FR-008**: The manual chapter that already explains writing files safely MUST repeat that boundary.
- **FR-009**: `ReadPageDetailAsync` MUST be written with `await foreach` and MUST keep returning the first
  page of the range, or `null` when the range yields nothing. The analyzer's single-iteration finding (`S1751`)
  MUST be recorded as a scoped decision in `src/.editorconfig` with `// NOSONAR` on the line, not answered by
  giving up the construct.
- **FR-010**: The change MUST NOT add a runtime dependency, a target framework, or a file-system guard that
  rejects a path an unconstrained caller could legitimately ask for.

### Key Entities

Not applicable: the feature changes the cost and the documentation of existing members, and introduces no new
type, record or value object.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Materialising a payload allocates one array of the payload's size where it allocated two; the
  test asserts the aliasing rather than a counter, so the outcome is deterministic.
- **SC-002**: The public API snapshot diff for all five packages is empty.
- **SC-003**: `dotnet build .\SharpCorePDF.slnx -c Release` reports zero warnings on `net10.0` and `net11.0`,
  and the full suite passes on both legs.
- **SC-004**: Every line this feature changes is covered by a test, and any Stryker survivor over the two
  files is named and explained rather than counted. The follow-up tasks T012–T016 take this further: the
  `NoCoverage` column of both files is zero, and each of the eighteen remaining mutants has its reason recorded
  in `docs/mutation-testing.md` under "The fifth record".

## Assumptions

- The three findings are the whole of what the review raised; nothing else in the two files is changed, and no
  drive-by reformatting is included (Development Workflow, gate 6).
- `MemoryStream.GetBuffer()` is safe on every stream the two materialising members create, because
  `new MemoryStream(capacity)` exposes its buffer; a stream that did not would be a defect worth its own test.
- A `byte[]` return type is the contract of `MergeToBytesAsync`, and changing it to `ReadOnlyMemory<byte>`
  would be a breaking change to a shipped member, which this feature is explicitly not.
- The reviewer's own framing — "not dramatic", "not per se a bug" — is accepted: this is hardening, not a
  defect fix, and the documentation is the deliverable for finding two.
- The two `Save*` members and the `OpenDestination` helper they share were reached by no test at all, which the
  first mutation reading reported as `NoCoverage`. T012–T016 test them rather than change them, so the production
  diff stays the three findings.
