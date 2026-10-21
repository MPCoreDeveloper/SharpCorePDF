# Contract: Authoring

**Feature**: `003-pdf-platform-parity` | **Type**: public library API

This contract describes outcomes, not final parameter names. Names are fixed during the Phase 0 API
review and then snapshotted. Existing `PdfDocumentBuilder`, `PdfPageCanvas` and `PdfTypeface` members
keep their current behaviour; the members below are additive.

## `PdfDocumentBuilder`

```csharp
public sealed class PdfDocumentBuilder
{
    public static PdfDocumentBuilder Create(PdfAuthoringOptions? options = null);

    public PdfDocumentBuilder WithMetadata(PdfMetadata metadata);
    public PdfDocumentBuilder WithXmp(PdfXmpMetadata xmp);
    public PdfDocumentBuilder WithOutline(PdfOutline outline);
    public PdfDocumentBuilder WithPdfA(PdfAProfile profile);

    public PdfDocumentBuilder AddPage(PdfPageSize size, Action<PdfPageCanvas>? draw = null);
    public PdfDocumentBuilder ImportPage(IPdfContentSource source, int pageNumber);
    public PdfDocumentBuilder ImportPages(IPdfContentSource source, PageRange range);

    public byte[] ToArray();
    public Task SaveAsync(Stream destination, CancellationToken cancellationToken = default);
    public PdfByteArraySource Build(string displayName = "authored.pdf");
}
```

**Guarantees**

- A builder with no page refuses `ToArray`, `SaveAsync` and `Build` with an actionable exception.
- Imported pages preserve content, resources, annotations, rotation and geometry; inherited attributes
  are materialised on the imported page.
- `WithPdfA` and encryption are mutually exclusive; the conflict is reported by `Validate`.
- The same inputs and fixed `CreationDate`/`DocumentId` produce identical bytes.

## `PdfPageCanvas`

```csharp
public sealed class PdfPageCanvas
{
    public double Width { get; }
    public double Height { get; }

    public PdfPageCanvas DrawLine(PdfPoint from, PdfPoint to, PdfStroke stroke);
    public PdfPageCanvas DrawRectangle(PdfRectangle rectangle, PdfPaint? fill, PdfStroke? stroke);
    public PdfPageCanvas DrawEllipse(PdfRectangle bounds, PdfPaint? fill, PdfStroke? stroke);
    public PdfPageCanvas DrawPath(PdfPath path, PdfPaint? fill, PdfStroke? stroke);
    public PdfPageCanvas DrawText(string text, double x, double y, PdfTypeface typeface,
        double fontSize, PdfColor? color = null);
    public PdfPageCanvas DrawTextLayout(PdfTextLayout layout, PdfPoint origin);
    public PdfPageCanvas DrawImage(PdfImageSource image, double x, double y);
    public PdfPageCanvas DrawImage(PdfImageSource image, double x, double y,
        double width, double height);
    public PdfPageCanvas DrawImage(ReadOnlyMemory<byte> encodedImage, double x, double y);

    public PdfPageCanvas SaveState();
    public PdfPageCanvas RestoreState();
    public PdfPageCanvas SetClip(PdfPath path, PdfFillRule fillRule = PdfFillRule.NonZero);
    public PdfPageCanvas SetTransform(PdfMatrix matrix);
}
```

**Guarantees**

- Coordinates are top-left, y down, as in 1.0. The writer converts to PDF user space.
- `DrawImage(ReadOnlyMemory<byte>)` detects PNG, JPEG, BMP and GIF by signature; an unknown signature
  is refused with the first bytes named.
- `SaveState`/`RestoreState` are balanced; an unbalanced restore is refused at the call site.
- A drawing call never silently clips or substitutes a colour/glyph. A refused value leaves the canvas
  unchanged.
- Operations are recorded, not written immediately; the builder writes them once per page.

## `PdfTypeface`

```csharp
public sealed class PdfTypeface
{
    public static PdfTypeface Standard(StandardFont family);
    public static PdfTypeface FromTrueType(ReadOnlyMemory<byte> fontProgram);

    public string Name { get; }
    public bool IsEmbedded { get; }
    public PdfTextMetrics Metrics { get; }

    public double Measure(string text, double fontSize);
    public double Measure(string text, double fontSize, double characterSpacing,
        double wordSpacing);
}
```

**Guarantees**

- Metrics come only from the supplied program; no system font is consulted.
- A character without a glyph is refused with the character and code point.
- The same program and text produce the same measurement on both target frameworks.
- Complex-script shaping is not performed; `PdfTextDirection.RightToLeft` is refused with an explanation
  when the font cannot supply the required glyph ordering.

## Image Sources

```csharp
public static class PdfImage
{
    public static PdfImageSource Open(ReadOnlyMemory<byte> encoded);
    public static PdfImageSource FromJpeg(ReadOnlyMemory<byte> jpeg);
    public static PdfImageSource FromPng(ReadOnlyMemory<byte> png);
    public static PdfImageSource FromBmp(ReadOnlyMemory<byte> bmp);
    public static PdfImageSource FromGif(ReadOnlyMemory<byte> gif);
}
```

**Guarantees**

- A JPEG with no EXIF orientation and no CMYK/Adobe transform is embedded with its original
  `DCTDecode` bytes; `IsPassthrough` is true.
- A JPEG that needs orientation or colour conversion is decoded, transformed and re-encoded, and the
  reason is reported.
- PNG alpha becomes `/SMask`; palette + `tRNS` is expanded; 16-bit and Adam7 are supported or refused by
  name in the Phase 1 test matrix, not silently mishandled.
- GIF uses the first frame and reports that later frames were ignored through a warning.
- Unsupported filters (`JPXDecode`, `JBIG2Decode`, `CCITTFaxDecode` when not implemented) are refused
  with the filter name.

## Text Layout

```csharp
public sealed record PdfTextStyle(
    PdfTypeface Typeface,
    double FontSize,
    PdfColor Color,
    double Leading = 0,
    double CharacterSpacing = 0,
    double WordSpacing = 0);

public sealed record PdfTextLayoutOptions(
    double MaxWidth,
    PdfTextAlignment Alignment = PdfTextAlignment.Left,
    double? LineHeight = null,
    bool BreakWords = false,
    PdfTextOverflow Overflow = PdfTextOverflow.Refuse);

public sealed class PdfTextLayout
{
    public IReadOnlyList<PdfTextLine> Lines { get; }
    public PdfRectangle Bounds { get; }
    public double LineHeight { get; }
}
```

**Guarantees**

- Wrapping is greedy and deterministic; a line never exceeds `MaxWidth` unless a single unbreakable
  word is wider and `BreakWords` is false, in which case the call is refused.
- `LineHeight` defaults to the font’s ascent + descent + line gap at the requested size.
- Alignment is applied within `MaxWidth`; justification only adjusts inter-word space.
- Text extraction returns the same characters that were drawn, including accents and spaces.

## Diagnostics

- A refused operation throws `ArgumentException`, `ArgumentOutOfRangeException` or
  `PdfUnsupportedFeatureException` at the call site, with the offending value in the message.
- A decision that changes output but is not a failure (EXIF rotation applied, GIF later frames ignored,
  a fallback line break) is reported through `PdfWarning` with a stable `PdfWarningKind`.
- No authored page reports success while silently dropping a character, image, path or colour.

## Out of Contract

- HTML/CSS layout, tables, automatic page breaks, footnotes, columns and text shaping.
- SVG input, JPEG 2000, TIFF, WebP and animated GIF output.
- System fonts, font fallback and font licensing.

