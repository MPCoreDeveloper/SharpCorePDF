using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The form half of <see cref="PdfPageContentReader"/>: the field tree of a document, read without
/// ever being written to.
/// </summary>
public sealed partial class PdfPageContentReader
{
    /// <summary>The depth to which a field tree is followed, as a guard against a damaged document.</summary>
    private const int MaxFieldDepth = 32;

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfFormFieldInfo>> ReadFormFieldsAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        NoteClosedEncryption(reader, warnings);

        return Result(reader, ReadFormFields(reader, warnings), warnings);
    }

    /// <summary>Reads the field tree of an already opened document.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <returns>Every field, in the order the form states them.</returns>
    private static List<PdfFormFieldInfo> ReadFormFields(PdfFileReader reader, List<PdfWarning> warnings)
    {
        var fields = new List<PdfFormFieldInfo>();
        var form = reader.ResolveDictionary(reader.Catalog?.Get("AcroForm"));

        if (reader.ResolveArray(form?.Get("Fields")) is not { } roots)
        {
            return fields;
        }

        var widgetPages = BuildWidgetPageMap(reader);
        var visited = new HashSet<long>();

        foreach (var root in roots.Items)
        {
            ReadField(reader, root, parentName: null, inheritedType: null, widgetPages, fields, warnings, visited, 0);
        }

        return fields;
    }

    /// <summary>Reads one node of the field tree, which may be a field, a widget, or both.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value of the node.</param>
    /// <param name="parentName">The fully-qualified name of the parent, or <see langword="null"/>.</param>
    /// <param name="inheritedType">The field type the parent states, or <see langword="null"/>.</param>
    /// <param name="widgetPages">The map from widget object numbers to page numbers.</param>
    /// <param name="fields">The list that receives the fields.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <param name="visited">The object numbers already read, so a damaged tree cannot loop.</param>
    /// <param name="depth">The depth of the node.</param>
    private static void ReadField(
        PdfFileReader reader,
        PdfObject? value,
        string? parentName,
        string? inheritedType,
        Dictionary<long, int> widgetPages,
        List<PdfFormFieldInfo> fields,
        List<PdfWarning> warnings,
        HashSet<long> visited,
        int depth)
    {
        if (depth > MaxFieldDepth)
        {
            return;
        }

        if (value is PdfReference reference && !visited.Add(reference.ObjectNumber))
        {
            return;
        }

        var dictionary = reader.ResolveDictionary(value);

        if (dictionary is null)
        {
            warnings.Add(new(
                PdfWarningKind.FormFieldUnreadable,
                $"'{reader.SourceName}': a form field could not be resolved and was skipped."));
            return;
        }

        var partial = ReadText(reader, dictionary.Get("T"));
        var name = parentName;

        if (!string.IsNullOrEmpty(partial))
        {
            name = parentName is null ? partial : $"{parentName}.{partial}";
        }

        var fieldType = ReadName(reader, dictionary.Get("FT")) ?? inheritedType;
        var widgets = new List<PdfWidgetInfo>();
        var kids = reader.ResolveArray(dictionary.Get("Kids"));

        if (kids is not null)
        {
            foreach (var kid in kids.Items)
            {
                var kidDictionary = reader.ResolveDictionary(kid);

                if (kidDictionary is null)
                {
                    warnings.Add(new(
                        PdfWarningKind.FormFieldUnreadable,
                        $"'{reader.SourceName}': a child of form field '{name ?? "?"}' could not be resolved."));
                    continue;
                }

                // A widget is a kid that displays the field rather than being a field of its own: it is
                // typed as one, and it contributes no name of its own to the tree.
                var isWidget = kidDictionary.GetName("Subtype") == "Widget"
                    && string.IsNullOrEmpty(ReadText(reader, kidDictionary.Get("T")));

                if (isWidget)
                {
                    widgets.Add(BuildWidget(reader, kid, widgetPages));
                    continue;
                }

                ReadField(reader, kid, name, fieldType, widgetPages, fields, warnings, visited, depth + 1);
            }
        }

        // A field that is its own widget — the usual shape for a single text box — has no widget kids.
        if (widgets.Count == 0 && dictionary.ContainsKey("Rect"))
        {
            widgets.Add(BuildWidget(reader, value, widgetPages));
        }

        if (string.IsNullOrEmpty(name) && fieldType is null)
        {
            return;
        }

        // A node that only groups children contributes its part of the name and nothing else: the
        // fields below it carry the fully-qualified names a caller addresses.
        if (fieldType is null && kids is not null)
        {
            return;
        }

        fields.Add(new PdfFormFieldInfo
        {
            Name = name ?? string.Empty,
            FieldType = fieldType,
            Value = ReadFieldValue(reader, dictionary.Get("V")),
            DefaultValue = ReadFieldValue(reader, dictionary.Get("DV")),
            Flags = (int)(dictionary.GetInteger("Ff") ?? 0),
            Widgets = widgets,
        });
    }

    /// <summary>Builds the description of one widget.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value of the widget.</param>
    /// <param name="widgetPages">The map from widget object numbers to page numbers.</param>
    /// <returns>The widget.</returns>
    private static PdfWidgetInfo BuildWidget(
        PdfFileReader reader,
        PdfObject? value,
        Dictionary<long, int> widgetPages)
    {
        var dictionary = reader.ResolveDictionary(value);

        return new PdfWidgetInfo
        {
            PageNumber = value is PdfReference reference && widgetPages.TryGetValue(reference.ObjectNumber, out var page)
                ? page
                : null,
            Rectangle = dictionary is null ? null : ReadRectangle(reader, dictionary.Get("Rect")),
        };
    }

    /// <summary>Maps the object number of every widget to the page that carries it.</summary>
    /// <param name="reader">The document.</param>
    /// <returns>The map.</returns>
    private static Dictionary<long, int> BuildWidgetPageMap(PdfFileReader reader)
    {
        var map = new Dictionary<long, int>();

        foreach (var page in reader.Pages)
        {
            if (reader.ResolveArray(page.Dictionary.Get("Annots")) is not { } annots)
            {
                continue;
            }

            foreach (var item in annots.Items)
            {
                if (item is PdfReference reference)
                {
                    map[reference.ObjectNumber] = page.Index + 1;
                }
            }
        }

        return map;
    }

    /// <summary>Reads the value of a field, which may be text, a name, or a list of either.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value of the entry.</param>
    /// <returns>
    /// The value, or <see langword="null"/> when the field has none — a signature's value is a
    /// dictionary and is not interpreted, because validating signatures is a non-goal.
    /// </returns>
    private static string? ReadFieldValue(PdfFileReader reader, PdfObject? value) => reader.Resolve(value) switch
    {
        PdfString text => text.ToText(),
        PdfName name => name.Value,
        PdfArray array => string.Join(
            ", ",
            array.Items.Select(item => ReadFieldValue(reader, item)).Where(item => item is not null)),
        _ => null,
    };
}