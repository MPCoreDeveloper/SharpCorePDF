# SharpCorePDF documentation

This directory holds the SharpCorePDF manual, the measurement record and the project's release and
governance documents. The [repository README](../README.md) is the short tour; the manual is the
long-form reference for the API.

## The manual

- **[Read or download the manual as a PDF](SharpCorePDF-Manual-1.3.0.pdf)** — 118 pages, A4, with a
  linked contents page and PDF bookmarks.
- **Read the chapters as Markdown** in [`manual/src`](manual/src). Each file is one chapter, and the
  file-name order is the chapter order. This edition describes `1.3.0`. Chapters 16 to 19 were written
  while `1.1.0` was the package on the feed, and their heads used to say which part of their surface it
  already carried — the annotation writer and the form model shipped there, while the form writer, the
  text layouter and the JPEG, BMP and GIF readers are what `1.2.0` added. They now describe the surface as
  it stands. The [`1.2.0`](SharpCorePDF-Manual-1.2.0.pdf) and
  [`1.1.0`](SharpCorePDF-Manual-1.1.0.pdf) editions stay beside this one as the record of what those
  releases shipped, and the chapter numbers are kept so that a cross-reference survives both.

| Chapter | What it covers |
| --- | --- |
| [Preface](manual/src/00-preface.md) | Who the manual is for, how it is organised, and the conventions every chapter uses. |
| [1. What SharpCorePDF Is](manual/src/01-what-it-is.md) | The four use cases, what a merge guarantees, and what "no third-party engine" means. |
| [2. What SharpCorePDF Is Not](manual/src/02-what-it-is-not.md) | The deliberate boundary: no layout engine, no page-content rewriting yet, rendering, conversion, OCR or PDF/A validation. |
| [3. Installation and First Merge](manual/src/03-installation.md) | Install the package, register the services, merge two files, and read the result. |
| [4. The Merge Plan](manual/src/04-mergeplan-basics.md) | How a plan is built, why source order is the merge order, and how validation and reuse work. |
| [5. Page Selection](manual/src/05-page-selection.md) | The one-based selection notation, building selections in code, and preflighting a range. |
| [6. Output Options](manual/src/06-output-options.md) | Target version, primary source, metadata policies and overrides, outlines, page labels and reproducible output. |
| [7. Encryption and Permissions](manual/src/07-encryption-permissions.md) | Algorithms, passwords, permission bits, encrypted sources, and what encryption does to reproducibility. |
| [8. Inspection](manual/src/08-inspection.md) | The document model, what inspection reports, the content reader (images, text, annotations, form, bookmarks, attachments, XMP and the inline budget), encrypted and damaged documents, and preflight. |
| [9. Errors, Exceptions and Warnings](manual/src/09-error-handling.md) | The exception family, what throws when, writing files safely, and warnings versus errors. |
| [10. Dependency Injection](manual/src/10-dependency-injection.md) | `AddSharpCorePDF`, engine options, configuration binding, logging and testing with the container. |
| [11. Architecture](manual/src/11-architecture.md) | The layers, the life of a merge, immutability, the memory model, extension points and concurrency. |
| [12. Authoring a Page](manual/src/12-authoring.md) | Writing a page from nothing: coordinates, standard and embedded fonts, PNG, JPEG, BMP and GIF images, determinism, and the boundary of the feature. |
| [13. Cookbook](manual/src/13-cookbook.md) | Complete recipes for folder merges, password prompts, extraction, encryption, uploads, logging, batching, preflight gates and cover pages. |
| [14. Editing an Existing Document](manual/src/14-editing.md) | Rotating, deleting, moving and duplicating pages, ordering rules, metadata overrides, warnings, reproducible output and what an edit cannot change. |
| [15. Drawing](manual/src/15-drawing.md) | Coordinates and the rectangle names, lines, rectangles, ellipses, paths and fill rules, paint and stroke style, dashes, clipping, transforms, the saved graphics state, and what the surface deliberately does not do. |
| [16. Text Layout](manual/src/16-text-layout.md) | Measuring a line, the metrics a face reports, wrapping a paragraph into a column, alignment, justification and overflow, drawing the result, and what a layout deliberately does not do. |
| [17. Image Formats](manual/src/17-image-formats.md) | What is read out of a PNG, a JPEG, a BMP and a GIF, the colour profile a PNG states, natural size and stretching, refusals by name, and what the surface deliberately does not do. |
| [18. Annotations](manual/src/18-annotations.md) | The kinds and their factories, appearance streams, updating and removing by identifier, clipping, the warnings, and what an annotation is not. |
| [19. Forms](manual/src/19-forms.md) | The field model and its factories, writing a form, filling by name, reset, flatten, appearance generation, the refusals, XFA, and reading the result back. |
| [20. Extraction](manual/src/20-extraction.md) | `PdfReadResult<T>`, letters, words, blocks, the four export formats, the coordinate flip, reproducible export bytes, and what extraction is not. |
| [21. Layout Analysis](manual/src/21-layout-analysis.md) | Segmentation and reading-order strategies, the em-based word rules, decorations versus prose, comparing two readings, and where the analysis stops. |
| [22. The Object Graph](manual/src/22-object-graph.md) | One node per listed object, references reported rather than followed, dangling references, streams without bytes, and the operators of a page. |
| [23. Digital Signatures](manual/src/23-signatures.md) | Appending a revision, detached CMS, the integrity/trust split, multiple signatures, and what a signature does not promise. |
| [24. Archival PDF (PDF/A)](manual/src/24-pdf-a.md) | The profiles, what is removed versus refused, archival edits, output intents, and the veraPDF evidence. |
| [25. How This Manual Is Built](manual/src/25-how-this-manual-is-built.md) | The four build stages, the Markdown subset, the browser as a layout engine, the two passes that measure the contents, and the footer, bookmarks and metadata written with SharpCorePDF. |

The first four chapters are the shortest path to a working merge. Chapters 5 to 10 cover one part of the
API each, chapters 11 to 14 are for design context, authoring, ready-to-adapt recipes and editing, chapters
15 to 19 continue the authoring surface — drawing, text layout, images, annotations and forms — chapters 20
to 22 cover the reading surface, chapters 23 and 24 cover signatures and archival output, and chapter 25
describes how the manual itself is built.
`Chapter 3` is the place to start if you only want to see a document merged.

### Building the PDF

The PDF is generated from the Markdown chapters; do not edit it by hand.

```powershell
dotnet run --project .\tools\SharpCorePDF.Manual                     # write docs/SharpCorePDF-Manual-1.3.0.pdf
dotnet run --project .\tools\SharpCorePDF.Manual -- --keep-html      # also keep docs/manual/build/manual.html
dotnet run --project .\tools\SharpCorePDF.Manual -- --no-stamp       # skip the footer, bookmarks and metadata
```

The build needs the .NET 11 SDK and Chrome or Edge for the headless print — the browser is only the
layout engine. The Markdown, the syntax highlighting, the contents page, the footer, the bookmarks and
the document metadata all come from [`tools/SharpCorePDF.Manual`](../tools/SharpCorePDF.Manual), which
uses SharpCorePDF itself to finish the document. It lays the book out twice: the first pass measures the
heading positions in the produced PDF, and the second writes the measured page numbers into the
contents, re-rendering if a measured number moved a heading. The stylesheet is
[`manual/manual.css`](manual/manual.css); the chapter sources are the files in
[`manual/src`](manual/src), ordered by file name. `SHARPCOREPDF_CHROME` points the printer at a browser
that is not in the place it looks first. Chapter 25 of the manual describes the same pipeline for the
reader of the PDF.

When adding or renaming a chapter, keep the two-digit numeric prefix and rebuild the PDF so the contents
page, running footer and bookmarks stay in step.

## Other documents

| Document | What it records |
| --- | --- |
| [`migration/pdfsharp.md`](migration/pdfsharp.md) | Moving from PDFsharp/PdfSharpCore: the substitution table, what does not survive the move, and the parity rows that are not covered yet. |
| [`migration/pdfpig.md`](migration/pdfpig.md) | Moving from PdfPig/UglyToad: pages are selected rather than enumerated, `password:` on every call, and a refused resource is a warning rather than a throw. |
| [`parity.md`](parity.md) | The 1.1 capability scorecard: reference behaviour, SharpCorePDF outcome, owner, named test and external validation command for every “Must” row. |
| [`parity/README.md`](parity/README.md) | The fixture corpus: manifest, provenance, licence, regeneration command and the integrity test. |
| [`api/1.1-api-review.md`](api/1.1-api-review.md) | The Phase 0 API review: placement, immutability, naming, dependency and platform rules for the 1.1 surface. |
| [`measurements.md`](measurements.md) | The recorded cost of a merge, of a content read and of writing a page — allocations, wall time, throughput, retained heap and output bytes — and the performance and memory budgets they are read against. |
| [`mutation-testing.md`](mutation-testing.md) | What the suite would notice changing: the Stryker configuration, how to read a report, the statuses that count toward the score, the first recorded run, the two tests its survivors produced, what the six that remain say about the tests, and what moved between two readings. |
| [`release-checklist.md`](release-checklist.md) | The gates a version has to clear before it reaches nuget.org: version, packages, tests, coverage, changelog, symbols and the dry run. |
| [`adr/`](adr/) | Architecture decision records. ADR-001 records the creation, editing and reading product boundary and the PDFsharp/PdfPig parity target; ADR-002 records the new-code coverage figure `1.1.0` ships with and the `1.2` task that closes it. |
| [`foundation-readiness.md`](foundation-readiness.md) | Every .NET Foundation eligibility criterion mapped to a repository artifact, with the remaining gaps named. |
| [`foundation-application.md`](foundation-application.md) | The eight conditions that decide when the Foundation application is filed, and the answers it will contain. |

## Tools

| Tool | What it does |
| --- | --- |
| [`tools/validate-pdf.py`](tools/validate-pdf.py) | Runs `qpdf --check`, decodes every stream, and prints the geometry, rotation and annotations of every page; `--password` does the same for an encrypted document. |
| [`tools/make-fixtures.py`](tools/make-fixtures.py) | Regenerates the PDF fixtures the test suite uses; `--parity` regenerates the checked-in parity corpus through [`tools/make_parity_fixtures.py`](tools/make_parity_fixtures.py). |

Both tools run through `uv`, as shown in the repository README:

```powershell
uv run --with pikepdf python .\docs\tools\validate-pdf.py <file.pdf>
uv run --with pikepdf python .\docs\tools\make-fixtures.py .\tests\SharpCorePDF.Tests\Assets
```
