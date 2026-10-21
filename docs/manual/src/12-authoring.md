# 12. Authoring a Page

SharpCorePDF can start a document from nothing: `PdfDocumentBuilder` adds pages, draws lines of text
with a standard or an embedded font, and places PNG images. It writes the document in memory, and the
result is an ordinary PDF, so it can be inspected and merged like any other source. The use it was
built for is a cover page: a logo, an address block and a few lines of text in front of a document that
already exists.

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
string is refused, because wrapping and line breaking belong to a layout engine and this is not one.

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
preserved. A subset of DejaVu Sans used for a cover is a few kilobytes rather than the 740 KB of the
whole face. A `/ToUnicode` map travels beside it, so `é`, `è`, `à`, `ç`, `ô` and `ï` can be extracted
again by `pypdf`, `pdftotext` or `IPdfContentReader.ReadTextAsync`.

Nothing is looked up in the operating system's font folder. The bytes you pass are the only source of
the face, which is what makes an embedded font work in a container that has no fonts installed. The
program must use TrueType (`glyf`) outlines; an OpenType font with PostScript (`CFF`) outlines is
refused with an explanation.

Each rune of the text is mapped through the font's `cmap`. A code point the font does not cover is
refused, and the message names the character, so a missing glyph becomes a build error rather than a
blank space on a customer's statement.

There is no `MeasureString`. The width of a line depends on the font, the size and the font's own
metrics, and the library does not ship a text shaper. Lay lines out with a fixed step — 14 points for
12 point text, the convention the example uses — or measure with the same font file in your own code.

## PNG images

```csharp
page.DrawImage(png, x: 40, y: 30, width: 152, height: 87); // explicit size in points
page.DrawImage(png, x: 40, y: 30);                        // natural size
```

The image is a PNG file in memory. The supported forms are the ones a logo or a screenshot is stored
in: **eight bits per sample, non-interlaced**, of colour type 0 (greyscale), 2 (RGB), 3 (palette),
4 (greyscale and alpha) or 6 (RGBA). An image with an alpha channel gets a `/SMask`; a palette whose
`tRNS` chunk makes entries transparent is expanded to RGB and a soft mask; a `tRNS` colour key on a
greyscale or RGB image becomes the `/Mask` array the format defines for it.

Compressed data is not decoded when it does not have to be: an opaque image keeps its `IDAT` bytes and
is embedded with `FlateDecode` and a PNG predictor, so a 67 KB logo stays a 67 KB stream. Only an image
whose transparency has to be split into a second stream is inflated and deflated again.

The natural size comes from the `pHYs` chunk: a 300 dpi logo of 152 × 87 pixels is drawn 36.48 × 20.88
points. A PNG without `pHYs` is drawn at one point per pixel.

Interlaced (Adam7) and sixteen bit PNGs are refused when the image is handed to `DrawImage`, with a
message that says which one it is. So are JPEG, GIF, SVG and every other format: re-save the file as an
eight bit PNG. The same file may be drawn on any number of pages; it is one image object in the
document, not one per page.

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
an output option of a merge, and defaults to `SharpCorePDF 1.0.0`.

The document identifier is derived from the content and the creation date, so two builds of the same
pages at the same moment are the same file. Supplying both `CreationDate` and `DocumentId` removes the
last two sources of variation and makes the output reproducible byte for byte, which is what a test
that compares files needs. `DocumentId` must be 32 hexadecimal digits.

## What it deliberately does not do

This is an authoring API for a page of fixed-position content, not a layout engine. There is no word
wrap, no paragraph flow, no table, no list, no vector drawing, no text shaping (kerning and ligatures
come from the font program, not from this library), no form authoring, no signing, no redaction and no
encryption of the authored page. JPEG and other image formats are not embedded. A document that needs
any of those is better produced by a reporting library; SharpCorePDF can still merge the result.

The authored page is written as PDF 1.7 with a classic cross-reference table, uncompressed content
streams and Flate-compressed image and font streams. It is not validated against PDF/A or PDF/UA.

## The surface at a glance

| Member | What it does |
| --- | --- |
| `PdfDocumentBuilder.Create(options?)` | Starts a document; the options control the date, the identifier and the producer. |
| `WithMetadata(metadata)` | Sets title, author, subject, keywords, creator, dates and custom entries. |
| `AddPage(size, draw?)` | Adds a page; `draw` receives a `PdfPageCanvas`, or `null` leaves the page blank. |
| `PdfPageCanvas.DrawText(text, x, y, typeface, size, color?)` | Draws one line at a baseline, in points, top-left origin. |
| `PdfPageCanvas.DrawImage(png, x, y, width, height)` | Draws a PNG at an explicit size. |
| `PdfPageCanvas.DrawImage(png, x, y)` | Draws a PNG at the size its `pHYs` chunk implies. |
| `PdfTypeface.Standard(font)` | Selects one of the twelve standard fonts. |
| `PdfTypeface.FromTrueType(bytes)` | Embeds and subsets a TrueType program. |
| `PdfColor.FromRgb(...)` / `FromHex(...)` | Selects a text colour. |
| `ToArray()` / `SaveAsync(stream)` / `Build(name)` | Writes the document to bytes, to a stream, or as a merge source. |
