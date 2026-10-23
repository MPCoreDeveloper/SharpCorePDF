# 21. Layout Analysis

Chapter 20 read a page and reported what it found. This chapter is about the two decisions that turn the
letters of a page into prose a caller can index: **what a block is** and **which block is read first**. They
are separate decisions, made by two named strategies, and both travel with the result.

Neither strategy changes what a letter says. The glyph read is the same one every other read of the content
uses — same page tree walk, same text state, same font decoding — and the grouping happens above it. A
docstring in the engine states the rule exactly: *a strategy never changes the glyph data; it only groups
it.*

## Why the two decisions are separate

A page can be segmented correctly and still be listed in the wrong order. A two-column page whose left
column is drawn after its right one is the ordinary case: the blocks are right, and the sequence a caller
reads them in is not. Fixing one variable at a time is also what makes a comparison meaningful — vary the
segmentation against a fixed reading order and the difference you see is segmentation.

```csharp
var blocks = await layoutReader.ReadBlocksAsync(
    source,
    PageRange.All,
    new PdfLayoutOptions
    {
        Segmentation = PdfPageSegmentationStrategy.RecursiveXYCut,
        ReadingOrder = PdfReadingOrderStrategy.ColumnAware,
    },
    cancellationToken: cancellationToken);

Console.WriteLine($"segmentation: {blocks.Segmentation}");
Console.WriteLine($"order:        {blocks.ReadingOrderStrategy}");
```

`PdfLayoutOptions` validates itself when it is created: a value that is not a strategy this release
implements throws `ArgumentOutOfRangeException` at the call site, before a document is opened. A read never
pays for a document it cannot segment.

## Segmentation

| `PdfPageSegmentationStrategy` | What it does | When to choose it |
| --- | --- | --- |
| `Raw` | Groups the words in the order the content stream draws them, starting a block wherever the drawing stops following a line down the page | To reproduce a producer exactly — it is the baseline a comparison is made against, not a reading of the page |
| `NearestNeighbour` | Groups words by their place on the page: lines top to bottom, paragraphs as the runs of lines that follow one another with the leading of a paragraph | A single-column page, or the grouping Chapter 20's text read uses |
| `RecursiveXYCut` | Splits the page recursively along the whitespace that separates two groups of words — the widest vertical gutter first, which separates columns, then the widest horizontal one — until a region admits no split | The default, and the right first attempt: it finds columns and headings without knowing what a column is |
| `Docstrum` | Groups lines by the distance between them: lines closer together than the page's own line spacing become one block, and a gutter wider than that spacing keeps two blocks apart | A page of uneven leading, or a two-column page whose columns do not share lines |

`RecursiveXYCut` is the default because it separates columns and headings before it looks at line spacing,
which is the order a page usually states. `Docstrum` reads a page of irregular leading better than a fixed
threshold and a two-column page only when the columns do not share lines — that is the trade, and it is why
both exist rather than one.

The words a segmentation groups come from the word read Chapter 20 described, and the segmentation decides
which of its strategies is used: `Raw` segmentation groups letters in content order, every other
segmentation groups them in the page's order. A caller does not state that twice.

## Reading order

| `PdfReadingOrderStrategy` | What it does |
| --- | --- |
| `Raw` | Reports the blocks in the order the segmentation produced them, which for a raw segmentation is the order the content stream draws them |
| `ColumnAware` | Reports the columns of a page left to right, and the blocks of a column top to bottom. A heading that spans the columns is read before the columns it spans |

`ColumnAware` is the default because it is the order a reader takes a page in. `Raw` is the one a caller
wants when it compares a read against the producer, or when the file's drawing order is itself the thing
under investigation.

## Keeping ink that is not prose

Two flags on `PdfLayoutOptions` describe the parts of a page that draw something without drawing a sentence:

| Option | Default | What it decides |
| --- | --- | --- |
| `IncludeDecorations` | `true` | Whether a block that carries no letter and no digit is kept — a rule drawn as a run of hyphens, a lone bullet, a separator |
| `IncludeImages` | `true` | Reserved: image regions are not part of a block, and this release's exports describe text only, so the flag adds nothing today |

A caller that indexes prose sets `IncludeDecorations = false` and gets only what a reader reads. A caller
that reproduces a page's text as a reader would see it keeps the default, because a separator between two
sections is part of what the page says.

`IncludeImages` is stated now and honoured later. It is in the released API so that the meaning of a layout
option does not change under a caller when image regions arrive; today it changes no result.

## Comparing two readings

The result names the rules that produced it, which is what makes a comparison self-describing. Read a page
twice and print what each read says about itself:

```csharp
async Task<PdfReadResult<PdfTextBlock>> ReadAsync(PdfPageSegmentationStrategy segmentation) =>
    await layoutReader.ReadBlocksAsync(
        source,
        PageRange.Single(1),
        new PdfLayoutOptions { Segmentation = segmentation },
        cancellationToken: cancellationToken);

var raw = await ReadAsync(PdfPageSegmentationStrategy.Raw);
var cut = await ReadAsync(PdfPageSegmentationStrategy.RecursiveXYCut);

Console.WriteLine($"raw:         {raw.Segmentation}, {raw.Count} block(s)");
Console.WriteLine($"recursive:   {cut.Segmentation}, {cut.Count} block(s)");

// The text of the two reads is the same text. A difference between them is a difference in grouping.
var sameText = raw.Items.Select(block => block.Text.Replace("\n", " ", StringComparison.Ordinal)).SequenceEqual(
    cut.Items.Select(block => block.Text.Replace("\n", " ", StringComparison.Ordinal)));
```

That equality is the property worth testing against a new document: a segmentation may move a word from one
block to another, but it may not change, lose or add a word. It is also why `Raw` is kept in the released
API — without a baseline there is nothing for the other three strategies to be compared against.

Two further properties hold for every strategy:

* **The read is deterministic.** The same document and the same options produce the same blocks in the same
  order on every run, on every machine. There is no random seed, no time input and no culture-dependent
  comparison.
* **The strategy sets are open.** A later release may add a segmentation or an order, so a `switch` over
  either enum must keep a `default` arm (Chapter 9's rule for warnings applies here too).

## Where the analysis stops

The library groups glyphs; it does not interpret a document. What that means in practice:

* **No table model.** A table is a set of blocks that happen to line up. Merging cells, identifying a header
  row or relating two columns as columns is the caller's work, on the geometry the read returns.
* **No semantic roles.** A block is not marked as a heading, a caption or a footnote. Size and position say
  something about that; nothing else does.
* **No font or style model beyond the drawing.** `PdfWord.FontName` and `PdfLetter.FontName` name the font
  the page used. Weight, emphasis and language are not derived.
* **No rendering, so no confidence.** A read that finds no letters on a page says so and returns no blocks;
  it does not guess from pixels ([ADR-001](../adr/0001-pdf-platform-boundary.md)).

The cost of a call is one open of the source, one walk of the pages you selected and one pass over their
content streams; the grouping is a pass over glyphs already in memory. Every call pays the read again, so a
tool that shows several readings of the same pages is cheaper reading a wide range once and grouping it
several times than calling `ReadBlocksAsync` once per strategy over the same range. The recorded cost of a
read is in [`docs/measurements.md`](../measurements.md).
