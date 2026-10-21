# Contract: Annotations and Forms

**Feature**: `003-pdf-platform-parity` | **Type**: public library API

Annotations and form fields are written through the same `PdfEditPlan` as page operations. Reading them
stays on `IPdfContentReader`; the new surface adds write models and appearances.

## Annotations

```csharp
public sealed record PdfAnnotation
{
    public PdfAnnotationKind Kind { get; init; }
    public PdfRectangle Rectangle { get; init; }
    public string? Contents { get; init; }
    public PdfColor? Color { get; init; }
    public double Opacity { get; init; } = 1;
    public PdfAnnotationFlags Flags { get; init; }
    public PdfBorder? Border { get; init; }
    public PdfDestination? Destination { get; init; }
    public PdfAnnotationAppearance? Appearance { get; init; }
}

public static class PdfAnnotations
{
    public static PdfAnnotation Link(PdfRectangle rectangle, string uri);
    public static PdfAnnotation Link(PdfRectangle rectangle, PdfDestination destination);
    public static PdfAnnotation Text(PdfRectangle rectangle, string contents);
    public static PdfAnnotation FreeText(PdfRectangle rectangle, string contents,
        PdfTextStyle style);
    public static PdfAnnotation Stamp(PdfRectangle rectangle, string iconName);
    public static PdfAnnotation Highlight(IReadOnlyList<PdfRectangle> quads, PdfColor color);
    public static PdfAnnotation Underline(IReadOnlyList<PdfRectangle> quads, PdfColor color);
    public static PdfAnnotation StrikeOut(IReadOnlyList<PdfRectangle> quads, PdfColor color);
    public static PdfAnnotation Square(PdfRectangle rectangle, PdfColor borderColor);
    public static PdfAnnotation Circle(PdfRectangle rectangle, PdfColor borderColor);
    public static PdfAnnotation Line(PdfPoint from, PdfPoint to, PdfColor borderColor);
    public static PdfAnnotation Ink(IReadOnlyList<IReadOnlyList<PdfPoint>> strokes,
        PdfColor color);
}
```

**Plan operations**

```csharp
public PdfEditPlan AddAnnotation(int pageNumber, PdfAnnotation annotation);
public PdfEditPlan UpdateAnnotation(int pageNumber, string annotationId,
    PdfAnnotation annotation);
public PdfEditPlan RemoveAnnotation(int pageNumber, string annotationId);
```

**Guarantees**

- Every created annotation has an appearance stream when the viewer is not required to synthesise one.
- Link requires exactly one of a URI or an internal destination.
- A destination to a missing page is refused; a destination to a page removed later is reported and
  removed.
- Annotation colours are preserved in their declared colour space; an unsupported colour space is
  refused rather than approximated.
- The page’s `/Annots` array is updated in place when possible; the source page content is untouched.
- Removal removes the annotation object and any appearance it owned; other annotations keep their
  identifiers.

## Forms

```csharp
public sealed record PdfForm
{
    public IReadOnlyList<PdfFormField> Fields { get; init; }
    public bool NeedAppearances { get; init; }
    public PdfTextStyle? DefaultAppearance { get; init; }
}

public sealed record PdfFormField
{
    public string Name { get; init; }
    public PdfFormFieldKind Kind { get; init; }
    public PdfFieldValue Value { get; init; }
    public PdfRectangle Rectangle { get; init; }
    public PdfFormFieldFlags Flags { get; init; }
    public IReadOnlyList<string> Options { get; init; }
    public PdfFormAppearance? Appearance { get; init; }
}

public static class PdfFields
{
    public static PdfFormField Text(string name, PdfRectangle rectangle, string? value = null);
    public static PdfFormField CheckBox(string name, PdfRectangle rectangle, bool isChecked);
    public static PdfFormField Radio(string name, PdfRectangle rectangle,
        IReadOnlyList<string> options, string? selected);
    public static PdfFormField Choice(string name, PdfRectangle rectangle,
        IReadOnlyList<string> options, string? selected, bool isCombo = true);
    public static PdfFormField PushButton(string name, PdfRectangle rectangle, string caption);
}
```

**Plan operations**

```csharp
public PdfEditPlan SetFormField(int pageNumber, string fieldName, PdfFieldValue value);
public PdfEditPlan ResetForm();
public PdfEditPlan FlattenForm();
```

**Guarantees**

- Field names are unique after qualification; a duplicate is refused before the source is opened.
- A text field preserves the value’s characters and writes a `/ToUnicode`-friendly appearance.
- A checkbox/radio value is one of its export values; an unknown value is refused.
- A choice value is in its option list; a multi-select value is an ordered subset of that list.
- A read-only field is refused for `SetFormField` and reported in the plan’s validation.
- `FlattenForm` writes each field’s appearance into the page content, removes the widget from the form
  and clears `NeedAppearances`; the result renders without a form viewer.
- No JavaScript, `/AA` action or calculation script is executed; action dictionaries may be copied and
  the plan reports that they are inert.

## Appearance Rules

- Text fields: Helvetica or the field’s declared `/DA` font, with auto-size, multiline and comb rules
  where the field declares them; the appearance is regenerated when the value changes.
- Checkboxes and radios: `/Za`/`/D` appearances or a generated check/dot; the selected export value is
  written to `/V` and the widget states.
- Choice fields: the selected text is clipped to the field rectangle; a list box highlights the
  selection.
- Push buttons: the caption is drawn; no action is executed.
- Signature fields: the signature writer supplies the appearance; a blank field is not reported as
  signed.
- A field whose appearance cannot be generated is refused if it must be flattened; otherwise the
  original appearance is preserved and `NeedAppearances` is set with a warning.

## Interaction With Other Features

- A PDF/A profile requires every form field to have a static appearance or be flattened; an
  interactive form is not a PDF/A error by itself, but an appearance is mandatory.
- Encryption does not change form semantics; the editor writes the form before encrypting strings and
  streams.
- An incremental update that fills a form leaves the prior revision and its signature intact.
- A merged document carries the primary source’s form, as in 1.0; the editor is the supported way to
  add a second form.

## Diagnostics

| Condition | Result |
| --- | --- |
| Unknown annotation kind | Refused before opening the source |
| Link with both URI and destination | Refused with the conflicting values |
| Annotation rectangle outside the page | Clipped to the page and reported |
| Duplicate field name | Refused with the name and both pages |
| Value not in choice list | Refused with the value and the allowed list |
| Read-only field update | Refused with the field name |
| Missing appearance on flatten | Generated, or refused if generation is impossible |
| Action or JavaScript found | Copied inert; warning names the field/annotation |

## Out of Contract

- JavaScript execution, form calculations, validation scripts and submit/reset actions.
- XFA forms; only AcroForm is supported.
- Digital signatures through a form field in this contract; signing is the separate signature contract.
- Redaction; a redaction annotation is not offered because the underlying content would remain.
- Annotation types not listed above (3D, movie, sound, screen, rich media, file attachment) in 1.1.

