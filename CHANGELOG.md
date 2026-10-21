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

