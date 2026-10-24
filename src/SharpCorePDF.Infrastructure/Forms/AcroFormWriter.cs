using System.Globalization;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Annotations;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Forms;

/// <summary>
/// Reads the AcroForm of the source document and writes the form of the result: the fields the plan
/// fills, the ones it creates, the ones a reset puts back to their default, and the ones a flatten
/// draws into the page.
/// </summary>
/// <remarks>
/// <para>
/// A field the document already carries is filled where it stands: its body and the bodies of its
/// widgets are rewritten as they are copied, so every reference the document had — the page's
/// <c>/Annots</c>, the parent's <c>/Kids</c>, the catalog's <c>/Fields</c> — keeps pointing at the same
/// object. That is what "filled in place" means: the geometry, the ancestors and the widgets survive,
/// and only the value and the appearance change.
/// </para>
/// <para>
/// A name the document does not carry becomes a new field, with the appearance this class generates,
/// and a name that would sit inside an existing one — <c>a.b</c> beside an existing <c>a</c>, or the
/// other way round — is refused, because a document cannot have two field trees under one name.
/// </para>
/// </remarks>
internal sealed class AcroFormWriter
{
    /// <summary>The depth to which the field tree of the source is followed.</summary>
    private const int MaxDepth = 32;

    private readonly PdfFileReader reader;
    private readonly PdfObjectCopier copier;
    private readonly PdfWriter writer;
    private readonly List<FormFieldEntry> fields = [];
    private readonly List<long> roots = [];
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly Dictionary<long, long> parentOf = [];
    private readonly Dictionary<long, Action<PdfDictionary>> rewrites = [];
    private readonly Dictionary<long, Flattenable> appearances = [];
    private readonly Dictionary<StandardFont, int> fontObjects = [];
    private readonly Dictionary<StandardFont, string> fontNames = [];
    private readonly List<(long Root, long Leaf)> created = [];
    private readonly HashSet<long> removed = [];

    /// <summary>Initializes a new instance of the <see cref="AcroFormWriter"/> class.</summary>
    /// <param name="reader">The document being edited.</param>
    /// <param name="copier">The copier that writes that document's objects into the result.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <exception cref="ArgumentNullException">One of the arguments is <see langword="null"/>.</exception>
    public AcroFormWriter(PdfFileReader reader, PdfObjectCopier copier, PdfWriter writer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(copier);
        ArgumentNullException.ThrowIfNull(writer);

        this.reader = reader;
        this.copier = copier;
        this.writer = writer;

        var form = reader.ResolveDictionary(reader.Catalog?.Get("AcroForm"));
        HasXfa = reader.Resolve(form?.Get("XFA")) is not null;

        if (reader.ResolveArray(form?.Get("Fields")) is not { } declared)
        {
            return;
        }

        foreach (var root in declared.Items)
        {
            if (root is PdfReference reference)
            {
                roots.Add(reference.ObjectNumber);
            }

            Walk(root, parentName: null, inheritedType: null, inheritedFlags: 0, parentNumber: 0, depth: 0);
        }
    }

    /// <summary>Gets a value indicating whether the source carries an XFA packet beside its fields.</summary>
    public bool HasXfa { get; }

    /// <summary>Gets the fields a fill or a reset can address, in the order the form states them.</summary>
    public IReadOnlyList<FormFieldEntry> Fields => fields;

    /// <summary>Finds the field a name states.</summary>
    /// <param name="name">The fully-qualified name.</param>
    /// <returns>The field, or <see langword="null"/> for a name the document does not carry.</returns>
    public FormFieldEntry? Find(string name) =>
        fields.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));

    /// <summary>States whether a name would collide with the field tree the document already has.</summary>
    /// <param name="name">The fully-qualified name of the field to create.</param>
    /// <returns>The conflicting name, or <see langword="null"/> when the tree has room for it.</returns>
    /// <remarks>
    /// A name that exists is filled rather than created, so it is not a conflict; what is refused is a
    /// name that would have to live inside an existing one, or an existing one inside it.
    /// </remarks>
    public string? ConflictOf(string name)
    {
        foreach (var existing in names)
        {
            if (!string.Equals(existing, name, StringComparison.Ordinal)
                && (existing.StartsWith($"{name}.", StringComparison.Ordinal)
                    || name.StartsWith($"{existing}.", StringComparison.Ordinal)))
            {
                return existing;
            }
        }

        return null;
    }

    /// <summary>Reads one node of the field tree.</summary>
    /// <param name="value">The value of the node.</param>
    /// <param name="parentName">The fully-qualified name of the parent, or <see langword="null"/>.</param>
    /// <param name="inheritedType">The field type the parent states, or <see langword="null"/>.</param>
    /// <param name="inheritedFlags">The field flags the parent states.</param>
    /// <param name="parentNumber">The object number of the parent field, or zero for a root.</param>
    /// <param name="depth">The depth of the node.</param>
    private void Walk(
        PdfObject? value,
        string? parentName,
        string? inheritedType,
        int inheritedFlags,
        long parentNumber,
        int depth)
    {
        if (depth > MaxDepth || reader.ResolveDictionary(value) is not { } dictionary)
        {
            return;
        }

        var number = value is PdfReference reference ? reference.ObjectNumber : 0;

        if (number != 0 && parentNumber != 0)
        {
            parentOf[number] = parentNumber;
        }

        var partial = dictionary.GetText("T");
        var name = parentName;

        if (!string.IsNullOrEmpty(partial))
        {
            name = parentName is null ? partial : string.Concat(parentName, ".", partial);
        }
        var fieldType = dictionary.GetName("FT") ?? inheritedType;
        var flags = (int)(dictionary.GetInteger("Ff") ?? inheritedFlags);

        if (!string.IsNullOrEmpty(name))
        {
            names.Add(name);
        }

        var widgets = new List<long>();
        var fieldKids = 0;

        if (reader.ResolveArray(dictionary.Get("Kids")) is { } kids)
        {
            foreach (var kid in kids.Items)
            {
                var kidDictionary = reader.ResolveDictionary(kid);
                var isWidget = kidDictionary?.GetName("Subtype") == "Widget"
                    && string.IsNullOrEmpty(kidDictionary.GetText("T"));

                if (isWidget)
                {
                    if (kid is PdfReference widget)
                    {
                        widgets.Add(widget.ObjectNumber);
                        parentOf[widget.ObjectNumber] = number;
                    }

                    continue;
                }

                fieldKids++;
                Walk(kid, name, fieldType, flags, number != 0 ? number : parentNumber, depth + 1);
            }
        }

        // A field that is its own widget — the usual shape for a single text box — carries the
        // rectangle itself and has no widget kids.
        if (widgets.Count == 0 && fieldKids == 0 && dictionary.ContainsKey("Rect") && number != 0)
        {
            widgets.Add(number);
        }

        // A node without a type is a group: it contributes its part of the name and nothing else.
        if (fieldType is null || fieldKids > 0 || number == 0)
        {
            return;
        }

        fields.Add(new FormFieldEntry
        {
            Name = name ?? string.Empty,
            FieldType = fieldType,
            Flags = flags,
            ObjectNumber = number,
            Widgets =
            [
                .. widgets.Select(widget => new FormWidgetEntry
                {
                    ObjectNumber = widget,
                    FieldNumber = number,
                    Rectangle = ReadRectangle(reader.ResolveDictionary(new PdfReference(widget, 0))?.Get("Rect")),
                }),
            ],
            HasActions = dictionary.ContainsKey("AA"),
        });
    }

    /// <summary>Fills a field the plan states, in place when the document already carries its name.</summary>
    /// <param name="index">The index of the operation, for diagnostics.</param>
    /// <param name="sourceName">The name of the document being edited.</param>
    /// <param name="field">The field to write.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="pageTargets">The object number of every page of the result, by one-based page number.</param>
    /// <returns>The object number of the widget a new field was written as, or zero for one filled in place.</returns>
    /// <exception cref="PdfEditPlanValidationException">The field is read-only, or its name would split an existing one.</exception>
    public int Fill(
        int index,
        string sourceName,
        PdfFormField field,
        PdfStandardTextStyle style,
        IReadOnlyDictionary<int, int> pageTargets)
    {
        if (Find(field.Name) is { } existing)
        {
            if (existing.FieldType == "Sig")
            {
                throw new PdfEditPlanValidationException(
                    $"operation {index}: {FieldAppearance.Describe(field)} of '{sourceName}' is a signature field, and this editor does not fill one: signing is a separate surface with its own contract.");
            }

            if (existing.IsReadOnly)
            {
                throw new PdfEditPlanValidationException(
                    $"operation {index}: {FieldAppearance.Describe(field)} is read-only in '{sourceName}', so its value cannot be written. A reset restores such a field; a fill does not change it.");
            }

            ApplyExisting(existing, field, style);
            return 0;
        }

        if (ConflictOf(field.Name) is { } conflict)
        {
            throw new PdfEditPlanValidationException(
                $"operation {index}: {FieldAppearance.Describe(field)} would sit inside the existing field '{conflict}' of '{sourceName}', and a document keeps one tree of field names.");
        }

        if (!pageTargets.TryGetValue(field.PageNumber, out var pageTarget))
        {
            throw new PdfEditPlanValidationException(
                $"operation {index}: {FieldAppearance.Describe(field)} names page {field.PageNumber}, which '{sourceName}' does not have.");
        }

        return Create(field, style, pageTarget);
    }

    /// <summary>Puts every field of the document back to the value it states as its default.</summary>
    /// <param name="style">The style the restored values are drawn in.</param>
    public void Reset(PdfStandardTextStyle style)
    {
        foreach (var entry in fields)
        {
            var dictionary = reader.ResolveDictionary(new PdfReference(entry.ObjectNumber, 0));
            var declared = reader.Resolve(dictionary?.Get("DV"));

            // A field with no default is emptied, which is what a viewer's own reset button does.
            var value = declared is PdfString or PdfName ? declared : null;
            ApplyExisting(entry, ShapeOf(entry, dictionary, value), style, value);
        }
    }

    /// <summary>Rewrites the body of an object of the source as it is copied.</summary>
    /// <param name="objectNumber">The object number in the source.</param>
    /// <param name="rewrite">The rewrite to apply to the copied body.</param>
    private void Rewrite(long objectNumber, Action<PdfDictionary> rewrite)
    {
        if (rewrites.TryGetValue(objectNumber, out var existing))
        {
            rewrites[objectNumber] = existing + rewrite;
            return;
        }

        rewrites[objectNumber] = rewrite;
        copier.Override(objectNumber, body =>
        {
            if (body is PdfDictionary dictionary && rewrites.TryGetValue(objectNumber, out var apply))
            {
                apply(dictionary);
            }

            return body;
        });
    }

    /// <summary>Writes the value and the appearances of a field the document already carries.</summary>
    /// <param name="entry">The field.</param>
    /// <param name="field">The field as the caller stated it, which supplies the kind and the value.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="stored">The value to store, or <see langword="null"/> to use the one the field carries.</param>
    private void ApplyExisting(
        FormFieldEntry entry,
        PdfFormField field,
        PdfStandardTextStyle style,
        PdfObject? stored = null)
    {
        var value = stored ?? ValueOf(field);
        var state = StateOf(field);
        var option = OptionIndexOf(field);

        foreach (var widget in entry.Widgets)
        {
            // The rectangle the document states wins: the field is filled where it stands, so the
            // caller's rectangle describes a placement that already exists.
            var view = field with { Rectangle = widget.Rectangle ?? field.Rectangle };
            var normal = WriteNormalAppearance(view, style, widget.ObjectNumber);

            Rewrite(widget.ObjectNumber, dictionary =>
            {
                dictionary.Set("AP", new PdfDictionary().Set("N", normal));

                if (state is not null)
                {
                    dictionary.Set("AS", new PdfName(state));
                }
            });
        }

        Rewrite(entry.ObjectNumber, dictionary =>
        {
            if (value is null)
            {
                dictionary.Remove("V");
                dictionary.Remove("I");
                return;
            }

            dictionary.Set("V", value);

            if (option >= 0)
            {
                dictionary.Set("I", new PdfArray([new PdfInteger(option)]));
            }
        });
    }

    /// <summary>Writes the normal appearance of one widget and returns what its <c>/AP</c> names.</summary>
    /// <param name="field">The field, with the rectangle of the widget it is drawn in.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="widgetNumber">The object number of the widget, which keys the appearance.</param>
    /// <returns>The value of <c>/N</c>: a stream, or the two states of a button.</returns>
    private PdfObject WriteNormalAppearance(PdfFormField field, PdfStandardTextStyle style, long widgetNumber)
    {
        var box = FieldAppearance.BoxOf(field);

        if (field.Kind is PdfFormFieldKind.CheckBox or PdfFormFieldKind.RadioButton)
        {
            var off = WriteAppearance(field, style, on: false);
            var on = WriteAppearance(field, style, on: true);
            var states = new PdfDictionary();
            states.Set("Off", new PdfReference(off, 0));
            states.Set(OnStateOf(field), new PdfReference(on, 0));
            appearances[widgetNumber] = new Flattenable
            {
                Rectangle = field.Rectangle,
                Box = box,
                StreamNumber = field.IsChecked ? on : off,
            };

            return states;
        }

        var number = WriteAppearance(field, style, on: true);
        appearances[widgetNumber] = new Flattenable
        {
            Rectangle = field.Rectangle,
            Box = box,
            StreamNumber = number,
        };

        return new PdfReference(number, 0);
    }

    /// <summary>Writes one appearance stream and returns its object number.</summary>
    /// <param name="field">The field, with the box the appearance is drawn in.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="on">Whether a button is drawn on.</param>
    /// <returns>The object number of the stream.</returns>
    private int WriteAppearance(PdfFormField field, PdfStandardTextStyle style, bool on)
    {
        var font = FieldAppearance.DrawsText(field) ? FontOf(style.Font) : 0;
        var number = writer.AllocateObject();
        writer.WriteObject(number, FieldAppearance.Build(field, style, on, font));
        return number;
    }

    /// <summary>Gets the value a field stores in its <c>/V</c> entry.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The value, or <see langword="null"/> for a field that holds none.</returns>
    private static PdfObject? ValueOf(PdfFormField field) => field.Kind switch
    {
        PdfFormFieldKind.Text or PdfFormFieldKind.Choice => field.Value is null ? null : PdfString.FromText(field.Value),
        PdfFormFieldKind.CheckBox or PdfFormFieldKind.RadioButton => new PdfName(
            field.IsChecked ? OnStateOf(field) : "Off"),
        _ => null,
    };

    /// <summary>Gets the state a button is in, as its widget states it.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The name of the state, or <see langword="null"/> for a field that has no states.</returns>
    private static string? StateOf(PdfFormField field)
    {
        if (field.Kind is not (PdfFormFieldKind.CheckBox or PdfFormFieldKind.RadioButton))
        {
            return null;
        }

        return field.IsChecked ? OnStateOf(field) : "Off";
    }

    /// <summary>Gets the position of the value of a choice field in its option list.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The index, or <c>-1</c> for a field whose value is not one of its options.</returns>
    private static int OptionIndexOf(PdfFormField field) => field.Kind == PdfFormFieldKind.Choice && field.Value is { } value
        ? field.Options.ToList().IndexOf(value)
        : -1;

    /// <summary>Gets the name a button is on under, never <c>Off</c>, which names the state that is not on.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The on state.</returns>
    private static string OnStateOf(PdfFormField field) =>
        string.IsNullOrWhiteSpace(field.OnState) || string.Equals(field.OnState, "Off", StringComparison.Ordinal)
            ? "Yes"
            : field.OnState;

    /// <summary>Builds a field from what the document states, so a reset can draw it again.</summary>
    /// <param name="entry">The field.</param>
    /// <param name="dictionary">The body of the field.</param>
    /// <param name="value">The value the document states, or <see langword="null"/>.</param>
    /// <returns>The field, as the appearance writer needs it.</returns>
    private PdfFormField ShapeOf(FormFieldEntry entry, PdfDictionary? dictionary, PdfObject? value)
    {
        var flags = entry.Flags;
        var kind = entry.FieldType switch
        {
            "Ch" => PdfFormFieldKind.Choice,
            "Btn" when (flags & (1 << 16)) != 0 => PdfFormFieldKind.PushButton,
            "Btn" when (flags & (1 << 15)) != 0 => PdfFormFieldKind.RadioButton,
            "Btn" => PdfFormFieldKind.CheckBox,
            _ => PdfFormFieldKind.Text,
        };

        var options = new List<string>();

        if (reader.ResolveArray(dictionary?.Get("Opt")) is { } declared)
        {
            options.AddRange(declared.Items
                .Select(item => reader.Resolve(item))
                .OfType<PdfString>()
                .Select(text => text.ToText()));
        }

        var widget = entry.Widgets.Count > 0 ? entry.Widgets[0] : null;
        var widgetDictionary = widget is null ? null : reader.ResolveDictionary(new PdfReference(widget.ObjectNumber, 0));

        return new PdfFormField
        {
            Name = entry.Name,
            Kind = kind,
            Rectangle = widget?.Rectangle ?? new PdfRectangle(0, 0, 1, 1),
            Value = value switch
            {
                PdfString text => text.ToText(),
                PdfName name => name.Value,
                _ => null,
            },
            IsChecked = value is PdfName state && !string.Equals(state.Value, "Off", StringComparison.Ordinal),
            OnState = OnStateOf(widgetDictionary ?? dictionary, options),
            Options = options,
            IsMultiline = (flags & (1 << 12)) != 0,
            IsPassword = (flags & (1 << 13)) != 0,
            IsCombo = (flags & (1 << 17)) != 0,
            IsEditable = (flags & (1 << 18)) != 0,
            AllowMultiSelect = (flags & (1 << 21)) != 0,
            MaxLength = (int?)dictionary?.GetInteger("MaxLen"),
        };
    }

    /// <summary>Reads the export value a check box or a radio button states in its widget.</summary>
    /// <param name="dictionary">The body of the widget, or of the field.</param>
    /// <param name="options">The options of the field, which are empty for a button.</param>
    /// <returns>The on state.</returns>
    private string OnStateOf(PdfDictionary? dictionary, List<string> options)
    {
        var normal = dictionary is null
            ? null
            : reader.ResolveDictionary(reader.ResolveDictionary(dictionary.Get("AP"))?.Get("N"));

        if (normal is not null)
        {
            var on = normal.Keys.FirstOrDefault(key => !string.Equals(key, "Off", StringComparison.Ordinal));

            if (on is not null)
            {
                return on;
            }
        }

        return options.Count > 0 ? options[0] : "Yes";
    }

    /// <summary>Writes a field the document does not carry, as a node of a new or existing tree.</summary>
    /// <param name="field">The field to create.</param>
    /// <param name="style">The style its value is drawn in.</param>
    /// <param name="pageTarget">The object number of the page of the result that carries it.</param>
    /// <returns>The object number of the widget, which the page's <c>/Annots</c> names.</returns>
    /// <remarks>
    /// A dotted name becomes a tree: one node per segment, each stating its part of the name, with the
    /// widget on the last. That is how a PDF spells <c>a.b</c>, and it is why the name of a field is
    /// only unique once its ancestors are joined to it.
    /// </remarks>
    private int Create(PdfFormField field, PdfStandardTextStyle style, int pageTarget)
    {
        var segments = field.Name.Split('.');
        var numbers = new int[segments.Length];

        for (var index = 0; index < numbers.Length; index++)
        {
            numbers[index] = writer.AllocateObject();
        }

        var leaf = numbers[^1];

        for (var index = 0; index < numbers.Length; index++)
        {
            var dictionary = new PdfDictionary().Set("T", PdfString.FromText(segments[index]));

            if (index > 0)
            {
                dictionary.Set("Parent", new PdfReference(numbers[index - 1], 0));
            }

            if (index == numbers.Length - 1)
            {
                FillLeaf(dictionary, field, style, pageTarget, leaf);
            }
            else
            {
                dictionary.Set("Kids", new PdfArray([new PdfReference(numbers[index + 1], 0)]));
            }

            writer.WriteObject(numbers[index], dictionary);
        }

        created.Add((numbers[0], leaf));
        names.Add(field.Name);
        return leaf;
    }

    /// <summary>Fills the body of the node that carries both the field and its widget.</summary>
    /// <param name="dictionary">The body being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="style">The style its value is drawn in.</param>
    /// <param name="pageTarget">The object number of the page that carries it.</param>
    /// <param name="widgetNumber">The object number the node is written as.</param>
    private void FillLeaf(
        PdfDictionary dictionary,
        PdfFormField field,
        PdfStandardTextStyle style,
        int pageTarget,
        int widgetNumber)
    {
        dictionary
            .SetName("Type", "Annot")
            .SetName("Subtype", "Widget")
            .SetName("FT", FieldTypeName(field.Kind))
            .Set("Rect", AppearanceStreams.ToArray(field.Rectangle))
            .Set("P", new PdfReference(pageTarget, 0))
            .SetInteger("F", 4)
            .SetInteger("Ff", field.Flags)
            .Set("DA", PdfString.FromText(DefaultAppearance(style)));

        if (ValueOf(field) is { } value)
        {
            dictionary.Set("V", value);
        }

        if (StateOf(field) is { } state)
        {
            dictionary.Set("AS", new PdfName(state));
        }

        if (field.Options.Count > 0)
        {
            dictionary.Set("Opt", new PdfArray(field.Options.Select(option => (PdfObject)PdfString.FromText(option))));
        }

        if (field.MaxLength is { } maximum)
        {
            dictionary.SetInteger("MaxLen", maximum);
        }

        if (field.ToolTip is { Length: > 0 } tip)
        {
            dictionary.Set("TU", PdfString.FromText(tip));
        }

        dictionary.Set("AP", new PdfDictionary().Set("N", WriteNormalAppearance(field, style, widgetNumber)));
    }

    /// <summary>Gets the field type as the document spells it.</summary>
    /// <param name="kind">The kind of the field.</param>
    /// <returns>The two-letter token.</returns>
    private static string FieldTypeName(PdfFormFieldKind kind) => kind switch
    {
        PdfFormFieldKind.Text => "Tx",
        PdfFormFieldKind.Choice => "Ch",
        _ => "Btn",
    };

    /// <summary>Builds the default appearance of a field: the font and the colour its value is drawn in.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The operators, as the string a <c>/DA</c> entry holds.</returns>
    private string DefaultAppearance(PdfStandardTextStyle style) => string.Create(
        CultureInfo.InvariantCulture,
        $"/{FontNameOf(style.Font)} {style.Size:0.###} Tf {style.Color.Red / 255.0:0.###} {style.Color.Green / 255.0:0.###} {style.Color.Blue / 255.0:0.###} rg");

    /// <summary>Gets the object number of a standard font, writing it the first time it is asked for.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The object number.</returns>
    private int FontOf(StandardFont font)
    {
        if (fontObjects.TryGetValue(font, out var existing))
        {
            return existing;
        }

        var number = writer.AllocateObject();
        writer.WriteObject(
            number,
            new PdfDictionary()
                .SetName("Type", "Font")
                .SetName("Subtype", "Type1")
                .SetName("BaseFont", BaseFontOf(font))
                .SetName("Encoding", "WinAnsiEncoding"));

        fontObjects[font] = number;
        fontNames[font] = string.Create(CultureInfo.InvariantCulture, $"F{fontObjects.Count}");
        return number;
    }

    /// <summary>Gets the resource name a font is selected by in the default appearance of a field.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The name, without the slash.</returns>
    private string FontNameOf(StandardFont font)
    {
        _ = FontOf(font);
        return fontNames[font];
    }

    /// <summary>Gets the name a standard font is written as.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The base font name.</returns>
    private static string BaseFontOf(StandardFont font) => font switch
    {
        StandardFont.Helvetica => "Helvetica",
        StandardFont.HelveticaBold => "Helvetica-Bold",
        StandardFont.HelveticaOblique => "Helvetica-Oblique",
        StandardFont.HelveticaBoldOblique => "Helvetica-BoldOblique",
        StandardFont.TimesRoman => "Times-Roman",
        StandardFont.TimesBold => "Times-Bold",
        StandardFont.TimesItalic => "Times-Italic",
        StandardFont.TimesBoldItalic => "Times-BoldItalic",
        StandardFont.Courier => "Courier",
        StandardFont.CourierBold => "Courier-Bold",
        StandardFont.CourierOblique => "Courier-Oblique",
        StandardFont.CourierBoldOblique => "Courier-BoldOblique",
        _ => throw new ArgumentOutOfRangeException(nameof(font), font, "The font is not a standard font."),
    };

    /// <summary>Gets the appearance a flattened page draws for a widget.</summary>
    /// <param name="widgetNumber">The object number of the widget in the source document.</param>
    /// <param name="style">The style an appearance is generated in when the widget states none.</param>
    /// <returns>The appearance, or <see langword="null"/> for a widget with nothing to draw.</returns>
    public Flattenable? TryDraw(long widgetNumber, PdfStandardTextStyle style)
    {
        if (appearances.TryGetValue(widgetNumber, out var generated))
        {
            return generated;
        }

        var widget = fields.SelectMany(field => field.Widgets).FirstOrDefault(entry => entry.ObjectNumber == widgetNumber);

        if (widget?.Rectangle is not { } rectangle)
        {
            return null;
        }

        var normal = reader.ResolveDictionary(reader.ResolveDictionary(new PdfReference(widgetNumber, 0))?.Get("AP"))?.Get("N");

        // A widget states its appearance either as one stream or as the dictionary of its states, and
        // a flatten draws the state the field is in — which is what the reader saw before the flatten.
        if (normal is PdfReference stream && reader.Resolve(stream) is PdfStream)
        {
            return Of(rectangle, stream.ObjectNumber);
        }

        if (reader.ResolveDictionary(normal) is { } states)
        {
            var value = reader.ResolveDictionary(new PdfReference(widget.FieldNumber, 0))?.GetName("V") ?? "Off";
            var chosen = states.Get(value) ?? states.Get("Off");

            if (chosen is PdfReference state && reader.Resolve(state) is PdfStream)
            {
                return Of(rectangle, state.ObjectNumber);
            }
        }

        return Generate(widget, style);
    }

    /// <summary>
    /// Draws the appearance of a widget that states none, from the value its field carries.
    /// </summary>
    /// <param name="widget">The widget.</param>
    /// <param name="style">The style the appearance is drawn in.</param>
    /// <returns>The appearance, or <see langword="null"/> for a kind this writer cannot draw.</returns>
    /// <remarks>
    /// A form in the wild often states no appearance at all and relies on the viewer to build one —
    /// which is what <c>/NeedAppearances</c> asks for. Flattening such a form has to draw the value,
    /// because there is no stream to copy, so the appearance is generated here before the field is
    /// taken off the page.
    /// </remarks>
    private Flattenable? Generate(FormWidgetEntry widget, PdfStandardTextStyle style)
    {
        var entry = fields.FirstOrDefault(field => field.ObjectNumber == widget.FieldNumber);
        var dictionary = reader.ResolveDictionary(new PdfReference(widget.FieldNumber, 0));

        if (entry is null || dictionary is null || entry.FieldType is not ("Tx" or "Btn" or "Ch"))
        {
            return null;
        }

        var value = reader.Resolve(dictionary.Get("V"));
        var shape = ShapeOf(entry, dictionary, value is PdfString or PdfName ? value : null);
        var number = WriteAppearance(shape, style, shape.IsChecked);
        var appearance = new Flattenable
        {
            Rectangle = widget.Rectangle ?? shape.Rectangle,
            Box = FieldAppearance.BoxOf(shape),
            StreamNumber = number,
        };

        appearances[widget.ObjectNumber] = appearance;
        return appearance;
    }

    /// <summary>Describes the appearance of a widget, reserving the stream in the result.</summary>
    /// <param name="rectangle">The rectangle the widget occupies.</param>
    /// <param name="streamNumber">The object number of the appearance in the source.</param>
    /// <returns>The appearance to draw.</returns>
    private Flattenable Of(PdfRectangle rectangle, long streamNumber)
    {
        var dictionary = reader.ResolveDictionary(new PdfReference(streamNumber, 0));

        return new Flattenable
        {
            Rectangle = rectangle,
            Box = ReadRectangle(dictionary?.Get("BBox")) ?? new PdfRectangle(0, 0, rectangle.Width, rectangle.Height),
            StreamNumber = copier.Reserve(streamNumber),
        };
    }

    /// <summary>Removes a widget from the form, which is what a flatten does to the fields it draws.</summary>
    /// <param name="widgetNumber">The object number of the widget in the source document.</param>
    public void Remove(long widgetNumber)
    {
        if (!removed.Add(widgetNumber))
        {
            return;
        }

        var widget = fields.SelectMany(field => field.Widgets).FirstOrDefault(entry => entry.ObjectNumber == widgetNumber);

        if (widget is null)
        {
            return;
        }

        // A field that is its own widget leaves the tree with the node its parent names; a widget that
        // is a kid of a field leaves the parent's /Kids array.
        var container = widget.ObjectNumber == widget.FieldNumber
            ? parentOf.GetValueOrDefault(widget.FieldNumber)
            : widget.FieldNumber;

        if (container == 0)
        {
            return;
        }

        var target = copier.Reserve(widgetNumber);

        Rewrite(container, dictionary =>
        {
            if (dictionary.Get("Kids") is PdfArray kids)
            {
                kids.Items.RemoveAll(item => item is PdfReference reference && reference.ObjectNumber == target);
            }
        });
    }

    /// <summary>Writes the form of the result.</summary>
    /// <returns>
    /// The object number of the <c>/AcroForm</c> dictionary, or zero when no field is left — a form
    /// whose last widget was flattened is removed rather than written empty.
    /// </returns>
    public int Write()
    {
        var declared = new PdfArray();

        foreach (var root in roots.Where(root => !IsGone(root)))
        {
            declared.Add(copier.Clone(new PdfReference(root, 0)));
        }

        foreach (var createdField in created.Where(entry => !removed.Contains(entry.Leaf)))
        {
            declared.Add(new PdfReference(createdField.Root, 0));
        }

        if (declared.Count == 0)
        {
            return 0;
        }

        var form = new PdfDictionary()
            .Set("Fields", declared)
            .Set("NeedAppearances", PdfBoolean.False);

        if (fontObjects.Count > 0)
        {
            var fonts = new PdfDictionary();

            foreach (var (font, objectNumber) in fontObjects)
            {
                fonts.Set(fontNames[font], new PdfReference(objectNumber, 0));
            }

            form.Set("DR", new PdfDictionary().Set("Font", fonts));
            form.Set("DA", PdfString.FromText(string.Create(CultureInfo.InvariantCulture, $"/{fontNames[fontObjects.Keys.First()]} 0 Tf 0 g")));
        }

        var number = writer.AllocateObject();
        writer.WriteObject(number, form);
        return number;
    }

    /// <summary>States whether a root field has no widget left on any page.</summary>
    /// <param name="root">The object number of the root field.</param>
    /// <returns><see langword="true"/> when every widget below it was flattened.</returns>
    private bool IsGone(long root)
    {
        var subtree = fields
            .Where(entry => entry.ObjectNumber == root || IsUnder(entry.ObjectNumber, root))
            .ToList();
        var widgets = subtree.SelectMany(entry => entry.Widgets).ToList();

        // A field that is drawn nowhere has no widget to flatten, so it survives a flatten even
        // though there is nothing of it on a page.
        return widgets.Count > 0 && widgets.All(widget => removed.Contains(widget.ObjectNumber));
    }

    /// <summary>States whether a field is a descendant of another one.</summary>
    /// <param name="number">The object number of the field.</param>
    /// <param name="root">The object number of the node it may sit under.</param>
    /// <returns><see langword="true"/> when the node is an ancestor.</returns>
    private bool IsUnder(long number, long root)
    {
        for (var depth = 0; depth < MaxDepth && parentOf.TryGetValue(number, out var parent); depth++)
        {
            if (parent == root)
            {
                return true;
            }

            number = parent;
        }

        return false;
    }

    /// <summary>Reads a rectangle the document states.</summary>
    /// <param name="value">The value of the entry.</param>
    /// <returns>The rectangle, or <see langword="null"/> when the entry is not one.</returns>
    private static PdfRectangle? ReadRectangle(PdfObject? value) => value is PdfArray { Count: 4 } array
        ? new PdfRectangle(
            ReadNumber(array.Items[0]),
            ReadNumber(array.Items[1]),
            ReadNumber(array.Items[2]),
            ReadNumber(array.Items[3]))
        : null;

    /// <summary>Reads one number of a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number, or zero when the value is not one.</returns>
    private static double ReadNumber(PdfObject? value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => 0.0,
    };
}

/// <summary>One field of the form of the document being edited.</summary>
internal sealed record FormFieldEntry
{
    /// <summary>Gets the fully-qualified name of the field.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the field type as the document spells it: <c>Tx</c>, <c>Btn</c>, <c>Ch</c> or <c>Sig</c>.</summary>
    public required string FieldType { get; init; }

    /// <summary>Gets the field flags the document states.</summary>
    public int Flags { get; init; }

    /// <summary>Gets the object number of the field.</summary>
    public long ObjectNumber { get; init; }

    /// <summary>Gets the widgets that display the field.</summary>
    public IReadOnlyList<FormWidgetEntry> Widgets { get; init; } = [];

    /// <summary>Gets a value indicating whether the field carries an action of its own.</summary>
    public bool HasActions { get; init; }

    /// <summary>Gets a value indicating whether the document states the field as read-only.</summary>
    public bool IsReadOnly => (Flags & 1) != 0;
}

/// <summary>One occurrence of a field, as the document states it.</summary>
internal sealed record FormWidgetEntry
{
    /// <summary>Gets the object number of the widget.</summary>
    public long ObjectNumber { get; init; }

    /// <summary>Gets the object number of the field the widget displays.</summary>
    public long FieldNumber { get; init; }

    /// <summary>Gets the rectangle the widget occupies, or <see langword="null"/> when it states none.</summary>
    public PdfRectangle? Rectangle { get; init; }
}

/// <summary>The appearance a flattened page draws where a widget stood.</summary>
internal sealed record Flattenable
{
    /// <summary>Gets the rectangle the widget occupies in page space.</summary>
    public required PdfRectangle Rectangle { get; init; }

    /// <summary>Gets the box the appearance is drawn in, which the rectangle is mapped onto.</summary>
    public required PdfRectangle Box { get; init; }

    /// <summary>Gets the object number of the appearance stream in the result.</summary>
    public int StreamNumber { get; init; }
}


