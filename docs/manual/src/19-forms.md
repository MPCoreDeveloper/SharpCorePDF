# 19. Forms

An AcroForm is a set of named fields that a viewer makes editable. This chapter writes them: creating the
fields, filling the ones a document already carries, putting them back to their default values, and
flattening them so the page shows the values to a reader that knows nothing about forms.

All of it goes through the same immutable plan as the rest of the editor (chapter 14), and all of it is
AcroForm: XFA is not a format this library writes, and a document that carries one loses it with a warning
the moment an edit writes a field (see *XFA* below).

**Release**: the field model shipped in `1.1.0`; the writer this chapter documents — filling, reset and
flatten — is new public API in `1.2`, not a behaviour change to an existing one.

## The model

A field is a record with a name, a kind and a rectangle, and the properties that kind can honour:

```csharp
var field = PdfFormFields.Text(1, "person.name", new PdfRectangle(72, 700, 320, 724), "Ada Lovelace")
    with { IsRequired = true, MaxLength = 64 };
```

The page comes first, because a field is placed before it is described — the same order every other page
operation uses.

| Factory | Kind | What a caller can put in it |
| --- | --- | --- |
| `PdfFormFields.Text` | `Text` | A value; `IsMultiline`, `IsPassword`, `MaxLength`, `Alignment` |
| `PdfFormFields.CheckBox` | `CheckBox` | `IsChecked`, and the `OnState` name the value is exported under |
| `PdfFormFields.RadioButton` | `RadioButton` | The same, drawn as a dot in a circle |
| `PdfFormFields.Choice` | `Choice` | `Options` and one value from them; `IsCombo` for a combo box, otherwise a list box |
| `PdfFormFields.PushButton` | `PushButton` | A `Caption` and no value at all |

`Flags` is computed, not stated: the bits are derived from the properties above, so a caller cannot ask for a
flag the writer does not honour — a JavaScript action, a rich-text value, a comb field. The bits that are
written are read-only, required, multiline, password, radio, push button, combo, editable and multi-select.

Two rules are checked before the source is opened, and both are refusals rather than repairs:

- a name is unique within a form, and two operations of one plan cannot write the same name;
- a value the kind cannot hold is refused with the reason — a choice value that is not an option, a button
  that carries a value, a text field that is both multiline and a password, a value longer than `MaxLength`.

## Writing a form

```csharp
var plan = PdfEditPlan.Create().SetForm(
    PdfForm.Create(
        PdfFormFields.Text(1, "person.name", new PdfRectangle(72, 700, 320, 724), "Ada Lovelace"),
        PdfFormFields.CheckBox(1, "agree", new PdfRectangle(72, 636, 90, 654), isChecked: true),
        PdfFormFields.Choice(1, "country", new PdfRectangle(72, 600, 260, 624), ["NL", "BE", "DE"], "NL", isCombo: true),
        PdfFormFields.PushButton(1, "submit", new PdfRectangle(72, 560, 200, 588), "Send"))
    .WithDefaultStyle(new PdfStandardTextStyle(StandardFont.Helvetica, 10)));
```

`PdfForm.Create` takes the fields in the order they are written, and that order is the order a viewer walks
them: tab order follows it. `WithDefaultStyle` states the style a field that carries none of its own is drawn
in; `PdfStandardTextStyle.Default` — Helvetica 12 — is the fallback.

A dotted name is written as a tree, which is what a PDF means by `person.name`: one node per segment, each
stating its part, with the widget on the last one. That is also why a name is only unique once its ancestors
are joined to it.

`SetForm` writes the fields a document does not carry yet and **fills the ones it does**, by name. Nothing
else in the form is touched, so ten `SetFormField` calls fill ten fields and leave the rest alone:

```csharp
var fill = PdfEditPlan.Create()
    .SetFormField(1, PdfFormFields.Text(1, "person.name", new PdfRectangle(0, 0, 1, 1), "Grace Hopper"));
```

The rectangle of a field the document already carries is ignored: the field is filled where it stands, with
the widget and the ancestors the document gave it. The rectangle matters only for a field the plan creates.

## Appearances

Every widget the editor writes gets an appearance stream — the form XObject a viewer draws inside the
rectangle. That is what makes a filled field visible to a printer, which never synthesises one, and it is why
the form the engine writes states `NeedAppearances false`.

- **Text and combo boxes** are drawn in the field's style; a value that does not fit a single line shrinks to
  fit, and a multiline value wraps at the spaces.
- **A password field is drawn masked**, one `*` per character: the appearance is what a reader shows, and
  writing the characters into it would undo the reason the field is masked.
- **A list box** draws its options and highlights the selected one.
- **A check box and a radio button** get both states, `Off` and the field's on state, and the widget states
  the one it is in.
- **A push button** draws its frame and its caption.
- **A field that states an appearance of its own** keeps it: the caller's operators are written, with the box
  and the font from `PdfAnnotationAppearance`.

The form also carries a `/DR` with every font it names and a `/DA` that selects the first one, which is what a
viewer uses if it decides to draw a field itself.

## Reset

```csharp
var plan = PdfEditPlan.Create().ResetForm();
```

A reset restores each field's `/DV`, the value the document stated when it was designed, and empties the
fields that state none. A read-only field is reset too: the reset restores what the document said, and
refusing it would leave a filled field that the caller asked to empty. The appearances are regenerated, so
what a reader shows and what the field holds cannot disagree.

## Flatten

```csharp
var plan = PdfEditPlan.Create().FlattenForm();          // every page
var one = PdfEditPlan.Create().FlattenForm(2);          // one page
```

Flattening draws each widget's appearance into the content of the page that carries it, so the value is part
of the page: a viewer without form support — and a printer — still shows it. The widgets leave the page's
`/Annots` array, the fields leave `/Fields`, and a form whose last widget is gone is removed from the catalog
rather than written empty.

The appearance is placed by the widget's rectangle, not by its default appearance: what the reader saw before
the flatten is what the page shows after it. Where a widget states no appearance at all, one is generated
from the value its field carries — a form in the wild often relies on the viewer for that, and a flatten
cannot copy a stream that is not there.

Two widgets deliberately survive a flatten:

- **a field that is drawn nowhere** has no widget to draw, so there is nothing to flatten;
- **a signature field** has no static appearance this writer can draw, so it is left interactive rather than
  dropped without a mark.

## Refusals

| Condition | Result |
| --- | --- |
| A field the document states as read-only | Refused with the field's name |
| A signature field | Refused: signing is a separate surface |
| A name that would sit inside an existing one | Refused with both names |
| Duplicate field name in a form, or two operations writing one name | Refused by the plan, before the source is opened |
| A value that is not one of the field's options | Refused with the value and the list |
| A page the field names does not exist | Refused with the page number |

## What the engine reports

| Warning | When |
| --- | --- |
| `FormAppearanceGenerated` | The fields of a page carry an appearance the engine drew |
| `XfaFormRemoved` | The source carried an XFA packet, which cannot be kept beside fields this editor writes |
| `FormFieldActionPreserved` | A field carries an action; it is copied as it stands and never executed |
| `FormFieldUnreadable` | A field of the source could not be resolved while reading |

## XFA

An XFA form is a form of its own, written as an XML packet beside the fields. A viewer that honours it shows
the XFA fields instead of the AcroForm ones, so writing AcroForm fields into a document that carries both
would produce a form nobody sees. The packet is removed, and `XfaFormRemoved` says so — a warning rather than
a refusal, because the alternative is refusing to fill a document that a lot of tools produce.

## Reading the result back

The read side is the same field tree a caller inspects:

```csharp
var fields = await reader.ReadFormFieldsAsync(new PdfFileSource("form-filled.pdf"));

foreach (var field in fields.Items)
{
    Console.WriteLine($"{field.Name} ({field.FieldType}) = {field.Value}");
}
```

`PdfFormFieldInfo` states the fully-qualified name, the document's own type token (`Tx`, `Btn`, `Ch`, `Sig`),
the value and the default value, the flags, and every widget with the page and rectangle it occupies. A value
the document does not state is `null`, never an empty string, so "absent" and "empty" stay distinguishable.

## Where it is tested

`tests/SharpCorePDF.Tests/PdfFormTests.cs` covers the model: the factories, the derived flags, the refusals.
`PdfFormEditorTests.cs` covers the engine: `Fills_and_reads_back_form_values`,
`Fills_a_field_the_document_already_carries`, `Refuses_a_read_only_field`,
`Refuses_an_unsupported_field_type`, `Flattens_a_form_without_a_form_viewer`,
`Resets_the_form_to_its_default_values` and `Removes_the_xfa_form_and_says_so`.

The `fill-form` example writes `form-created.pdf`, `form-filled.pdf` and `form-flattened.pdf`:

- `docs/tools/validate-pdf.py` reports `qpdf check: no problems` for all three, with page 1 carrying five
  annotations and a 62-byte content stream before the flatten, and no annotations and 226 bytes after it;
- `pypdf` reads `person.name = Grace Hopper`, `agree = /Off` and `country = BE` from the filled form, and no
  fields at all from the flattened one;
- PyMuPDF renders the flattened page and reads `Grace Hopper` as **page text**, which is what "visible
  without a form viewer" means in practice.

## What this chapter does not cover

No JavaScript is executed, no calculation or validation script runs, and no action on a field is fired: `/AA`
dictionaries are copied as they stand and reported. XFA is not written, a signature field is not filled by
this surface (chapter 23 signs), redaction is not offered, and rich-text and comb fields are not modelled
because the writer would not honour them.

