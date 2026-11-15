# ADR-005: Decoding the image filters the reader passes through

- **Status**: **Accepted** (2026-11-14) — `RunLengthDecode` (2026-11-14) and `CCITTFaxDecode` (2026-11-15)
  implemented; `DCTDecode`, `JPXDecode` and `JBIG2Decode` accepted in that order, one release slice each
- **Date**: 2026-11-14
- **Related**: [`docs/parity.md`](../parity.md) row 14,
  [`docs/manual/src/08-inspection.md`](../../manual/src/08-inspection.md),
  [`ADR-001`](0001-pdf-platform-boundary.md)

## Context

Row 14 of the parity matrix asks the reader to "decode image pixels when asked". It does that for
`FlateDecode` (with predictors), `LZWDecode`, `ASCIIHexDecode`, `ASCII85Decode` and, since this ADR,
`RunLengthDecode`. Every other filter is handed back as stored bytes with `ImageFilterUnsupported` rather
than decoded, which is correct for a filter nobody has written a decoder for — but four of those filters
are common enough that "the caller can read the JPEG itself" is not an answer for a *pixel* API:

| Filter | What it is | Why it matters |
| --- | --- | --- |
| `CCITTFaxDecode` | Group 3 and Group 4 fax coding for bilevel data | the shape a scanned page usually has |
| `DCTDecode` | JPEG (baseline, extended, progressive) | the shape a photograph or a screenshot usually has |
| `JPXDecode` | JPEG 2000 (JP2 or raw codestream) | mandated for PDF/A-2 and later, so archival documents carry it |
| `JBIG2Decode` | JBIG2 generic region, symbol dictionary, text region | the compact shape a scanned text page can have |

`CCITTFaxDecode`, `JPXDecode` and `JBIG2Decode` are **not** bytes a caller can hand to another reader: they
have no container a JPEG viewer accepts. `DCTDecode` is the exception — its bytes *are* a JPEG — which is
why it was the one pass-through this library documented.

## Decision

Each filter is **hand-written**, in this repository, against the specification, with no new package
reference. ADR-001 keeps the product free of a third-party PDF or imaging engine, and the trimming and
Native AOT claims rest on `src/` depending on the base class library alone. A decoder is added in
`PdfFilters` (a partial beside `PdfFilters.Lzw.cs`), wired into `PdfImageFilters.TryDecodeOne`, and exposed
through the same contract `ImagePixels.Of` already applies: `Gray8`, `Rgb24` or `Cmyk32`, a payload that
matches the geometry, and `null` for anything the decoder cannot state.

Shipping order and acceptance criteria:

| Filter | Variants in scope | Acceptance |
| --- | --- | --- |
| `RunLengthDecode` | — | **Done.** Literal and repeat runs, an optional end-of-data byte, a truncated stream decoded as far as it goes. `PdfImageFilterTests` and the `FilterChains` fixture pin it, and the image is exposed as `Gray8`. |
| `CCITTFaxDecode` | Group 3 one-dimensional (`/K` = 0), Group 3 two-dimensional (`/K` > 0), Group 4 (`/K` < 0); `/Columns`, `/Rows`, `/BlackIs1`, `/EncodedByteAlign`, `/EndOfLine` | The decoded bilevel bits equal an independent decoder's output on the same bytes. Evidence: Pillow's CCITT reader through `pikepdf` on the fixture, and a real fax sample in `tests/.../Assets/parity/images`. |
| `DCTDecode` | SOF0, SOF1, SOF2; restart markers; 1, 3 and 4 components; 4:4:4, 4:2:2, 4:2:0; YCbCr and CMYK/YCCK (Adobe APP14); EXIF orientation | Geometry and component count exact; every sample within **2 levels** of a reference decoder (an integer IDCT is allowed to differ by rounding). Evidence: Pillow on the `parity/images/jpeg-*.jpg` fixtures, compared pixel by pixel, with the per-image maximum recorded in `docs/mutation-testing.md`'s sibling measurement note. |
| `JPXDecode` | JP2 and raw J2K codestream; 5/3 reversible and 9/7 irreversible; tiles and tile-parts | Reversible path **bit-exact**; irreversible path within 2 levels. Evidence: `opj_decompress` on the same codestream. |
| `JBIG2Decode` | Generic region, symbol dictionary, text region, MMR | Bit-exact bilevel output. Evidence: `jbig2dec` on the same stream. |

Every slice also lands a manual section (chapter 8's filter table and the image chapter), a sentence in
parity row 14, a changelog entry, and a scoped Stryker run over the new decoder whose survivors are read
and recorded, the way this repository treats every other file.

## Rejected alternatives

- **A package** (`SkiaSharp`, `ImageSharp`, `Magick.NET`): breaks ADR-001 and the "depends on nothing but the
  base class library" claim the AOT smoke app checks; it also puts a native load in the first call that
  reads a page.
- **Shelling out** to `opj_decompress`, `jbig2dec` or `djpeg`: a library that needs an external process to
  read a page is not the product.
- **Leaving `DCTDecode` as a pass-through**: a caller can already read the JPEG from `PdfImage.Data`; what
  it cannot do is get *pixels*, which is what row 14 asks for.

## Consequences

- The order is by frequency: most scanned pages are CCITT G4 or JPEG, so the first two slices cover the
  common case before the archival and text-optimised codecs follow.
- `JPXDecode` and `JBIG2Decode` are the largest. If a slice's acceptance criteria prove too broad (JPX
  tiling, JBIG2 symbol refinement), that slice gets its own ADR rather than widening this one.
- A decoded JPEG or JP2 image stops reporting `IsDecodable=false`, so the `ImagePixels` budget rules apply:
  a payload above the inline budget is measured rather than held, and `PixelFormat` stays `null` for it.
- `PdfFilters.Decode` (the strict route for object streams and attachments) is unchanged: a filter is added
  there only when the specification allows it in an object stream, which `RunLengthDecode` and the four
  image codecs do not.

## Fixtures for the CCITT slice (2026-11-14)

The next slice starts from data rather than from a description. Two strips and the bitmap they must produce
are committed beside the other image fixtures:

- `tests/SharpCorePDF.Tests/Assets/ccitt/ccitt-g4.bin` — 26 bytes, Group 4, a 16 x 16 bilevel fixture
  (a diagonal and a 6 x 6 block), written by Pillow 13.0.0 through libtiff.
- `ccitt-g3.bin` — the same bitmap as Group 3 one-dimensional, 68 bytes.
- `ccitt-expected.bin` — the 32 bytes Pillow decodes for that bitmap (`tobytes()`, 1 is white), rows padded
  to a whole byte; the geometry is 16 x 16.

They sit under `Assets/ccitt`, not `Assets/parity`, because `ParityCorpusTests` treats every file under
`Assets/parity` as a manifest entry.

The generator is the one that produced `lzw-tiff-strip.bin`: `Image.save(format='TIFF',
compression='group4'|'group3')`, then a TIFF IFD read of `StripOffsets` (273) and `StripByteCounts` (279).

Two facts the first attempt established, so the next one does not rediscover them:

1. **The raw bitmap and `tobytes()` are complements.** Pillow's TIFF states a photometric that makes the
   decoded samples the inverse of the mode-`1` pixels, so a decoder that fills black runs with 1 matches
   `ccitt-expected.txt` only under the matching `BlackIs1` value.
2. **A line may start with a zero-length white run.** The transition at position 0 is what a line that
   starts black states; a decoder that records only transitions after the first pixel shifts the parity of
   every later `b1`. Record every transition, including one at 0.
3. **`b1` is measured from the last changing element, not from the paint position.** After a vertical or
   horizontal mode the last transition *is* the new position, so a search of `>= position` selects the
   transition the decoder has just consumed. That is what the Group 4 fixture hits on its twelfth line:
   reference line 11 (`FFEF`) has transitions `[0, 11, 12, 16]`, the decoder reaches position 12 with
   white, and `b1 = 12` makes the stream's `0000010` (V-3) resolve to 9, left of the position. The fix is
   to carry `a0` — starting at -1 and set to the transition after every mode (pass moves it to `b2`,
   horizontal to `a2`, vertical to `a1`) — and to search for the first reference transition **strictly
   greater than `a0`**. With that, line 12's `b1` is 16, V-3 resolves to 13, and the line completes.

Both slices are shipped: the Group 4 fixture decodes every line against this data, the Group 3 fixture
decodes as well, and `PdfCcittFilterTests` pins both.

## Status of the slices

| Slice | State |
| --- | --- |
| `RunLengthDecode` | **Implemented** (2026-11-14) |
| `CCITTFaxDecode` | **Implemented** (2026-11-15) |
| `DCTDecode` | Planned — the next slice |
| `JPXDecode` | Planned |
| `JBIG2Decode` | Planned |
