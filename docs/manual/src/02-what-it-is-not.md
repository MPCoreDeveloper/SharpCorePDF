# 2. What SharpCorePDF Is Not

A precise boundary is part of the API. This chapter states what the library does not do, why, and what
to use instead. If one of these lines is a requirement, SharpCorePDF is the wrong tool and no amount of
configuration will change that.

## It is not a page layout or reporting library

SharpCorePDF can start a document from a blank page: `PdfDocumentBuilder` adds pages of a chosen size,
draws lines of text in a standard or an embedded TrueType font, and places PNG images at fixed positions
(Chapter 12). That is enough for a cover page, an address block or a fixed marker, and it is where
authorship stops. There is no word wrap, paragraph flow, table, list or vector drawing; text is not
shaped beyond what the font program itself does; JPEG and other image formats are not embedded; and no
font is ever read from the machine the code runs on. The merge side still receives documents as a file,
a byte array, a stream or a custom `IPdfContentSource`, copies the pages you select and combines them.

**Use instead:** a reporting or layout library that produces PDF when a page has to flow, wrap or lay
out a table. SharpCorePDF can merge those reports afterwards and can add its own fixed-position cover.

## It is not a content editor

SharpCorePDF can change the **assembly** of an existing document: rotate a page, delete it, move it,
duplicate it, and override the document information (Chapter 14). What it cannot do is change what a
page draws. There is no API to replace text or an image, remove an annotation, fill a form, add a
digital signature, add a redaction, or change a page's media box, crop box or size. Page content and
page geometry that survive an edit are copied verbatim.

**Use instead:** a viewer/editor SDK for interactive editing, a form-filling library when field values
have to change, and a redaction product when content has to be removed. If the change is which pages
the document contains, in what order and with what rotation, that is what an edit plan does.

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
PDF/UA or any accessibility profile, and the library does not claim conformance. The primary source's
catalog entries for language, marked content (`MarkInfo`) and output intents are copied, and colour
profiles that belong to copied page resources travel with them, but no tagged structure tree is copied
or rebuilt, and no profile is added, repaired or certified.

**Use instead:** a dedicated validator when a compliance profile is a contractual requirement.

## It is not a general-purpose PDF parser

Inspection reports what a merge needs and what an operator wants to know before merging: the page
count, page geometry, document information, outline size, embedded files, form presence, encryption
state and warnings. It does not expose the object graph, does not enumerate fonts, does not report
image resolutions and does not let you walk the content streams.

**Use instead:** a low-level PDF object model when you need to inspect arbitrary structures.

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
| Output file structure | Classic cross-reference table, complete rewrite, PDF 1.4–2.0. No incremental update, no linearisation, no object streams in the output |
| Digital signatures | Signature fields and their widget annotations may be copied with the primary form or the pages, but a copied signature is not a valid signature of the merged document. The library does not sign and does not claim a copied signature remains trustworthy |

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
| Rotate, delete, move or duplicate pages in an existing document | Yes |
| Override the title, author or other document information | Yes |
| Preserve bookmarks, annotations and metadata | Yes |
| Encrypt the merged result | Yes |
| Learn page counts, sizes, outline size or encryption state before merging | Yes |
| Read the images, text, comments, form fields and bookmarks a document carries | Yes |
| Create a fixed-position page from scratch: text, PNG images, standard or embedded fonts | Yes |
| Lay out a report: flow, wrapping, tables, vector graphics, JPEG images | No |
| Change page size, crop box, text, images or annotations | No |
| Render, convert, OCR or reflow text | No |
| Fill forms, sign or redact | No |
| Validate PDF/A or accessibility profiles | No |
