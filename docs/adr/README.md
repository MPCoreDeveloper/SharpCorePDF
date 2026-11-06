# Architecture decision records

An architecture decision record (ADR) captures a decision that changes the shape of the product or
the constraints its code must respect. It records why the decision was taken, what it rules out and
what it obliges later work to prove; it does not restate the implementation.

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-pdf-platform-boundary.md) | Accepted | SharpCorePDF is a dependency-free PDF creation, editing and reading library, targeting capability parity with PDFsharp 6.5 and PdfPig 0.1.x while keeping rendering, conversion, OCR, conformance validation, services and CLIs outside the product. |
| [0002](0002-new-code-coverage-1.1.0.md) | Accepted | `1.1.0` ships with its new-code coverage measured at 77.76% against the 90% floor, recorded rather than waived; no test is written to move the number, and a named `1.2` task closes the gap on the parsers' refusal branches. |
| [0003](0003-per-letter-read-model.md) | Accepted | The per-letter read model (`PdfGlyph`, `PdfLetter`, `PdfRectangle`) stays a record graph in 1.x: the struct conversion was measured and raised allocations, because `PdfWord` stores its own copies. The 2.0 candidate is slices over one array plus structs, to be measured before adoption. |

New records use the next four-digit number and are added to this table in the same pull request. A
record is superseded, not rewritten: a later ADR states which decision replaces it and why.
