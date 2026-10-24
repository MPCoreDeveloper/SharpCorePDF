# 2. What SharpCorePDF Is Not

A precise boundary is part of the API. This chapter states what the library does not do, why, and what
to use instead. If one of these lines is a requirement, SharpCorePDF is the wrong tool and no amount of
configuration will change that.

## It is not a page layout or reporting library

SharpCorePDF can start a document from a blank page: `PdfDocumentBuilder` adds pages of a chosen size,
draws lines of text in a standard or an embedded TrueType font, places images at fixed positions and
draws fixed-position vector graphics — lines, rectangles, ellipses, arbitrary paths, dashed strokes,
transparency and clipping (Chapter 12, and Chapter 15 for the shape surface in full). It can also wrap a
paragraph into a column, align or justify each line and draw it in one call (Chapter 16). That is enough for a
cover page, an address block, a form-shaped frame or a fixed marker, and it is where authorship stops. There is
no **page flow**: no table or list layout, no page breaks, no columns, and no hyphenation — a block that does
not fit its column is refused, broken by character, clipped or continued, and a block that does not fit its
page runs off it. Text is not shaped beyond what the font program itself does; the image formats are the
four Chapter 12 covers, with SVG and the rest re-saved by the caller; and no font is ever read from the
machine the code runs on. The merge side still receives documents
as a file, a byte array, a stream or a custom `IPdfContentSource`, copies the pages you select and combines
them.

**Use instead:** a reporting or layout library that produces PDF when a report has to flow across pages, lay
out a table or hyphenate its text. SharpCorePDF can merge those reports afterwards and can add its own
fixed-position cover.

## It does not rewrite page content yet

SharpCorePDF can change the **assembly** of an existing document: rotate a page, delete it, move it,
duplicate it, insert an authored page, crop the area a page shows, and override the document information
(Chapter 14). What it cannot do *in this release* is change what a page draws. There is no API to replace
text or an image, remove an annotation, fill a form, add a redaction, or change a page's media box or its
size. Page content and page geometry that survive an edit are copied verbatim.

Signing and archival output are separate surfaces that change the file rather than its assembly: a
signature appends one revision to the bytes it is given (Chapter 23), and the archival writer states the
structures a PDF/A profile requires (Chapter 24). Neither rewrites what a page paints.

The 1.1 parity plan extended editing to page content, annotations, forms and page geometry. Page geometry
and the page-level operations shipped in `1.1.0`; annotation and form authoring, text layout and the
remaining image formats shipped in `1.2.0`, and an edit can now record a page of another document over or
under a page of the source. What an edit still does not do is rewrite the operators a page already carries:
a layer is added beside them. The capabilities that stay outside the product at every release —
rendering, conversion, OCR, conformance validation, services and CLIs — are the sections below.

**Use instead:** a viewer/editor SDK for interactive editing, a form-filling library when field values
have to change, and a redaction product when content has to be removed. If the change is which pages the
document contains, in what order, with what rotation and within what visible area, that is what an edit
plan does.

## It is not a renderer, converter or OCR engine

The library does not rasterise pages, does not render them to an image, does not convert PDF to Word,
HTML or a reflowed document, and does not recognise scanned characters. It does read the text a document
draws (Chapter 8): the glyphs, the words they make and the blocks those words make, each with its
coordinates — what the file already says, not what a picture of it might say. That is enough for search
over a born-digital document and not enough for a scan.

**Use instead:** a rendering engine for thumbnails and previews, an OCR product for scans, and a
layout-aware conversion product when the output has to be a reflowed document rather than text with
coordinates.

## It is not a PDF/A or PDF/UA validator

The output is a well-formed PDF in the version you ask for. It is not validated against PDF/A, PDF/X,
PDF/UA or any accessibility profile, and the library does not claim conformance. Writing *for* a profile is
a different thing from validating one: `IPdfAWriter` states the structures PDF/A-1b, -2b or -3b requires —
a stated version, an XMP identification, an output intent, embedded font programs, no encryption — and
removes or refuses what the profile forbids, reporting each decision as a warning or a refusal
(Chapter 24). What it does not do is certify the result, and a tagged structure tree for PDF/UA is not
copied or rebuilt.

**Use instead:** a dedicated validator when a compliance profile is a contractual requirement — veraPDF for
PDF/A, for example. Use the archival writer to make the document as close as the API allows, then validate.

## It is not a general-purpose PDF parser, but it does show you the file

Inspection reports what a merge needs and what an operator wants to know before merging: the page count,
page geometry, document information, outline size, embedded files, form presence, encryption state and
warnings. Above that, the reading surface publishes the file's own structures as read-only projections:

* `IPdfContentReader` hands back the images, text, annotations, forms, bookmarks, attachments and XMP a
  document carries (Chapter 8), and `IPdfLayoutReader` the letters, words and blocks it draws, exportable
  as ALTO, PAGE XML, hOCR or SVG (Chapters 20 and 21).
* `IPdfObjectInspector` walks the object graph — one node per listed object, references reported rather
  than followed — and lists the operators a page's content stream states (Chapter 22).

What it is not is a mutable object model: there is no way to edit an object and write the document back,
nothing here is a round-trip parser, and fonts are not enumerated as a first-class API — image metadata is
reported per image, not as a document-level index.

**Use instead:** a general-purpose PDF object model for mutation and round-tripping. SharpCorePDF reads
what it can describe and writes documents it assembled itself.

## What is deliberately bounded inside a merge

These are not missing features in the sense above; they are decisions about what a merged document
can sensibly contain. The library makes them explicit, and where a decision loses information it
records a warning.

| Area | Behaviour |
| --- | --- |
| Document-level features | Metadata follows `MetadataPolicy` (the primary source by default); viewer preferences, page layout and mode, form fields, named destinations and embedded files come from the **primary source**, because they cannot sensibly exist twice in one document |
| Page-level features | Content, resources, annotations, page size and rotation are merged from **every** source |
| Editing | An edit plan rotates (additively), deletes, moves and duplicates pages, and overrides document information. Page content and geometry are copied, not changed. Page labels are dropped when the page order or count changes, and XMP is dropped when document information is overridden; both are reported as warnings |
| Outlines | Merged into one tree, top-level entries in source order; bookmarks whose destination cannot be resolved are skipped with a warning. An edit redirects a destination to the first surviving page it named |
| Page labels | Dropped by default, because labels are tied to absolute page indices and misalign when sources are reordered or partially selected |
| AcroForm fields | The interactive form of the primary source is carried over; fields of other sources remain visible as page annotations but are not part of the merged form |
| Embedded files | Carried over from the primary source only |
| Named destinations | Carried over from the primary source; destinations in other sources are dropped with a warning |
| Encryption | Read: standard security handler revisions 2–6. Written: RC4-40, RC4-128, AES-128, AES-256. Public-key encryption is not supported |
| Output file structure | Classic cross-reference table, complete rewrite, PDF 1.4–2.0, no linearisation and no object streams in the output. A signature is the one exception: it appends a single incremental revision (Chapter 23) |
| Digital signatures | Signature fields and their widget annotations may be copied with the primary form or the pages, but a copied signature is not a valid signature of the merged document. The library signs a document it is given as a separate, explicit step (Chapter 23); a merge does not |

> **The signature point deserves its own sentence.** If you merge a signed document, the result is a
> new document that no signer signed. Do not present the output as signed. Validate signatures before
> merging if their presence matters to your workflow.

## What is deliberately bounded inside inspection

- Encrypted documents that cannot be opened are **described**, not thrown: `IsReadable` is `false`,
  `RequiresPassword` says whether a password would help, and inspection still returns a value. A merge
  of the same source throws `PdfEncryptionException` because it cannot proceed.
- Damaged documents are repaired where repair is safe and reported through warnings. When repair is
  not possible, inspection throws `PdfSourceFormatException` instead of returning a half-truth.
- Inspection reads the whole source into memory (bounded by `MaxSourceBytes`). It is a document-level
  operation, not a streaming scan.

## Not a service, not a CLI

The repository contains a library and examples, not a hosted API, a command line tool or a Windows
shell extension. A service that merges documents is a few dozen lines around `IPdfMerger`; the
cookbook shows one.

## The short version

| If you need to… | SharpCorePDF |
| --- | --- |
| Combine existing PDFs, all pages or a selection | Yes |
| Reorder or repeat pages from a source | Yes |
| Rotate, delete, move, duplicate, insert or crop pages in an existing document | Yes |
| Override the title, author or other document information | Yes |
| Preserve bookmarks, annotations and metadata | Yes |
| Encrypt the merged result | Yes |
| Learn page counts, sizes, outline size or encryption state before merging | Yes |
| Read the images, text, comments, form fields and bookmarks a document carries | Yes |
| Create a fixed-position page from scratch: text, PNG, JPEG, BMP and GIF images, vector graphics, standard or embedded fonts | Yes |
| Draw lines, paths, ellipses, clipping and transforms on a page (Chapter 15) | Yes |
| Sign a document, or validate a signature's integrity and trust | Yes |
| Write for PDF/A-1b, -2b or -3b | Yes |
| Read letters, words, blocks and reading order; export ALTO, PAGE XML, hOCR or SVG | Yes |
| Walk the object graph and the operators of a page, read-only | Yes |
| Lay out a report: page flow, tables, columns, hyphenation, JPEG images | No |
| Wrap and align a paragraph inside a fixed box (Chapter 16) | Yes |
| Change a page's size or media box, replace text or images, add or edit annotations and form fields | No |
| Fill forms, redact, or rebuild tagged structure | No |
| Render, convert, OCR or reflow text | No |
| Certify PDF/A or accessibility conformance | No |
