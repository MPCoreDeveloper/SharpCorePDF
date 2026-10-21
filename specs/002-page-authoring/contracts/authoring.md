# Contract: Authoring

The public surface of `SharpCorePDF.Infrastructure` that writes a document. Every member is in the
approved-API snapshot; the XML documentation on the types is the authoritative wording.

## Document

```csharp
public sealed class PdfDocumentBuilder
{
    public static PdfDocumentBuilder Create(PdfAuthoringOptions? options = null);

    public PdfAuthoringOptions Options { get; }
    public PdfMetadata Metadata { get; }
    public int PageCount { get; }

    public PdfDocumentBuilder WithMetadata(PdfMetadata metadata);
    public PdfDocumentBuilder AddPage(PdfPageSize size, Action<PdfPageCanvas>? draw = null);

    public byte[] ToArray();
    public Task SaveAsync(Stream destination, CancellationToken cancellationToken = default);
    public PdfByteArraySource Build(string displayName = "authored.pdf");
}
```

* `Build` writes the document now and returns `PdfByteArraySource`, which implements
  `IPdfContentSource`. The bytes do not change when the builder is used afterwards.
* `ToArray` and `Build` throw `InvalidOperationException` when no page exists or the options are not
  usable. `SaveAsync` throws `ArgumentException` for a destination that is not writable and leaves it
  open.
* The builder is mutable and not thread safe; a typeface is immutable and may be shared.

## Page

```csharp
public sealed class PdfPageCanvas
{
    public PdfPageSize Size { get; }
    public double Width { get; }
    public double Height { get; }

    public PdfPageCanvas DrawText(
        string text, double x, double y, PdfTypeface typeface, double fontSize, PdfColor? color = null);

    public PdfPageCanvas DrawImage(ReadOnlyMemory<byte> png, double x, double y, double width, double height);
    public PdfPageCanvas DrawImage(ReadOnlyMemory<byte> png, double x, double y);
}
```

### Coordinate contract

| Axis | Origin | Direction | Unit |
| --- | --- | --- | --- |
| x | left edge | right | point (1/72 inch) |
| y | top edge | down | point (1/72 inch) |
| text `y` | baseline of the line | — | — |
| image `(x, y)` | top-left corner of the image | — | — |

The conversion to PDF's bottom-left origin is internal. Coordinates are not clamped: content may fall
outside the page, as it may in a PDF produced by any tool.

### Validation

| Input | Refusal |
| --- | --- |
| `null` text or typeface | `ArgumentNullException` |
| non-finite coordinate | `ArgumentOutOfRangeException` |
| font size ≤ 0 or non-finite | `ArgumentOutOfRangeException` |
| image width or height ≤ 0 or non-finite | `ArgumentOutOfRangeException` |
| a line break in one text call | `ArgumentException` ("one line", draw each line separately) |
| empty text | accepted; draws nothing |
| a character the face cannot encode | `ArgumentException` at write time, naming the character and code point |
| a PNG that is empty, not a PNG, interlaced or 16-bit | `ArgumentException` / `NotSupportedException` at `DrawImage` |

## Typeface

```csharp
public sealed class PdfTypeface
{
    public static PdfTypeface Standard(StandardFont family);
    public static PdfTypeface FromTrueType(ReadOnlyMemory<byte> fontProgram);

    public StandardFont? Family { get; }
    public bool IsEmbedded { get; }
    public string Name { get; }
    public ReadOnlyMemory<byte>? FontProgram { get; }
}
```

* `Standard` accepts the twelve defined faces; anything else is `ArgumentOutOfRangeException`.
* `FromTrueType` copies and parses the bytes immediately. An empty or malformed program throws
  `ArgumentException`; an OpenType/CFF program throws `NotSupportedException`.
* An embedded face is subsetted per document to the glyphs that document draws. Glyph ids are
  preserved. A `/ToUnicode` map and a width array are written.
* No system font is read at any point.

## Colour and standard fonts

```csharp
public readonly record struct PdfColor(byte Red, byte Green, byte Blue)
{
    public static PdfColor Black { get; }
    public static PdfColor White { get; }
    public static PdfColor FromRgb(byte red, byte green, byte blue);
    public static PdfColor FromHex(string hexadecimal);
}

public enum StandardFont
{
    Helvetica, HelveticaBold, HelveticaOblique, HelveticaBoldOblique,
    TimesRoman, TimesBold, TimesItalic, TimesBoldItalic,
    Courier, CourierBold, CourierOblique, CourierBoldOblique,
}
```

A standard face is a `/Type1` font with `/WinAnsiEncoding`. Text outside Windows-1252 is refused; an
embedded face is the answer for anything else.

## Options

```csharp
public sealed record PdfAuthoringOptions
{
    public static PdfAuthoringOptions Default { get; }

    public DateTimeOffset? CreationDate { get; init; }
    public string? DocumentId { get; init; }        // 32 hexadecimal digits
    public string Producer { get; init; }

    public IReadOnlyList<string> Validate();
}
```

* `CreationDate` null means `DateTimeOffset.UtcNow`; `ModDate` follows `PdfMetadata.ModificationDate`,
  or the creation date.
* `DocumentId` null means the identifier is derived from the document (version, date, page sizes,
  content, image bytes and font programs). An explicit identifier is written twice in the trailer.
* With both fixed, two builds of the same pages are byte-identical.
* `Producer` must be non-empty; it is written after custom metadata entries and cannot be overridden
  by one of them.

## Document guarantees

* PDF 1.7, classic cross-reference table, one flat page tree, `/MediaBox [0 0 w h]`, no rotation.
* Content streams are uncompressed; image and font streams are Flate compressed.
* An image and a standard face are written once per document; an embedded face is one subset that
  collects the glyphs of every page.
* The result is read by `PdfDocumentReader`, inspected by `IPdfInspector` and merged by `IPdfMerger`
  like any other source.
* Not guaranteed: PDF/A or PDF/UA conformance, encryption of the authored page, word wrap, vector
  drawing, JPEG, forms, annotations or a text shaper.
