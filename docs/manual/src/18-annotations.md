# 18. Annotations

An annotation is something a viewer draws **over** a page: a highlight, a sticky note, a stamp, a link. It is
not page content. That distinction is the whole design of this chapter, and it is why adding a comment to a
document does not change a byte of the page's content stream — only the page's `/Annots` array, which is the
list of annotations the page carries.

Annotations are written through the same immutable plan as every other edit (chapter 14), so a plan that
names a kind this library cannot draw, a link with both a page and a URL, or a rectangle outside the page is
refused before anything is written.

**Release**: the annotation writer shipped in `1.1.0`, so everything this chapter describes is in that package and in every one since.

## Adding one

```csharp
var plan = PdfEditPlan.Create()
    .AddAnnotation(1, PdfAnnotations.Highlight(
        [new PdfRectangle(72, 700, 320, 716)],
        PdfColor.FromHex("#FFF176")) with { Name = "summary-highlight" })
    .AddAnnotation(1, PdfAnnotations.FreeText(
        new PdfRectangle(72, 640, 340, 672),
        "Reviewed against the checklist.",
        new PdfStandardTextStyle(StandardFont.HelveticaOblique, 11)))
    .AddAnnotation(1, PdfAnnotations.Link(
        new PdfRectangle(72, 520, 300, 540),
        "https://github.com/MPCoreDeveloper/SharpCorePDF"));
```

The factories cover the kinds a document realistically carries:

| Factory | Kind | What it draws |
| --- | --- | --- |
| `PdfAnnotations.Text` | `Text` | A sticky note, folded corner and all |
| `PdfAnnotations.FreeText` | `FreeText` | The text itself, wrapped and clipped to the box |
| `PdfAnnotations.Stamp` | `Stamp` | A frame and a caption |
| `PdfAnnotations.Highlight` | `Highlight` | The quads, filled with the colour; set `Opacity` to make it translucent |
| `PdfAnnotations.Underline` / `StrikeOut` | `Underline`, `StrikeOut` | A rule under, or through, each quad |
| `PdfAnnotations.Square` / `Circle` | `Square`, `Circle` | A border, or an ellipse inscribed in the box |
| `PdfAnnotations.Line` | `Line` | One stroked segment, `LineFrom` to `LineTo` |
| `PdfAnnotations.Ink` | `Ink` | Every stroke as a polyline |
| `PdfAnnotations.Link` | `Link` | Nothing: a link is an active area |

`PdfAnnotation` is a record, so everything the factories do not state is set with a `with` expression:
`Contents`, `Color`, `Opacity`, `Border`, `Flags`, `TextStyle`, `IconName`. Setting a value the kind cannot
draw is refused by `Validate`, which the plan calls before the source is opened.

## Appearance streams

Every annotation the editor creates gets an appearance stream — the small form XObject a viewer draws
instead of synthesising the mark. That is what makes a highlight visible to a printer, which never
synthesises anything, and to a viewer that does not either.

The stream is written in the annotation's own space: the lower-left corner of the rectangle is the origin, so
the operators are small numbers that do not depend on where on the page the annotation sits.

A caller that wants a different face supplies one:

```csharp
var stamp = PdfAnnotations.Stamp(new PdfRectangle(420, 560, 560, 610), "DRAFT")
    with
    {
        Appearance = new PdfAnnotationAppearance(
            Content: "q 1 0.9 0.2 rg 0 0 140 50 re f Q",
            BoundingBox: new PdfRectangle(0, 0, 140, 50)),
    };
```

`PdfAnnotationAppearance` carries the operators, the box they are drawn in, and the font they select as
`/Helv`. Only an operator stream is accepted: an appearance never carries a font program, because the
editor's fonts are the twelve standard ones.

A link has no appearance at all. A link is an active area with nothing to draw, and a viewer that drew one
would paint over the page.

## Updating and removing

```csharp
var plan = PdfEditPlan.Create()
    .UpdateAnnotation(1, "summary-highlight", PdfAnnotations.Highlight(
        [new PdfRectangle(72, 700, 320, 716)],
        PdfColor.FromHex("#FFE082")) with { Contents = "Reviewed" })
    .RemoveAnnotation(1, "old-note");
```

Both refer to the identifier the annotation was written with, which is its `/NM` entry. An annotation that
states no name of its own is written as `scpdf-N`, where `N` is the index of the operation, so a plan can
always name what it added. An update or a removal that names an annotation the page does not carry is
refused, and the refusal happens before a byte is written.

The page's array is rebuilt rather than mutated: a page that shares an annotation with another page keeps it
there, and an annotation the plan removes simply stops being reached — and therefore stops being written at
all.

## What the editor reports

| Warning | When |
| --- | --- |
| `AnnotationClipped` | The rectangle reached outside the page and was clipped, or lay entirely outside and was not written |
| `AnnotationAppearanceRegenerated` | The page carries an appearance stream the engine drew, so a viewer that does not synthesise one still shows the marks |
| `AnnotationDestinationUnresolved` | A link points at a page the same edit removed, so the link was not written |
| `AnnotationUnreadable` | A source annotation could not be resolved while reading |

Two behaviours are worth stating outright, because they are choices rather than consequences:

- **A link to a removed page is dropped, not redirected.** Silently pointing it at a different page would
  make the document lie about where it goes.
- **The geometry is clipped to the crop box, not to the media box.** What a reader sees is what the
  annotation is kept inside of.

## Where it is tested

`tests/SharpCorePDF.Tests/PdfAnnotationTests.cs` covers the factories, the flags and the refusals;
`PdfDocumentEditorTests.Annotations.cs` writes marks through the plan and reads them back with
`ReadAnnotationsAsync`. The `annotate` example produces `annotated.pdf`, which `docs/tools/validate-pdf.py`
reports as `qpdf check: no problems`.
