# Research: PDF Platform Parity

**Feature**: `003-pdf-platform-parity` | **Date**: 2026-10-21

This file records the decisions that turn “PDFsharp / PdfPig level” into an implementable, testable
target. Sources were read on 2026-10-21.

## Sources

- **PDFsharp documentation home** — <https://docs.pdfsharp.net/>: PDFsharp 6.5.0 is the current stable
  release, 7.0.0 Preview 2 is newer; .NET 8/9/10 and .NET Framework are supported; PDFsharp creates and
  processes PDFs with GDI+-style drawing; only basic text layout is supported and page breaks are not
  automatic; MigraDoc is the separate flow-layout product.
- **PDFsharp repository** — <https://github.com/empira/PDFsharp>: source tree includes
  `PdfSharp/Pdf.Signatures`, `PdfSharp/Pdf.Forms`, `PdfSharp/Pdf.Annotations`, `PdfSharp/Pdf.PdfA`,
  `PdfSharp.Imaging` with BMP, JPEG and PNG importers, and GDI+ drawing classes. The licence is AGPL or
  commercial.
- **PdfPig README** — <https://raw.githubusercontent.com/UglyToad/PdfPig/master/README.md>: reading text
  and content, basic creation, letters/words/blocks, annotations, bookmarks, forms, hyperlinks,
  embedded files and merging; creation has explicit limits (no form editing, no annotation/metadata/
  structure changes, no text editing with existing fonts).
- **PdfPig wiki** — <https://github.com/UglyToad/PdfPig/wiki>: positioned letters, image retrieval,
  annotations, forms, embedded documents, hyperlinks, metadata, internal structure, creation with text
  and path operations, encrypted-file reading, document layout analysis, and ALTO/PageXML/hOCR export.
  It states that forms are read-only, tables are not directly supported, rendering is not provided, and
  HTML conversion is not provided.
- **SharpCorePDF repository** — `README.md`, `docs/manual/src/01-what-it-is.md`,
  `docs/manual/src/02-what-it-is-not.md`, `specs/002-page-authoring/spec.md`,
  `.specify/memory/constitution.md` and `docs/measurements.md`: current merge, inspection, encryption,
  bounded authoring, layer graph, quality gates and measured budgets.
- **Standard validation tools** — qpdf/pikepdf, pypdf, PyMuPDF, veraPDF, pyHanko and fontTools. Their
  documentation defines the commands used in `quickstart.md`; none becomes a shipped dependency.

## Decisions

### D1. Target PDFsharp 6.5, PdfPig 0.1.x, and capability parity

**Decision**: Use PDFsharp **6.5.0** (current stable) and PdfPig **0.1.x** (pre-1.0, API changes
allowed) as the reference surfaces. Match documented jobs and observable outcomes, not type names,
namespaces or method signatures.

**Rationale**: PDFsharp 7 is still a preview; targeting it would make a moving preview the parity
baseline. PdfPig explicitly does not follow SemVer before 1.0, so matching its exact API would be
unstable. Capability parity is the durable target and avoids copying another project’s API.

**Alternatives considered**: API-compatible shims (rejected: large surface, licensing and maintenance
cost, and it freezes this repository to another project’s design); PDFsharp 7 preview (rejected:
unstable baseline); “merge plus a few drawing calls” (rejected: does not reach either reference).

### D2. Amend the constitution before implementation

**Decision**: Make CA-001 the first, blocking phase. Constitution 2.0.0 redefines Principle VII so
creation and editing are part of the product, while rendering, OCR, HTML/Office conversion, PDF/A
validation and PDF/UA authoring remain excluded.

**Rationale**: Constitution 1.0.0 said the library was not a creation library or an editor.
Implementing this feature without an amendment would have made the repository’s own governing
document false. The amendment records the product decision, not a code detail, and belongs in the
same review as the parity target.

**Alternatives considered**: Keep the boundary and implement only reader parity (rejected: does not
meet “PDFsharp level”); treat the new surface as experimental and undocumented (rejected: violates
Principle VII and makes the README false).

### D3. No new assembly and no new runtime dependency

**Decision**: Keep the four-layer solution. Add pure values and plans to Domain, ports/services to
Application, file-aware code to Infrastructure, and registrations to DependencyInjection.

**Rationale**: The object model already lives in Infrastructure, and the package shape is part of the
public product. A fifth assembly would require a new package, a new dependency edge and a new release
matrix without adding capability. The BCL covers compression, XML, hashing and CMS.

**Alternatives considered**: A `SharpCorePDF.Pdf` low-level package (rejected: fifth package and
duplicate object model); `System.Drawing`/SkiaSharp for drawing (rejected: native/platform dependency
and violates Principle I); PdfSharp/PdfPig as a dependency (rejected: violates the product’s reason to
exist and imports AGPL/commercial code).

### D4. One graphics/content core, two writers

**Decision**: Record drawing as typed operations and serialise them through one content-stream builder.
Use a full-rewrite writer for ordinary output and an incremental writer for signatures and multi-revision
preservation.

**Rationale**: Creation, overlays, annotations and form appearances are all content-stream producers. A
single builder prevents subtle differences between “authoring” and “editing”. A full rewrite cannot
preserve a prior signature, so the incremental writer is not optional.

**Alternatives considered**: Separate authoring and editing serialisers (rejected: drift and duplicated
fixes); always incremental (rejected: more complex output and no benefit for a new document); always
full rewrite (rejected: invalidates signatures).

### D5. Sign with BCL CMS, timestamp through a caller callback

**Decision**: Build detached CMS/PKCS#7 with `System.Security.Cryptography.Pkcs`, verify the digest and
signature with the BCL, and expose `IPdfTimestampProvider` for RFC 3161. The caller performs the network
exchange; the library parses/embeds the returned token. Report integrity and certificate trust
separately.

**Rationale**: The BCL supplies the cryptographic primitives without a native dependency. The
constitution forbids the library from opening a socket, so a timestamp client cannot live inside it.
Separating integrity from trust prevents the common and dangerous conflation of “the bytes are intact”
with “I trust the signer”.

**Alternatives considered**: BouncyCastle (rejected: new runtime dependency); built-in HTTP timestamp
client (rejected: violates no-network rule); report a single “valid” flag (rejected: hides the trust
decision and is unsafe).

### D6. Pass-through images first

**Decision**: Embed JPEG as `DCTDecode` when no colour transform/orientation change is needed; keep the
PNG `IDAT` predictor path; decode BMP and GIF to samples; refuse unsupported variants by name.

**Rationale**: Re-encoding a JPEG loses quality and changes bytes for no reason; the existing PNG path
already proves pass-through works. Refusal with a `PdfWarning`/exception is honest and testable.

**Alternatives considered**: Decode every image to raw samples (rejected: output size and quality loss);
add an imaging library (rejected: native dependency); support every filter (rejected: JPX/CCITT/JBIG2
are outside the common surface and can be added later).

### D7. Metrics-based greedy text layout, no shaping engine

**Decision**: Measure with the font’s own tables (`head`, `hhea`, `maxp`, `hmtx`, `cmap`), wrap greedily
to a width, and support leading and alignment. Refuse complex-script shaping rather than approximate it.

**Rationale**: PDFsharp’s own documentation says only basic text layout is supported. A shaping engine
(HarfBuzz) would be a native dependency and a much larger product. Deterministic greedy wrapping is
testable and covers reports, invoices and covers.

**Alternatives considered**: Full Unicode shaping (rejected for 1.1: native dependency and scope);
system font fallback (rejected: non-deterministic and violates no-system-font rule); no wrapping
(rejected: not PDFsharp-level).

### D8. PDF/A profiles are writer modes, validated externally

**Decision**: Implement PDF/A-1b, -2b and -3b as output profiles that embed fonts, add an output intent
and XMP, remove encryption/forbidden actions and refuse unsupported colour spaces. Validate every
fixture with veraPDF outside the build.

**Rationale**: PDF/A is a set of structural rules, not a runtime library. The writer can enforce what it
knows and warn about what it cannot; only an independent validator can support a conformance claim.

**Alternatives considered**: Claim conformance from the writer alone (rejected: unverifiable and
misleading); run veraPDF in CI as a hard dependency (rejected: Java/toolchain cost and the repo’s
external-validation convention); skip PDF/A (rejected: a standard reason teams choose PDFsharp).

### D9. Reading analysis is built on the existing scanner

**Decision**: Reuse `PdfTextScanner`, `PdfFontDecoder`, `PdfToUnicodeCMap` and the content scanner for
letters. Implement nearest-neighbour words, recursive XY cut, Docstrum-style segmentation and reading
order as pluggable stages. Export through `System.Xml`.

**Rationale**: The existing scanner already handles encodings, form XObjects and damaged documents. A
separate extraction engine would duplicate that work and produce a second interpretation of a file.
Pluggable stages let a caller choose raw content order (best for exact reconstruction) or a layout
heuristic (best for search).

**Alternatives considered**: Link PdfPig (rejected: dependency and licence); hard-code one segmentation
algorithm (rejected: different documents need different strategies); expose only block text (rejected:
does not reach letter-level parity).

### D10. Public object graph is read-only

**Decision**: Add `PdfObjectView` as a projection with node kinds, scalar values, references and
dictionary/array children. No public mutation; `PdfDocumentEditor` is the only write path.

**Rationale**: PdfPig exposes internal structure, but a mutable object graph would let callers break
cross-references, encryption and page-tree invariants. A read-only projection supports audits and keeps
one writer.

**Alternatives considered**: Make `PdfObject` public (rejected: mutable internal model, snapshot and
invariant risk); no object graph (rejected: not PdfPig-level); expose raw bytes only (rejected: the
caller has to parse PDF syntax, which is the job of the library).

### D11. Validate against independent tools, not against competitor packages

**Decision**: Use qpdf/pikepdf, pypdf, PyMuPDF, veraPDF, pyHanko, OpenSSL and fontTools. Do not add
PDFsharp or PdfPig packages to the solution or test project.

**Rationale**: The product promise is independence. Linking a reference implementation into tests can
mask a bug (both sides share an assumption), imports an AGPL/commercial licence into an MIT repository,
and turns a capability claim into “it matches this build of a competitor”. Public documentation plus
independent validators is a stronger and cleaner evidence chain.

**Alternatives considered**: Reference PDFsharp/PdfPig in a separate harness (rejected for 1.1:
licence and supply-chain cost); snapshot their output as golden files (rejected: golden files hide the
reason and become stale).

### D12. Release 1.1, additive on the 1.x line

**Decision**: Ship the parity surface as 1.1.0 because the public surface grows additively and no 1.0
signature is removed. Keep 1.0 open for fixes during the transition; mark future removals `[Obsolete]`
before removal.

**Rationale**: SemVer is part of the constitution. New types and optional parameters are a minor
change; the product-boundary change is recorded in the constitution at 2.0.0, which is a separate
document from the package version. A package major is reserved for a public API break.

**Alternatives considered**: 2.0 (rejected: overstates an additive API change); 1.0.x patch (rejected:
a patch cannot add a new public surface); never releasing the parity surface (rejected: hides the
capability gap).

## Open Questions Resolved Before Phase 1

- **Does PDFsharp render PDFs?** No. Its documentation describes creation/processing and GDI+-style
  drawing; MigraDoc renders its own document model. The repository’s Ghostscript converter is a test
  utility. Rendering is therefore not a parity requirement.
- **Does PdfPig edit forms or annotations?** No. Its README/wiki says forms are read-only and
  annotations cannot be edited. Those are PDFsharp-side requirements in this plan.
- **Does PdfPig render?** No. It exposes content; rendering is left to separate projects. Rendering
  remains a non-goal.
- **Does PDFsharp support signatures, forms and PDF/A?** Its source tree contains dedicated
  `Pdf.Signatures`, `Pdf.Forms`, `Pdf.Annotations` and `Pdf.PdfA` areas, so they are part of the
  reference surface. The plan matches the documented capability, not every edge case.
- **Can the library timestamp without a network?** Only through a caller-supplied token/provider. The
  plan uses `IPdfTimestampProvider` and keeps the socket out of the library.
- **Does “parity” require API compatibility?** No. The spec and plan define capability parity and
  explicitly exclude source/binary compatibility.
