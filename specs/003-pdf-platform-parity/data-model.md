# Data Model: PDF Platform Parity

**Feature**: `003-pdf-platform-parity` | **Date**: 2026-10-21

This model describes the values and plans that cross the public surface. It is deliberately
file-agnostic where the type lives in Domain; types that understand PDF syntax stay in Infrastructure.
No type below is a public mutable object graph.

## Invariants

1. A coordinate, size, fontSize or alpha is finite; sizes and font sizes are greater than zero.
2. A path has at least one subpath; every curve has the number of control points its segment type
   requires; a closed path has no trailing open segment.
3. A colour component is 0–255; alpha is 0.0–1.0; a dash array is non-negative and not all zero.
4. A page number is one-based. A page reference in an edit plan resolves to a page that exists after
   every preceding operation.
5. An edit plan is immutable; every `With…`/`Add…` method returns a new plan.
6. A field name is unique within a form after expansion of its fully qualified name.
7. A signature covers a byte range that lies inside the written revision and does not overlap itself.
8. A PDF/A profile refuses encryption, JavaScript and any font it cannot embed.
9. A read-only view cannot expose a mutable array, dictionary or stream.
10. Every lossy decision has a `PdfWarningKind`; no operation returns “success” after silently dropping
    content.

## Geometry and Graphics

### `PdfPoint`

| Field | Type | Rule |
| --- | --- | --- |
| `X` | `double` | finite |
| `Y` | `double` | finite |

### `PdfMatrix`

| Field | Type | Rule |
| --- | --- | --- |
| `A`, `B`, `C`, `D`, `E`, `F` | `double` | finite; default is identity |

Operations: `Identity`, `Translate`, `Scale`, `Rotate(degrees)`, `Multiply`, `Transform(PdfPoint)`.

### `PdfPath`

| Field | Type | Rule |
| --- | --- | --- |
| `Segments` | `IReadOnlyList<PdfPathSegment>` | at least one |
| `IsClosed` | `bool` | closing segment is implicit |

### `PdfPathSegment` (closed hierarchy)

- `MoveTo(PdfPoint)`
- `LineTo(PdfPoint)`
- `CubicTo(control1, control2, end)`
- `QuadraticTo(control, end)`
- `Close`

### `PdfPaint`

- `PdfColor Color`
- `double Alpha` (0–1)
- `PdfFillRule FillRule` (`NonZero`, `EvenOdd`)
- optional `PdfColorSpace` (DeviceRGB, DeviceGray, DeviceCMYK; no ICC in 1.1)

### `PdfStroke`

- `PdfPaint Paint`
- `double Width` (> 0)
- `PdfLineCap Cap` (`Butt`, `Round`, `Square`)
- `PdfLineJoin Join` (`Miter`, `Round`, `Bevel`)
- `double MiterLimit` (≥ 1)
- `IReadOnlyList<double> DashArray`
- `double DashPhase`
- optional `PdfStrokeAdjustment`

### `PdfGraphicsState`

Immutable snapshot of: current transform, fill paint, stroke, alpha, blend mode (`Normal`,
`Multiply`, `Screen`, `Overlay`, `Darken`, `Lighten`), clipping path, font and text state. `Save()` and
`Restore()` on the canvas are represented as a stack of states; unbalanced restores are refused.

## Images

### `PdfImageSource` (abstract/closed)

| Field | Type | Rule |
| --- | --- | --- |
| `WidthPixels` | `int` | > 0 |
| `HeightPixels` | `int` | > 0 |
| `ColorSpace` | `PdfImageColorSpace` | RGB, Gray, CMYK, Indexed, ICC-based |
| `BitsPerComponent` | `int` | 1, 2, 4, 8 or 16 where the format allows |
| `HasAlpha` | `bool` | true for soft mask or explicit alpha |
| `NaturalWidthPoints` | `double?` | null when no resolution metadata |
| `NaturalHeightPoints` | `double?` | null when no resolution metadata |
| `Filter` | `string` | source filter for pass-through (`DCTDecode`, `FlateDecode`, …) |
| `IsPassthrough` | `bool` | true when encoded bytes are embedded unchanged |

Variants: `PdfJpegImage` (pass-through unless EXIF orientation/CMYK transform is applied),
`PdfPngImage` (alpha/palette predictor path), `PdfBmpImage`, `PdfGifImage` (first frame).

Validation: dimensions and bits match the declared colour space; an image with an unsupported
filter/colour space is refused with the filter named; a mask’s dimensions match the base image.

## Text

### `PdfTextStyle`

| Field | Type | Rule |
| --- | --- | --- |
| `Typeface` | `PdfTypeface` | not null |
| `FontSize` | `double` | > 0 |
| `Color` | `PdfColor` | default black |
| `CharacterSpacing` | `double` | finite |
| `WordSpacing` | `double` | finite |
| `HorizontalScale` | `double` | > 0 |
| `Leading` | `double` | finite |
| `Rise` | `double` | finite |

### `PdfTextMetrics`

`Ascent`, `Descent`, `LineGap`, `CapHeight`, `XHeight`, `UnitsPerEm`, `Measure(string, double)`.

### `PdfTextLayoutOptions`

| Field | Type | Rule |
| --- | --- | --- |
| `MaxWidth` | `double` | > 0 |
| `Alignment` | `PdfTextAlignment` | Left, Center, Right, Justify |
| `LineHeight` | `double` | > 0; defaults to `Leading` |
| `BreakWords` | `bool` | default false; a word wider than the line is refused unless true |
| `Direction` | `PdfTextDirection` | LeftToRight, RightToLeft; shaping is not supported |
| `Overflow` | `PdfTextOverflow` | Refuse, Clip, or Continue |

### `PdfTextLayout`

`Lines` (`IReadOnlyList<PdfTextLine>`), `Bounds`, `Baseline`, `LineCount`. A `PdfTextLine` carries its
text, runs, start point, width and baseline.

## Document Edits

### `PdfEditPlan`

| Field | Type | Rule |
| --- | --- | --- |
| `Operations` | `IReadOnlyList<PdfEditOperation>` | ordered; immutable |
| `Output` | `PdfEditOutputOptions` | target version, metadata, outline policy, PDF/A profile, encryption |
| `Validate()` | `IReadOnlyList<string>` | every error before opening a source |

Operations: `ImportPage`, `InsertPage`, `DeletePage`, `DuplicatePage`, `ReorderPages`, `RotatePage`,
`CropPage`, `ResizePage`, `SplitDocument`, `ExtractPages`, `AppendDocument`, `OverlayPage`,
`UnderlayPage`, `SetMetadata`, `SetXmp`, `SetOutline`, `SetPageLabels`, `SetViewerPreferences`,
`AddAnnotation`, `UpdateAnnotation`, `RemoveAnnotation`, `SetFormField`, `FlattenForm`.

Each operation has a stable `Kind` and a human-readable `ToString`; validation reports the operation
index so a caller can locate the bad edit.

### `PdfEditOutputOptions`

`TargetVersion`, `Metadata`, `MetadataPolicy`, `OutlinePolicy`, `PageLabelPolicy`, `Encryption`,
`PdfAProfile?`, `ModificationDate?`, `DocumentId?`, `UseIncrementalUpdate`.

## Annotations

### `PdfAnnotation`

| Field | Type | Rule |
| --- | --- | --- |
| `Kind` | `PdfAnnotationKind` | Link, Text, FreeText, Stamp, Highlight, Underline, StrikeOut, Square, Circle, Line, Ink |
| `Rectangle` | `PdfRectangle` | inside or overlapping the page; inverted rectangles are normalised |
| `Contents` | `string?` | optional |
| `Color` | `PdfColor?` | kind-dependent |
| `Opacity` | `double` | 0–1 |
| `Flags` | `PdfAnnotationFlags` | Hidden, Print, NoZoom, NoRotate, ReadOnly, Locked, … |
| `Border` | `PdfBorder?` | width, style, dash |
| `Destination` | `PdfDestination?` | required for Link; internal or URI |
| `Appearance` | `PdfAnnotationAppearance?` | generated when absent |

Validation: Link requires exactly one of URI or internal destination; a markup annotation requires at
least one quad/point; a Stamp requires an icon name or a custom appearance; Ink requires at least one
ink list.

## Forms

### `PdfForm`

| Field | Type | Rule |
| --- | --- | --- |
| `Fields` | `IReadOnlyList<PdfFormField>` | fully qualified names unique |
| `NeedAppearances` | `bool` | false after flattening |
| `DefaultAppearance` | `PdfTextStyle?` | used when a field has none |

### `PdfFormField`

| Field | Type | Rule |
| --- | --- | --- |
| `Name` | `string` | non-empty, unique after qualification |
| `Kind` | `PdfFormFieldKind` | Text, CheckBox, Radio, Choice, PushButton, Signature |
| `Value` | `PdfFieldValue` | type must match `Kind` |
| `Rectangle` | `PdfRectangle` | on the page |
| `Flags` | `PdfFormFieldFlags` | ReadOnly, Required, NoExport, … |
| `Options` | `IReadOnlyList<string>` | Choice/Radio only |
| `Appearance` | `PdfFormAppearance?` | generated when absent |
| `IsFlattened` | `bool` | true after flatten |

Validation: a radio group has at least two options and one selected value; a choice value is in its
option list; a read-only field cannot be set by the editor; flattening requires an appearance that can
be generated or copied.

## Signatures

### `PdfSignatureOptions`

| Field | Type | Rule |
| --- | --- | --- |
| `Certificate` | `X509Certificate2` | has a private key |
| `Chain` | `IReadOnlyList<X509Certificate2>` | optional; leaf first |
| `HashAlgorithm` | `PdfHashAlgorithm` | SHA-256, SHA-384 or SHA-512 |
| `Reason` | `string?` | written to `/Reason` |
| `Location` | `string?` | written to `/Location` |
| `ContactInfo` | `string?` | written to `/ContactInfo` |
| `FieldName` | `string` | default `Signature1`; unique |
| `Appearance` | `PdfSignatureAppearance?` | optional visible signature |
| `TimestampProvider` | `IPdfTimestampProvider?` | caller-supplied; no network in library |
| `UseCades` | `bool` | ETSI CAdES when the profile requires it |

### `PdfSignatureInfo`

`FieldName`, `SubFilter`, `ByteRange`, `SignerSubject`, `SignerThumbprint`, `SigningTime`,
`TimestampTime?`, `RevisionIndex`, `CoversWholeDocument`.

### `PdfSignatureValidation`

| Field | Type | Meaning |
| --- | --- | --- |
| `Integrity` | `PdfSignatureIntegrity` | Valid, Invalid, Unknown |
| `Trust` | `PdfTrustResult` | Trusted, Untrusted, Unknown, NotEvaluated |
| `Signer` | `PdfSignatureInfo` | parsed signer |
| `Problems` | `IReadOnlyList<PdfSignatureProblem>` | structural or cryptographic details |
| `IsValid` | `bool` | true only when integrity is Valid |

`Integrity = Valid` never implies `Trust = Trusted`; a caller must read both. A chain is evaluated
against the caller’s trust store, not a bundled root list.

## Archival Profiles

### `PdfAProfile`

| Value | Restrictions |
| --- | --- |
| `PdfA1B` | PDF 1.4 structures, embedded fonts, output intent, no encryption, no JavaScript, no audio/video |
| `PdfA2B` | PDF/A-1 rules plus PDF 1.7 features permitted by ISO 19005-2, transparency rules |
| `PdfA3B` | PDF/A-2 rules plus embedded files allowed by ISO 19005-3 |

`PdfAConversionResult` reports `Profile`, `IsWritten`, `Warnings` and `UnsatisfiedRequirements`. The
writer never returns `IsWritten = true` with an unsatisfied hard requirement.

## Reading and Analysis

### `PdfLetter`

| Field | Type | Rule |
| --- | --- | --- |
| `Value` | `string` | Unicode text; may be empty for an unmapped glyph |
| `FontSize` | `double` | > 0 |
| `FontName` | `string?` | null when unavailable |
| `BoundingBox` | `PdfRectangle` | page coordinates |
| `StartBaseLine` | `PdfPoint` | page coordinates |
| `EndBaseLine` | `PdfPoint` | page coordinates |
| `TextDirection` | `PdfTextDirection` | derived from the baseline |
| `IsWhitespace` | `bool` | true for spacing glyphs |

### `PdfWord`

Existing read model, extended with `Letters` (ordered) and `BoundingBox`; no breaking change to its
current members.

### `PdfPageSegment`

`Kind` (Text, Image, Line, TableCandidate), `BoundingBox`, `ReadingOrder`, `Words`, `Images`.

### `PdfHyperlink`

`Rectangle`, `Uri?`, `Destination?`, `IsExternal`. Exactly one of `Uri`/`Destination` is set.

### `PdfObjectView`

Read-only node with `Kind` (Null, Boolean, Integer, Real, String, Name, Array, Dictionary, Stream,
Reference), `Scalar`, `Reference`, `Children`, `StreamLength`, `Filter`. No setter and no mutable child
collection.

### `PdfContentOperationView`

`Operator`, `Operands`, `Offset`, `PageNumber`. Operands are immutable scalar/array/name views.

## Relationships

```text
PdfDocumentBuilder
  └── PdfPageCanvas (0..n)
        ├── PdfGraphicsState (stack)
        ├── PdfPath + PdfPaint/PdfStroke (0..n)
        ├── PdfTextLayout (0..n)
        └── PdfImageSource (0..n)

PdfEditPlan
  ├── PdfEditOperation (ordered)
  │     ├── PageOperation ──> source page(s)
  │     ├── AnnotationOperation ──> PdfAnnotation
  │     └── FormOperation ──> PdfFormField
  └── PdfEditOutputOptions
        ├── PdfEncryptionOptions?
        └── PdfAProfile?

PdfDocumentEditor
  ├── reads source through IPdfContentSource
  ├── applies PdfEditPlan
  └── writes full or incremental output

PdfLayoutReader
  ├── PdfLetter ──> PdfWord ──> PdfTextBlock ──> PdfPageSegment
  ├── PdfHyperlink
  └── PdfImageInfo (existing) + decoded pixels

PdfObjectInspector
  └── PdfObjectView (read-only)
```

## State Transitions

### Authoring

`Created` → `PageAdded` → `Built` → `ImmutableBytes`. Adding a page after `Build` is allowed and does
not change previously returned bytes; the next build produces a new document.

### Editing

`Validated` → `SourceOpened` → `OperationsApplied` → `Written` → `ReopenedAndVerified`. A validation
error stops before `SourceOpened`; a write error leaves the caller’s destination untouched or reports
the partial write; the source is never mutated.

### Signing

`Unsigned` → `SignatureFieldAdded` → `ByteRangeReserved` → `DigestComputed` → `CmsEmbedded` →
`IncrementalRevisionWritten` → `Validated`. A timestamp is an optional `Timestamped` transition between
`DigestComputed` and `CmsEmbedded`.

### Form flattening

`Interactive` → `ValueSet` → `AppearanceGenerated` → `Flattened`. A field cannot move from `Flattened`
back to `Interactive` in the same plan.

### PDF/A conversion

`Plain` → `ProfileSelected` → `FontsEmbedded` → `MetadataAndOutputIntentWritten` → `Validated`.
An unsatisfied hard requirement ends in `Refused`, not `Validated`.

## Validation Summary

| Area | Refusal examples | Warning examples |
| --- | --- | --- |
| Geometry | NaN, zero size, negative radius | path extends beyond the page |
| Text | missing glyph, line break in single-line draw, word wider than line with `BreakWords=false` | fallback line break, clipped overflow |
| Images | unsupported filter, CMYK JPEG without transform, 16-bit PNG when unsupported | EXIF orientation applied, image downsampled by caller |
| Edits | page out of range, duplicate field name, invalid output profile | removed page had a destination, annotation destination unresolved |
| Annotations | link without destination, stamp without appearance | appearance regenerated |
| Forms | read-only field set, value not in choice list | field has no appearance and one was generated |
| Signatures | certificate without private key, unsupported hash | timestamp unavailable, chain untrusted |
| PDF/A | encryption requested with a profile, font cannot be embedded | output intent substituted |
| Reading | malformed document | no `/ToUnicode`, unsupported image filter, unresolved destination |

## Compatibility Rules

- Existing 1.0 members keep their names, parameter order and defaults; new parameters are optional and
  appended.
- New members go to `PublicAPI.Unshipped.txt` in the same commit.
- A new enum member is additive; callers must keep a `default` arm.
- `PdfWarningKind` may grow; existing members keep their meaning.
- `PdfWord` and `PdfTextBlock` may gain properties but may not lose or rename existing ones in 1.x.
- The new public object graph is read-only and separate from the internal mutable model.
