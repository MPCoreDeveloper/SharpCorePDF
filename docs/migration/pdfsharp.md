# Migrating from PDFsharp

**Audience**: a codebase that creates and edits documents with PDFsharp 6.5 and is considering
SharpCorePDF 1.1.0. **Scope**: what replaces what, and what 1.1.0 does not do yet. The final table walks
every parity row in [`docs/parity.md`](../parity.md) and names either the example that shows it or the
release that carries it.

SharpCorePDF is not a source-compatible replacement — row 21 of the scorecard is explicitly out of scope —
so a migration is a rewrite of the call sites, not a change of package reference. What transfers is the
model: pages have a size, content is placed in points from the bottom-left corner, and a written document
is a byte array or a file.

## The substitutions

| PDFsharp | SharpCorePDF 1.1.0 | Example |
| --- | --- | --- |
| `new PdfDocument()`, `document.AddPage()`, `PdfSharp.PageSize.A4` | `PdfDocumentBuilder.Create(...).AddPage(PdfPageSize.A4, page => …)` | `author-page` |
| `XGraphics.FromPdfPage(page)`, `gfx.DrawString(...)` | `PdfPageCanvas.DrawText(text, x, y, typeface, size)` — the canvas *is* the page surface handed to the callback | `author-page` |
| `gfx.DrawLine`, `DrawRectangle`, `DrawEllipse`, `DrawImage` | `PdfPageCanvas.DrawLine`, `DrawRectangle`, `DrawEllipse`, `DrawImage`, each returning the canvas so calls chain | `edit-pages` |
| `gfx.Save()` / `gfx.Restore()` | `PdfPageCanvas.SaveState()` / `RestoreState()` | manual chapter 12 |
| `new XFont("Arial", 12)` | `PdfTypeface.Standard(StandardFont.Helvetica)` for the standard 14, `PdfTypeface.FromTrueType(bytes)` for a caller-supplied face | `author-page` |
| `XImage.FromFile("logo.png")` | `DrawImage(pngBytes, x, y, width, height)` — the bytes travel with the call, so nothing is read from disk behind your back | `author-page` |
| `document.Save(path)` | `builder.Build("cover.pdf")` gives a document with `Bytes`; `await File.WriteAllBytesAsync(path, cover.Bytes)` writes it | `author-page` |
| `PdfReader.Open(path)` and `document.Pages.Count` | `await inspector.InspectAsync(new PdfFileSource(path))` returns `PdfDocumentInfo` with `PageCount` and per-page size and rotation, and reports an unreadable file instead of throwing | `inspect-first` |
| `document.Pages.Insert(0, importedPage)` | `MergePlan.Create().AddSource(...)` — source order is merge order | `merge-files` |
| `page.Orientation`, `page.Width`, `page.CropBox` | an edit plan: `RotatePage`, `CropPage`, `ResizePage`, `DeletePage`, `ReorderPages`, `InsertPage` | `edit-pages` |
| Encrypting on save (`PdfSecuritySettings`) | `MergeOutputOptions.Encryption` or `PdfEditOutputOptions` with `PdfEncryptionOptions`; AES-256, AES-128, RC4-128, RC4-40 | `protect-result` |
| PDFsharp's PDF/A manager | `IPdfAWriter.ConvertAsync` with `PdfAProfile.PdfA1b`, `PdfA2b` or `PdfA3b` | `archive-pdfa` |
| A signing handler over a byte range | `IPdfSigner.SignAsync` with `PdfSignatureOptions`, and `IPdfSignatureValidator.ValidateAsync` for integrity and trust as separate answers | manual chapter 23 |

Three habits do not survive the move, on purpose:

* **Nothing is edited in place.** A `PdfEditPlan` is immutable and is applied in one pass to a source you
  never mutate, so an operation list can be built, logged and replayed.
* **A document that cannot be produced is refused.** SharpCorePDF raises a `PdfException` subclass or
  writes nothing, rather than writing a document that is subtly wrong. The `errors` example runs every
  refusal.
* **What was changed is reported.** A merge or an edit returns `Warnings`; the `warnings` example prints
  the two channels — what inspection saw in a document, and what the operation had to do to it.

## Every parity row

The numbers are the rows of [`docs/parity.md`](../parity.md), which is the authority if this table and the
scorecard ever disagree.

| # | Capability | 1.1.0 | Where to look |
| --- | --- | --- | --- |
| 1 | Page creation, fixed-position text, standard and embedded fonts | Ships | `author-page`; manual chapter 12 |
| 2 | Vector drawing: lines, paths, curves, rectangles, ellipses, fill, stroke, clipping, transforms | Ships for paths, ellipses, rectangles, clipping, graphics-state balance and constant alpha; **dash, cap, join and `SetTransform` are 1.2** | `edit-pages` draws a frame, a dashed rule, an ellipse and a translucent band; the operator matrix is in `PdfDocumentBuilderTests.Drawing` |
| 3 | Text measurement, wrapping, alignment, leading, basic layout | **Not in 1.1.0 — 1.2** | No example yet: `PdfTextMetrics` exists, the layout writer does not |
| 4 | PNG, JPEG, BMP and GIF images | Ships for PNG (alpha, palette, 16-bit, colour key, `pHYs`); **JPEG, BMP and GIF are 1.2** | `author-page` (an RGBA logo with an `/SMask`) |
| 5 | Page import, insert, delete, reorder, rotate, crop, resize, split, extract, append | Ships | `edit-pages`, `select-pages`; manual chapter 14 |
| 6 | Overlay, underlay, watermark, stamp | **Not in 1.1.0 — 1.2** | No example yet |
| 7 | Metadata, XMP, outlines, destinations, page labels, viewer preferences | Ships for the information dictionary and XMP (`SetMetadata`, metadata policy and override); **outline, page-label and viewer-preference writing is 1.2** | `options` |
| 8 | AcroForm creation, filling, flattening, appearance generation | **Reading only; writing is 1.2** | `read-content` reads the field |
| 9 | Annotation creation, editing, removal and appearance streams | **Reading only; writing is 1.2** | `read-content` reads the comment and the link |
| 10 | Incremental updates that preserve existing signatures | **Not in 1.1.0 — 1.2**: `Append` and `ExtractPages` rewrite the document instead of appending a revision | No example yet |
| 11 | Digital signatures: sign, validate, timestamp, integrity and trust separately | Ships | Manual chapter 23; the tests are the evidence, the `sign-and-archive` example is 1.2 (T094) |
| 12 | PDF/A-1b, PDF/A-2b and PDF/A-3b output | Ships | `archive-pdfa`; manual chapter 24 |
| 13 | Positioned letters, words and blocks; segmentation and reading order | Ships | `extract-layout` |
| 14 | Images, hyperlinks, form values, annotations, embedded files, metadata, bookmarks | Ships for every resource except image pixels, which are 1.2 | `read-content`, `extract-layout` |
| 15 | Public read-only object graph and content-stream inspection | Ships | `inspect-objects` |
| 16 | Export to ALTO, PAGE XML, hOCR and SVG text | Ships | `extract-layout` |
| 17 | Rendering pages to images or raster previews | **Out of scope** — no rasteriser, thumbnail generator, viewer or print pipeline | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 18 | OCR, HTML-to-PDF, PDF-to-Word/HTML, reflow, tagged-PDF/PDF-UA authoring | **Out of scope** | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 19 | MigraDoc-style flow engine with automatic page breaks | **Out of scope** — the caller places content on pages | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 20 | JavaScript execution, 3D, multimedia, geospatial, public-key encryption | **Out of scope** — actions may be copied or refused with a warning, never executed | [`ADR-001`](../adr/0001-pdf-platform-boundary.md) |
| 21 | Drop-in source or binary compatibility with PDFsharp or PdfPig | **Out of scope** — capability parity only | [`docs/parity.md`](../parity.md) |

Rows 3, 6, 8, 9 and 10 are *Must* capabilities that 1.1.0 does not complete. They are not dropped: each is
still committed, its task is named in
[`tasks.md`](../../specs/003-pdf-platform-parity/tasks.md), and the scorecard carries the release that
completes it. A migration that depends on one of them should wait for 1.2 rather than work around it.

## What to read next

* [`docs/measurements.md`](../measurements.md) — what a merge, an edit, a read and an archive cost in time
  and memory, measured on this machine with the method written down.
* [`docs/parity.md`](../parity.md) — the scorecard, with the test and the external command behind each row.
* [`examples/README.md`](../../examples/README.md) — the nineteen examples, each of which runs.
