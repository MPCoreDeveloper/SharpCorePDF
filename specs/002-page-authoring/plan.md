# Implementation Plan: Authoring a Page

**Feature**: `002-page-authoring` | **Spec**: [spec.md](./spec.md) | **Status**: Implemented

## Summary

Add a bounded authoring capability to the existing engine: an in-memory `PdfDocumentBuilder` that adds
pages, draws text with a standard or an embedded TrueType font, and places PNG images, then writes a
PDF 1.7 document that the merge engine can read like any other source. The writer reuses the existing
`PdfWriter`, `PdfSerializer`, `PdfDictionary` model and `PdfByteArraySource`; no new package or
dependency is introduced.

## Technical Context

- **Targets**: `net10.0` and `net11.0` (C# 14 / C# 15 preview), like the rest of `src/`.
- **Dependencies**: the .NET base class library only. `System.IO.Compression.ZLibStream` handles Flate;
  `System.Security.Cryptography` handles the document identifier and the subset tag.
- **Layers**: `PdfColor` and `StandardFont` are domain value objects; everything else is infrastructure.
- **Testing**: xUnit v3 on Microsoft.Testing.Platform, both legs; a checked-in 17.5 KB DejaVu Sans
  subset as the real-world font fixture; PNGs built in test code.
- **Validation**: `pikepdf` (which embeds qpdf) for `--check`, `pypdf` for strict parsing and text,
  PyMuPDF for a render, `fontTools` for the embedded subset's tables and checksums.

## Structure Decision

A builder is a pure factory: it owns no connection, file, clock or configuration, so it needs no port
and no DI registration. `Build()` returns the existing `PdfByteArraySource`, which implements the
`IPdfContentSource` port the merge side already consumes. That keeps the authoring code out of the
Domain and Application layers except for two value objects, and it means a cover can be written,
inspected, merged or discarded without the container being involved.

```text
src/SharpCorePDF.Domain/ValueObjects/
    PdfColor.cs                    RGB colour, eight bits per component
    StandardFont.cs                the twelve standard faces
src/SharpCorePDF.Infrastructure/
    PdfDocumentBuilder.cs          pages, metadata, ToArray / SaveAsync / Build
    PdfPageCanvas.cs               DrawText / DrawImage, operations
    PdfTypeface.cs                 standard face or parsed TrueType program
    PdfAuthoringOptions.cs         creation date, document ID, producer
    Authoring/
        PdfAuthoringWriter.cs      catalog, page tree, info, trailer, identifier
        PageContentWriter.cs       content stream, top-left to PDF coordinates
        AuthoringResources.cs      font and image objects shared by pages
        FontResource.cs            Type1 or Type0 + descriptor + program + ToUnicode
        PageResources.cs           one page's /Font and /XObject dictionaries
        PngImage.cs                PNG parser, predictor pass-through, alpha split
        TrueTypeFont.cs            cmap, metrics, outlines, name
        TrueTypeSubsetWriter.cs    glyf/loca rebuild and sfnt checksums
```

## Key Decisions

1. **Top-left coordinates, documented.** `DrawText(x, y)` places the baseline `y` points below the top
   edge; `DrawImage(x, y)` places the top-left corner. The writer converts to PDF's bottom-left origin.
   This is the coordinate system a layout is written in and the one PdfSharp uses, so a migration needs
   no arithmetic.
2. **The twelve standard fonts, with WinAnsi, and no fallback.** A standard font needs no file and is
   written as `/Type1` + `/WinAnsiEncoding`. A character outside Windows-1252 is refused, never replaced
   by `?`, because a silent change of text is worse than a failed build.
3. **Identity-preserving subsets.** The embedded font keeps the glyph numbers of its source. A subset
   that renumbers glyphs must rewrite every composite glyph and every code already written into a
   content stream; keeping the numbers lets content carry the glyph id directly and lets a composite
   glyph be copied byte for byte. Unused outlines are still omitted: the DejaVu subset is 5.3 KB.
4. **`Type0` + `Identity-H` + `ToUnicode`.** Two-byte glyph ids are written as hexadecimal strings in
   the content stream. The CMap maps each code to its character, which is what makes `pypdf`,
   `pdftotext` and `IPdfContentReader` return `é` rather than a glyph number.
5. **PNG, no imaging dependency.** `IDAT` is embedded as it stands with `/Predictor 15` unless the image
   has alpha or palette transparency, in which case it is inflated row by row, unfiltered (all five PNG
   filters), split, and recompressed with filter 0 rows. A palette with `tRNS` is expanded; a `tRNS`
   colour key on grey/RGB becomes `/Mask`. Interlacing and 16-bit samples are refused with an exception.
6. **One object per image and per face.** Images are deduplicated by a digest of their prepared bytes,
   dimensions and colour space; standard faces by their enum value; embedded faces by instance. The
   same logo on ten pages is one image XObject.
7. **Deterministic identity.** The hash covers the version, the creation date, page sizes, content
   streams, image bytes and font programs; the first 16 bytes become `/ID`. Supplying `CreationDate`
   and `DocumentId` makes two builds byte-identical. No timestamp, path or machine name enters the file.
8. **No `MeasureString`.** Metrics belong to the font and to a shaper; the API says so rather than
   shipping an approximation that would drift from the rendered width.
9. **Uncompressed content streams.** A cover's content is a few kilobytes; compressing it would make
   the output harder to inspect and save little. Image and font programs are Flate compressed.
10. **Reuse the merge writer.** `PdfWriter`, `PdfSerializer`, `PdfDictionary`, `PdfStream` and the
    classic cross-reference table are the ones the merge already produces, so an authored document is
    read by the same reader and passes the same checks.

## Embedded Font Pipeline

1. `TrueTypeFont.Parse` validates the sfnt version (TrueType only, `OTTO` is refused), reads the table
   directory, requires `head, hhea, maxp, hmtx, cmap, loca, glyf`, and chooses the best `cmap` subtable
   (format 12, then 4, then 6, then 0, preferring Windows Unicode encodings).
2. `Encode` maps every rune through the `cmap`. A missing glyph refuses the document with its
   character and code point. The glyph id is recorded and written as two big-endian bytes.
3. `TrueTypeSubsetWriter.Build` adds glyph 0, closes over composite components, rebuilds `glyf` with
   only the used outlines (four-byte aligned), rebuilds `loca` in short or long form as the size needs,
   updates `head.indexToLocFormat`, copies `cmap`, `hmtx`, `hhea` and `maxp` unchanged, copies
   `OS/2`, `post`, `name`, `cvt `, `fpgm`, `prep` and `gasp` when present, sorts the table directory,
   recomputes every table checksum and the whole-program `checkSumAdjustment`.
4. The PDF side writes a `/Type0` font with `/Encoding /Identity-H`, a `/CIDFontType2` descendant with
   `/W` widths in thousandths of an em, a descriptor with a scaled `/FontBBox`, `/Ascent`,
   `/Descent` and `/CapHeight`, a `/FontFile2` stream with `/Length1`, and a `/ToUnicode` CMap of
   `bfchar` entries in blocks of at most 100.
5. The subset tag is six deterministic uppercase letters derived from SHA-256 of the program, so two
   builds of the same font produce the same `/BaseFont`.

## PNG Pipeline

1. `PngImage.Parse` validates the signature and `IHDR` (eight bits, no interlace, colour type 0/2/3/4/6),
   collects `PLTE`, `tRNS`, `pHYs` and every `IDAT`, and requires `IEND`.
2. No transparency: the concatenated `IDAT` is embedded with `/FlateDecode` and a predictor of 15,
   `Colors` 1 or 3, `Columns` = width. No decode and no recompression.
3. Alpha (type 4 or 6): inflate, unfilter rows (None, Sub, Up, Average, Paeth), split colour and alpha,
   compress each with filter-0 rows, write `/SMask` as a `/DeviceGray` image.
4. Palette with `tRNS`: inflate and unfilter indices, expand to RGB from `PLTE`, build an alpha row per
   pixel, write RGB plus `/SMask`.
5. `tRNS` on grey/RGB: keep the samples and write the PDF colour-key `/Mask` array.
6. `pHYs` with unit 1 gives natural points: `pixels × 72 × 39.37007874015748 / pixelsPerMetre`.
7. A digest of the prepared colour data, alpha, dimensions and colour space is the dedupe key.

## Testing

| Level | What it proves |
| --- | --- |
| `PdfDocumentBuilderTests` (23 tests) | A4 size and text, all six accents, embedded font markers and extraction, the issue's cover, RGBA `/SMask` and its samples, palette + `tRNS`, greyscale + alpha, RGB colour key, `pHYs` natural size, image dedupe, merge in front, byte-identical repeat build, metadata, `SaveAsync`, and every refusal. |
| Read side | Every assertion goes through `PdfDocumentReader` / `IPdfContentReader`, so a document that is only right in its own bytes fails. |
| External | `pikepdf` `check_pdf_syntax()` = `[]`; `pypdf` strict parse and exact accented text; PyMuPDF render shows the logo and its transparent margin; `fontTools` parses the subset with checksum verification. |
| Both legs | The full suite is 549 tests on `net10.0` and on `net11.0`; Release build 0 warnings. |

## Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| A subset that a renderer rejects | Rebuilt tables and checksums, verified with `fontTools` and PyMuPDF; composite glyphs copied unchanged because ids are preserved. |
| Text that extracts as glyph ids | `/ToUnicode` for every embedded glyph; external `pypdf` assertion, not only the library's own reader. |
| A PNG whose filters or chunks differ from the fixture | All five filters implemented; synthetic RGBA, RGB, greyscale and palette PNGs cover every supported colour type; interlaced and 16-bit are refused. |
| Standard font text that silently changes | Characters outside WinAnsi are refused, with the character and code point. |
| Different bytes between runs | Creation date is explicit in tests; the identifier hashes content only; no clock, path or machine data is written. |
| Font licensing | The library ships no font; tests and examples carry a DejaVu subset under the Bitstream Vera licence with the licence file beside it. |

## Recorded Cost

The example builds a cover with a logo, an RGBA mask, a standard font and an embedded subset, merges it
in front of a two-page document and reads page 1 back: **225–360 ms** end to end on the recorded machine
(`Release`, `net11.0`), with an 8.6 KB cover before merging and a 5.3 KB embedded subset. Both the font
and the image are written once per document, however many pages use them.

## Documentation

`docs/manual/src/12-authoring.md` is the chapter; `13-cookbook.md` gains the cover recipe; the preface,
the "what it is not" chapter, `docs/README.md` and `README.md` are updated; `CHANGELOG.md` records the
addition; `examples/SharpCorePDF.Examples/AuthoringExamples.cs` is the runnable version of the cover.
