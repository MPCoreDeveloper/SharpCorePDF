using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// What ISO 19005 states about annotation dictionaries and the actions of a form, shared by the analysis that
/// decides whether a document can be written and the rewrite that makes the copy conform.
/// </summary>
/// <remarks>
/// <para>
/// The two halves read the same rules from here so that they cannot disagree: an annotation the analysis
/// accepts is one the rewrite leaves as it states it or repairs, and one the analysis refuses is never copied.
/// </para>
/// <para>
/// A repair is limited to what changes no page. A missing <c>/F</c>, or a Print bit that is not set, is made
/// right because the annotation is shown as it was and only prints as well; a flag that hides the annotation is
/// refused instead, because clearing it would put something on the page that nobody saw. The action of a widget
/// or a field is dropped because the form is read without running anything, as the profile requires.
/// </para>
/// </remarks>
internal static class PdfAAnnotationRules
{
    private const long Invisible = 1;
    private const long Hidden = 2;
    private const long Print = 4;
    private const long NoView = 32;
    private const long ToggleNoView = 256;

    private static readonly HashSet<string> Part1Types = new(StringComparer.Ordinal)
    {
        "Circle", "FreeText", "Highlight", "Ink", "Line", "Link", "Popup", "PrinterMark", "Square", "Squiggly",
        "Stamp", "StrikeOut", "Text", "TrapNet", "Underline", "Widget",
    };

    private static readonly HashSet<string> Part2And3Types = new(StringComparer.Ordinal)
    {
        "Caret", "Circle", "FileAttachment", "FreeText", "Highlight", "Ink", "Line", "Link", "PolyLine", "Polygon",
        "Popup", "PrinterMark", "Redact", "Square", "Squiggly", "Stamp", "StrikeOut", "Text", "TrapNet",
        "Underline", "Watermark", "Widget",
    };

    private static readonly HashSet<string> Part4Types = new(StringComparer.Ordinal)
    {
        "Caret", "Circle", "FreeText", "Highlight", "Ink", "Line", "Link", "PolyLine", "Polygon", "Popup",
        "PrinterMark", "Projection", "Redact", "Square", "Squiggly", "Stamp", "StrikeOut", "Text", "TrapNet",
        "Underline", "Watermark", "Widget",
    };

    /// <summary>Determines whether a profile permits an annotation type.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="subtype">The <c>/Subtype</c> of the annotation.</param>
    /// <returns><see langword="true"/> when the type is one the profile lists.</returns>
    /// <remarks>
    /// The lists are the ones the profile's reference validator applies: the types the underlying ISO 32000
    /// edition defines, less the ones the part forbids (3D, Sound, Screen and Movie everywhere; in part 4 also
    /// RichMedia and FileAttachment; in part 1 also the types PDF 1.4 does not have).
    /// </remarks>
    public static bool PermitsType(PdfAProfile profile, string subtype) => profile switch
    {
        PdfAProfile.PdfA1B => Part1Types.Contains(subtype),
        PdfAProfile.PdfA4 => Part4Types.Contains(subtype),
        _ => Part2And3Types.Contains(subtype),
    };

    /// <summary>Determines whether a profile requires a flags entry on an annotation.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="subtype">The <c>/Subtype</c> of the annotation.</param>
    /// <returns><see langword="true"/> when the annotation has to state <c>/F</c>.</returns>
    /// <remarks>Parts 2, 3 and 4 exempt a popup, which a viewer places beside the annotation it belongs to; part 1 does not.</remarks>
    public static bool RequiresFlags(PdfAProfile profile, string? subtype) =>
        profile == PdfAProfile.PdfA1B || !string.Equals(subtype, "Popup", StringComparison.Ordinal);

    /// <summary>Gets the flag bits an annotation of a profile must not set.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The bits.</returns>
    /// <remarks>Toggling the view is a PDF 1.5 flag, so part 1 does not name it.</remarks>
    public static long ForbiddenFlags(PdfAProfile profile) =>
        profile == PdfAProfile.PdfA1B ? Invisible | Hidden | NoView : Invisible | Hidden | NoView | ToggleNoView;

    /// <summary>Determines whether the flags of an annotation hide it, which the profile forbids.</summary>
    /// <param name="annotation">The annotation dictionary.</param>
    /// <param name="profile">The profile.</param>
    /// <returns><see langword="true"/> when a bit the profile forbids is set.</returns>
    public static bool IsHidden(PdfDictionary annotation, PdfAProfile profile) =>
        ((annotation.GetInteger("F") ?? 0L) & ForbiddenFlags(profile)) != 0;

    /// <summary>Determines whether the flags of an annotation need the Print bit set.</summary>
    /// <param name="annotation">The annotation dictionary.</param>
    /// <param name="profile">The profile.</param>
    /// <returns><see langword="true"/> when the entry is missing or does not set Print.</returns>
    public static bool NeedsPrintFlag(PdfDictionary annotation, PdfAProfile profile) =>
        RequiresFlags(profile, annotation.GetName("Subtype")) && ((annotation.GetInteger("F") ?? 0L) & Print) == 0;

    /// <summary>Determines whether an annotation is a widget, the annotation a form field stands on.</summary>
    /// <param name="annotation">The annotation dictionary.</param>
    /// <returns><see langword="true"/> for a widget.</returns>
    public static bool IsWidget(PdfDictionary annotation) =>
        string.Equals(annotation.GetName("Subtype"), "Widget", StringComparison.Ordinal);

    /// <summary>Rewrites the copy of an annotation so that it states what the profile requires.</summary>
    /// <param name="body">The copy of the annotation dictionary, or any other object, which is returned as it is.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The body to write.</returns>
    public static PdfObject Repair(PdfObject body, PdfAProfile profile)
    {
        if (body is not PdfDictionary annotation || annotation.GetName("Subtype") is null)
        {
            return body;
        }

        if (NeedsPrintFlag(annotation, profile))
        {
            annotation.SetInteger("F", (annotation.GetInteger("F") ?? 0L) | Print);
        }

        if (IsWidget(annotation))
        {
            annotation.Remove("A");
            annotation.Remove("AA");
        }

        return body;
    }

    /// <summary>
    /// Registers, for every annotation and form field of a document, the rewrite that makes its copy state what the
    /// profile requires.
    /// </summary>
    /// <param name="reader">The source document.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="profile">The profile.</param>
    /// <remarks>
    /// Only an object the document states as its own — a reference — can be rewritten when it is copied; an
    /// annotation written directly inside its page's array is a rarity this registration does not reach, and the
    /// analysis refuses what it cannot make right in that position.
    /// </remarks>
    public static void Register(PdfFileReader reader, PdfObjectCopier copier, PdfAProfile profile)
    {
        // Fields first: a widget that is also a field is registered again as an annotation, and that rewrite,
        // which also drops its actions, is the one that stays.
        RegisterFields(reader, copier, reader.ResolveDictionary(reader.Catalog?.Get("AcroForm"))?.Get("Fields"), depth: 0, visited: []);

        foreach (var page in reader.Pages)
        {
            foreach (var annotation in reader.ResolveArray(page.Dictionary.Get("Annots"))?.Items ?? [])
            {
                if (annotation is PdfReference reference)
                {
                    copier.Override(reference.ObjectNumber, body => Repair(body, profile));
                }
            }
        }
    }

    private static void RegisterFields(
        PdfFileReader reader,
        PdfObjectCopier copier,
        PdfObject? fields,
        int depth,
        HashSet<PdfDictionary> visited)
    {
        if (depth > 32 || reader.ResolveArray(fields) is not { } array)
        {
            return;
        }

        foreach (var item in array.Items)
        {
            if (reader.ResolveDictionary(item) is not { } field || !visited.Add(field))
            {
                continue;
            }

            if (item is PdfReference reference)
            {
                copier.Override(reference.ObjectNumber, RepairField);
            }

            RegisterFields(reader, copier, field.Get("Kids"), depth + 1, visited);
        }
    }

    /// <summary>Rewrites the copy of a form field so that it carries no action.</summary>
    /// <param name="body">The copy of the field dictionary, or any other object, which is returned as it is.</param>
    /// <returns>The body to write.</returns>
    public static PdfObject RepairField(PdfObject body)
    {
        if (body is PdfDictionary field)
        {
            field.Remove("A");
            field.Remove("AA");
        }

        return body;
    }
}
