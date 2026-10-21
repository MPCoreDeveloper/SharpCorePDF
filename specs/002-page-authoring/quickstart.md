# Quickstart: a cover page in front of a document

This is the shortest path from no PDF to a covered one: an A4 page with a PNG logo and two lines of
French text, merged in front of a statement. It needs no font file if the text is Windows-1252; the
embedded-font variant is two lines further down.

```csharp
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;

var logo = await File.ReadAllBytesAsync("logo.png");

var cover = PdfDocumentBuilder.Create()
    .AddPage(PdfPageSize.A4, page => page
        .DrawImage(logo, x: 40, y: 30, width: 152, height: 87)
        .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", 150, 400,
            PdfTypeface.Standard(StandardFont.Helvetica), 12)
        .DrawText("pour la période du 01/01/2025 au 31/12/2025.", 150, 414,
            PdfTypeface.Standard(StandardFont.Helvetica), 12))
    .Build("cover.pdf");

var plan = MergePlan.Create()
    .AddSource(PdfSource.Create(cover))
    .AddFile(@"C:\documents\statement.pdf");

await merger.MergeToFileAsync(plan, @"C:\documents\with-cover.pdf");
```

`logo.png` is an eight-bit PNG; RGBA, RGB, greyscale and palette-with-`tRNS` all work, interlaced and
sixteen-bit files do not. The image is drawn at 152 × 87 points; call `DrawImage(logo, 40, 30)` to use
the size its `pHYs` chunk implies instead.

The coordinates are **points from the top-left corner**, x to the right and y downwards, and `y` of
`DrawText` is the baseline. A4 is 595.276 × 841.89 points, so `y = 400` is 400 points below the top
edge.

## Round-tripping the text

Read the first page of the result with the same reader an application uses:

```csharp
var info = await inspector.InspectAsync(
    new PdfByteArraySource(await File.ReadAllBytesAsync(@"C:\documents\with-cover.pdf"), "with-cover.pdf"),
    cancellationToken: cancellationToken);

Console.WriteLine(info.Pages[0].Size);                 // 595.28 x 841.89 pt

var text = await reader.ReadTextAsync(
    new PdfByteArraySource(await File.ReadAllBytesAsync(@"C:\documents\with-cover.pdf"), "with-cover.pdf"),
    PageRange.Single(1),
    cancellationToken: cancellationToken);

foreach (var block in text.Items)
{
    Console.WriteLine(block.Text);                     // accents included
}
```

The same text comes back from `pypdf` and `pdftotext`. A standard font is extracted through
`/WinAnsiEncoding`; an embedded font through its `/ToUnicode` map.

## When the text does not fit Windows-1252

```csharp
var body = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync("fonts/DejaVuSans.ttf"));

var cover = PdfDocumentBuilder.Create()
    .AddPage(PdfPageSize.A4, page => page.DrawText("Sending 1/3 — été", 150, 550, body, 12))
    .Build();
```

The program is copied and parsed when `FromTrueType` is called, embedded as a subset, and never read
from the machine's font folder. A glyph the font does not cover is refused when the document is built,
with the character and its code point in the message.

## A reproducible cover

```csharp
var options = PdfAuthoringOptions.Default with
{
    CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    DocumentId = "00112233445566778899AABBCCDDEEFF",
};

var first = PdfDocumentBuilder.Create(options).AddPage(PdfPageSize.A4, Draw).ToArray();
var second = PdfDocumentBuilder.Create(options).AddPage(PdfPageSize.A4, Draw).ToArray();

// first and second are byte-identical.

static void Draw(PdfPageCanvas page) => page.DrawText(
    "Cover", 150, 400, PdfTypeface.Standard(StandardFont.Helvetica), 12);
```

Leave both out in production and the document is stamped with the moment it was written and an
identifier derived from its content.

## What to read next

* [Chapter 12 of the manual](../../docs/manual/src/12-authoring.md) states the whole contract,
  including the PNG rules and the boundary of the feature.
* [The cover recipe](../../docs/manual/src/13-cookbook.md#write-a-cover-page-and-put-it-in-front-of-a-document)
  puts this in a method with a logo and a font path.
* The `author-page` example runs this code:
  `dotnet run --project .\examples\SharpCorePDF.Examples -- author-page`.
