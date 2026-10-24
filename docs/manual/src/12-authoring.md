# 12. Authoring a Page

SharpCorePDF can start a document from nothing: `PdfDocumentBuilder` adds pages, draws lines of text
with a standard or an embedded font, places PNG, JPEG, BMP and GIF images and draws shapes. It writes
the document in memory, and the result is an ordinary PDF, so it can be inspected and merged like any
other source. The use it was built for is a cover page: a logo, an address block and a few lines of text
in front of a document that already exists. Measuring those lines, and wrapping a paragraph into lines,
is Chapter 16; this chapter draws what a layouter hands it. The shape surface — paths, fill and stroke,
clipping and transforms — has its own chapter, Chapter 15, and the formats an image can be read from, and
what each one is refused for, are Chapter 17.

## A page from nothing

```csharp
var cover = PdfDocumentBuilder.Create()
    .AddPage(PdfPageSize.A4, page => page
        .DrawImage(logoPng, x: 40, y: 30, width: 152, height: 87)
        .DrawText("Veuillez trouver ci-joint le décompte", 150, 400,
            PdfTypeface.Standard(StandardFont.Helvetica), 12))
    .Build("cover.pdf");                    // a PdfByteArraySource, usable as a merge source

var plan = MergePlan.Create()
    .AddSource(PdfSource.Create(cover))
    .AddFile(@"C:\documents\statement.pdf");

await merger.MergeToFileAsync(plan, @"C:\documents\with-cover.pdf");
```

`Build()` writes the document and returns its bytes as a content source. `ToArray()` returns the same
bytes, and `SaveAsync(Stream)` writes them to a stream you supply. Nothing touches the file system, so a
cover can be produced, merged and discarded inside one request.

The builder collects pages and writes them when one of those three members is called. Adding a page
afterwards does not change bytes that were already returned, and a builder is not thread safe: build one
document on one thread. A typeface is immutable and may be shared by any number of builders.

## Coordinates

The page uses the coordinate system a layout is written in, not the one PDF stores:

* the origin is the **top-left** corner of the page;
* **x** grows to the right, **y** grows downwards, in points (1 point = 1/72 inch);
* `DrawText` places the **baseline** of the text at `y`, not its top edge;
* `DrawImage` places the **top-left** corner of the image at `(x, y)`.

For an A4 page, `page.Width` is 595.276 and `page.Height` is 841.89. A line at `y = 400` sits 400 points
below the top edge. The conversion to PDF's bottom-left origin is done for you, so nothing in an
authored document needs the `page.Height - y` arithmetic.

Every call is independent: five lines of text are five calls at five `y` values. A line break inside one
string is refused, because `DrawText` draws the baseline it is given and nothing else; a paragraph is laid
out into lines first — Chapter 16 — and those lines are drawn, either one at a time or with
`DrawTextLayout`.

## Text and fonts

```csharp
var heading = PdfTypeface.Standard(StandardFont.Helvetica);
var body = PdfTypeface.FromTrueType(File.ReadAllBytes("fonts/DejaVuSans.ttf"));

PdfDocumentBuilder.Create().AddPage(PdfPageSize.A4, page => page
    .DrawText("Décomptes SPRL", 330, 180, heading, 12, PdfColor.FromHex("#1F3A5F"))
    .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", 150, 400, body, 12)
    .DrawText("Sending 1/3", 150, 550, body, 12));
```

A **standard font** is one of the twelve faces every reader supplies: Helvetica, Times and Courier,
each in four styles. It is written as a `/Type1` font with `/WinAnsiEncoding`, which needs no font file
and keeps the document small. The encoding is Windows-1252: Latin-1 plus the typographic characters
(curly quotes, en and em dashes, the euro sign). A character outside it — an arrow, a CJK ideograph, a
ligature — is refused when the document is written, with the character and its code point in the
message. That refusal is deliberate: writing `?` would silently change the text.

An **embedded font** is a TrueType program you supply as bytes. `PdfTypeface.FromTrueType` copies and
parses it immediately, so a malformed program is reported where it is handed over rather than at the
first page that draws with it. The font is embedded as a subset: the outlines of the glyphs the document
draws are kept, the outlines of every other glyph are left out, and the glyph numbers of the source are
preserved. A cover that draws two lines of text with the full 738 KB DejaVu Sans is a 58 KB document:
the outlines of every other glyph are gone, and what remains is the per-glyph bookkeeping — glyph
locations, advance widths and the character map — which keeps its size because the numbering is
preserved. A `/ToUnicode` map travels beside it, so `é`, `è`, `à`, `ç`, `ô` and `ï` can be extracted
again by `pypdf`, `pdftotext` or `IPdfContentReader.ReadTextAsync`.

Nothing is looked up in the operating system's font folder. The bytes you pass are the only source of
the face, which is what makes an embedded font work in a container that has no fonts installed. The
program must use TrueType (`glyf`) outlines; an OpenType font with PostScript (`CFF`) outlines is
refused with an explanation.

Each rune of the text is mapped through the font's `cmap`. A code point the font does not cover is
refused, and the message names the character, so a missing glyph becomes a build error rather than a
blank space on a customer's statement.

The width of a line is not guessed: `PdfTypeface.Measure` returns it in points from the face's own data, and
`PdfTypeface.Metrics` states the ascent, descent and line gap the face recommends. Chapter 16 covers both,
together with `PdfTextLayouter`, which uses them to wrap a paragraph into lines that fit a column. The
example above keeps a fixed step of 14 points between baselines because a cover needs no more than that.

## Images

```csharp
// A file opened once and drawn wherever the document needs it.
var logo = PdfImage.Open(File.ReadAllBytes("logo.jpg"));

page.DrawImage(logo, x: 40, y: 30, width: 152, height: 87); // explicit size in points
page.DrawImage(logo, x: 40, y: 30);                         // natural size

// The same two calls take the bytes directly, which reads them on every draw.
page.DrawImage(pngBytes, x: 40, y: 30, width: 152, height: 87);
```

`PdfImage.Open` decides the format from the signature of the bytes, never from a file name. `FromPng`,
`FromJpeg`, `FromBmp` and `FromGif` do the same read with the format stated, and refuse a file whose
signature disagrees rather than guessing at it. `Format`, `WidthPixels`, `HeightPixels`, `ColorSpace`,
`BitsPerComponent`, `HasAlpha`, `NaturalWidth`, `NaturalHeight` and `FrameCount` report what the file
holds. Two images opened from the same bytes state the same `Key` and become one image object in the
document, so a logo drawn on forty pages is stored once.

Each format has its own policy — what is read, what is refused by name, and what the document stores —
and Chapter 17 is its reference. `1.2` embeds all four: the JPEG, BMP and GIF readers and the colour
profile a PNG may state arrived with this release, and `1.1.0` embedded PNG images only. In short: a PNG is
read at eight bits in its five colour types, where an alpha channel, a palette `tRNS` and a colour key each
become the mask PDF defines for it, and an `iCCP` profile is declared as `/ICCBased` rather than
dropped; a JPEG is passed through as its own bytes under `DCTDecode`; a BMP and a GIF are decoded,
because PDF has no filter for their pixels, and a GIF contributes its first frame. Interlaced and
sixteen bit PNGs, a JPEG whose EXIF orientation is not the identity, and the BMP compressions this reader
does not decode are refused with the name of what was found.

The natural size of a PNG comes from its `pHYs` chunk, of a JPEG from its JFIF density and of a BMP from
its pixels-per-metre fields, so a 300 dpi logo of 152 × 87 pixels is drawn 36.48 × 20.88 points. A file
that states no resolution, which every GIF does, is drawn at one point per pixel.

SVG is not read at all, and a caller with another format re-saves it as one of the four.

## Metadata, identity and reproducible output

```csharp
var options = PdfAuthoringOptions.Default with
{
    CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    DocumentId = "00112233445566778899AABBCCDDEEFF",
};

var document = PdfDocumentBuilder.Create(options)
    .WithMetadata(new PdfMetadata
    {
        Title = "Décompte 12345",
        Author = "Décomptes SPRL",
        Creator = "Billing service",
    })
    .AddPage(PdfPageSize.A4, page => ...);
```

`WithMetadata` writes the document information dictionary. `Producer` is an authoring option, like it is
an output option of a merge, and defaults to `SharpCorePDF 1.3.0`.

The document identifier is derived from the content and the creation date, so two builds of the same
pages at the same moment are the same file. Supplying both `CreationDate` and `DocumentId` removes the
last two sources of variation and makes the output reproducible byte for byte, which is what a test
that compares files needs. `DocumentId` must be 32 hexadecimal digits.

## An XMP packet, bookmarks and imported pages

```csharp
var document = PdfDocumentBuilder.Create()
    .WithXmp(xmpPacket)                       // the catalog's /Metadata stream, byte for byte
    .WithOutline(
    [
        new PdfOutlineItem { Title = "Couverture", Destination = new PdfDestination { PageNumber = 1 } },
        new PdfOutlineItem
        {
            Title = "Sommaire",
            Destination = new PdfDestination { PageNumber = 2 },
            IsOpen = false,
            Children =
            [
                new PdfOutlineItem { Title = "Totaux", Destination = new PdfDestination { PageNumber = 2 } },
            ],
        },
        new PdfOutlineItem { Title = "Site", Destination = new PdfDestination { Uri = "https://example.com" } },
    ])
    .AddPage(PdfPageSize.A4, page => ...)
    .AddPage(PdfPageSize.A5, page => ...)
    .ImportPage(PdfFileSource.From("annexe.pdf"), 1);
```

`WithXmp` takes a packet as bytes, or as the `PdfXmpMetadata` a reader returned, and writes it to the
catalog's `/Metadata` stream exactly as it was given and uncompressed — a validator compares the bytes it
reads with the ones a producer signed, and PDF/A-1 forbids compressing them. Bytes that contain neither
an `x:xmpmeta` nor an `rdf:RDF` element are refused at the call rather than written as metadata. It is
how a caller carries a profile or a schema the library does not build itself.

`WithOutline` writes the bookmark tree, nesting included. A destination is a one-based page number of
*this* document, a URL, or nothing; `IsOpen` states whether a viewer shows the children. Because pages can
still be added after the outline is set, the page numbers are checked when the document is written: a
bookmark that names a page the document does not have is refused there instead of being written dangling.
A destination that states an `Area` becomes `/XYZ` at its top-left corner with the zoom left to the
viewer; an external file destination, or a name that no table in the document resolves, is refused with
`PdfUnsupportedFeatureException`.

`ImportPage` and `ImportPages` copy pages out of another document: the same copy a merge performs, on one
page or one range. The source is read when the call is made, so a document that cannot be read, needs a
password, or has no such page fails there and not when the result is written. The copy keeps its content,
its resources, its annotations, its rotation and its geometry, and states on itself the attributes it
inherited from its own page tree, because the authored document has a single flat one. A link or a
bookmark that points at a page that was not imported points at the imported page rather than at nothing.

## What it deliberately does not do

This is an authoring API for fixed-position content, not a page-flow engine. Text wraps, aligns and
justifies inside a column — Chapter 16 — but there is no table, no list, no page break, no column set, no
text shaping (kerning and ligatures come from the font program, not from this library), no form authoring,
no redaction and no encryption of the authored page. SVG is not embedded, and neither is any format
beyond the PNG, JPEG, BMP and GIF described in Chapter 17: a caller that has another format re-saves it. A
document that needs any of those is better produced by a reporting library; SharpCorePDF can still merge
the result.

Fixed-position **vector drawing** is part of this surface: lines, rectangles, ellipses, arbitrary paths of
move/line/curve/close segments, filled or stroked with a colour and an alpha, dashed strokes, clipping and
a saved graphics state. What it is not is a shape model with a scene graph: you place shapes at
coordinates. Chapter 15 covers that surface in full — coordinates, style, clip, transforms and the state.
Every shape is written into the page's content stream in the order you call it, so later shapes paint over
earlier ones.

The authored page is written as PDF 1.7 with a classic cross-reference table, uncompressed content
streams and Flate-compressed image and font streams. It is not validated against PDF/A or PDF/UA.

## The surface at a glance

| Member | What it does |
| --- | --- |
| `PdfDocumentBuilder.Create(options?)` | Starts a document; the options control the date, the identifier and the producer. |
| `WithMetadata(metadata)` | Sets title, author, subject, keywords, creator, dates and custom entries. |
| `WithXmp(packet)` / `WithXmp(xmpMetadata)` | Writes an XMP packet into the catalog exactly as given. |
| `WithOutline(bookmarks)` | Writes the bookmark tree, with nesting, open state and page or URL destinations. |
| `AddPage(size, draw?)` | Adds a page; `draw` receives a `PdfPageCanvas`, or `null` leaves the page blank. |
| `ImportPage(source, pageNumber)` / `ImportPages(source, range)` | Copies pages out of another document, with their content, resources, annotations and geometry. |
| `PdfPageCanvas.DrawText(text, x, y, typeface, size, color?)` | Draws one line at a baseline, in points, top-left origin. |
| `PdfPageCanvas.DrawTextLayout(layout, origin)` | Draws every line of a `PdfTextLayout`, the top-left corner of its box at `origin`. |
| `PdfImage.Open(bytes)` | Reads a PNG, JPEG, BMP or GIF file; the `FromPng`, `FromJpeg`, `FromBmp` and `FromGif` factories state the format. |
| `PdfPageCanvas.DrawImage(image, x, y, width, height)` | Draws an image — bytes or a `PdfImage` — at an explicit size. |
| `PdfPageCanvas.DrawImage(image, x, y)` | Draws an image at the size the file's resolution states, one point per pixel when it states none. |
| `PdfPageCanvas.DrawLine(from, to, stroke)` | Draws one stroked segment. |
| `PdfPageCanvas.DrawRectangle(rect, fill?, stroke?)` | Fills and/or strokes a rectangle. |
| `PdfPageCanvas.DrawEllipse(bounds, fill?, stroke?)` | Fills and/or strokes an ellipse inside a box. |
| `PdfPageCanvas.DrawPath(path, fill?, stroke?)` | Fills and/or strokes a path of `PdfPathSegment` moves, lines, curves and closes. |
| `PdfPageCanvas.SetClip(path, fillRule?)` | Restricts everything drawn afterwards to a path. |
| `PdfPageCanvas.SaveState()` / `RestoreState()` | Brackets a change of fill, stroke, clip or transform. |
| `PdfTypeface.Standard(font)` | Selects one of the twelve standard fonts. |
| `PdfTypeface.FromTrueType(bytes)` | Embeds and subsets a TrueType program. |
| `PdfTypeface.Measure(text, size)` / `.Metrics` | The width of a line in points, and the face's vertical metrics in font units (Chapter 16). |
| `PdfTextLayouter.Layout(text, style, options)` | Wraps a paragraph into a `PdfTextLayout` that fits a width (Chapter 16). |
| `PdfColor.FromRgb(...)` / `FromHex(...)` | Selects a text colour. |
| `PdfPaint(color, alpha?, fillRule?)` / `PdfStroke(paint, width, …)` | States a fill, a stroke, their transparency and their dash pattern. |
| `ToArray()` / `SaveAsync(stream)` / `Build(name)` | Writes the document to bytes, to a stream, or as a merge source. |
