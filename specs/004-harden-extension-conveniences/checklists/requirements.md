# Requirements Quality Checklist: Extension Hardening

**Purpose**: judge the specification, not the implementation. Every box is a question the spec has to answer
before `plan.md` is worth writing.

**Scope**: `specs/004-harden-extension-conveniences/spec.md`

## Completeness

- [x] Every finding in the review appears as a user story, with a priority and an independent test.
- [x] The one finding that is not a code change (the destination path) says so, and says why the obvious code
      change is the wrong one.
- [x] The split between the two materialising members and `MergeToBytesAsync` is explained, so the reader knows
      why one copy stays.
- [x] Edge cases name the payload whose length is unknown (capacity zero or above `int.MaxValue`) and the
      lifetime of the returned array after the stream is disposed.
- [x] The trust boundary is stated in terms of what the library does and does not enforce, not in terms of what
      a caller "should" do.

## Clarity

- [x] "View of the buffer" is defined by what a test can observe: the same array, the payload's length.
- [x] "Confine" is defined as the caller's job, with the reason no root exists for the library to enforce.
- [x] `await foreach` is justified by disposal on `return`, not by taste.

## Consistency

- [x] FR-005 (no public signature change) agrees with Principle V and with SC-002.
- [x] FR-010 (no path guard) agrees with User Story 2: the boundary is documented, not moved.
- [x] FR-007 and FR-008 place the same sentence in `SECURITY.md` and in the manual, as Principle VII requires.
- [x] SC-004 agrees with `stryker-config.json`: survivors are read and explained, and the run is not gated.

## Testability

- [x] Each acceptance scenario can be checked by a named test or by reading a named document.
- [x] SC-001 is checkable without a stopwatch or an allocation counter.
- [x] Nothing depends on a benchmark being stable on a busy machine.

## Scope

- [x] No new type, dependency, target framework or package is introduced.
- [x] Changing `MergeToBytesAsync` to `ReadOnlyMemory<byte>` is recorded as explicitly out of scope.
- [x] `ReadPageDetailAsync` is a refactor with unchanged behaviour, and the spec does not claim otherwise.
