# Implementation Plan: Extension Hardening

**Branch**: `004-harden-extension-conveniences` | **Date**: 2026-11-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-harden-extension-conveniences/spec.md`

## Summary

Three findings from a review of the two extension files, answered in three different ways because they are
three different kinds of finding.

1. **The copy in the materialising reads is removed.** `ReadImageDataAsync` and `ReadEmbeddedFileDataAsync`
   return `ReadOnlyMemory<byte>` from a `MemoryStream` that already holds exactly the payload; both hand back
   `GetBuffer().AsMemory(0, (int)Length)` instead of `ToArray()`. No signature changes, no new type.
2. **The copy in `MergeToBytesAsync` stays and is documented.** An exact `byte[]` cannot come out of a growing
   `MemoryStream` without a copy, so the return type is the cost. The XML remarks say so, next to the member.
3. **The destination path is documented, not confined.** There is no root for the library to enforce and no
   `..` it can reject without breaking callers who build relative paths on purpose. `SECURITY.md`, the manual's
   error-handling chapter and three `<remarks>` blocks state the boundary once each.
4. **`ReadPageDetailAsync` is rewritten as `await foreach`.** Behaviour is unchanged; the enumerator is still
   disposed on return.

One new test file pins the aliasing that finding 1 introduces, and is the test that fails when the change is
reverted. The documentation changes cannot fail a test; they are read, not asserted.

## Technical Context

**Language/Version**: C# 14 on `net10.0`, C# 15 with `runtime-async=on` on `net11.0` (`src/Directory.Build.props`).

**Primary Dependencies**: none added. `MemoryStream.GetBuffer`, `Span`/`Memory` slicing and `await foreach` are
in the base class library; the test file uses `System.Runtime.InteropServices.MemoryMarshal`, which is in-box.

**Storage**: the file system, as a destination the caller names. No new storage, no new handle.

**Testing**: xUnit v3 4.0.0 on Microsoft.Testing.Platform 2.4.0, both legs. `net10.0` needs `-r win-x64`.

**Target Platform**: any platform the four layer packages support; nothing here is platform-specific.

**Project Type**: class library, layered (`Domain` → `Application` → `Infrastructure` →
`DependencyInjection`), with the `SharpCorePDF` entry point on top.

**Performance Goals**: a materialised payload costs one array of its own size where it cost two.
`MergeToBytesAsync` is unchanged at two — the merged output plus the array it returns.

**Constraints**: zero public API diff; zero build warnings on both legs; no new runtime dependency; no path
guard (FR-010).

**Scale/Scope**: two source files, one new test file, `SECURITY.md`, one manual chapter, `CHANGELOG.md`.

## Constitution Check

*GATE: must pass before Phase 0 research. Re-checked after Phase 1 design — unchanged.*

| Principle | How this plan meets it |
| --- | --- |
| I. Every PDF byte is ours | Nothing re-encodes anything. The bytes a payload had are the bytes reported, before and after. |
| II. Dependencies point inward | Both files stay in `Application`; the extension-block members still call only `IPdfContentReader` and `IPdfMerger`. |
| III. Copy bytes, never re-encode | Unaffected: the change is about where the copied bytes live, not whether they are identical. |
| IV. A test that fails on revert | `PdfContentReaderExtensionsTests` asserts the returned memory is the buffer the copy route wrote into; `ToArray()` hands back a different array and the test fails. The `await foreach` rewrite has no behavioural delta and the pull request says so rather than manufacturing a test. |
| V. Public surface is snapshotted | No member is added, removed or re-shaped. `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` are untouched, and `RS0016`/`RS0017` have nothing to say. |
| VI. Zero warnings | The new code is one expression and one loop. `GetBuffer()` replaces `ToArray()` on a stream constructed three lines above, so no nullable or analyzer finding is introduced; the build is the check. |
| VII. The boundary is part of the API | This is the whole of finding 2. `SECURITY.md` gains a trust-boundary section, the manual repeats it, and three XML blocks state it where a caller reads it. |

**Post-design re-check**: the design adds no project, no dependency and no public member, so the gate is
unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/004-harden-extension-conveniences/
├── spec.md              # What the review found and what "addressed" means
├── plan.md              # This file
├── research.md          # The three decisions, and the alternatives rejected
├── quickstart.md        # How to see the change, and how to see the test fail on revert
├── checklists/
│   └── requirements.md  # Requirements quality, not implementation quality
└── tasks.md             # T001–T008 (/speckit-tasks output)
```

`data-model.md` and `contracts/` are deliberately absent: no entity, record or port changes, and the one
contract that does change — what the returned memory aliases — is stated in the spec and in the XML remark a
caller reads. A contracts document restating an existing XML comment would be a second copy to drift.

### Source Code (repository root)

```text
src/SharpCorePDF.Application/Extensions/
├── PdfContentReaderExtensions.cs   # The view in two members, await foreach in a third, three remarks
└── PdfMergerExtensions.cs          # The remark that names the copy in MergeToBytesAsync

src/.editorconfig                            # S1751, scoped to the file that answers it with a decision
tests/SharpCorePDF.Tests/
├── PdfContentReaderExtensionsTests.cs       # New: the view, the unknown length, the guards, the page detail
├── PdfContentReaderExtensionsTests.Save.cs  # New: the two save members, and the helper they share
└── PdfMergerExtensionsTests.cs              # The four file cases and the three refusals, over a stub

SECURITY.md                                  # Trust boundary for destination paths
docs/manual/src/09-error-handling.md         # The same boundary, where files are already discussed
docs/mutation-testing.md                     # The fourth record, and the fifth that closes its gaps
CHANGELOG.md                                 # Unreleased / Changed
```

**Structure Decision**: the change lands where the review pointed — the two extension files — and nowhere else.
`OpenDestination` in `PdfContentReaderExtensions` and the inline file handling in `PdfMergerExtensions` keep
their duplicated body: extracting a shared helper would refactor a third thing, and gate 6 forbids it.

## Complexity Tracking

No violations: no new project, no new dependency, no new public member, no exception to a principle.
