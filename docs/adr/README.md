# Architecture decision records

An architecture decision record (ADR) captures a decision that changes the shape of the product or
the constraints its code must respect. It records why the decision was taken, what it rules out and
what it obliges later work to prove; it does not restate the implementation.

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-pdf-platform-boundary.md) | Accepted | SharpCorePDF is a dependency-free PDF creation, editing and reading library, targeting capability parity with PDFsharp 6.5 and PdfPig 0.1.x while keeping rendering, conversion, OCR, conformance validation, services and CLIs outside the product. |
| [0002](0002-new-code-coverage-1.1.0.md) | Accepted | `1.1.0` ships with its new-code coverage measured at 77.76% against the 90% floor, recorded rather than waived; no test is written to move the number, and a named `1.2` task closes the gap on the parsers' refusal branches. |
| [0003](0003-per-letter-read-model.md) | Accepted | The per-letter read model (`PdfGlyph`, `PdfLetter`, `PdfRectangle`) stays a record graph in 1.x: the struct conversion was measured and raised allocations, because `PdfWord` stores its own copies. The 2.0 candidate is slices over one array plus structs, to be measured before adoption. |
| [0004](0004-buffer-model.md) | Accepted | The document model stays one `byte[]` and `System.IO.Pipelines`/`ReadOnlySequence<byte>` are not adopted in 1.x: the read path was measured at 1.00× input and a file-backed merge at 2.04×, because the application layer reads each source once to resolve the selection and the engine reads it again. The recommendation is to hand the inspected bytes to the engine (B1), which needs a maintainer's approval before any code moves. |

New records use the next four-digit number and are added to this table in the same pull request. A
record is superseded, not rewritten: a later ADR states which decision replaces it and why.
