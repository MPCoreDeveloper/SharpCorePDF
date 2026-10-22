# ADR-001: Adopt a PDF creation, editing and reading product boundary

- **Status**: Accepted
- **Date**: 2026-10-21
- **Amends**: `.specify/memory/constitution.md`, Principle VII (constitution 2.0.0)
- **Related**: [`specs/003-pdf-platform-parity`](../../specs/003-pdf-platform-parity/spec.md)

## Context

SharpCorePDF 1.0 is a merge engine with inspection, encryption and a bounded fixed-position page
author. Its constitution said the library was **not** a creation library or an editor. That was a
useful boundary for 1.0, but it left the jobs callers most often need — drawing a vector chart,
wrapping a paragraph, cropping a page, filling a form, signing a document — to a second PDF library.

The parity feature in `specs/003-pdf-platform-parity` proposes to close those gaps with code in this
repository. Implementing it without changing the governing product statement would make the
constitution false, and hiding the capability behind an “experimental” or internal API would make the
README false. The boundary therefore has to change before the code does.

## Decision

1. **The product is a dependency-free PDF creation, editing and reading library for .NET.** Merging
   remains the first-class story, but it is no longer the only one. No shipped package may depend on a
   third-party PDF engine, native library or platform-specific binary; every PDF byte is read and
   written by code in this repository.
2. **The parity target is capability parity, not API compatibility.** Release 1.1 aims at the
   documented creation and processing surface of **PDFsharp 6.5** and the documented reading and
   layout-analysis surface of **PdfPig 0.1.x**. Source or binary compatibility with either project is
   not promised, and no PDFsharp or PdfPig code, package or binary is used at runtime or in tests.
3. **The four-layer architecture and package shape stay unchanged.** Pure values and plans belong in
   Domain, orchestration in Application, file-aware readers, writers and editors in Infrastructure,
   and registrations in DependencyInjection. A fifth low-level package is not introduced.
4. **Copy bytes unless the operation requires a rewrite.** Merge and unaffected streams remain
   byte-copied. Editing and signing may rewrite the revision they change; an incremental update
   preserves earlier revisions byte for byte. Every lossy or unrepresentable decision is reported as
   a `PdfWarning`.
5. **Release 1.1 is additive.** Existing 1.0 signatures remain; new members are snapshotted in
   `PublicAPI.Unshipped.txt` in the same commit, and a 1.x change that cannot be additive is marked
   `[Obsolete]` rather than removed.
6. **Evidence is part of the capability.** A parity row is not “done” until it names a test that
   fails when the capability is removed and an external validation command that has been run on the
   release commit. The public scorecard is `docs/parity.md`.

## Excluded surface

The following remain outside the product and may not be added by a fix or an incidental feature:

- a page renderer, raster preview, print pipeline or PDF viewer;
- OCR, document understanding or a semantic table-extraction product;
- HTML-to-PDF, PDF-to-Word/HTML, reflow or a MigraDoc-style flow engine with automatic pagination;
- PDF/A, PDF/X or PDF/UA **validation** or certification claims. The writer may target PDF/A
  profiles, but conformance is only claimed after an independent validator passes every fixture;
- tagged-PDF or PDF/UA authoring, JavaScript execution, 3D, multimedia, geospatial data, JPEG 2000
  and public-key encryption;
- a hosted service, network client or command-line tool inside the library;
- drop-in source or binary compatibility with PDFsharp or PdfPig.

Rendering a page to an image for a test is an external validation step, not a library capability.

## Consequences

**Positive**

- A caller can complete the same jobs as with PDFsharp or PdfPig without adding either package.
- The constitution, README, manual and release gates now describe the same product.
- The parity matrix makes an unsupported claim visible before it ships.

**Constraints**

- Every new capability needs a named test, a validation command and a place in `docs/parity.md`.
- The no-third-party, no-native, no-network rules still apply to the expanded surface.
- Existing 1.0 code and tests must continue to pass unchanged.
- PDF/A output requires external validation before it may be advertised.
- **CA-001** (this ADR, constitution 2.0.0, documentation correction and the release-checklist
  entry) must land before Phase 1 of the parity plan starts.

## Alternatives considered

- **Keep the merge-only boundary.** Rejected: it leaves the exact gaps that force callers to install a
  second PDF library.
- **Reproduce the PDFsharp or PdfPig API.** Rejected: it copies another project’s design, licensing
  and maintenance cost, and freezes this library to an unstable target.
- **Ship the new surface as experimental and undocumented.** Rejected: it violates the constitution’s
  rule that the boundary is part of the API and makes the public documentation false.
- **Add a fifth `SharpCorePDF.Pdf` package.** Rejected: it duplicates the object model and changes
  the package shape without adding a capability.

## References

- [Constitution 2.0.0](../../.specify/memory/constitution.md)
- [Feature specification](../../specs/003-pdf-platform-parity/spec.md)
- [Implementation plan](../../specs/003-pdf-platform-parity/plan.md)
- [Research decisions](../../specs/003-pdf-platform-parity/research.md)
- [Release checklist](../release-checklist.md)
