# Contract: Reading and Analysis

**Feature**: `003-pdf-platform-parity` | **Type**: public library API

The existing `IPdfContentReader` stays as it is. Letter-level extraction, configurable layout analysis,
hyperlinks, the object graph and exporters are additive through `IPdfLayoutReader` and
`IPdfObjectInspector`.

## `IPdfLayoutReader`

```csharp
public interface IPdfLayoutReader
{
    Task<PdfReadResult<PdfLetter>> ReadLettersAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<PdfReadResult<PdfWord>> ReadWordsAsync(
        IPdfContentSource source,
        PageRange range,
        PdfWordExtractionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<PdfReadResult<PdfTextBlock>> ReadBlocksAsync(
        IPdfContentSource source,
        PageRange range,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<PdfReadResult<PdfHyperlink>> ReadHyperlinksAsync(
        IPdfContentSource source,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<PdfReadResult<PdfTextBlock>> ExportAsync(
        IPdfContentSource source,
        PageRange range,
        PdfLayoutFormat format,
        Stream destination,
        PdfLayoutOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
```

**Guarantees**

- `ReadLettersAsync` returns one entry per drawn glyph, in content-stream order by default, with
  Unicode text, font size, font name when available, bounding box, baseline start/end and direction.
- A glyph that cannot be mapped is skipped and produces `PdfWarningKind.TextUnreadable`; it is never
  guessed.
- `ReadWordsAsync` uses the existing geometric grouping unless a strategy is selected; a strategy is
  immutable and reusable.
- `ReadBlocksAsync` supports raw content order, nearest-neighbour words, recursive XY cut and
  Docstrum-style segmentation. The selected strategy is recorded in the result.
- `ExportAsync` writes a well-formed document for `PdfLayoutFormat.Alto`, `PageXml`, `Hocr` or
  `SvgText`; coordinates are in page space and text is XML-escaped. It returns the blocks it wrote, with
  the warnings and the strategy that produced them, so a page whose font could not be mapped is exported
  as an empty page plus a `TextUnreadable` warning rather than as silence.

## Extraction Options

```csharp
public sealed record PdfWordExtractionOptions
{
    public PdfWordExtractionStrategy Strategy { get; init; } =
        PdfWordExtractionStrategy.NearestNeighbour;
    public double MaxHorizontalGap { get; init; } = 0.25;
    public double MaxVerticalGap { get; init; } = 0.5;
    public bool IncludeWhitespace { get; init; }
}

public sealed record PdfLayoutOptions
{
    public PdfPageSegmentationStrategy Segmentation { get; init; } =
        PdfPageSegmentationStrategy.RecursiveXYCut;
    public PdfReadingOrderStrategy ReadingOrder { get; init; } =
        PdfReadingOrderStrategy.ColumnAware;
    public bool IncludeImages { get; init; } = true;
    public bool IncludeDecorations { get; init; } = true;
}
```

**Guarantees**

- A strategy never changes the glyph data; it only groups it.
- Raw content order is always available and is the baseline for regression comparison.
- A page with no text yields an empty result, not an error.
- An image with no decodable filter is returned with metadata and `IsDecodable = false`, not dropped.

## Hyperlinks

`PdfHyperlink` carries `Rectangle`, `Uri?`, `Destination?` and `IsExternal`. Exactly one of URI or
destination is set. A link whose destination does not resolve is returned with the name that failed and
a `NamedDestinationUnresolved` warning.

## Object Inspector

```csharp
public interface IPdfObjectInspector
{
    Task<PdfObjectView> ReadTrailerAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);

    Task<PdfObjectView?> ReadObjectAsync(
        IPdfContentSource source,
        int objectNumber,
        string? password = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<PdfContentOperationView> ReadContentOperationsAsync(
        IPdfContentSource source,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default);
}
```

**Guarantees**

- The projection is read-only: no setter, no mutable child collection, no direct reference to an
  internal `PdfObject`.
- A dangling reference is returned as a reference node with `IsResolved = false`; it is not replaced by
  null without a warning.
- A stream’s raw bytes are not returned by the projection; `StreamLength` and `Filter` are. A separate
  payload-copy method, if added, requires an explicit destination and the document’s password.
- Content operations expose operator, operands and offset; inline images are one operation with a
  byte length, not a decoded image.

## Images and Existing Read Side

- `ReadImagesAsync` keeps its current contract: one entry per distinct image object, every page that
  draws it, raw bytes and placement.
- Phase 5 adds `DecodedPixels`/`PixelFormat` only when the filter is supported and the caller explicitly
  asks for pixels; the default stays byte-preserving.
- DCT/JPEG decoding returns RGB or gray samples after the declared colour transform; a CMYK JPEG is
  decoded or refused with the colour space named.
- `/SMask`, `/Mask` and colour-key masks are reported with the base image and are never applied
  silently to the returned pixels.
- An unsupported filter keeps the raw bytes, sets `IsDecodable = false` and reports
  `ImageFilterUnsupported`.

## Export Formats

| Format | Shape | Guarantee |
| --- | --- | --- |
| ALTO | XML per page, text blocks/lines/words, coordinates in page space | Well-formed; schema-valid where a schema is available |
| PAGE XML | XML per page, regions, lines, words, reading order | Well-formed; coordinates preserved |
| hOCR | XHTML with `ocr_page`, `ocr_carea`, `ocr_par`, `ocr_line`, `ocrx_word` | Well-formed XHTML; `bbox` on every text element |
| SVG text | SVG per page with positioned `<text>`/`<tspan>` | Well-formed SVG; no embedded raster, no font embedding |

- Exports never include the source PDF’s raw bytes, encryption dictionary or secrets.
- A page with no text still produces a valid page element with no text children.
- Exported coordinates use a top-left origin, y down, matching the authoring surface; the mapping from
  PDF bottom-left is documented in each export.
- A caller can export a single page or a range; a range is written in ascending page order.

## Diagnostics

| Condition | Result |
| --- | --- |
| Missing `/ToUnicode` | Glyphs skipped, `TextUnreadable` warning names the font |
| Unsupported image filter | Image metadata returned, `ImageFilterUnsupported`, no pixel decode |
| Destination not found | Link/bookmark returned with no page and `NamedDestinationUnresolved` |
| Malformed content stream | Page yields what was read, `ContentStreamUnreadable` warning |
| Dangling object reference | Reference node with `IsResolved = false` |
| Unsupported layout strategy | Refused before opening the source |
| Export destination not writable | `ArgumentException` before work starts |

## Out of Contract

- Page rasterisation, thumbnails or OCR.
- Semantic table extraction, form understanding, reading-order guarantees for arbitrary graphics.
- Handwriting or vector-text recognition.
- A mutable object graph or a general PDF editing API through the inspector.
- Converting a page to an image, Office document, HTML or reflowed text.
- Font shaping or reconstructing text that the source does not encode.
