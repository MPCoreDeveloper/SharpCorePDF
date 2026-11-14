# 26. Fonts

Chapter 8 reads a document's summary and Chapter 22 reads its object graph. This chapter answers one
question those leave open — **which fonts does this file need, and does it carry them?** — because it is the
question behind the two defects a reader meets most often: a page that prints in the wrong face on a machine
that has no copy of the font, and a page whose text cannot be copied out at all.

```csharp
var fonts = await inspector.ReadFontsAsync(
    new PdfFileSource("report.pdf"),
    password: null,
    cancellationToken: cancellationToken);

foreach (var font in fonts.Items)
{
    Console.WriteLine(font);   // ABCDEF+Arial: TrueType, embedded, subset, 12 page(s)
}
```

`IPdfFontInspector` takes any `IPdfContentSource`, exactly as `IPdfInspector` does, and is registered by
`AddSharpCorePDF()` beside the other readers. The call writes nothing and opens the source once.

## What one font says

| Property | Type | Meaning |
| --- | --- | --- |
| `ResourceName` | `string` | The name the page resources give it, for example `F1`, without a slash |
| `BaseFont` | `string` | The name the font dictionary states, for example `ABCDEF+Arial` |
| `Kind` | `PdfFontKind` | `Type1`, `MMType1`, `Type3`, `TrueType`, `Type0`, `CIDFontType0`, `CIDFontType2` or `Unknown` |
| `Program` | `PdfEmbeddedFontProgramKind` | `None`, `FontFile`, `FontFile2`, `FontFile3` or `Type3Procedural` |
| `IsEmbedded` | `bool` | Whether the document carries the program the font draws with |
| `IsSubset` | `bool` | Whether the base font name carries the six-letter subset tag the specification defines |
| `Encoding` | `string?` | The encoding the font states, for example `WinAnsiEncoding` or `Identity-H`, or `custom` |
| `HasToUnicode` | `bool` | Whether the font states a `/ToUnicode` map, which is what makes its text characters |
| `PageNumbers` | `IReadOnlyList<int>` | The one-based pages whose resources select the font, ascending |

A font is reported **once** however many pages select it: `PageNumbers` carries the pages, and `ResourceName`
is the name the first of them gives it, because a resource name belongs to the page that states it rather
than to the font. A composite font is reported as the descendant it points at, so a `Type0` font whose
descendant is a `CIDFontType2` reads as `CIDFontType2` — the kind is what the document states two references
deep, not the wrapper.

## Reading the answers

- **`IsEmbedded` is false.** The document relies on a copy of the face being installed wherever it is opened.
  A PDF/A conversion of that document is refused rather than written with a label it does not deserve;
  Chapter 24 has the mechanism, and `FontNotEmbedded` is the requirement it names.
- **`IsSubset` is true.** The program holds only the glyphs the document draws, which is normal for a
  generated file and is why a text edit that introduces a new character needs a different font, not a
  different string.
- **`HasToUnicode` is false with a `Type0` font.** The glyphs are named by number with nothing that maps them
  back to characters, so extraction can find no text even though the page clearly shows some. The content
  reader reports that as `TextUnreadable` rather than guessing (Chapter 20).
- **`Kind` is `Type3`.** The glyphs are content streams rather than a font program. `Program` is
  `Type3Procedural` when the font states its `/CharProcs`, and nothing outside the document is needed.
- **`Encoding` is `custom`.** The font states an encoding dictionary with no base encoding, so its codes are
  described by a `/Differences` array of its own.

## Warnings, and what they mean

The bytes a read could not turn into an answer are reported with the answer, never instead of it. Two kinds
are specific to this read:

| Kind | What happened |
| --- | --- |
| `FontUnreadable` | A resource a page selects does not resolve to a font dictionary. The font is missing from the result, and the remark names the page and the resource. |
| `FontDescriptorMissing` | A font states no `/FontDescriptor`, so neither its program nor its subset can be reported. The font itself is reported. |

`FontDescriptorMissing` is not a defect: a standard font may legally state no descriptor, which is precisely
how a document says "use the Helvetica the reader already has". A document that is encrypted and could not be
opened yields no font and the `EncryptionNotOpened` remark, the same shape every other read reports.

## What this chapter does not read

The inspector reports what the document states about a font. It does not open the font program, so it cannot
say which glyphs a subset holds or whether a program is damaged; it does not resolve a font that is selected
only from a page whose resources are themselves unreadable; and it does not claim PDF/A conformance, because
conformance is established by an independent validator (Chapter 24).

## Where to go next

- For the document summary, the content reader and preflight, read Chapter 8.
- For the archival rules a font participates in, read Chapter 24.
- For the object graph behind these dictionaries, read Chapter 22.
