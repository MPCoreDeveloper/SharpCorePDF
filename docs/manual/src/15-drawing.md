# 15. Drawing

Chapter 12 starts a page from nothing: text in a standard or an embedded font, PNG, JPEG, BMP and GIF
images, and a document written to bytes. This chapter is the rest of that surface — the shapes. Lines,
rectangles, ellipses and arbitrary paths, the paint and stroke style applied to them, clipping, transforms
and the saved graphics state. All of it shipped in `1.1.0`.

Every call appends operators to the page's content stream in the order you make it, so the calls read like
the drawing they produce: the background first, the shapes that cover it after. There is no scene graph, no
retained shape and no second layout pass. What you draw is what the stream contains.

## Coordinates, and the one name that misleads

A canvas measures from the **top-left** corner of the page, with **y** growing downwards, in points. Page
space — the coordinates the PDF file itself stores — has its origin at the bottom-left corner with y growing
upwards. The conversion happens when the page is written, so a caller never writes `page.Height - y`.

`PdfRectangle` keeps PDF's edge names: `left`, `bottom`, `right`, `top`, where `bottom` is the edge with the
smaller y and `top` the one with the larger y. On a canvas that means **`bottom` is the edge nearer the top
of the page**:

```csharp
// A 100 x 50 point box whose upper edge is 20 points below the top of the page.
var box = new PdfRectangle(left: 40, bottom: 20, right: 140, top: 70);

box.Width;      // 100
box.Height;     // 50
box.IsEmpty;    // false
```

If the names get in the way, read the constructor as "left, smaller y, right, larger y" — that pair is always
in that order. `PdfRectangle.FromCorners(x1, y1, x2, y2)` builds the same rectangle from any two opposite
corners and normalises them, so `(140, 70, 40, 20)` describes the same box. A `right` smaller than `left`, a
`top` smaller than `bottom`, or a coordinate that is not a finite number is an `ArgumentOutOfRangeException`,
raised where the value is created rather than when the page is written.

## Lines

```csharp
page.DrawLine(
    new PdfPoint(72, 120),
    new PdfPoint(523, 120),
    new PdfStroke(PdfPaint.Black, width: 0.75));
```

A line is one open subpath: a move, a line, and the paint. The `Cap` of the stroke decides what the two ends
look like, and a single segment has no join, so `Join` and `MiterLimit` are written but have nothing to act
on. `DrawLine` takes a `PdfStroke` and no fill: filling an open line is nothing a reader will show you.

## Rectangles

```csharp
page.DrawRectangle(
    new PdfRectangle(40, 20, 140, 70),
    fill: new PdfPaint(PdfColor.FromHex("#1F3A5F"), alpha: 0.08),
    stroke: new PdfStroke(PdfPaint.Black, width: 0.5));
```

The rectangle is a closed subpath. Pass a fill, a stroke, or both; supplying neither is an
`ArgumentException`, because a path that is constructed and never painted is a mistake rather than a no-op.
A box with no width or no height is refused for the same reason. When both are supplied the shape is painted
with one operator (`B`, or `B*` under the even-odd rule), so no other drawing can slip between its fill and
its outline.

## Ellipses

`DrawEllipse(bounds, fill?, stroke?)` fits an ellipse inside a box. The box does not have to be square, and an
ellipse in a wide box is a wide ellipse. The curve is four cubic Béziers with the control-point constant every
PDF producer uses — 0.5522847498 × the radius — which leaves a worst-case radial error of about 0.03% of the
radius: a thousandth of a point on a four-centimetre radius, below what a printer resolves. A caller who
needs an exact curve draws it with `DrawPath` and as many segments as the job requires.

## Paths

`PdfPath` is a list of `PdfPathSegment` records and a flag. The five segment kinds are `MoveTo`, `LineTo`,
`CubicTo` (two control points and an end), `QuadraticTo` (one control point and an end) and `Close`:

```csharp
var arrow = new PdfPath(
    [
        new PdfPathSegment.MoveTo(new PdfPoint(100, 400)),
        new PdfPathSegment.LineTo(new PdfPoint(220, 400)),
        new PdfPathSegment.LineTo(new PdfPoint(220, 388)),
        new PdfPathSegment.LineTo(new PdfPoint(244, 406)),
        new PdfPathSegment.LineTo(new PdfPoint(220, 424)),
        new PdfPathSegment.LineTo(new PdfPoint(220, 412)),
        new PdfPathSegment.LineTo(new PdfPoint(100, 412)),
    ],
    isClosed: true);

page.DrawPath(arrow, new PdfPaint(PdfColor.Black), stroke: null);
```

Four rules, all checked when the path is constructed:

* the segments may not be empty, and the first must be a `MoveTo` — a path that does not start somewhere is
  ambiguous about where its first segment begins;
* a null segment is refused rather than skipped;
* `Close` closes the current subpath back to its own start point, which matters in a path with several
  subpaths, while `isClosed` closes after the last segment. Use one or the other, not both;
* a `QuadraticTo` is converted to the equivalent cubic when it is written, because PDF has no quadratic
  operator. The curve is the same curve, not an approximation of it.

A second `MoveTo` starts a new subpath. When the path is filled, the fill rule decides what a subpath inside
another subpath means: `PdfFillRule.NonZero` (the default) fills the *union*, so an inner subpath drawn the
same way round does not cut a hole; `PdfFillRule.EvenOdd` alternates, so it does. The rule is a property of
the paint and reaches the stream as `f*`/`B*` instead of `f`/`B`. It has no effect on a stroke.

## Fill and stroke

Two immutable values describe how a shape is painted. `PdfPaint` states the colour, its opacity, the fill
rule and the device colour space. `PdfStroke` states a paint plus the line style:

```csharp
var fill = new PdfPaint(
    PdfColor.FromRgb(0x1F, 0x3A, 0x5F),
    alpha: 0.35,                             // 0 to 1, through an /ExtGState
    fillRule: PdfFillRule.EvenOdd,
    colorSpace: PdfColorSpace.DeviceRGB);    // DeviceGray, DeviceRGB (default) or DeviceCMYK

var stroke = new PdfStroke(
    paint: PdfPaint.Black,
    width: 1.2,
    cap: PdfLineCap.Round,
    join: PdfLineJoin.Miter,
    miterLimit: 4,
    dashArray: [6, 2, 1, 2],                 // dash, gap, dot, gap
    dashPhase: 1,                            // one point into the pattern
    strokeAdjustment: PdfStrokeAdjustment.Enabled);
```

| Member | Default | What it states | Written as |
| --- | --- | --- | --- |
| `PdfPaint.Color` / `.ColorSpace` | black / `DeviceRGB` | The device colour, and the space its components are written in. | `rg`, `g` or `k` (fill); `RG`, `G` or `K` (stroke) |
| `PdfPaint.Alpha` | `1.0` | One opacity for the whole shape. | `/ca` and `/CA` in an `/ExtGState`, selected with `gs` |
| `PdfPaint.FillRule` | `NonZero` | What counts as inside when filling. | `f` vs `f*`, `B` vs `B*` |
| `PdfStroke.Width` | `1.0` | The line width in points. | `w` |
| `PdfStroke.Cap` | `Butt` | The end of an open subpath: `Butt`, `Round` or `Square`. | `J` (`0`, `1`, `2`) |
| `PdfStroke.Join` | `Miter` | Where segments meet: `Miter`, `Round` or `Bevel`. | `j` (`0`, `1`, `2`) |
| `PdfStroke.MiterLimit` | `10.0` | How far a miter may run before it is cut to a bevel. | `M` |
| `PdfStroke.DashArray` | empty | Dash and gap lengths **in points** — not multiples of the width, which is the PostScript convention. | `d` |
| `PdfStroke.DashPhase` | `0.0` | How far into the pattern the line starts. | `d` |
| `PdfStroke.StrokeAdjustment` | `null` | Whether the viewer may snap strokes to device pixels. | `/SA` in an `/ExtGState` |

Alpha is not a fourth colour component: the viewer composites it, so it applies to the shape as a whole
rather than to each pixel of a photograph, and it costs one entry in the page's graphics-state resources. The
writer keeps one entry per distinct combination of fill alpha, stroke alpha and stroke adjustment, so a
thousand translucent shapes share one `/GS1`; a paint whose alpha is `1` needs no entry at all.

The refusals are the ones a caller is likely to hit, and they arrive before anything is recorded: a width
that is not a positive finite number, a miter limit below 1, a dash phase that is not finite, a dash array
with a negative or non-finite value or one whose values are all zero (PDF would draw nothing), and an enum
value the engine does not define. The dash array is **copied** when the stroke is created, so mutating the
list you passed in cannot reach a stroke that is already recorded.

The writer emits the path, then the style, then the painting operator. That order is legal — a viewer takes
the style at the moment the path is painted — and it has a consequence worth knowing: the stream cannot be
read as "set the style, then draw", and a shape that has already been painted cannot be restyled.

## Clipping

`SetClip` restricts everything drawn afterwards to the inside of a path — the usual way to crop a
photograph, to confine a table to a column, or to keep an overlay inside a rounded panel:

```csharp
var panel = new PdfPath(
    [
        new PdfPathSegment.MoveTo(new PdfPoint(40, 40)),
        new PdfPathSegment.LineTo(new PdfPoint(555, 40)),
        new PdfPathSegment.LineTo(new PdfPoint(555, 300)),
        new PdfPathSegment.LineTo(new PdfPoint(40, 300)),
    ],
    isClosed: true);

page.SaveState()
    .SetClip(panel, PdfFillRule.EvenOdd)
    .DrawImage(photographPng, 0, 0, page.Width, page.Height)
    .RestoreState();
```

A clip is part of the graphics state, not of the operation that follows it. It stays in force for every later
drawing until it is restored, and it can only be undone by `RestoreState`, never by setting an empty clip. A
clip set outside a save therefore applies to the rest of the page, which is occasionally what you want and
usually a mistake. The fill rule decides which side of the path is inside, exactly as it does for a fill, and
the call paints nothing itself.

## Transforms

`SetTransform` concatenates a matrix onto the current transform. The matrix is stated in canvas coordinates —
the same top-left, y-down system as every coordinate in this chapter — and the conversion to page space is
folded into the `cm` operator, so a translation of `(10, 20)` moves content ten points right and twenty
points down the page:

```csharp
page.SaveState()
    .SetTransform(PdfMatrix.Scale(2).Multiply(PdfMatrix.Translate(60, 40)))
    .DrawRectangle(new PdfRectangle(0, 0, 100, 50), fill, stroke: null)
    .RestoreState();
```

`Multiply` follows PDF's row-vector convention: `left.Multiply(right)` applies `left` first and `right`
afterwards, so the example scales about the origin and then moves the result. Compose a group in that order —
scale, then rotate, then translate — or a rotation will turn the placement as well as the content, because
`Translate(60, 40).Multiply(Rotate(-15))` rotates the point the group was moved to. The other factories are
`Scale(x, y)` and `Rotate(degrees)`, and any matrix can be built directly with the six-argument constructor
`new PdfMatrix(a, b, c, d, e, f)`.

A rotation deserves one sentence of care, because the sign surprises almost everyone the first time. The
canvas y axis points **down**, so `PdfMatrix.Rotate(90)` maps a point ten points to the right of the origin to
a point ten points *below* it: the content turns clockwise on the page. Use `Rotate(-90)` for a
counter-clockwise turn. The same reason makes `Scale(1, -1)` a flip about the horizontal line through the
origin rather than about the bottom of the page.

A transform applies to everything drawn after it, not only to shapes: text baselines placed with `DrawText` or
`DrawTextLayout`, and images placed with `DrawImage`, are all written through the current transform. That is
what makes a rotated stamp — text and logo together — one `SetTransform` rather than arithmetic on every
coordinate. Bound it with `SaveState` and `RestoreState` unless you intend the rest of the page to move.

## The saved graphics state

`q` saves the graphics state and `Q` restores it. On the canvas the pair is `SaveState()` and
`RestoreState()`, and what travels in it is everything the sections above change: the fill paint, the stroke
style, the alpha pair, the clipping path and the transform.

```csharp
page.SaveState()                                      // q
    .SetTransform(PdfMatrix.Rotate(-90))              // cm
    .SetClip(corner)                                  // W n
    .DrawText("DRAFT", 20, 20, typeface, 48)          // BT ... ET
    .RestoreState();                                  // Q
```

Two refusals keep the stream balanced. `RestoreState()` on a canvas with no matching save throws
`InvalidOperationException` where you call it, so the mistake is reported at its own line rather than as a
corrupt page. A page still holding a saved state when the document is written is refused the same way:
`ToArray()`, `SaveAsync()` and `Build()` throw rather than emit a stream that leaves a clip or a transform
behind.

## What it deliberately does not do

This is a drawing surface, not a vector graphics library with a document model:

* **No scene graph and no retained shapes.** There is no object to move after it has been drawn, no z-order
  beyond paint order and no hit testing. Coordinates are the model.
* **No gradients, patterns, shadings or spot colours** through the canvas. `PdfPaint` carries a device colour
  — grey, RGB or CMYK — and one alpha. A gradient is not expressible, and neither is a tiling pattern.
* **No blend modes.** `PdfBlendMode` exists in the model behind the content writer, but the canvas exposes
  opacity only: a shape is composited normally, with alpha.
* **No stroke-to-outline conversion, no text on a path, no clipping to text.** Text clipping is a reader
  feature; this surface writes.
* **No SVG, and no image format beyond the PNG, JPEG, BMP and GIF of Chapter 12.** These are the formats
  the authoring side reads; anything else is a file the caller re-saves.
* **No validation against PDF/A or PDF/UA.** What is written is plain content-stream operators in an authored
  page; Chapter 24 covers the archival output that is a separate path.

Everything is emitted in call order, so painting over what came before is how you stack: an opaque rectangle
over a photograph hides it, and a translucent one tints it.

## The surface at a glance

| Member | What it does |
| --- | --- |
| `DrawLine(from, to, stroke)` | One stroked segment; the cap applies at both ends. |
| `DrawRectangle(rect, fill?, stroke?)` | A closed box, filled, stroked, or both in one operator. |
| `DrawEllipse(bounds, fill?, stroke?)` | An ellipse fitted to a box as four cubic Béziers. |
| `DrawPath(path, fill?, stroke?)` | Any path of move, line, cubic, quadratic and close segments. |
| `SetClip(path, fillRule?)` | Restricts everything afterwards to the inside of a path. |
| `SetTransform(matrix)` | Concatenates a canvas-space transform; affects paths, text and images. |
| `SaveState()` / `RestoreState()` | Brackets any change of paint, stroke, clip or transform. |
| `PdfPaint(color, alpha?, fillRule?, colorSpace?)` | A device colour, its opacity, the fill rule and the space. |
| `PdfStroke(paint, width?, cap?, join?, miterLimit?, dashArray?, dashPhase?, strokeAdjustment?)` | A paint plus the line style written with it. |
| `PdfPath(segments, isClosed?)` / `PdfPathSegment.*` | The path model: `MoveTo`, `LineTo`, `CubicTo`, `QuadraticTo`, `Close`. |
| `PdfRectangle(left, bottom, right, top)` / `FromCorners(...)` | A box in page space, normalised on the way in. |
| `PdfPoint(x, y)` / `PdfMatrix.Translate/Scale/Rotate` | The two other values shapes are built from. |

## Where to go next

- To start a page, draw text and place images, read Chapter 12.
- To measure a line, wrap a paragraph and draw the result, read Chapter 16.
- For complete methods that combine shapes with a merge — a stamped header, a watermark panel, a caption
  under an image — read the cookbook, Chapter 13.
