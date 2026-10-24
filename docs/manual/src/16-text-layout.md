# 16. Text Layout

> **Availability.** This chapter documents the text-layout surface `1.2` adds: a typeface that measures, a
> layouter that turns a paragraph into lines, and a canvas call that draws the result. `1.1.0` drew one line
> per call and measured nothing, so this is new API rather than a change to the call Chapter 12 describes —
> which is why the surface has a chapter of its own instead of a second section there.

Chapter 12 draws one line at a baseline at a time. Everything above that — how wide a line is, where the
lines of a paragraph break, and where each line starts inside a column — is this chapter. Three types do the
work: `PdfTypeface`, which measures; `PdfTextLayouter`, which wraps; and `PdfPageCanvas.DrawTextLayout`,
which draws.

## Measuring a line

`PdfTypeface.Measure` returns the advance width of a string in points, using the same bytes the page will be
written with: the AFM widths of a standard font, or the `hmtx` table of the embedded program.

```csharp
var heading = PdfTypeface.Standard(StandardFont.Helvetica);

var width = heading.Measure("Décomptes SPRL", 12);        // 12 point text
var x = (page.Width - width) / 2d;                        // centre it on the page
page.DrawText("Décomptes SPRL", x, 180, heading, 12);
```

The overload with two extra arguments measures the line as it will be drawn once character spacing and word
spacing are set, because those are PDF text state the writer applies and the face cannot guess:

```csharp
var spaced = heading.Measure("Ref: 12345", 12, characterSpacing: 0.4, wordSpacing: 1.2);
```

That number is *before* horizontal scaling. `PdfTextStyle.HorizontalScale` is a percentage the writer sets in
the content stream, so a caller that scales text has to multiply the measurement by it — the layouter does
exactly that and you do not have to.

Two refusals are worth knowing, because they happen where the string is measured rather than where it is
drawn:

* A **standard font** is written with `/WinAnsiEncoding`, so a character outside Windows-1252 — an arrow, a
  CJK ideograph, a ligature — throws `ArgumentException` naming the character and its code point. A width
  is not guessed for a glyph the font cannot show.
* An **embedded font** throws the same exception for a code point its `cmap` does not map.

A size that is not a positive finite number is an `ArgumentOutOfRangeException` in both cases. Nothing is
read from the operating system: the same string measures the same on every machine and both target
frameworks, which is what lets a document be rebuilt byte for byte.

## Metrics, and the line height they imply

`PdfTypeface.Metrics` returns a `PdfTextMetrics` in the face's own **font units**, not in points:

| Member | What it states |
| --- | --- |
| `UnitsPerEm` | Font units in one em: 1000 for a standard font, the program's own value otherwise (2048 for DejaVu Sans). |
| `Ascent` | Distance from the baseline to the top of the ascent; positive. |
| `Descent` | Distance from the baseline to the bottom of the descent; normally negative. |
| `LineGap` | The extra space the font recommends between lines. |
| `CapHeight` | Height of capital letters; from `OS/2`, or from the outline of `H` when the table says nothing. |
| `XHeight` | Height of lowercase letters; from `OS/2`, or from the outline of `x`. |

`PdfTextMetrics.LineHeight(size)` converts them to points — `(Ascent - Descent + LineGap) * size /
UnitsPerEm` — which is the default distance between baselines:

```csharp
var metrics = PdfTypeface.Standard(StandardFont.Helvetica).Metrics;

metrics.UnitsPerEm;             // 1000
metrics.Ascent;                 // 718
metrics.Descent;                // -207
metrics.LineHeight(12);         // 11.1 points
```

That 11.1 is a *tight* line height: it is what the font states, not what a page designer would choose. A
statement set at 12 points with 15 points between baselines states `leading: 15` on the style, and this is
where that number belongs.

## Laying a paragraph out in a box

`PdfTextLayouter.Layout` takes the text, the style it will be drawn with, and a `PdfTextLayoutOptions` that
describes the column. The result is a `PdfTextLayout`: the lines, each line's start point and runs, and the
box they occupy.

```csharp
var style = new PdfTextStyle(PdfTypeface.Standard(StandardFont.Helvetica), 12, leading: 15);

var layout = PdfTextLayouter.Layout(
    "Veuillez trouver ci-joint le décompte pour l'immeuble 12345. "
    + "Nous restons à votre disposition pour toute question.",
    style,
    new PdfTextLayoutOptions(maxWidth: 240, alignment: PdfTextAlignment.Justify));

PdfDocumentBuilder.Create()
    .AddPage(PdfPageSize.A4, page => page.DrawTextLayout(layout, new PdfPoint(72, 400)))
    .Build("statement.pdf");
```

The short overload, `Layout(text, style, maxWidth)`, is the same call with the defaults below: left aligned,
refusing what does not fit. `text` may contain line breaks and any number of paragraphs.

| Option | Default | What it decides |
| --- | --- | --- |
| `MaxWidth` | required | The width of the column, in points. Must be finite and greater than zero. |
| `Alignment` | `Left` | Where a line sits inside the column: `Left`, `Center`, `Right` or `Justify`. |
| `LineHeight` | `null` | The distance between baselines. `null` falls back to the style's `Leading`, then to `Metrics.LineHeight(FontSize)`. |
| `BreakWords` | `false` | Whether a word wider than the column may be split into pieces that fit. |
| `Overflow` | `Refuse` | What happens to a word that does not fit: `Refuse`, `Clip` or `Continue`. |
| `Direction` | `LeftToRight` | The reading direction. `RightToLeft` is refused today — see below. |

An undefined enum value, a width that is not a width or a line height that is not a line height is an
`ArgumentOutOfRangeException` from the options constructor, before any text is looked at.

### What wrapping does

The rules are few, and they are deterministic:

* A **line break** (`\n`, `\r\n` or `\r`) starts a new line and is never wrapped away. An empty paragraph
  stays a line, so a blank line in the input is a blank line in the output.
* Lines are built out of **words**, and words are separated by spaces. A run of spaces between two words
  collapses to the single space the line is rebuilt from; a tab, a non-breaking space and a hyphen are part
  of the word, so none of them is a place where a line may break.
* Wrapping is **greedy**: a word is added to the current line while it and the space before it fit
  `MaxWidth`, and the next word starts a new line. No line is balanced against the next one, and nothing
  tries to avoid a short last line.
* The width a word is measured at is the width it will be drawn at: the style's character spacing, word
  spacing and horizontal scale are included.

### Alignment

`Left`, `Center` and `Right` place each line inside the column, measured from its left edge. A line is never
moved outside the column and never broken again because of its alignment.

`Justify` stretches the **gaps between words** so that both edges of the column are used. It applies to a
line that is not the last line of its paragraph and has at least two words; the last line of a paragraph is
placed left, because stretching it would add space where the reader expects none. The words themselves are
never stretched and no gap is narrowed, so a line whose natural width already fills the column is left as it
is.

### Overflow

One word can be wider than the column — a URL, a long identifier, a number in a narrow field. Four policies
decide what happens, in this order:

| Setting | What happens to the word |
| --- | --- |
| `Overflow = Refuse` (default) | `ArgumentException` naming the word, its width and the column width. Nothing is drawn and nothing is guessed. |
| `BreakWords = true` | The word is split by character into the longest pieces that fit. Each piece but the last takes a line of its own; the last shares a line with whatever follows. |
| `Overflow = Clip` | The word is cut at the column edge: the longest leading part that fits is kept and the rest of the word is dropped. |
| `Overflow = Continue` | The whole word is placed, and the line is allowed to be wider than `MaxWidth`. |

A single **character** wider than the column follows the same policy: it is refused, dropped or placed alone
on its line. `Clip` and `Continue` are the caller's stated choice, so neither reports a warning — if
dropping characters silently is not acceptable, leave the policy at `Refuse` and catch the exception.

### Direction

`PdfTextDirection.RightToLeft` throws `PdfUnsupportedFeatureException` with the feature
`right-to-left text layout`. The engine writes glyphs in the order the string states them and does not
reorder or shape them, so a right-to-left paragraph would be drawn backwards; refusing it names the gap
instead of producing a document that looks wrong. Text in a right-to-left script can still be drawn with
`DrawText` when the string is already in visual order.

## Drawing the layout

`DrawTextLayout(layout, origin)` draws every line of the layout at the point you give it:

```csharp
page.DrawTextLayout(layout, new PdfPoint(72, 400));
```

`origin` is the **top-left corner of the box**, in the page coordinates Chapter 12 describes. The first
baseline is one ascent below that corner — 8.6 points for 12 point Helvetica — and each later baseline is a
line height below the one before. A block asked for at 400 therefore has its first baseline at about 408.6,
and its capitals start at about 400, where the box starts.

The engine writes each **run** as one text operation, with the style's character spacing, word spacing,
horizontal scale, rise and colour in the text state, so the characters can be extracted again by
`IPdfContentReader`, `pypdf` or `pdftotext`. A paragraph that was justified is drawn as one operation per
word-plus-gap, because that is what a stretched line is; a plain line is one operation, and an empty
paragraph draws nothing at all.

`Bounds` is a description of the box, not a second coordinate space: the call adds `origin` to each run's
start point and takes no offset from `Bounds`. A layout whose `Bounds.Height` is 71 points is a layout that is
71 points tall — drawing it does not move it up by that much.

## The layout result

`PdfTextLayout` is immutable and can be drawn more than once, on more than one page, and measured before it is
drawn.

| Member | What it holds |
| --- | --- |
| `Style` | The `PdfTextStyle` the lines were measured with; `DrawTextLayout` draws with it. |
| `Lines` | The `PdfTextLine` records, in reading order. |
| `Bounds` | `PdfRectangle` `(0, 0, MaxWidth, height)`, where the height runs from the top of the box to the last line's descender. |
| `Baseline` | The y coordinate of the first line's baseline — what `DrawText` would need to draw the first line itself; zero for a layout constructed with no lines. |
| `LineHeight` | The distance between baselines that was used, after the option/leading/font fallback. |
| `LineCount` | The number of lines, blank lines included. |

Each `PdfTextLine` states `Text`, `Runs`, `Start` (`PdfPoint`), `Width` and `Baseline`. Each `PdfTextRun`
states `Text`, `Start` and `Width`. A run's `Start` is relative to the box, so `Start.X` is the indent of
that word inside the column on a justified line and `0` (or the alignment offset) elsewhere.

Both records have public constructors, so a layout can also be assembled by hand — a table cell, a caption
placed at a computed offset — and drawn with the same call:

```csharp
var amount = "42.00";
var width = heading.Measure(amount, 12);
var line = new PdfTextLine(amount, new PdfPoint(40 - width, 8.6), width, 8.6);
var cell = new PdfTextLayout(style, [line], new PdfRectangle(0, 0, 40, 11.1), 11.1);

page.DrawTextLayout(cell, new PdfPoint(page.Width - 60, 700));   // the line ends at x = 40 in the box
```

A layout of an empty string is one empty line: `LineCount` is 1, `Lines[0].Text` is `""` and nothing is
inked. That keeps "the text was empty" and "the layout was never built" distinguishable.

## Determinism

Laying text out is a pure function of the string, the style and the options. Widths come from the AFM data of
a standard font or the tables of the embedded program, never from the operating system, the culture or a
font-fallback chain, so the same call produces the same lines on Windows, Linux and both target frameworks.
The layouter is static and takes no shared state; a `PdfTextLayout` is immutable, so one may be cached and
drawn on many pages.

Combined with a fixed `CreationDate` and `DocumentId` (Chapter 12), that makes a laid-out page reproducible
byte for byte — which is what a test that compares two builds, or a document that must be reissued
identically, needs.

A character the face cannot encode is refused when the text is *measured*, so a layout cannot succeed and
then fail at `Build()`, and a document is never written with a character missing from a line.

## What it deliberately does not do

This is a text layout, not a typesetting engine:

* **No hyphenation and no dictionary.** A line breaks at spaces only; a word that does not fit is refused,
  broken by character, clipped or continued, as the options say.
* **No shaping and no bidirectional algorithm.** Ligatures and kerning come from the font program; Arabic,
  Hebrew and Indic reordering do not happen, and a right-to-left layout is refused rather than drawn
  backwards.
* **One style per layout.** A bold word inside a sentence needs two layouts, or one layout per run of style,
  placed by hand. A line's `Runs` exist to place and measure words — notably a justified line — not to carry
  per-word styling.
* **No flow.** There are no tables, columns, page breaks, footnotes, floats or text wrapped around an image.
  A block that does not fit a page is a block that runs off it; the caller decides where the next block
  starts.
* **No vertical text and no rotation.** `DrawTextLayout` writes horizontal text; a rotated block needs the
  page canvas transform, which is a drawing concern.
* **No last-line justification** and no shrinking of a gap: justification only ever adds space between words.
* **No overflow warning.** `Clip` and `Continue` are policies the caller states, so they are not reported as
  warnings; the refusal is the exception.

## The surface at a glance

| Member | What it does |
| --- | --- |
| `PdfTypeface.Metrics` | The face's vertical metrics in font units, from its AFM data or its own tables. |
| `PdfTypeface.Measure(text, size)` | The advance width of a string in points. |
| `PdfTypeface.Measure(text, size, characterSpacing, wordSpacing)` | The width as it will be drawn with that text state. |
| `PdfTextMetrics.LineHeight(size)` | Ascent, descent and line gap at a size, in points. |
| `PdfTextLayouter.Layout(text, style, maxWidth)` | Lays text out in a width, left aligned, refusing what does not fit. |
| `PdfTextLayouter.Layout(text, style, options)` | The same, with alignment, line height, overflow and direction stated. |
| `PdfTextLayout.Lines` / `Bounds` / `Baseline` / `LineCount` / `LineHeight` | The result: lines, box, first baseline and the leading that was used. |
| `PdfTextLine.Text` / `Start` / `Width` / `Baseline` / `Runs` | One line, in the layout's own space. |
| `PdfTextRun.Text` / `Start` / `Width` | One contiguous piece of a line, at its own start point. |
| `PdfPageCanvas.DrawTextLayout(layout, origin)` | Draws every run at `origin` plus its own start point. |
| `new PdfTextLayoutOptions(maxWidth, alignment, lineHeight, breakWords, overflow, direction)` | Describes the column and what to do when the text does not fit it. |



