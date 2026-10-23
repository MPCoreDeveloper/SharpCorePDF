# Preface

SharpCorePDF is a PDF **creation, editing and reading** library for .NET. It merges PDF documents,
reads the images, text, annotations, forms and bookmarks they already contain, writes fixed-position
pages from nothing, and changes the assembly of an existing document — which pages it has, in what
order and with what rotation. It does all of that without calling a native library, a command line
tool or a third-party PDF engine.

This manual describes version **1.1.0** of the library. It is written for the developer who has to ship a
feature, not only for the developer who wants to read source code: every chapter explains the behaviour you
can rely on, the behaviour you should not rely on, and the code that exercises it.

## Who this manual is for

You will get the most out of it if you are comfortable with C#, asynchronous methods and dependency
injection. No knowledge of the PDF specification is assumed. Where a term from the specification
matters — outline, page label, document information, security handler — the chapter explains it in the
terms a caller needs, and stops there.

## How the manual is organised

The first four chapters are the shortest path to a working merge:

- **Chapter 1** describes what the library does and the guarantees it makes.
- **Chapter 2** states, just as plainly, what it does not do.
- **Chapter 3** installs the package and merges three documents.
- **Chapter 4** explains the merge plan, the object at the centre of the API.

Chapters 5 to 10 cover one part of the API each: page selection, output options, encryption,
inspection, errors and warnings, and dependency injection. Chapters 11 to 14 are for readers who want
to understand the design, write a page that did not exist, need a recipe they can adapt, or change the
assembly of an existing document: the architecture, authoring, a cookbook, then editing.

Chapters 20 to 22 cover the reading surface that turns a page into data a program can use: extraction and
its four export formats, the layout analysis that decides what a block is and which block is read first,
and the object graph and content operators that describe the file itself. Chapters 23 and 24 cover the
trust surface: digital signatures and archival (PDF/A) output.

**Chapters 15 to 19 are reserved and not part of this edition.** They document the additions queued for
`1.2`: a dedicated drawing chapter (the surface it describes is documented inside Chapter 12 today), text
layout, the remaining image formats, and writing annotations and form fields. The numbers are reserved
rather than reassigned so that a cross-reference in this edition keeps pointing at the same chapter in the
next one.

You do not have to read the chapters in order. Each one starts from a complete example and refers to
earlier chapters only for background.

## Conventions

**Page numbers are one-based.** Page 1 is the first page of a document, and every page number that
crosses the API — in a page selection, in `PdfPageInfo.Number`, in an error message — is one-based.
The PDF format itself numbers objects from zero internally; that detail never reaches you.

**Code is complete enough to compile.** Snippets omit `using` directives and surrounding class
declarations when they are obvious, but every method call, property and argument name is the one in
the documented API. The cookbook contains full methods for the recipes that need more than a fragment.

**Warnings are not errors.** A document that was repaired while being read still merges. A chapter
that had to drop a bookmark still produces a result. Whenever the library makes such a decision it
records a `PdfWarning`, and Chapter 9 explains which warnings mean what. Treat the `Kind` of a warning
as the contract, never the wording of its message.

**The manual is built from Markdown.** The chapters in `docs/manual/src` are the source of both the
GitHub documentation and the PDF. If you find a discrepancy between this text and the code, the code
is the source of truth; please open an issue so the text can be corrected.

> **Version note.** This manual is written against 1.1.0. The public API is covered by an approved snapshot
> in each project, so a change to a public member is a deliberate, reviewed event rather than an accident of
> a refactoring. Code that compiles against 1.0.0 keeps compiling across a minor release — every addition is
> additive — and the changelog records every addition and every change of behaviour.

## What you need

- The .NET SDK that can target **.NET 10** or **.NET 11**. The packages ship assemblies for both.
- A PDF to merge. Any reader can produce one; the tests use documents written by a different producer
  precisely so that the library is not tested only against its own output.
- For building this manual: Python with `markdown`, `pygments` and `pypdf`, and Chrome or Edge. That is
  a maintainer concern, not a requirement of the library.
