# 8. Inspection

`IPdfInspector` answers "what is this file?" without changing it and without producing a merge. It is the
**summary** read side of the library: it describes the document, and the content reader later in this
chapter reads what the pages carry. Both use the same reader as the merger, so what you inspect is what you
will merge.

```csharp
var info = await inspector.InspectAsync(
    new PdfFileSource("report.pdf"),
    password: null,
    cancellationToken: cancellationToken);
```

The method takes any `IPdfContentSource`, not just a file, so the same call works for a byte array or a
stream. There is no inspection overload that takes a path directly; wrap the path in a `PdfFileSource`,
which also supplies the display name used in messages.

## The document model

`PdfDocumentInfo` is an immutable record. The properties you are most likely to use:

| Property | Type | Meaning |
| --- | --- | --- |
| `SourceName` | `string` | The display name of the source |
| `Version` | `PdfVersion` | The version from the document header |
| `PageCount` | `int` | Number of pages, or `0` when the page tree is unreadable |
| `IsReadable` | `bool` | Whether the structure could be read at all |
| `IsEncrypted` | `bool` | Whether the document declares encryption |
| `RequiresPassword` | `bool` | Whether a password is what stands in the way |
| `SecurityHandlerRevision` | `int?` | The revision of the security handler, or `null` |
| `Metadata` | `PdfMetadata` | Title, author, subject, keywords, dates and custom entries |
| `Pages` | `IReadOnlyList<PdfPageInfo>` | One entry per page, in document order |
| `HasOutlines` | `bool` | Whether the document has bookmarks |
| `OutlineItemCount` | `int` | Bookmarks, counting nested items |
| `EmbeddedFileCount` | `int` | Number of attachments |
| `HasAcroForm` | `bool` | Whether the document has an interactive form |
| `SourceLengthBytes` | `long?` | Size of the source when known |
| `Warnings` | `IReadOnlyList<PdfWarning>` | Non-fatal remarks about the document |

Two computed properties save a loop: `FirstPageSize` is the first page's size or `null` for an empty
document, and `HasUniformPageSize` is `true` when every page has the same size — convenient for
deciding whether a merge will produce a uniform document.

`PdfPageInfo` describes one page:

| Property | Type | Meaning |
| --- | --- | --- |
| `Number` | `int` | The one-based page number |
| `Size` | `PdfPageSize` | The size as stored, before rotation |
| `Rotation` | `PageRotation` | How a viewer rotates the page before displaying it |
| `HasContent` | `bool` | Whether the page has a content stream |
| `AnnotationCount` | `int` | Number of annotations on the page |
| `DisplaySize` | `PdfPageSize` | `Size` with the rotation applied |

`PdfPageSize` is a value object with `WidthPoints` and `HeightPoints` as the source of truth, plus the
millimetre and inch conversions, `IsLandscape`, `IsPortrait` and the predefined `A4`, `A5`, `A3`,
`Letter` and `Legal` sizes. Use `DisplaySize`, not `Size`, when you care about what a reader sees: a
landscape page is frequently stored as a portrait page plus a 90-degree rotation.

```csharp
Console.WriteLine($"{info.SourceName}: PDF {info.Version}, {info.PageCount} page(s)");

if (info.FirstPageSize is { } size)
{
    Console.WriteLine($"First page: {size.WidthMillimeters:0} x {size.HeightMillimeters:0} mm");
}

if (!info.HasUniformPageSize)
{
    var sizes = info.Pages
        .Select(page => page.DisplaySize)
        .Distinct()
        .Select(size => size.ToString());

    Console.WriteLine($"Mixed page sizes: {string.Join(", ", sizes)}");
}

Console.WriteLine(info.HasOutlines
    ? $"{info.OutlineItemCount} bookmark(s)"
    : "No bookmarks");
```

## What inspection is not

Inspection reads structure, not content. It does not read the text a page draws, render it, count words or
check that a font is embedded — the content reader later in this chapter does that reading. `HasContent`
says only that a content stream exists, not that it draws anything visible. `AnnotationCount` counts
annotations, not the form fields they belong to.

It is also not free. The inspector parses the document, including its page tree, so the cost is close
to reading the same document for a merge. Calling it on every request to display a page count doubles
the work; if you need both, merge and read the count from `MergeResult.PageCount` instead, or cache the
`PdfDocumentInfo` returned by one inspection.

## Encrypted and damaged documents

Inspection has a different failure contract from merging, and the difference is deliberate.

- An **encrypted document whose encryption stayed closed** does not throw. You get a
  `PdfDocumentInfo` with `IsEncrypted = true`, `IsReadable = false`, `RequiresPassword` telling you
  whether a password would help, and a warning explaining what happened. `PageCount` is zero and
  `Pages` is empty, but `SourceName`, `SecurityHandlerRevision` and `Warnings` are populated.
- A **malformed document that is not a readable PDF** throws `PdfSourceFormatException`. There is no
  partial result to return, because the object structure could not be established.
- **A document that is readable but damaged** does not throw. The reader repairs what it can — rebuilds
  a broken cross-reference table, skips an unreadable page-tree branch, assumes A4 for a page without a
  media box — and reports each repair as a warning.

```csharp
var info = await inspector.InspectAsync(new PdfFileSource(path), password);

if (!info.IsReadable)
{
    Console.WriteLine(info.RequiresPassword
        ? "The document is encrypted; supply the user or owner password."
        : "The document is encrypted with a security handler this library does not implement.");
}
```

`RequiresPassword` is about the *user* password. A document that opens without a password but is
restricted against editing has `IsEncrypted = true`, `IsReadable = true` and
`RequiresPassword = false`; its restrictions do not block inspection or merging. `PdfDocumentInfo`
does not expose the permission flags themselves — inspect the encryption dictionary with another tool
if you need to display them.

## Reading what the pages contain

Inspection describes the document. `IPdfContentReader` reads the content it already carries — images, text,
comments, the form, the bookmarks, the attachments and the XMP packet — and it changes nothing: no source
is modified or disposed, and nothing is opened that you did not supply.

```csharp
using SharpCorePDF.Application.Extensions;

var reader = provider.GetRequiredService<IPdfContentReader>();
var source = PdfFileSource.From("report.pdf");

var images = await reader.ReadImagesAsync(source, PageRange.FromTo(1, 10), cancellationToken: ct);

foreach (var image in images.Items)
{
    Console.WriteLine(
        $"image {image.Id} on page(s) {string.Join(", ", image.Pages)}: "
        + $"{image.Width}x{image.Height}, {image.ByteLength} bytes, inline {image.IsBuffered}");
}
```

| Method | Answers |
| --- | --- |
| `ReadImagesAsync` | One entry per distinct image object the range paints: every page that draws it, the position of the first draw, geometry, filter, the soft or explicit mask beside the samples, and — when the payload was decoded — the pixels themselves as `DecodedPixels` in `Gray8` or `Rgb24`. |
| `CopyImageDataAsync` | Writes the bytes of one image to a stream you supply. |
| `CopyImageMaskDataAsync` | Writes the bytes of one image's soft or explicit mask to a stream you supply; a colour key mask has no bytes and is refused by name. |
| `ReadAnnotationsAsync` | Every annotation with its page, subtype, rectangle, flags, text, author, date, appearance and, for a link, the resolved `Target`. |
| `ReadFormFieldsAsync` | Every field of the interactive form with its fully-qualified name (`person.name`), type, value, default value, flags and widgets. |
| `ReadOutlineAsync` | The bookmark tree in document order, with each item's title, open state, resolved destination and children. |
| `ReadEmbeddedFilesAsync` | One entry per attachment in document order, each with a distinct `Id`, its name, MIME type, relationship, date and length. |
| `CopyEmbeddedFileDataAsync` | Writes the bytes of one attachment to a stream you supply. |
| `ReadMetadataAsync` | The XMP packet byte-exact in `RawPacket`, its UTF-8 text, and the `dc:`, `pdf:` and `xmp:` values parsed into `Properties`. |
| `ReadTextAsync` | The text a page range draws: one `PdfTextBlock` per paragraph, in reading order, carrying its text, line count, bounding box and the words and glyphs it is made of. |
| `ReadPageDetailsAsync` | An `IAsyncEnumerable<PdfPageDetail>`: one page at a time, carrying its images, annotations, links, form fields and own warnings. |

The interface has exactly those eleven members and no overloads. The convenience members in
`SharpCorePDF.Application.Extensions` sit beside it for the common shapes: `ReadImagesAsync(source, page)`,
`ReadTextAsync(source, page)` and `ReadAnnotationsAsync(source, page)` for one page,
`ReadImageDataAsync` / `ReadEmbeddedFileDataAsync` for a payload as a `ReadOnlyMemory<byte>`,
`SaveImageDataAsync` / `SaveEmbeddedFileAsync` for a payload written to a file, and
`ReadPageDetailAsync(source, page)` for a single `PdfPageDetail?`.

## The result shape

Every read except `ReadMetadataAsync` and `ReadPageDetailsAsync` returns `PdfReadResult<T>`:
`Items` (never null), `Warnings` (empty when nothing went wrong) and a derived `Count`. The two states a
caller has to tell apart are:

* **nothing found** — `Items` is empty and `Warnings` is empty. The document genuinely has no image, no
  annotation, no attachment or no outline.
* **nothing could be read** — `Items` is empty and `Warnings` is not. Something was there and could not be
  returned; the kind says what.

A partial read is the normal case: one damaged image adds an `ImageDataUnreadable` warning and the other
images of the range are still returned. `ReadMetadataAsync` returns `PdfXmpResult`, whose `Metadata` is
`null` when the document has no packet, and whose malformed packet is a non-null `Metadata` with
`RawPacket` intact, empty `Properties` and one `XmpPacketMalformed` warning. A page enumeration reports a
damaged page through that page's own `Warnings`, so a bad page never abandons the pages after it.

## Payloads and the inline budget

A record carries its bytes inline only when they fit `PdfEngineOptions.MaxInlineContentBytes`, 256 KiB by
default (`0` means metadata only). Above the budget `Data` is `null`, `IsBuffered` is `false` and
`ByteLength` still tells the truth — including when the payload was withheld rather than missing. No
warning is raised: withholding bytes is the rule working, not a failure.

```csharp
var large = images.Items.First(image => !image.IsBuffered);

await reader.CopyImageDataAsync(source, large, destinationStream, cancellationToken: ct);
```

`CopyImageDataAsync`, `CopyImageMaskDataAsync` and `CopyEmbeddedFileDataAsync` run through one bounded
buffer, so the peak working set does not grow with the payload; they flush the destination and leave it
open, because they did not open it.
`ReadImageDataAsync` and `ReadEmbeddedFileDataAsync` materialise the payload and are only appropriate when
its size is known to fit in memory; `SaveImageDataAsync` and `SaveEmbeddedFileAsync` are the copy route with
the file opened for you. A `ReadOnlyMemory<byte>` that escapes a call never aliases a rented buffer.

### Pixels

`Data` is the payload as the document stores it, which for most images is samples behind a compression
filter. `DecodedPixels` is the same samples already unpacked — tightly packed, top row first, one byte per
sample — so a caller that wants to look at a picture does not write a decoder first. `PixelFormat` names the
layout: `Gray8`, one channel, or `Rgb24`, three interleaved.

```csharp
foreach (var image in images.Items)
{
    if (image.DecodedPixels is not { } pixels)
    {
        continue;                       // a payload, not pixels — see the table below for why
    }

    var channels = image.PixelFormat == "Rgb24" ? 3 : 1;
    var stride = image.Width!.Value * channels;
    var first = pixels.Span[..stride]; // the top row, one byte per sample
}
```

The pixels are present only when nothing is left to interpret: the library decoded the payload itself, the
document states the width, the height and eight bits per sample, the colour space is one whose samples are
its channels (`DeviceGray`, `CalGray`, `G`, `DeviceRGB`, `CalRGB`, `RGB`), and the payload is exactly
`Width × Height × channels` bytes. That last one is an invariant a caller can check instead of trusting, and
it is why an image whose geometry disagrees with its data is reported rather than reinterpreted.

Three cases yield `null`, and the record always states which one it is:

| Case | What the entry says | Why no pixels |
| --- | --- | --- |
| A `DCTDecode` image | `IsDecodable` true, `Filter` `DCTDecode`, `PixelFormat` null | The bytes are already a JPEG, which a caller can read; unpacking it here would mean shipping a second decoder. |
| A filter the library passes through — `JPXDecode`, `JBIG2Decode` and the rest | `IsDecodable` false, `Filter` names it | Nothing was decoded, so there are no samples to hand back. |
| Another bit depth, a palette, a `CMYK` or an `ICCBased` space, or a geometry the document does not state | `BitsPerComponent` or `ColorSpace` states which | Pixels are eight bit and channel-ordered by contract; converting or guessing would hide the mismatch from the caller. |

`DecodedPixels` is subject to the same inline budget as `Data`: when a payload is withheld, the pixels are
`null` as well, because they are the payload. Nothing is resampled, colour-converted or rotated here, and
nothing in this library renders a page to a raster — Chapter 20's exports describe a page rather than paint
it. This surface exists so that text extraction, an OCR pipeline or a checksum can see the samples without
carrying a decoder of its own.


`ReadPageDetailsAsync` is how an index is built: it parses and decodes one page, yields its detail, and only
then reads the next, so a 200-page document costs the pages consumed rather than the document.

```csharp
await foreach (var page in reader.ReadPageDetailsAsync(source, PageRange.All, cancellationToken: ct))
{
    index.Add(page.PageNumber, page.DisplaySize, page.Links.Count);

    if (index.Count == 50)
    {
        break;   // abandonment stops the walk; the remaining pages are never read
    }
}
```

`DisplaySize` is `Size` with `Rotation` applied, which is the size a viewer shows. Abandoning the loop
disposes the enumerator and with it the read, so a caller that has seen enough does not pay for the rest.

## Reading the text

`ReadTextAsync` answers "what does this page say?" It returns one `PdfTextBlock` per paragraph, ordered the
way a reader takes them: blocks down the page, words along the line. The order comes from where the glyphs
land, not from the order the content stream draws them, so a page whose header is painted last still reads
with the header first, and a stream that zig-zags across a page comes back in the order the eye follows
rather than in the order of its operators.

```csharp
var text = await reader.ReadTextAsync(source, PageRange.All, cancellationToken: ct);

foreach (var block in text.Items)
{
    Console.WriteLine($"page {block.PageNumber}, block {block.ReadingOrder}: {block.Text}");
}
```

| Record | Carries |
| --- | --- |
| `PdfTextBlock` | `PageNumber`, `ReadingOrder` (one-based, restarting on every page), `Text`, `LineCount`, `BoundingBox` and `Words` |
| `PdfWord` | `Text`, `PageNumber`, `BoundingBox`, `Glyphs`, and from the first glyph its `FontName`, `FontSize` and `BaselineY` |
| `PdfGlyph` | The decoded `Text`, `PageNumber`, `X`, `Y`, `Width`, `FontSize`, `FontName` and `BaseFont` as drawn |

`Text` is the block as plain text: its lines joined with a newline and each line's words joined with a
single space — the form a search box, a translation call or a `LIKE` clause wants. `Words` and `Glyphs`
carry the geometry for a caller that has to draw something back on the page or compare positions.

Three rules decide the grouping, all stated in ems so a 6pt footnote and a 30pt title are grouped alike:

* **glyphs to words** — a whitespace glyph ends the word, and so does a gap wider than a fifth of an em;
  a kerning overlap does not, so a word drawn one glyph at a time is still one word;
* **words to lines** — words share a line when their baselines are within a quarter of an em and their
  boxes reach across to each other; two runs on one baseline far apart stay two lines, which is how two
  table cells or two columns keep their places;
* **lines to blocks** — a line continues the block above when it drops by no more than 1.8 em and starts
  within 2 em of where that block started. A wider gap is the blank between paragraphs; a start further
  right is a column of its own.

That is a reading of the page, not a claim about its structure. Nothing here knows what a heading is, and
a table comes back as one block per row. A two-column page comes back as blocks that alternate between the
columns, one line at a time, because the lines of the two columns sit at the same heights. When a caller
knows better, the glyph coordinates are enough to sort or merge the blocks again — no richer rule is
attempted, because guessing would be a layout engine dressed as a reader.

The text state is followed in full: `Tf`, `Tc`, `Tw`, `Tz`, `TL`, `Ts`, `Td`, `TD`, `Tm`, `T*`, `TJ` and
`q`/`Q`, with `cm` and a form's `/Matrix` applied on top, so a glyph lands where a viewer draws it inside
a scaled, rotated or nested form. A `TJ` adjustment is a word break when it is wider than the same fifth
of an em and a kern when it is narrower.

Characters come from the font, never from the raw code byte. A simple font is read through its `/Encoding`
(`WinAnsiEncoding`, `MacRomanEncoding` or `StandardEncoding` by default) and its `/Differences`, which name
a glyph for individual codes — the standard glyph names, including the Latin-1 accented set such as
`eacute`, are understood. A composite font is read through its `/ToUnicode` map. Widths come from
`/Widths` with `/FirstChar`, or from `/W` and `/DW` for a composite font, with `/MissingWidth` as the
fallback. When none of these answers for a code, the glyph is skipped and the page gets one
`TextUnreadable` warning naming the font: a document in an encoding this library cannot follow reads as
fewer words plus that warning, never as invented characters, and the blocks from the fonts that did read
are still returned. A page with nothing readable on it contributes no block, which keeps "this page has no
text" and "this page's text could not be read" two different answers.

The read does not run OCR, does not consult the embedded font program, and does not reflow. It reports the
text the document draws and where it draws it; anything beyond that — hyphenation, reading order across
columns, what a table means — is the caller's decision, made on the geometry this read returns.

`PdfPageDetail` does not carry text, so an index that needs both text and images calls `ReadTextAsync` for
the page beside the enumeration, rather than paying for a text read of the whole range.

## Warnings

`PdfWarning` pairs a stable `Kind` with a human-readable `Message`. The kind is the contract; the text
may be reworded in any release, so match on `Kind` and log `Message`. The set of kinds is open, which
means a `switch` over `PdfWarningKind` must keep a `default` arm.

The kinds fall into five groups:

| Group | Examples | What it means |
| --- | --- | --- |
| Structure repaired | `CrossReferenceRebuilt`, `CrossReferenceEntryMismatch`, `ObjectStreamTruncated` | The reader recovered; the document may be damaged |
| Page tree | `PageTreeMissing`, `PageTreeCycle`, `PageSizeAssumed` | Pages were skipped, cut off, or described with a default size |
| Encryption | `EncryptionOpened`, `EncryptionHandlerUnsupported`, `CryptFilterUndefined` | The encryption was understood, opened, or could not be used |
| Merge decisions | `NamedDestinationsDropped`, `OutlinesDroppedByPolicy`, `BookmarksTruncated` | Content was intentionally not carried over |
| Content reading | `ContentStreamUnreadable`, `ImageFilterUnsupported`, `ImageDataUnreadable`, `XObjectResourceMissing`, `NamedDestinationUnresolved`, `EmbeddedFileUnreadable`, `EmbeddedFileNameCollision`, `XmpPacketMalformed`, `FormFieldUnreadable`, `AnnotationUnreadable`, `TextUnreadable` | A page, an image, an attachment, a packet, a named destination or a font could not be read; the rest of the read is still returned |

`EncryptionOpened` is a warning even though it describes success: it tells a caller that the document
was protected and is not simply unprotected. `OutputVerified` is likewise a success signal, reported
only when `PdfEngineOptions.VerifyOutput` is enabled (Chapter 10).

Warnings from a source are folded into `MergeResult.Warnings`, so a caller who only looks at the merge
result still learns that a source had to be repaired. Identical warnings are not repeated; because the
message names the source, two damaged sources normally produce two warnings rather than one.

A practical handling policy:

```csharp
foreach (var warning in result.Warnings)
{
    logger.LogWarning("{Kind}: {Message}", warning.Kind, warning.Message);
}
```

Treat structural warnings as reasons to keep the source but investigate it, and treat merge-decision
warnings as expected when the plan deliberately drops something. Do not fail a merge because warnings
exist: if a condition was fatal, the merge would have thrown.

## Using inspection as a preflight

The inspector is most useful immediately before a merge:

1. Inspect each source with its password.
2. Reject documents that are unreadable, and ask for a password when `RequiresPassword` is true.
3. Resolve page selections against `PageCount` so a bad range is a validation message rather than an
   exception halfway through writing (Chapter 5).
4. Warn the user about repairs, encryption and unusual page sizes.
5. Merge only the sources that passed.

This mirrors the order the merger itself uses — open, read structure, resolve selections, write — but
gives you a place to interject a message, a prompt or a policy decision. It does not make the merge
safer in the sense of catching every error: a file can change between inspection and merge, and the
merger validates again. The gain is a better conversation with the user, not a substitute for handling
`PdfException` (Chapter 9).
