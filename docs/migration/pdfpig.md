# Migrating from PdfPig

**Audience**: a codebase that reads documents with PdfPig 0.1.x and is considering SharpCorePDF 1.1.0.
**Scope**: what replaces what on the reading side, and what changes when the same library also writes. The
final table walks every parity row in [`docs/parity.md`](../parity.md) and names either the example that
shows it or the release that carries it.

PdfPig is a reader with a small creation side; SharpCorePDF is a merger, an editor, an author and a reader
behind four assemblies. The reading calls therefore map closely — the same letters, words, images and form
fields come back — but they arrive as one `PdfReadResult<T>` per call, with warnings attached, rather than
as properties on a page object you keep alive.

## The substitutions

| PdfPig | SharpCorePDF 1.1.0 | Example |
| --- | --- | --- |
| `PdfDocument.Open(path)` | `new PdfFileSource(path)` is the source; nothing is parsed until a port is called | `inspect-first` |
| `document.NumberOfPages`, `document.GetPage(n)` | `await inspector.InspectAsync(source)` gives every page's size, rotation, content flag and annotation count | `inspect-first` |
| `page.Letters` | `IPdfLayoutReader.ReadLettersAsync(source, pages)` returns `PdfLetter` — Unicode text, font name, size, box and baseline | `extract-layout` |
| `page.Text` and `page.GetWords()` | `ReadWordsAsync` with `PdfWordExtractionOptions`, and `ReadTextAsync` for blocks of text in reading order | `extract-layout` |
| The word extractor and the page segmenter | `PdfWordExtractionStrategy`, `PdfPageSegmentationStrategy` and `PdfReadingOrderStrategy` are named values, and the result records which ones produced it | `extract-layout` |
| `page.Images` | `ReadImagesAsync` returns `PdfImageInfo` with the colour space, bits per component, mask and placement; **pixels are 1.2** | `read-content` |
| `page.GetHyperlinks()` | `ReadHyperlinksAsync` returns `PdfHyperlink` with the resolved destination or the unresolved one and a warning | `read-content` |
| `page.Annotations` | `ReadAnnotationsAsync` returns kind, rectangle, contents and appearance presence | `read-content` |
| `document.TryGetForm(out var form)` | `ReadFormFieldsAsync` returns `PdfFormFieldInfo` per field and widget | `read-content` |
| `page.Operations` (the content-stream operators) | `IPdfObjectInspector.ReadOperationsAsync(source, page)` returns each operator with the offset it starts at | `inspect-objects` |
| `document.Structure`, raw object access | `IPdfObjectInspector.ReadObjectsAsync(source)` returns an immutable `PdfObjectView` graph, dangling references visible and never followed | `inspect-objects` |
| `document.Information`, XMP access | `ReadMetadataAsync` returns the XMP packet and the values parsed beside it | `read-content` |
| `document.GetBookmarks()` and similar outline walking | `ReadOutlineAsync` returns a tree of `PdfOutlineItem` with resolved destinations | `read-content` |
| PdfPig's `PdfDocumentBuilder` | `PdfDocumentBuilder.Create(...)` — same idea, different type in a different assembly; pages, text, vector drawing, PNG, JPEG, BMP and GIF images and PDF/A output are what it adds | `author-page`, `edit-pages` |

Three differences that a port has to account for:

* **Pages are selected, not enumerated.** Every read call takes a `PageRange`, so one call reads pages
  `1,3-5` and the result carries per-page results and warnings — there is no page object to hold open, and
  no document to dispose.
* **Encrypted documents are a parameter, not a separate path.** `password:` is on every call, and a
  document that needs one is reported by inspection instead of throwing.
* **A refused resource is a warning.** An unresolved destination, an unsupported filter or a truncated
  stream produces a `PdfWarning` beside the values that were read, so a caller can decide whether a partial
  answer is useful. PdfPig tends to throw or to return empty.

## Every parity row

The numbers are the rows of [`docs/parity.md`](../parity.md), which is the authority if this table and the
scorecard ever disagree. The reading rows are the ones a PdfPig port meets first; the creation rows are
listed because a migrating caller usually needs both halves.

| # | Capability | 1.1.0 | Where to look |
| --- | --- | --- | --- |
| 1 | Page creation, fixed-position text, standard and embedded fonts | Ships | `author-page`; manual chapter 12 |
| 2 | Vector drawing: lines, paths, curves, rectangles, ellipses, fill, stroke, clipping, transforms | Ships for paths, ellipses, rectangles, clipping, graphics-state balance and constant alpha; **dash, cap, join and `SetTransform` are 1.2** | `edit-pages`; `PdfDocumentBuilderTests.Drawing` |
| 3 | Text measurement, wrapping, alignment, leading, basic layout | **Not in 1.1.0 — 1.2** | No example yet; `PdfTextMetrics` exists, the layout writer does not |
| 4 | PNG, JPEG, BMP and GIF images | Ships for PNG; **JPEG, BMP and GIF are 1.2** | `author-page` |
| 5 | Page import, insert, delete, reorder, rotate, crop, resize, split, extract, append | Ships | `edit-pages`, `select-pages`; manual chapter 14 |
| 6 | Overlay, underlay, watermark, stamp | **Not in 1.1.0 — 1.2** | No example yet |
| 7 | Metadata, XMP, outlines, destinations, page labels, viewer preferences | Ships for the information dictionary and XMP; **outline, page-label and viewer-preference writing is 1.2** | `options`; reading an outline is `read-content` |
| 8 | AcroForm creation, filling, flattening, appearance generation | **Reading only; writing is 1.2** | `read-content` via `ReadFormFieldsAsync` |
| 9 | Annotation creation, editing, removal and appearance streams | **Reading only; writing is 1.2** | `read-content` via `ReadAnnotationsAsync` |
| 10 | Incremental updates that preserve existing signatures | **Not in 1.1.0 — 1.2** | No example yet |
| 11 | Digital signatures: sign, validate, timestamp, integrity and trust separately | Ships for the writing and validating halves | Manual chapter 23 |
| 12 | PDF/A-1b, PDF/A-2b and PDF/A-3b output | Ships | `archive-pdfa`; manual chapter 24 |
| 13 | Positioned letters, words and blocks; segmentation and reading order | Ships — the row a PdfPig port starts from | `extract-layout` |
| 14 | Images, hyperlinks, form values, annotations, embedded files, metadata, bookmarks | Ships for every resource except image pixels, which are 1.2 | `read-content` (all of them on one document), `extract-layout` |
| 15 | Public read-only object graph and content-stream inspection | Ships | `inspect-objects` |
| 16 | Export to ALTO, PAGE XML, hOCR and SVG text | Ships | `extract-layout` |
| 17 | Rendering pages to images or raster previews | **Out of scope** — no rasteriser, thumbnail generator, viewer or print pipeline | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 18 | OCR, HTML-to-PDF, PDF-to-Word/HTML, reflow, tagged-PDF/PDF-UA authoring | **Out of scope** | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 19 | MigraDoc-style flow engine with automatic page breaks | **Out of scope** — the caller places content on pages | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 20 | JavaScript execution, 3D, multimedia, geospatial, public-key encryption | **Out of scope** — actions may be copied or refused with a warning, never executed | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 21 | Drop-in source or binary compatibility with PDFsharp or PdfPig | **Out of scope** — capability parity only | [`docs/parity.md`](../parity.md) |

Rows 3, 6, 8, 9 and 10 are *Must* capabilities that 1.1.0 does not complete. Each keeps its task in
[`tasks.md`](../../specs/003-pdf-platform-parity/tasks.md) and the release that completes it in the
scorecard; a port that needs one of them should wait for 1.2.

## What to read next

* [`docs/parity.md`](../parity.md) — the scorecard, with the test and the external command behind each row.
* [`manual/src/08-inspection.md`](../manual/src/08-inspection.md) — the reading API in the manual, chapter
  by chapter: the document model, the content reader, the inline budget and preflight.
* [`examples/README.md`](../../examples/README.md) — `read-content`, `extract-layout` and `inspect-objects`
  are the three examples a PdfPig port should run first.
