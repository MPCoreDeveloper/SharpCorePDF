# 5. Page Selection

A page selection says which pages of one source document enter the merged document, and in what order.
It is the only place where the library lets you rearrange pages, and it is deliberately small: a
comma-separated list of one-based ranges.

```csharp
var plan = MergePlan.Create()
    .AddFile("report.pdf", pages: "1,3-5,8-")
    .AddFile("appendix.pdf", pages: "2,1");
```

## The notation

| Text | Meaning | Pages contributed from a 10-page source |
| --- | --- | --- |
| `"1"` | One page | 1 |
| `"2-4"` | A closed range, inclusive | 2, 3, 4 |
| `"3-"` | From a page to the last page | 3, 4, 5, 6, 7, 8, 9, 10 |
| `"-2"` | From the first page to a page | 1, 2 |
| `"1,3-5,8-"` | Several ranges, in this order | 1, 3, 4, 5, 8, 9, 10 |
| `"3,1,3"` | Out of order, with a duplicate | 3, 1 |
| `" 2 - 4 "` | Whitespace around numbers is ignored | 2, 3, 4 |

The rules behind the table:

- **Pages are one-based.** Page 1 is the first page a reader shows. PDF's internal zero-based index
  never appears in this API.
- **Ranges are inclusive at both ends.** `2-4` contains 2, 3 and 4.
- **A range must not run backwards.** `5-2` is a `FormatException`, not an empty selection.
- **A range must start at page 1 or later.** `0-3` is a `FormatException`.
- **Order is preserved.** Ranges are read left to right, and pages inside a range ascend. `"3,1"`
  puts page 3 before page 1.
- **Duplicates collapse on first occurrence.** `"3,1,3"` selects page 3, then page 1; the second 3 is
  dropped because it was already selected. Repeating a page is not how you duplicate it — add the
  source twice instead (Chapter 4).
- **A closed range may pass the last page; an open range may not start past it.** `"2-99"` on a
  four-page document contributes pages 2, 3 and 4 to the merge, and `"9-"` fails because it starts
  after the last page. `Resolve` itself still returns 2 to 99, so a preview must drop numbers greater
  than `PageCount`; the merge does that when it copies pages. A closed range that starts inside the
  document never throws for its tail.
- **The selection counts physical pages, not page labels.** If a document displays "iv" on its fourth
  physical page, select `"4"`, not `"-iv"`. Page labels are a presentation feature and are dropped by
  default anyway (Chapter 6).

Whitespace around the whole text and around each number is accepted. An empty string is not a valid
selection: `PageSelection.Parse("")` throws `FormatException`. The `AddFile` extension treats `null`,
`""` and whitespace as "every page" because it checks before calling `Parse`; if you call
`PageSelection.Parse` yourself, handle the empty case explicitly.

## Building a selection without text

Text is a convenience. The value objects are public, and a selection can be built directly when it
comes from a UI, a database or a calculation.

```csharp
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

PageSelection firstThree = PageSelection.FromTo(1, 3);
PageSelection appendix = PageSelection.FromPage(12);
PageSelection coverAndBack = PageSelection.Of(
    PageRange.Single(1),
    PageRange.ToPage(2),
    PageRange.FromTo(8, 9));
PageSelection everything = PageSelection.All;

var source = PdfSource.Create(new PdfFileSource("report.pdf"), pages: coverAndBack);
```

| Factory | Produces |
| --- | --- |
| `PageSelection.All` | `1-` |
| `PageSelection.Single(page)` | `page` |
| `PageSelection.FromTo(from, to)` | `from-to` |
| `PageSelection.FromPage(from)` | `from-` |
| `PageSelection.Of(params PageRange[])` | The ranges in the given order |
| `PageRange.Single(page)` | One range of one page |
| `PageRange.FromTo(from, to)` | A closed range |
| `PageRange.FromPage(from)` | An open-ended range |
| `PageRange.ToPage(to)` | A range from page 1 |
| `PageRange.All` | `1-` |
| `PageSelection.Parse(text)` / `TryParse` | A selection from the notation |
| `PageRange.Parse(text)` / `TryParse` | A single range from the notation |

`PageSelection` is immutable. `Add` appends a range and `Concat` appends another selection, both
returning a new instance. `Ranges` exposes the ranges, `RangeCount` their number and `IsAll` tells you
whether the selection is the `1-` shortcut.

## Resolving against a document

A selection is document-independent until it is resolved. `Resolve(pageCount, sourceName)` returns the
concrete page numbers, in merge order and without duplicates, and throws
`MergePlanValidationException` when the document has no pages or a range starts past its end.

```csharp
var info = await inspector.InspectAsync(new PdfFileSource("report.pdf"));
var selection = PageSelection.Parse("1,3-5,8-");

IReadOnlyList<int> pages = selection.Resolve(info.PageCount, info.SourceName);

Console.WriteLine(string.Join(", ", pages));
```

You rarely call `Resolve` in application code. The merger resolves every selection while it prepares
the merge, before the engine writes pages, and the same resolution rules apply. Calling it yourself is
useful for a preview — "this plan will produce 14 pages" — or for rejecting a request before a
destination file exists.

Two properties are worth knowing when you build that preview:

- `ResolveCount(pageCount, sourceName)` returns `Resolve(...).Count`. It counts the *selected numbers*,
  including a closed range's tail beyond the document, so it can overstate what is written. For an
  exact count, count only the resolved numbers at most `pageCount`, or use the `PageCount` of a completed
  merge.
- Selection is not an I/O optimisation. The reader still reads the whole source document; the
  selection decides which pages are copied into the result, not how much of the file is read.

## Preflight instead of exceptions

When page ranges come from a user, checking them before the merge produces a better message than
catching the exception afterwards. The inspection model makes that a few lines:

```csharp
var info = await inspector.InspectAsync(new PdfFileSource(path));

if (!info.IsReadable)
{
    return $"'{path}' cannot be read: {info.Warnings.FirstOrDefault()?.Message}";
}

if (PageSelection.TryParse(requestedPages, out var selection))
{
    try
    {
        var pages = selection.Resolve(info.PageCount, info.SourceName);
        Console.WriteLine($"{pages.Count} of {info.PageCount} pages selected.");
    }
    catch (MergePlanValidationException exception)
    {
        Console.WriteLine(exception.Message);
    }
}
```

The preflight and the merge share the same `Resolve` implementation, so an accepted selection is
accepted by the merge. That is the advantage of the value object being public: you do not have to
reimplement its rules in a validator.

## Common shapes

```csharp
// Everything, explicitly.
.AddFile("a.pdf", pages: "1-")

// Everything except the cover, then the cover at the end.
.AddFile("report.pdf", pages: "2-")
.AddFile("report.pdf", pages: "1", displayName: "Cover")

// A chapter per source, each starting at its own page 1.
.AddFile("chapter-01.pdf", pages: "1-")
.AddFile("chapter-02.pdf", pages: "1-")
.AddFile("chapter-03.pdf", pages: "1-")

// Two specific pages in reverse order.
.AddFile("spread.pdf", pages: "2,1")
```

The third shape is the one to reach for when documents are already split into chapters: the merge plan
mirrors the file list, and no page arithmetic is needed. The second shape shows that a document may
appear twice; each appearance is a separate source with its own selection and display name.

## A note on bookmarks and links

When a document is partially selected, bookmarks, links and form fields that point at pages outside
the selection still need a destination. The library points them at the first page the source
contributes instead of dropping the whole bookmark or link. The result opens, the navigation stays
usable, and the destination is approximate by design.

If a bookmark's destination cannot be resolved at all — for example because it refers to an object the
source itself does not contain — it is skipped and a warning is recorded. Chapter 9 lists the warning
kinds that can appear for bookmarks, named destinations and page labels.
