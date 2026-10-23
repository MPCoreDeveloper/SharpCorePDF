# Changelog

What changed, from the point of view of someone who has to decide whether to install this version.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and the version numbers
follow [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html). A version gets an entry here
before it is tagged, not after: `docs/release-checklist.md` G3 refuses a release whose notes do not exist,
and a version with no notes is a version a consumer cannot evaluate.

**`1.0.0-preview.1` was published to nuget.org on 2026-10-15**, as four packages — `SharpCorePDF.Domain`,
`.Application`, `.Infrastructure` and `.DependencyInjection` — pushed by `publish.yml` from tag
`v1.0.0-preview.1`. The `SharpCorePDF` entry point did not exist in that tree, so the only id on the feed
that a consumer could have installed was a layer. `1.0.0-preview.2` and `1.0.0-preview.3` were bumped here
and never pushed, which is why this file keeps no entries for them: there is no consumer of a version no
feed ever served.

`1.0.0` is therefore the first version that changes the *shape* of the install rather than only the
contents of the layers, and the entry below is written against the published `preview.1` rather than
against an empty feed.

## [Unreleased]

### Added

- **An existing document can be edited into a new one.** `IPdfDocumentEditor`, resolved by
  `AddSharpCorePDF()`, applies an immutable `PdfEditPlan` to one source: `RotatePage` adds a quarter
  turn to a page, `DeletePage` removes it, `MovePage` changes its position, `DuplicatePage` copies it
  to another position, and `SetMetadata` overrides document information. Page numbers are one-based
  and are resolved against the state produced by the preceding operations, so a plan reads like a
  sequence of edits. Each operation is a value; the plan is validated before the source is read, and
  an operation that reaches past the last page fails with `PdfEditPlanValidationException` before any
  output is written. `PdfEditResult` reports the page count, bytes written, output version, elapsed
  time, the operations applied and the warnings the edit produced.
- **Pages can be cropped and resized without re-rendering their content.** `CropPage` sets an
  explicit `/CropBox` inside the media box: a box that reaches outside it is clamped and reported as
  `PdfWarningKind.PageGeometryNormalized`, and a box with no overlap is refused before any output is
  written. `ResizePage` changes `/MediaBox` and wraps the untouched original content streams in a
  graphics state (`q`, a `cm` transform, then `Q`), so `PdfPageResizeMode.Scale` scales the content
  to the new size and `PdfPageResizeMode.Preserve` keeps it at its original size and centres it. A
  crop box set before a resize is carried through the same transform.
- **Editing copies pages, it does not re-render them.** The engine writes a complete new document and
  copies every surviving page's content streams, resources, fonts, images and annotations verbatim;
  inherited `MediaBox`, `CropBox`, `Resources` and `Rotate` are materialised onto each page. Bookmarks,
  named destinations and form widgets that name a source page are redirected to the first surviving
  occurrence, or to the first page of the result when the named page did not survive. Page labels are
  dropped when an operation changes the page order or count, and an
  XMP packet is dropped when document information is overridden; both decisions are reported through
  `PdfWarningKind.PageLabelsDropped` and `PdfWarningKind.XmpPacketDropped` rather than silently
  producing a document whose metadata contradicts itself.
- **An edit is reproducible when its inputs are fixed.** `PdfEditOutputOptions` chooses the output
  version, metadata overrides, producer, modification date and document identifier. The file
  identifier is derived from the version, date, page count, identifier and applied operations, so a
  fixed `ModificationDate` and `DocumentId` make repeated edits of the same source byte-identical.
  The editing release reads encrypted sources through the existing standard security handler; the
  result is written in the clear unless `Encryption` is set (see below).
- **A page written from nothing.** `PdfDocumentBuilder` creates an in-memory document — one or more
  pages of any `PdfPageSize` — whose pages are drawn through `PdfPageCanvas`: `DrawText` sets a line in a
  standard or an embedded font at a baseline, in a top-left coordinate system with `y` downwards, and
  `DrawImage` places a PNG at an explicit size or at the size its `pHYs` chunk states. `Build()` returns
  a `PdfByteArraySource`, so the page can lead a `MergePlan` without a temporary file; `ToArray()` and
  `SaveAsync(Stream)` are the other two routes. `WithMetadata` writes the document information and
  `PdfAuthoringOptions` fixes the creation date and document identifier, which makes repeated builds
  byte-identical.
- **Two levels of font, neither of them a system font.** `PdfTypeface.Standard` selects one of the twelve
  standard PDF faces with `WinAnsiEncoding` and no font file; `PdfTypeface.FromTrueType` embeds a TrueType
  program the caller supplies, subsetted to the glyphs the pages draw, with a `/ToUnicode` map so the text
  — `é`, `è`, `à`, `ç`, `ô`, `ï` included — extracts again. Glyph numbers are preserved, unused outlines
  are left out, and the program is never looked up in the machine's font folder. A character the face
  cannot encode is refused with the character and code point in the message, rather than replaced.
- **PNG images without an imaging dependency.** Eight bit, non-interlaced PNGs of colour types 0, 2, 3,
  4 and 6 are embedded: an alpha channel becomes an `/SMask`, a palette with `tRNS` is expanded to RGB and
  a soft mask, a colour key becomes `/Mask`, and an opaque image keeps its original `IDAT` bytes with a
  PNG predictor instead of being recompressed. Interlaced and sixteen bit images are refused with an
  explanation; JPEG and other formats are out of scope. One image drawn on several pages is one object.
- **Reading what a document already contains.** `IPdfContentReader`, resolved by `AddSharpCorePDF()` and
  constructible by hand, answers the questions a caller asks before drawing anything: `ReadImagesAsync`
  returns every image a page range paints — one entry per object, carrying every page that draws it, with
  the bytes the file holds (a `DCTDecode` image is never re-encoded) and the area it occupies;
  `ReadAnnotationsAsync` returns comments, highlights, stamps and links with their geometry, their text
  and their target, resolving direct and named destinations; `ReadFormFieldsAsync` returns the field tree
  with fully-qualified names, types, values and every widget of each field; `ReadMetadataAsync` returns the
  XMP packet byte-exact with the `dc:`, `pdf:` and `xmp:` values parsed beside it; `ReadEmbeddedFilesAsync`
  returns the attachments with a distinct identifier per entry; `ReadOutlineAsync` returns the bookmark
  tree with its destinations resolved; and `ReadPageDetailsAsync` yields everything known about one page at
  a time, so a 200-page index never holds a report for the whole file.
- **One payload rule for every read.** A record carries its bytes inline only within
  `PdfEngineOptions.MaxInlineContentBytes` (new, default 256 KiB, `0` for metadata only); above it `Data`
  is `null`, `ByteLength` stays truthful and `IsBuffered` says so, with no warning because nothing failed.
  `CopyImageDataAsync`, `CopyImageMaskDataAsync` and `CopyEmbeddedFileDataAsync` write such a payload to a
  stream the caller supplies through a pooled 64 KiB buffer, and `ReadImageDataAsync` / `SaveImageDataAsync`
  and their embedded-file twins are the convenience members beside them. A `ReadOnlyMemory<byte>` that
  escapes a call never aliases a rented buffer.
- **Reading the text the pages draw.** `IPdfContentReader.ReadTextAsync` returns `PdfTextBlock` records in
  the order a reader would take them — down the page, then along the line — rather than in the order the
  content stream happens to draw them. A block carries its `Text`, `LineCount`, page and `BoundingBox`, and
  holds the `PdfWord` records it is built from, each with a baseline, a font size and a font name and the
  `PdfGlyph` records behind it, so a caller that needs a coordinate never parses the page a second time.
  Words end where the document draws a space and where a `TJ` adjustment is wide enough to stand in for
  one; `Tc`, `Tw`, `Tz`, `TL`, `Ts`, `Td`, `TD`, `Tm`, `T*` and `q`/`Q` are all honoured, so a line inside
  a scaled or rotated form lands where a viewer draws it. A simple font is read through its `/Encoding`,
  its `/Differences` and the standard glyph names, a composite font through its `/ToUnicode` map, and the
  width of a code comes from `/Widths` or `/W` with the fallbacks the specification states. One page
  overload, `ReadTextAsync(source, page)`, reads a single page without a range.
- **Text is read or reported, never guessed.** A glyph whose code no font entry maps is skipped and its
  font named in one `TextUnreadable` warning per page, so a document whose encoding this library cannot
  follow reads as fewer words plus a remark instead of a page of plausible nonsense, and a page whose font
  is unreadable still leaves the pages around it readable.
- **Eleven new `PdfWarningKind` members** — `ContentStreamUnreadable`, `ImageFilterUnsupported`,
  `ImageDataUnreadable`, `XObjectResourceMissing`, `NamedDestinationUnresolved`, `EmbeddedFileUnreadable`,
  `EmbeddedFileNameCollision`, `XmpPacketMalformed`, `FormFieldUnreadable`, `AnnotationUnreadable` and
  `TextUnreadable` — so a filter the library does not decode, a damaged image, a name that does not
  resolve, a malformed XMP packet or a font it cannot read is reported as data instead of being skipped in
  silence.
- **The layout comes out with the text.** `IPdfLayoutReader`, resolved by `AddSharpCorePDF()` and
  constructible by hand, reads a page the way a search or an analytics pipeline needs it:
  `ReadLettersAsync` returns one `PdfLetter` per glyph in content order, with its Unicode value, its font
  size, the font the document declares, its box, its baseline start and end and the direction that
  baseline runs in; `ReadWordsAsync` groups those letters into `PdfWord` records that keep the letters
  they were grouped from, with the order of the page as the default and `PdfWordExtractionStrategy.Raw`
  for a caller that needs the order of the producer; and `ReadHyperlinksAsync` returns the clickable area
  of every link with either the URI it opens or the destination it jumps to. `PdfWordExtractionOptions`
  states both rules in ems — a quarter em as a space, half an em as one line — and refuses a negative
  distance or a strategy this release does not implement when it is created, before a document is
  opened. Whitespace letters can be kept in the word they follow, so the words of a page concatenate back
  to the text it draws, and a letter whose baseline runs towards the left is read in its own direction
  with a box that covers the area between its two baseline points.
- **A page's blocks, and the order a reader takes them in.** `IPdfLayoutReader.ReadBlocksAsync` groups the
  words of a page into `PdfTextBlock` records — the text, the words it keeps, the area it covers and its
  one-based position on the page — by the segmentation `PdfLayoutOptions` selects: `Raw`, which reproduces
  the producer's drawing order and is the baseline a comparison starts from; `NearestNeighbour`, which
  groups the lines of a paragraph; `RecursiveXYCut`, the default, which splits a region along the widest
  vertical gutter and then the widest horizontal one, so two columns and a heading are found without
  knowing what a column is; and `Docstrum`, which measures the page's own leading instead of assuming it.
  The reading order is chosen separately, because a page can be segmented correctly and still be listed in
  the wrong order: `ColumnAware`, the default, reports the columns of a page left to right and the blocks
  of a column top to bottom, and `Raw` keeps the order the segmentation produced. The segmentation and the
  order travel with the result, so a page that yielded no block is not the same answer as a page that was
  never segmented; `PdfLayoutOptions.IncludeDecorations` decides whether a block that carries no letter and
  no digit — a rule drawn as a run of hyphens, a lone bullet — is reported; and a strategy only groups the
  letters and words a read produced, never changing what one of them says.
- **The reading of a page written down for another tool.** `IPdfLayoutReader.ExportAsync` writes what a block
  read produced as `PdfLayoutFormat.Alto`, `PageXml`, `Hocr` or `SvgText`, straight into a stream the caller
  supplies: ALTO 4 with a `Page` per page, a `TextBlock`, a `TextLine` and a `String` per word; PAGE XML with
  a `TextRegion` per block, a `TextLine` per line and the reading order that names them; hOCR as XHTML with
  `ocr_page`, `ocr_carea`, `ocr_par`, `ocr_line` and `ocrx_word` elements, each carrying its `bbox`; and SVG
  with one `text` element per word on the baseline it is drawn on. Coordinates are the page's own numbers
  with the origin moved to the top left corner and the y axis turned down, which is the convention of every
  one of the four formats; text is escaped for the document it lands in, and a character XML cannot carry
  becomes the replacement character instead of aborting the write. A page that draws no text still produces
  its page element, a range is written in ascending page order, and no document states a timestamp or a
  random identifier, so the same read produces the same bytes on every run. The call returns the blocks it
  wrote, with the segmentation and the order that produced them and the warnings the read made, so an export
  of a page whose font could not be mapped is not silent. A destination that cannot be written and a format
  this release does not produce are both refused before the document is opened, and the exports describe text
  only: `PdfLayoutOptions.IncludeImages` is reserved for the image regions a later release places beside it.
- **A document can be signed without rewriting a byte that is already signed.** `IPdfDocumentSigner` —
  resolved by `AddSharpCorePDF()` and constructible by hand like every other engine port — appends one
  revision to a source and returns a `PdfSignatureInfo` read back from the file it wrote: the field, the
  filter and sub-filter, the digest, the signer, the byte range and the revision the signature covers.
  The CMS is built here rather than by `SignedCms`, because a detached signature over a reserved byte
  range needs the digest of everything except its own placeholder, the signed attributes CAdES names, and
  a size known before the value exists: the writer reserves `ByteRange` and `Contents`, hashes the
  revision with the gap removed, patches the range, and only then writes the signature into it. The
  certificate's key decides the algorithm — RSA PKCS#1 v1.5 or ECDSA — and the options decide the digest.
  A certificate with no private key, an encrypted document and a revision too small for the chain are
  refused before anything is written, so a half-signed file cannot exist; a visible appearance writes a
  form XObject, a widget and an `/Annots` entry in that same revision, and `/AcroForm` is added when the
  document had none.
- **A signature can carry a timestamp the caller obtained.** `PdfSignatureOptions.TimestampProvider` is
  the whole of it: the library hands the caller's `IPdfTimestampProvider` the digest of the signature
  value and embeds the RFC 3161 token it answers with as the `signatureTimeStampToken` unsigned
  attribute. Nothing here opens a socket, and a provider that throws stops the signature instead of
  leaving a document that only looks signed. The token travels unverified, and says so: a token that
  cannot be read is a `TimestampInvalid` problem rather than a claim about its time, and
  `PdfTrustOptions.CheckTimestampTrust` is reserved for the release that checks the authority's own
  signature.
- **Every signature in a document is checked, and the two questions are answered apart.**
  `IPdfDocumentSignatureValidator` finds each signature field in the form tree and on the page widgets,
  reads its byte range and its CMS, and reports one `PdfSignatureValidation` per signature: `Integrity` —
  the digest the CMS carries matches the bytes the range covers — and, only when the caller names anchors
  through `PdfTrustOptions`, `Trust` — the chain leads to one of them. They are separate results because
  they are separate failures: a document changed after it was signed is not the same defect as a
  certificate that leads nowhere, and `IsValid` speaks only about integrity, which is the half this
  library can decide on its own. No machine trust store is consulted, no certificate is fetched and no
  authority is asked whether one was revoked; a placeholder signature, an unreadable structure or a range
  that does not reach the end of the file is `Unknown`, never `Valid`.
- **A document can be written for archival delivery.** `IPdfAWriter`, resolved by `AddSharpCorePDF()`
  and constructible by hand, writes a complete PDF/A-1b, -2b or -3b document: the version the profile
  names, an uncompressed UTF-8 XMP packet carrying `pdfaid:part` and `pdfaid:conformance` with the
  document information mirrored into it, an `/N 3` output intent built from a generated sRGB colour
  profile, and `/NeedAppearances false` on a form. `PdfEditOutputOptions.PdfAProfile` routes an ordinary
  edit through the same writer. The analysis runs before a byte is written: a font that is not embedded,
  a CID subset that states no `/CIDSet` for 1b, transparency, `JPXDecode`/`LZWDecode`, optional content or
  an attachment the profile forbids, an annotation without an appearance stream, a JavaScript/`Launch`
  and similar action, or an external file stream is refused with `IsWritten = false`, a named
  requirement and an untouched destination — never written with a label it does not deserve. What can be
  removed without changing a page is removed and reported as `PdfWarningKind.PdfAFeatureRemoved`.
  `docs/tools/validate-pdfa.py` wraps veraPDF; the release is recorded in `docs/parity.md` on veraPDF
  1.30.3 reporting `PASS` for all three profiles of both an authored page and a real
  "Microsoft: Print To PDF" document.
- **An edit can write an encrypted result.** `PdfEditOutputOptions.Encryption` applies the same standard
  security handler a merge uses — AES-256 by default, AES-128 and RC4 available — and the identifier of
  the result keys it. The engine refuses a plan that asks for a PDF/A profile and encryption together,
  before the source is read, and with `VerifyOutput` on it reads its own encrypted output back with the
  password it was written with.
- **Embedded TrueType subsets now state a `/CIDSet`.** ISO 19005-1 requires the set of CIDs a subset
  describes, and veraPDF failed a PDF/A-1b conversion without it. The writer emits one that names every
  CID the preserved numbering makes present, which also satisfies the PDF/A-2 rule for subsets that carry
  a set.

### Fixed

- **Images decode to the samples their filter names, and a mask can be fetched.** A `/Predictor` image is
  now inflated and undone one row at a time instead of into a single buffer, so an image whose stored form
  is small but whose samples are large is counted rather than materialised and the copy route writes rows
  as it produces them. An inline image's data ends where its header says it does — the separator after `ID`
  is consumed once and `EI` is found even when a delimiter follows it — so a sample stream containing a
  space followed by `EI` is no longer cut short, and an image with no `EI` at all keeps what it read and
  raises `ImageDataUnreadable` instead of ending in silence, and a page's streams are separated only
  *between* streams, so an unterminated image ends at the last byte the page holds. An inline dictionary
  whose value is a name (`/CS /G`) keeps its colour space instead of losing it and misreading the entry
  after it. Three smaller corrections ride with them: `CopyImageMaskDataAsync` fetches a soft or explicit
  mask by the `(ImageId, Kind)` its record carries and refuses a colour key mask, which has no payload, by
  name; `PdfImageInfo.IsInline` tells an inline image from an object; and every image warning names the
  source and the page, plus the image wherever the failure belongs to one, so a 200-page report can be
  triaged.
- **The examples no longer contain a password.** The encryption example in `examples/` wrote a literal user
  password and a literal owner password into the document it demonstrates — the kind of constant a static
  analysis scanner reports as a hardcoded credential, and the kind a reader copies into their own code. Both
  are now made per run, the user password is printed so the document can still be opened, and the example
  opens it with the value it just wrote. No package changes with this; the finding is what turned the
  project's security rating red.

### Changed

- **What 1.1 delivers, and what it defers to 1.2.** The capability described in this entry is what
  `1.1.0` ships: page creation with standard and embedded fonts, PNG images, the path writer, every page
  operation of the editor (rotate, delete, move, duplicate, reorder, crop, resize, extract, split, import,
  insert, append and metadata overrides), reading with layout analysis and the four text exporters,
  digital signatures and PDF/A-1b, -2b and -3b output. Text measurement and word wrap,
  JPEG/BMP/GIF, overlays, annotation and form authoring, and incremental updates are **not** in `1.1.0`.
  They stay in the parity matrix as `1.2` rows with a named task rather than disappearing, so a caller can
  see the gap instead of discovering it: [`docs/parity.md`](docs/parity.md) names the release of every
  row, and the specification carries the dated scope amendment behind that split.
- **The product boundary now includes creation and editing.** Constitution 2.0.0 and
  [ADR-001](docs/adr/0001-pdf-platform-boundary.md) define SharpCorePDF as a dependency-free PDF
  creation, editing and reading library. The 1.1 target is capability parity with PDFsharp 6.5 for
  creation and processing and PdfPig 0.1.x for reading and layout analysis; rendering, conversion,
  OCR, conformance validation, services and CLIs stay outside the product. This release implements
  page-assembly editing only — page content, annotations and forms are still copied, not rewritten.
- **A release waits for the quality gate, on the commit being released.** `publish.yml` reads the SonarCloud
  verdict in a `sonar-gate` job that runs before the build, before the pack and before the OIDC exchange, and
  stops when the gate is red, when the snapshot it judged carried no coverage report, or when the newest
  analysis of `main` is of a different commit than the tag. It changes when a version appears on the feed,
  not what is in it; the rule it adds is that a tag goes on the commit whose analysis is the newest on `main`.

## [1.0.0] - 2026-10-17

### Added

- **`SharpCorePDF`, the package to install.** The four layers stay separate assemblies with separate API
  snapshots, but a consumer no longer has to work out which of them to reference: `SharpCorePDF` is a
  meta-package whose only content is a dependency on `SharpCorePDF.Domain`, `.Application`,
  `.Infrastructure` and `.DependencyInjection`, and `dotnet add package SharpCorePDF` is the whole
  install. The four remain installable on their own.
- **`NOTICE`**, stating in one page who holds the copyright, what third-party material exists (no
  third-party source code; `Microsoft.Extensions.*` in `SharpCorePDF.DependencyInjection` only; test
  fixtures generated by `docs/tools/make-fixtures.py`) and what the name is not.
- **`SECURITY.md`**, `CODE_OF_CONDUCT.md` and `CONTRIBUTING.md` at the repository root: a private
  reporting route with a 7-day acknowledgement, Contributor Covenant 2.1 with two enforcement channels,
  and the build, test and review bar a change has to clear.
- **`.github/`**: issue forms for a defect and a feature request, a pull request template, and a
  Dependabot configuration for the NuGet packages and the workflow actions.
- **`SonarAnalyzer.CSharp` in the build** (`src/Directory.Build.props`, dev-time only). The rules
  SonarCloud applies now run in `dotnet build`, at the line that causes them. The three rules this
  repository answers with a decision rather than a change (`S101`, `S1694`, `S4790`) are silenced per file
  in `src/.editorconfig` with the reason, and their lines carry `// NOSONAR`.
- **The public surface is an approved snapshot** (`Microsoft.CodeAnalysis.PublicApiAnalyzers`, dev-time only).
  Each project declares what it exports in `PublicAPI.Shipped.txt` and queues additions in
  `PublicAPI.Unshipped.txt`, and RS0016/RS0017 are build errors: a public member the snapshot does not list,
  or an entry that outlives its member, stops the build rather than the next release.

### Changed

- **`publish.yml` checks the entry point differently from the layers.** Every package still has to carry
  `lib/net10.0` and `lib/net11.0` and the README nuget.org renders; `SharpCorePDF` carries no assemblies
  by design, so it is held to the opposite rule instead: it must depend on all four layers.
- **The trusted publishing policy needs two subjects, not one.** `SharpCorePDF.*` matches the four layers
  and does not match `SharpCorePDF` itself, which has no dot after the product name; a policy holding only
  the glob pushes four packages and refuses the fifth.
- **`ci.yml` enforces the SonarCloud quality gate instead of reporting it.** The scan passes
  `sonar.qualitygate.wait` to `begin` — the `end` phase rejects every property but the token — so a failing
  gate fails the run, and the gate's coverage condition reads the report this run produced:
  `dotnet-coverage collect` measures the net11.0 leg, and the Visual Studio XML
  it yields is named exactly (`artifacts/coverage/net11.0.xml`) instead of by a glob. Coverage is measured
  on every run rather than only when a SonarCloud token exists, and every action is pinned to a commit hash
  rather than a tag.
- **Codecov is gone, and with it the second coverage report.** One service measuring the number is the
  arrangement that cannot disagree with itself: `codecov.yml` asked for 90% on the project and on the
  change, while SonarCloud was already importing the same measurement, so the gate holds the number and
  the cobertura conversion that only Codecov read is no longer produced. The 90% is a condition to enter on
  the quality gate on sonarcloud.io — above Sonar's 80% default on new code, and the only place it can be
  enforced once the file that declared it is gone — and `README.md`, `CONTRIBUTING.md` and
  `docs/release-checklist.md` say so instead of pointing at a file in the tree.
- **The SonarCloud findings are fixed in the source, not silenced**: loops that read better as
  `Where`/`Select`/`Distinct`, a nested ternary in the AES object-key derivation, two helpers moved into
  the walker that uses them, and `PdfReference` now implements `IEquatable<PdfReference>`, so the
  `Equals` overload it already carried is the one a generic comparer calls.

### Known limits at 1.0.0

Two things are known and deliberately not promised: there is no symbol package yet, so a debugger shows no
source for a stack trace, and nothing is code-signed. Both are gates in the release checklist rather than
intentions. Neither affects what the packages contain or what the assemblies do.

The shipped leg is two, not one: `lib/net10.0` and `lib/net11.0` carry the same API, as `preview.1`
already advertised, because a 1.0.0 that narrowed to a single framework would withdraw a target the feed
has promised since 2026-10-15 (`docs/release-checklist.md` G2). The public surface is the one frozen by
`PublicAPI.Shipped.txt` — additions after this version go through `PublicAPI.Unshipped.txt` and a minor
version, not through a silent change to a shipped signature.

## [1.0.0-preview.3], [1.0.0-preview.2]

Never pushed. Both names exist only as values the repository's `<Version>` passed through, so no feed ever
served them, nothing was downloaded from them, and skipping them broke no promise to anyone.

## [1.0.0-preview.1] - 2026-10-15

The first and — until 1.0.0 — only published version: `SharpCorePDF.Domain`, `.Application`,
`.Infrastructure` and `.DependencyInjection` at `1.0.0-preview.1`, from tag `v1.0.0-preview.1`. No entry
was written here for it at the time, and this heading does not invent one after the fact; it records that
the four ids are live and immutable, which is why their next version is `1.0.0` and not a re-cut of
`preview.1`.

