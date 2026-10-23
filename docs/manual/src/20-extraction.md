# 20. Extraction

Extraction is the read that answers "what does this page say, and where does it say it". It is the same
walk of the page tree, the same text state and the same font decoding that the merger uses, so a page you
extract is a page you could have merged, and the two cannot disagree about what it draws.

Two ports offer it. `IPdfContentReader.ReadTextAsync` reads blocks of text — the summary read of
Chapter 8. `IPdfLayoutReader` reads the same page at three levels — letters, words and blocks — and writes
the result out in the four formats a repository ingests. Both take an `IPdfContentSource`, a `PageRange` and
an optional password, and both return a `PdfReadResult<T>`.

```csharp
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;

var blocks = await contentReader.ReadTextAsync(
    new PdfFileSource("report.pdf"),
    PageRange.All,
    cancellationToken: cancellationToken);

foreach (var block in blocks.Items)
{
    Console.WriteLine($"p{block.PageNumber} #{block.ReadingOrder}: {block.Text}");
}
```

## The shape of a read

`PdfReadResult<T>` is the same wrapper for every read in the library:

| Member | Type | Meaning |
| --- | --- | --- |
| `Items` | `IReadOnlyList<T>` | What was read, in reading order |
| `Count` | `int` | `Items.Count` |
| `Warnings` | `IReadOnlyList<PdfWarning>` | What could not be read, and what had to be assumed |
| `Segmentation` | `PdfPageSegmentationStrategy?` | The rule that grouped the words — set by the block reads only |
| `ReadingOrderStrategy` | `PdfReadingOrderStrategy?` | The rule that ordered the blocks — set by the block reads only |

The two strategy members are `null` on a read that does not group anything, and they are filled in even
when the page turned out to be empty: an empty result still names the rule that found nothing, so a caller
comparing two runs never has to recall which options produced which. Chapter 21 explains what the
strategies mean and how to choose one.

A read reports a page it could not decode rather than throwing: a content stream that cannot be walked
produces a `ContentStreamUnreadable` warning and no blocks for that page, the other pages still read, and a
font whose encoding cannot be decoded yields fewer letters rather than invented text (`TextUnreadable`). The
guard is the same one Chapter 9 states for merges — treat the presence of a warning as information, and
never as a failed read unless the exception itself was thrown.

## Letters

A letter is the smallest thing a page draws that carries text. It has its own box, the baseline it sits on,
the size it was drawn at and the font it came from:

```csharp
var letters = await layoutReader.ReadLettersAsync(
    source,
    PageRange.Single(1),
    cancellationToken: cancellationToken);

foreach (var letter in letters.Items.Take(5))
{
    Console.WriteLine(
        $"{letter.Value,-2} box {letter.BoundingBox} baseline {letter.StartBaseLine} "
        + $"{letter.FontSize:0.0}pt {letter.FontName ?? "(no name)"}");
}
```

| Property | Type | Meaning |
| --- | --- | --- |
| `Value` | `string` | The character the letter draws |
| `PageNumber` | `int` | The one-based page it was drawn on |
| `BoundingBox` | `PdfRectangle` | The area it covers, in page space |
| `StartBaseLine`, `EndBaseLine` | `PdfPoint` | The baseline it sits on, from its start to its end |
| `FontSize` | `double` | The size it was drawn at, in points |
| `FontName` | `string?` | The font's name, when the document states one |
| `TextDirection` | `PdfTextDirection` | Left to right or right to left |
| `IsWhitespace` | `bool` | Whether the value is whitespace |
| `Width` | `double` | The length of the baseline |

Whitespace letters are reported. A caller that segments text itself measures the gaps between words, and a
read that silently dropped the spaces would force it to measure the gaps between letters instead.

`Width` is measured along the baseline, not from the box: a letter in a rotated run has a box that says
nothing about the advance, while its baseline always does.

## Words

`ReadWordsAsync` groups letters into words by geometry, with two thresholds stated in ems rather than in
points — a fraction of the size of the letters being grouped — so one rule serves a 6pt footnote and a 30pt
title:

```csharp
var words = await layoutReader.ReadWordsAsync(
    source,
    PageRange.All,
    new PdfWordExtractionOptions
    {
        MaxHorizontalGap = 0.25,
        MaxVerticalGap = 0.5,
        IncludeWhitespace = false,
    },
    cancellationToken: cancellationToken);
```

| Option | Default | What it decides |
| --- | --- | --- |
| `Strategy` | `NearestNeighbour` | Whether letters are taken in the page's own order or in the order the content stream drew them |
| `MaxHorizontalGap` | `0.25` em | How wide a gap may be and still sit inside one word; a space is about a quarter of an em |
| `MaxVerticalGap` | `0.5` em | How far two baselines may drift apart and still be one line; half an em tolerates a superscript |
| `IncludeWhitespace` | `false` | Whether a space belongs to the word it follows, so concatenating the words reproduces the drawn text |

`PdfWord` carries `Text`, `PageNumber`, `BoundingBox`, the `Letters` it was built from, the raw `Glyphs`
behind those letters, and three conveniences derived from them: `FontName`, `FontSize` and `BaselineY`. The
glyphs are the document's own units — position, width, base font — and the letters are the same run restated
with baselines and a direction, which is what a caller measuring lines needs.

A word never spans a line and never spans a page, so the `BoundingBox` of every word in a result is one
rectangular area with a single baseline through it.

## Blocks, and the exports

A block is a run of words that belong together: a paragraph, a heading, a table cell, a caption. It is what
a caller indexes when it wants prose rather than geometry.

| Property | Type | Meaning |
| --- | --- | --- |
| `PageNumber` | `int` | The one-based page the block sits on |
| `ReadingOrder` | `int` | The block's position on its page, from 1 |
| `Text` | `string` | The block's text, with a newline between lines |
| `BoundingBox` | `PdfRectangle` | The area the block covers |
| `Words` | `IReadOnlyList<PdfWord>` | The words the block was built from |
| `LineCount` | `int` | The number of lines in `Text` |

`ExportAsync` writes that same read out in one of four formats. It does not produce a second reading: the
blocks are read once, with the options you pass, and the format decides only the vocabulary they are written
in.

```csharp
await using var destination = File.Create("report.alto.xml");

var written = await layoutReader.ExportAsync(
    source,
    PageRange.All,
    PdfLayoutFormat.Alto,
    destination,
    cancellationToken: cancellationToken);

Console.WriteLine($"{written.Count} block(s), {written.Warnings.Count} warning(s)");
```

| `PdfLayoutFormat` | What it writes |
| --- | --- |
| `Alto` | One ALTO document, a `Page` per page with its text blocks, lines, words and print space |
| `PageXml` | One PAGE XML document, a text region per block, a text line per line and the reading order that names them |
| `Hocr` | One XHTML document of `ocr_page`, `ocr_carea`, `ocr_par`, `ocr_line` and `ocrx_word` elements, each with its box |
| `SvgText` | One SVG document, a group per page and a `text` element per word, placed on its baseline |

Every format carries the same three things — the text, the area each word covers and the order the page is
read in — and differs only in what its consumer already parses. Three properties are worth relying on:

* **Coordinates are flipped for you.** The read reports page space with the origin at the bottom left, as
  the PDF format stores it. All four formats are written with the origin at the top left and the y axis
  running down, which is the convention of the authoring surface (Chapter 12). Each export flips the y
  coordinate against the height of the page; nothing else about a box changes.
* **The bytes are reproducible.** No timestamp, random identifier or machine name is written, so the same
  document read with the same options produces the same file on every run. That is what makes an export
  diffable in a test.
* **An export never renders the page.** Nothing produces an image, embeds a font or copies the bytes of the
  source. ALTO's measurement unit is `pixel` in the sense ALTO gives it — one unit of the page's own
  coordinate space, a point — because there is no raster for a physical measurement to refer to. A consumer
  that needs a length divides by 72 to get inches.

An export writes to a stream you own, so the caller decides the file name, the encoding wrapper and the
lifetime. `ExportAsync` validates the format and the destination before it opens the document: a format
this release does not write throws `ArgumentOutOfRangeException`, and a destination that cannot be written
throws `ArgumentException`, both at the call rather than after a full read.

## What an extraction is not

* **It is not OCR.** A page that carries only an image of text has no letters to read and produces no
  blocks. Nothing here invents text from pixels.
* **It is not a layout engine.** `LineCount` and `BoundingBox` are what the page draws; a table, a column
  or a heading is the caller's interpretation of the geometry. Chapter 21 is about the groupings the library
  does offer, and where it stops.
* **It is not a conversion to an editable document.** The four exports describe a page; they do not rebuild
  one, and re-importing them is out of scope ([ADR-001](../adr/0001-pdf-platform-boundary.md)).

The `extract-layout` example runs the whole of this chapter against the sample document: blocks in reading
order, the letters of page 1, and all four exports written to disk with their sizes reported.
