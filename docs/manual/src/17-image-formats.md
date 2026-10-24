# 17. Image Formats

> **Availability.** This chapter documents the image surface `1.2` adds: the JPEG pass-through, the BMP and
> GIF readers, and the colour profile a PNG may state. `1.1.0` embedded PNG images only — Chapter 12
> describes the authoring surface as it stands today — so every reader here is new API, and the format a
> caller draws is now a choice rather than a restriction.

A page shows a picture by naming a file, and everything after that — the bytes the document stores, the
colour space it declares, the mask that makes part of it transparent — is decided by the format of that
file. This chapter is the reference for those decisions: what SharpCorePDF reads out of a PNG, a JPEG, a
BMP and a GIF, what it refuses by name, and why a logo drawn on forty pages is stored once.

Chapter 12 covers the drawing surface a page offers and how an image is placed in it; this chapter is
about the file behind the call.

## One call, four formats

```csharp
var logo = PdfImage.Open(File.ReadAllBytes("logo.png"));   // the format comes from the signature

page.DrawImage(logo, x: 40, y: 30, width: 152, height: 87); // an explicit size in points
page.DrawImage(logo, x: 40, y: 30);                         // the size the file states

page.DrawImage(pngBytes, x: 40, y: 30, width: 152, height: 87); // the same, from bytes
```

`PdfImage.Open` reads the first bytes and never the file name: a `.png` whose content is a JPEG is read as
a JPEG, and a file whose content is neither is refused with those first bytes in the message. `FromPng`,
`FromJpeg`, `FromBmp` and `FromGif` do the same read with the format stated, so a caller that can vouch
for a file gets a refusal when the file disagrees instead of a picture read as something it is not.

An opened image reports what the file holds:

| Member | What it states |
| --- | --- |
| `Format` | `Png`, `Jpeg`, `Bmp` or `Gif`, as read from the signature. |
| `WidthPixels`, `HeightPixels` | The pixel size of the samples. |
| `ColorSpace` | The colour space of the samples: `Gray`, `Rgb`, `Cmyk`, `Indexed` or `IccBased`. |
| `BitsPerComponent` | Eight for every format this release reads. |
| `HasAlpha` | Whether the file states transparency, as a soft mask or a colour key. |
| `NaturalWidth`, `NaturalHeight` | The size in points the file's resolution metadata implies. |
| `FrameCount` | The number of frames: one for every format but GIF. |
| `Key` | A stable identifier of the prepared bytes, used to store one image object per picture. |

Opening the same bytes twice produces the same `Key`, so the document carries one image object however
many pages and however many calls draw it. The key covers everything that changes what a reader shows —
the samples or the encoded bytes, the filter, the colour space, the mask, the decoding and any colour
profile — so two pictures that differ in a way a reader can see are never folded together.

## PNG

A PNG is read at eight bits per sample, without interlacing, of colour type 0 (greyscale), 2 (RGB), 3
(palette), 4 (greyscale and alpha) or 6 (RGBA). Sixteen bit samples and Adam7 interlacing are refused
with a message that says which one the file uses and what to do about it, because decoding them halfway
would show a picture nobody asked for.

Transparency arrives in three shapes, and each becomes the structure PDF defines for it:

* an alpha channel of colour type 4 or 6 is split out of the samples into a `/SMask` stream;
* a palette whose `tRNS` chunk makes entries transparent is expanded to RGB, because PDF transparency is
  per sample and not per palette entry, and the alpha becomes a `/SMask` as well;
* a `tRNS` colour key on a greyscale or RGB image becomes the `/Mask` array — a minimum and a maximum
  for each component, which is how PDF states "this colour is transparent" — and the samples are stored
  exactly as the file holds them.

Compressed data is kept when it does not have to be decoded. An image without transparency keeps its
`IDAT` bytes and is embedded with `FlateDecode` and a PNG predictor, so a 67 KB logo is a 67 KB stream in
the document and nothing is inflated and deflated again. Only an image whose samples have to be split —
an alpha channel, an expanded palette — is decoded and re-compressed.

The `pHYs` chunk states the resolution: with the metre unit it becomes the natural size in points, so a
PNG at 11811 pixels per metre draws 300 dots to the inch.

### Colour profiles in a PNG

A PNG may state the colour space its samples are measured in, in an `iCCP` chunk. That profile is
declared rather than dropped: it is written as its own Flate-compressed stream, the image's colour space
becomes `[/ICCBased …]`, and `/N` states the number of components the profile describes.

```text
13 0 obj   % the image
<< /Type /XObject /Subtype /Image /Width 152 /Height 87
   /ColorSpace [/ICCBased 14 0 R] /BitsPerComponent 8 /Filter /FlateDecode … >>
14 0 obj   % the profile, exactly as the file carried it
<< /N 3 /Alternate /DeviceRGB /Filter /FlateDecode >>
```

Leaving such a picture as device values would show it through whatever profile the viewer happens to
use, which is a colour shift no one is told about — the silent substitution this library refuses. The
alternate space is stated too, so a reader that cannot use the profile still knows what the samples are
meant to be, and `PdfImage.ColorSpace` reports `IccBased` so a caller can see the picture is managed.

A profile that does not describe the samples is refused by name rather than written: a `GRAY` profile on
an RGB image, or a `CMYK` profile in a PNG, would describe the picture wrongly. A `sRGB` chunk needs no
work, because an unlabelled PNG is already read as sRGB — the same values, stated twice. `gAMA` and
`cHRM` chunks are not used: they describe a space the samples would have to be converted from, and this
release does not convert.

## JPEG

A JPEG is passed through. The file's own bytes become the image stream, decoded by `DCTDecode`, so no
entropy-coded data is walked, decoded or re-encoded, and nothing is lost between the file and the page.

| What the file states | What the document holds |
| --- | --- |
| One component | `/ColorSpace /DeviceGray` |
| Three components | `/ColorSpace /DeviceRGB` |
| Four components with an Adobe `APP14` transform | `/ColorSpace /DeviceCMYK` and `/Decode [1 0 1 0 1 0 1 0]` |

The `/Decode` array is there because Adobe wrote those samples inverted; without it a CMYK photograph
would come out as its negative.

`SOF0` (baseline), `SOF1` (extended sequential) and `SOF2` (progressive) frames are read; arithmetic
coding, lossless, differential and hierarchical frames are refused by name, as is a sample precision
other than eight bits. An EXIF orientation other than the identity is refused too: honouring it means
rotating or mirroring the samples and re-encoding them, which is a different operation from placing a
picture. A four-component file with no Adobe transform is refused, because whether the samples are
inverted cannot be known; five or more components are outside what PDF's device colour spaces can name.

The JFIF density states the natural size, in dots per inch or per centimetre.

## BMP

A BMP is a pixel format PDF has no filter for, so it is decoded: eight bit samples through a palette, or
twenty-four or thirty-two bit samples, with `BITMAPINFOHEADER` or a later, longer header, and with either
row order. The palette is expanded to RGB, the rows are written top row first, and the samples are
compressed with `FlateDecode` and a PNG predictor.

The fourth byte of a thirty-two bit pixel is padding, not transparency: a BMP cannot state an alpha
channel this reader would honour, and inventing one would make an opaque picture partly invisible.

`BI_RLE8`, `BI_RLE4`, `BI_BITFIELDS`, the headers that embed another image, one, four and sixteen bit
samples, and the OS/2 core header are refused with the name of what was found. The pixels-per-metre
fields state the natural size.

## GIF

A GIF is read frame by frame, of which the first is drawn — PDF has no animation. The LZW codes are
decoded into palette indices, a global or a local palette is expanded to RGB, an interlaced frame is put
back into row order, and a graphic control extension that names a transparent palette entry becomes a
`/SMask`.

`FrameCount` states how many frames the file holds; the others are counted, not decoded. That count is
how a caller learns that a picture it drew is an animation the document cannot carry — the authoring
side has no warning channel, unlike the read side, where the same fact arrives as a `PdfWarning`.

A GIF states no resolution, so it is drawn at one point per pixel unless a size is given.

## Natural size, placement and stretching

Without a size, an image is drawn at the size its own metadata implies: `pHYs` for a PNG, the JFIF
density for a JPEG, the pixels-per-metre fields for a BMP, and one point per pixel for everything else. A
logo of 152 × 87 pixels at 300 dots to the inch covers 36.48 × 20.88 points.

With a width and a height, the image fills that box. The scale is applied to each axis separately, so a
call that gives a box of another proportion stretches the picture; nothing crops it and no part of it is
placed outside the box. The top-left corner sits at `(x, y)` in canvas coordinates, and a transform set
on the canvas moves the image like it moves a path — see Chapter 15.

## What a draw costs

The first draw of a file parses it and copies what the document will hold; a later draw of the same
bytes parses them again and finds the same key, so the document still carries one object. A caller that
draws one picture many times should open it once and draw the `PdfImage`, which parses it once.

For a PNG and a JPEG the bytes the document stores are compressed already, so a draw costs a copy and a
hash. For a BMP and a GIF the samples are decoded, filtered and compressed, which is where the cost of
those two formats comes from.

## Refusals

Every refusal happens at the call, before anything is written, and names what was found:

| Refusal | Exception | The message names |
| --- | --- | --- |
| The signature is none of the four | `ArgumentException` | The first eight bytes, as hexadecimal |
| A PNG is interlaced or sixteen bit | `NotSupportedException` | Adam7, or the bit depth |
| A PNG states another colour type | `NotSupportedException` | The colour type and the supported ones |
| A PNG profile does not describe the samples | `NotSupportedException` | The profile's colour space and the components written |
| An `iCCP` chunk holds no profile | `ArgumentException` | The missing `acsp` signature |
| A JPEG uses an unsupported frame or precision | `PdfUnsupportedFeatureException` | The frame marker or the precision |
| A JPEG states an EXIF orientation | `PdfUnsupportedFeatureException` | The orientation |
| A BMP uses a compression this reader does not decode | `NotSupportedException` | `BI_RLE8`, `BI_RLE4`, `BI_BITFIELDS` or the header |

A refused call leaves the canvas unchanged: no half-drawn picture, and no image registered for a file
that was not read.

## What this chapter does not do

* No SVG, TIFF, WebP, JPEG 2000 or animated GIF: a caller with another format re-saves it as one of the
  four this chapter describes.
* No EXIF rotation: a photograph that expects its orientation to be applied is refused, not turned.
* No colour conversion: a CMYK JPEG stays CMYK, and a profiled PNG keeps its profile instead of being
  converted to sRGB. A reader shows both correctly; only a converter would change the samples.
* No profile validation: an `iCCP` chunk is checked for its signature and its component count, and what
  is inside the profile is the profile's business.
* No resampling on the page: the image is drawn into a box, and how fine it looks is the viewer's
  business — an image drawn larger than its pixels is soft, and nothing here guesses an interpolation.
* No pixel access on this side: `PdfImage` describes the file it read and draws it, and the samples a *read*
  hands back are `PdfImageInfo.DecodedPixels` in `Gray8` or `Rgb24` — Chapter 8. A JPEG has no pixels there
  either, for the same reason it is passed through here: it is already a format the caller can decode.
* No PDF/A image rules here: Chapter 24 covers the profiles, which refuse an image they cannot describe.
