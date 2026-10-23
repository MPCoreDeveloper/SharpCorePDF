using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>What one analysis of a source found: what must be refused, and what may be removed.</summary>
/// <param name="Unsatisfied">The requirements the profile states that the document does not meet.</param>
/// <param name="Removable">The features the writer removes, each with the reason it is allowed to.</param>
internal sealed record PdfARequirementReport(
    IReadOnlyList<string> Unsatisfied,
    IReadOnlyList<string> Removable);

/// <summary>
/// Decides whether a source document can be written for a PDF/A profile, before a single byte of the
/// result is produced.
/// </summary>
/// <remarks>
/// <para>
/// The analysis is deliberately conservative. Everything it refuses is a rule of the profile that
/// cannot be repaired without rewriting content — an unembedded font has no program to embed, a form
/// XObject that blends cannot be made opaque without changing what the page looks like, an attachment
/// is data the caller did not agree to lose. Everything it accepts as removable is metadata or a
/// catalog entry whose removal changes no page: a JavaScript name tree, a catalog open action that
/// runs script, a form that asks the reader to build appearances it was supposed to carry.
/// </para>
/// <para>
/// The analysis is not a validator. It looks for the features this writer cannot repair, not at every
/// rule of ISO 19005; veraPDF remains the authority on whether the result conforms.
/// </para>
/// </remarks>
internal static class PdfARequirements
{
    /// <summary>The action types ISO 19005 forbids in every part.</summary>
    private static readonly HashSet<string> ForbiddenActions =
        new(StringComparer.Ordinal) { "JavaScript", "Launch", "Sound", "Movie", "Rendition", "ImportData", "SubmitForm" };

    /// <summary>The deepest chain of form XObjects the analysis follows.</summary>
    private const int MaxFormDepth = 32;

    /// <summary>Analyses a document for a profile.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="profile">The profile it would be written for.</param>
    /// <returns>The requirements that are not met, and the features that will be removed.</returns>
    public static PdfARequirementReport Analyze(PdfFileReader reader, PdfAProfile profile)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var unsatisfied = new List<string>();
        var removable = new List<string>();
        var fonts = new Dictionary<string, bool>(StringComparer.Ordinal);
        var filterProblems = new HashSet<string>(StringComparer.Ordinal);

        var catalog = reader.Catalog;

        CheckCatalog(reader, catalog, profile, unsatisfied, removable);
        CheckPages(reader, profile, fonts, filterProblems, unsatisfied);

        foreach (var entry in fonts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!entry.Value)
            {
                unsatisfied.Add(
                    $"the font '{entry.Key}' is not embedded; a PDF/A document must carry the program of every font it uses.");
            }
        }

        foreach (var problem in filterProblems.OrderBy(problem => problem, StringComparer.Ordinal))
        {
            unsatisfied.Add(problem);
        }

        // The same font, form or annotation can be reachable from several pages; the caller is told
        // once per condition, not once per page that repeats it.
        return new PdfARequirementReport([.. unsatisfied.Distinct(StringComparer.Ordinal)], removable);
    }

    /// <summary>Checks the catalog: its name trees, its open action and its optional content.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="catalog">The catalog of the source.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    /// <param name="removable">The list that receives the features the writer removes.</param>
    private static void CheckCatalog(
        PdfFileReader reader,
        PdfDictionary? catalog,
        PdfAProfile profile,
        List<string> unsatisfied,
        List<string> removable)
    {
        if (catalog is null)
        {
            return;
        }

        var names = reader.ResolveDictionary(catalog.Get("Names"));

        if (names?.Get("JavaScript") is not null)
        {
            removable.Add("the JavaScript name tree of the catalog");
        }

        if (names?.Get("EmbeddedFiles") is not null && !PdfAProfileRules.AllowsEmbeddedFiles(profile))
        {
            unsatisfied.Add(
                $"the document carries embedded files, which {PdfAProfileRules.Name(profile)} does not allow.");
        }

        if (catalog.Get("OCProperties") is not null && PdfAProfileRules.ForbidsTransparency(profile))
        {
            unsatisfied.Add($"the document carries optional content, which {PdfAProfileRules.Name(profile)} forbids.");
        }

        if (reader.ResolveDictionary(catalog.Get("OpenAction")) is { } openAction)
        {
            var kind = openAction.GetName("S");

            if (string.Equals(kind, "JavaScript", StringComparison.Ordinal))
            {
                removable.Add("the catalog open action, which runs JavaScript");
            }
            else if (kind is not null && ForbiddenActions.Contains(kind))
            {
                unsatisfied.Add(
                    $"the catalog open action is a '{kind}' action, which {PdfAProfileRules.Name(profile)} forbids.");
            }
        }

        CheckActionDictionary(
            reader,
            reader.ResolveDictionary(catalog.Get("AA")),
            "the catalog",
            profile,
            unsatisfied);

        var form = reader.ResolveDictionary(catalog.Get("AcroForm"));

        if (form?.Get("NeedAppearances") == PdfBoolean.True)
        {
            removable.Add("the /NeedAppearances flag of the form, which asks the reader to build appearances");
        }
    }

    /// <summary>Checks every page, its resources, its content and its annotations.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="fonts">The fonts found so far, with whether each is embedded.</param>
    /// <param name="filterProblems">The list that receives the filters the profile forbids.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    private static void CheckPages(
        PdfFileReader reader,
        PdfAProfile profile,
        Dictionary<string, bool> fonts,
        HashSet<string> filterProblems,
        List<string> unsatisfied)
    {
        // A form XObject can be painted by several pages; its resources and its content are checked
        // once, because an object number is what a reference to it names.
        var visited = new HashSet<long>();

        for (var index = 0; index < reader.Pages.Count; index++)
        {
            var page = reader.Pages[index];
            var where = $"page {index + 1}";

            CheckStreams(reader, page.Dictionary.Get("Contents"), $"the content stream of {where}", profile, fonts, filterProblems, unsatisfied, visited);

            var resources = reader.ResolveDictionary(page.Dictionary.Get("Resources"))
                ?? reader.ResolveDictionary(page.Inherited.Get("Resources"));

            CheckResources(reader, resources, where, profile, fonts, filterProblems, unsatisfied, visited, depth: 0);

            if (PdfAProfileRules.ForbidsTransparency(profile))
            {
                CheckTransparencyGroup(reader, page.Dictionary, where, unsatisfied);
            }

            foreach (var annotation in ReadArray(reader, page.Dictionary.Get("Annots")))
            {
                CheckAnnotation(reader, annotation, where, profile, fonts, filterProblems, unsatisfied, visited);
            }
        }
    }

    /// <summary>Checks the resources of a page or of a form XObject.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="resources">The resource dictionary, or <see langword="null"/> when there is none.</param>
    /// <param name="where">The description of what the resources belong to, used in the report.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="fonts">The fonts found so far, with whether each is embedded.</param>
    /// <param name="filterProblems">The list that receives the filters the profile forbids.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    /// <param name="visited">The objects already inspected.</param>
    /// <param name="depth">The depth of the form XObjects above this one.</param>
    private static void CheckResources(
        PdfFileReader reader,
        PdfDictionary? resources,
        string where,
        PdfAProfile profile,
        Dictionary<string, bool> fonts,
        HashSet<string> filterProblems,
        List<string> unsatisfied,
        HashSet<long> visited,
        int depth)
    {
        if (resources is null || depth > MaxFormDepth)
        {
            return;
        }

        if (reader.ResolveDictionary(resources.Get("Font")) is { } fontDictionary)
        {
            foreach (var entry in fontDictionary.Entries)
            {
                if (reader.ResolveDictionary(entry.Value) is not { } font)
                {
                    continue;
                }

                var name = FontName(reader, font, entry.Key);
                var embedded = IsEmbedded(reader, font);

                fonts[name] = fonts.TryGetValue(name, out var alreadyEmbedded) ? alreadyEmbedded && embedded : embedded;

                if (embedded
                    && profile == PdfAProfile.PdfA1B
                    && IsCidSubsetWithoutSet(reader, font))
                {
                    unsatisfied.Add(
                        $"the CID font subset '{name}' states no /CIDSet, which PDF/A-1b requires of a subset; the writer cannot invent one for a program it did not subset.");
                }
            }
        }

        if (reader.ResolveDictionary(resources.Get("ExtGState")) is { } states
            && PdfAProfileRules.ForbidsTransparency(profile))
        {
            foreach (var entry in states.Entries)
            {
                if (reader.ResolveDictionary(entry.Value) is { } state && UsesTransparency(reader, state))
                {
                    unsatisfied.Add(
                        $"the graphics state '{entry.Key}' used by {where} blends or fades, which {PdfAProfileRules.Name(profile)} forbids.");
                }
            }
        }

        if (reader.ResolveDictionary(resources.Get("XObject")) is { } xobjects)
        {
            foreach (var entry in xobjects.Entries)
            {
                if (entry.Value is PdfReference reference && !visited.Add(reference.ObjectNumber))
                {
                    continue;
                }

                CheckStreams(
                    reader,
                    entry.Value,
                    $"the object '{entry.Key}' of {where}",
                    profile,
                    fonts,
                    filterProblems,
                    unsatisfied,
                    visited,
                    depth + 1);
            }
        }
    }

    /// <summary>Checks one annotation: its actions, its appearance and the resources of its appearance.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="value">The annotation, possibly as a reference.</param>
    /// <param name="where">The description of what carries the annotation, used in the report.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="fonts">The fonts found so far, with whether each is embedded.</param>
    /// <param name="filterProblems">The list that receives the filters the profile forbids.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    /// <param name="visited">The objects already inspected.</param>
    private static void CheckAnnotation(
        PdfFileReader reader,
        PdfObject value,
        string where,
        PdfAProfile profile,
        Dictionary<string, bool> fonts,
        HashSet<string> filterProblems,
        List<string> unsatisfied,
        HashSet<long> visited)
    {
        if (reader.ResolveDictionary(value) is not { } annotation)
        {
            return;
        }

        var subtype = annotation.GetName("Subtype") ?? "annotation";
        var description = $"the {subtype} of {where}";

        CheckAction(reader, annotation.Get("A"), description, profile, unsatisfied);
        CheckActionDictionary(reader, reader.ResolveDictionary(annotation.Get("AA")), description, profile, unsatisfied);

        if (annotation.Get("AP") is null)
        {
            // A link or a popup may be carried without an appearance; every other annotation without
            // one is content a viewer would have to invent.
            if (!string.Equals(subtype, "Popup", StringComparison.Ordinal)
                && !string.Equals(subtype, "Link", StringComparison.Ordinal))
            {
                unsatisfied.Add(
                    $"{description} has no appearance stream, which {PdfAProfileRules.Name(profile)} requires.");
            }

            return;
        }

        if (reader.ResolveDictionary(annotation.Get("AP")) is { } appearances)
        {
            CheckStreams(
                reader,
                appearances.Get("N"),
                $"the appearance of {description}",
                profile,
                fonts,
                filterProblems,
                unsatisfied,
                visited);
        }
    }

    /// <summary>Checks one or more streams: their filters, their masks and their resources.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="value">The stream, an array of streams, or a reference to either.</param>
    /// <param name="where">The description of the stream, used in the report.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="fonts">The fonts found so far, with whether each is embedded.</param>
    /// <param name="filterProblems">The list that receives the filters the profile forbids.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    /// <param name="visited">The objects already inspected.</param>
    /// <param name="depth">The depth of the form XObjects above this one.</param>
    private static void CheckStreams(
        PdfFileReader reader,
        PdfObject? value,
        string where,
        PdfAProfile profile,
        Dictionary<string, bool> fonts,
        HashSet<string> filterProblems,
        List<string> unsatisfied,
        HashSet<long> visited,
        int depth = 0)
    {
        switch (reader.Resolve(value))
        {
            case PdfArray array:
                foreach (var item in array.Items)
                {
                    CheckStreams(reader, item, where, profile, fonts, filterProblems, unsatisfied, visited, depth);
                }

                return;

            case PdfStream stream:
                CheckStream(reader, stream, where, profile, fonts, filterProblems, unsatisfied, visited, depth);
                return;

            default:
                return;
        }
    }

    /// <summary>Checks the dictionary and the resources of one stream.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="stream">The stream.</param>
    /// <param name="where">The description of the stream, used in the report.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="fonts">The fonts found so far, with whether each is embedded.</param>
    /// <param name="filterProblems">The list that receives the filters the profile forbids.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    /// <param name="visited">The objects already inspected.</param>
    /// <param name="depth">The depth of the form XObjects above this one.</param>
    private static void CheckStream(
        PdfFileReader reader,
        PdfStream stream,
        string where,
        PdfAProfile profile,
        Dictionary<string, bool> fonts,
        HashSet<string> filterProblems,
        List<string> unsatisfied,
        HashSet<long> visited,
        int depth)
    {
        var dictionary = stream.Dictionary;
        var profileName = PdfAProfileRules.Name(profile);

        if (dictionary.Get("F") is not null)
        {
            unsatisfied.Add($"'{where}' takes its bytes from an external file, which {profileName} forbids.");
        }

        if (PdfAProfileRules.ForbidsTransparency(profile))
        {
            foreach (var filter in ReadFilters(reader, dictionary.Get("Filter"))
                .Where(filter => filter is "LZWDecode" or "JPXDecode"))
            {
                filterProblems.Add($"'{where}' uses the {filter} filter, which {profileName} forbids.");
            }

            if (string.Equals(dictionary.GetName("Subtype"), "Image", StringComparison.Ordinal)
                && dictionary.Get("SMask") is not null)
            {
                unsatisfied.Add($"'{where}' carries a soft mask, which {profileName} forbids.");
            }

            CheckTransparencyGroup(reader, dictionary, $"'{where}'", unsatisfied);
        }

        if (depth <= MaxFormDepth && reader.ResolveDictionary(dictionary.Get("Resources")) is { } nested)
        {
            CheckResources(reader, nested, where, profile, fonts, filterProblems, unsatisfied, visited, depth);
        }
    }

    /// <summary>Checks whether a dictionary states a transparency group.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="dictionary">The page or form XObject dictionary.</param>
    /// <param name="where">The description of the object, used in the report.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    private static void CheckTransparencyGroup(
        PdfFileReader reader,
        PdfDictionary dictionary,
        string where,
        List<string> unsatisfied)
    {
        if (string.Equals(
            reader.ResolveDictionary(dictionary.Get("Group"))?.GetName("S"),
            "Transparency",
            StringComparison.Ordinal))
        {
            unsatisfied.Add($"{where} paints a transparency group, which PDF/A-1b forbids.");
        }
    }

    /// <summary>Checks one action.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="value">The action, possibly as a reference.</param>
    /// <param name="where">The description of what carries the action, used in the report.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    private static void CheckAction(
        PdfFileReader reader,
        PdfObject? value,
        string where,
        PdfAProfile profile,
        List<string> unsatisfied)
    {
        if (reader.ResolveDictionary(value)?.GetName("S") is { } kind && ForbiddenActions.Contains(kind))
        {
            unsatisfied.Add(
                $"{where} carries a '{kind}' action, which {PdfAProfileRules.Name(profile)} forbids.");
        }
    }

    /// <summary>Checks every action of an additional-actions dictionary.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="dictionary">The additional-actions dictionary, or <see langword="null"/>.</param>
    /// <param name="where">The description of what carries the actions, used in the report.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    private static void CheckActionDictionary(
        PdfFileReader reader,
        PdfDictionary? dictionary,
        string where,
        PdfAProfile profile,
        List<string> unsatisfied)
    {
        if (dictionary is null)
        {
            return;
        }

        foreach (var entry in dictionary.Entries)
        {
            CheckAction(reader, entry.Value, where, profile, unsatisfied);
        }
    }

    /// <summary>Reads an array of values, tolerating a reference and a missing entry.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="value">The array or the reference to one.</param>
    /// <returns>The items, or an empty list.</returns>
    private static List<PdfObject> ReadArray(PdfFileReader reader, PdfObject? value) =>
        reader.ResolveArray(value)?.Items ?? [];

    /// <summary>Reads the names of the filters of a stream.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="value">The filter entry: a name, an array of names, or a reference to either.</param>
    /// <returns>The filter names.</returns>
    private static IEnumerable<string> ReadFilters(PdfFileReader reader, PdfObject? value) => reader.Resolve(value) switch
    {
        PdfName name => [name.Value],
        PdfArray array => array.Items.Select(item => (item as PdfName)?.Value).OfType<string>(),
        _ => [],
    };

    /// <summary>Determines whether the program of a font is carried by the document.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <returns><see langword="true"/> when the font is embedded.</returns>
    private static bool IsEmbedded(PdfFileReader reader, PdfDictionary font)
    {
        if (string.Equals(font.GetName("Subtype"), "Type3", StringComparison.Ordinal))
        {
            return font.Get("CharProcs") is not null;
        }

        var descriptor = FindDescriptor(reader, font);

        return descriptor is not null
            && (descriptor.Get("FontFile") is not null
                || descriptor.Get("FontFile2") is not null
                || descriptor.Get("FontFile3") is not null);
    }

    /// <summary>Determines whether a font is a subset of a CID font that states no CID set.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <returns><see langword="true"/> when PDF/A-1b would refuse the font.</returns>
    /// <remarks>
    /// ISO 19005-1 requires a subset of a CID font to carry a <c>/CIDSet</c> stream that says which CIDs
    /// the embedded program describes. A program this library subsetted states one; one it copied from
    /// a source cannot be given an accurate set without reading the program, so the document is refused
    /// for PDF/A-1b rather than written with a set that may be wrong.
    /// </remarks>
    private static bool IsCidSubsetWithoutSet(PdfFileReader reader, PdfDictionary font)
    {
        if (!string.Equals(font.GetName("Subtype"), "Type0", StringComparison.Ordinal))
        {
            return false;
        }

        var baseFont = font.GetName("BaseFont") ?? string.Empty;

        // The subset tag is six capital letters and a plus sign, as the specification defines it.
        if (baseFont.Length < 8 || baseFont[6] != '+' || !baseFont[..6].All(char.IsAsciiLetterUpper))
        {
            return false;
        }

        return FindDescriptor(reader, font)?.Get("CIDSet") is null;
    }

    /// <summary>Finds the font descriptor of a simple or composite font.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <returns>The descriptor, or <see langword="null"/> when the font states none.</returns>
    private static PdfDictionary? FindDescriptor(PdfFileReader reader, PdfDictionary font)
    {
        var descriptor = reader.ResolveDictionary(font.Get("FontDescriptor"));

        if (descriptor is null
            && reader.ResolveArray(font.Get("DescendantFonts")) is { Items.Count: > 0 } descendants
            && reader.ResolveDictionary(descendants.Items[0]) is { } descendant)
        {
            // A composite font states its descriptor on the descendant, not on the Type0 dictionary.
            descriptor = reader.ResolveDictionary(descendant.Get("FontDescriptor"));
        }

        return descriptor;
    }

    /// <summary>Gets the name a font is reported under.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="font">The font dictionary.</param>
    /// <param name="resourceName">The resource name the page selects the font with.</param>
    /// <returns>The base font name, or the resource name when the font states none.</returns>
    private static string FontName(PdfFileReader reader, PdfDictionary font, string resourceName)
    {
        if (font.GetName("BaseFont") is { Length: > 0 } name)
        {
            return name;
        }

        if (reader.ResolveArray(font.Get("DescendantFonts")) is { Items.Count: > 0 } descendants
            && reader.ResolveDictionary(descendants.Items[0])?.GetName("BaseFont") is { Length: > 0 } descendantName)
        {
            return descendantName;
        }

        return resourceName;
    }

    /// <summary>Determines whether a graphics state fades or blends.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="state">The graphics state dictionary.</param>
    /// <returns><see langword="true"/> when the state uses transparency.</returns>
    private static bool UsesTransparency(PdfFileReader reader, PdfDictionary state)
    {
        foreach (var key in (string[])["ca", "CA"])
        {
            if (ReadNumber(reader.Resolve(state.Get(key))) is { } alpha && alpha < 1)
            {
                return true;
            }
        }

        if (state.GetName("BM") is { } blend && blend is not ("Normal" or "Compatible"))
        {
            return true;
        }

        // An /SMask of /None is the way a producer states that there is no soft mask.
        return state.Get("SMask") is { } mask
            && reader.Resolve(mask) is not PdfName { Value: "None" };
    }

    /// <summary>Reads a number, or nothing when the value is not one.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    private static double? ReadNumber(PdfObject? value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => null,
    };
}
