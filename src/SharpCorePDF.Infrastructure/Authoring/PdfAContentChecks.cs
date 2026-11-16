using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// The rules of ISO 19005 that can only be seen in what a content stream says: the colour spaces it paints
/// in, and the operators it uses.
/// </summary>
/// <remarks>
/// <para>
/// A device colour space has no meaning of its own, so the standard allows one only where the document says
/// what it stands for: a matching output intent, or a <c>Default</c> space the resources define. This writer
/// states one output intent, sRGB unless the caller supplies another, so content that paints in DeviceCMYK is
/// refused instead of being written under an intent that contradicts it — the one rule four clauses of the
/// archival corpus reached.
/// </para>
/// <para>
/// A stream counts as painting in a space when it names it: an operator that selects it, a colour space entry
/// its operators select, an image or shading that states it, or an inline image. An image in the resources that no
/// operator paints is counted too, which can refuse a document whose unused resource would never have been seen;
/// refusing too much is the safe side of a check whose answer is "may this be archived".
/// </para>
/// </remarks>
internal static class PdfAContentChecks
{
    private const int MaxDepth = 8;

    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        "b", "B", "b*", "B*", "BDC", "BI", "BMC", "BT", "BX", "c", "cm", "CS", "cs", "d", "d0", "d1", "Do", "DP",
        "EI", "EMC", "ET", "EX", "f", "F", "f*", "G", "g", "gs", "h", "i", "ID", "j", "J", "K", "k", "l", "m", "M",
        "MP", "n", "q", "Q", "re", "RG", "rg", "ri", "s", "S", "SC", "sc", "SCN", "scn", "sh", "T*", "Tc", "Td",
        "TD", "Tf", "Tj", "TJ", "TL", "Tm", "Tr", "Ts", "Tw", "Tz", "v", "w", "W", "W*", "y", "'", "\"",
    };

    private static readonly HashSet<string> Literals = new(StringComparer.Ordinal) { "true", "false", "null" };

    /// <summary>The device colour spaces a stream painted in.</summary>
    [Flags]
    private enum Families
    {
        None = 0,
        Rgb = 1,
        Cmyk = 2,
    }

    /// <summary>Checks the content of every page, form, pattern and annotation appearance of a document.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="profile">The profile the document would be written for.</param>
    /// <param name="intentComponents">The number of colour components of the output intent the result will state.</param>
    /// <param name="unsatisfied">The list that receives the requirements that are not met.</param>
    public static void Check(PdfFileReader reader, PdfAProfile profile, int intentComponents, List<string> unsatisfied)
    {
        var visited = new HashSet<long>();

        for (var index = 0; index < reader.Pages.Count; index++)
        {
            var page = reader.Pages[index];
            var where = $"page {index + 1}";
            var resources = reader.ResolveDictionary(page.Dictionary.Get("Resources"))
                ?? reader.ResolveDictionary(page.Inherited.Get("Resources"));

            CheckContent(reader, page.Dictionary.Get("Contents"), resources, $"the content of {where}", profile, intentComponents, unsatisfied);
            CheckResources(reader, resources, where, profile, intentComponents, unsatisfied, visited, depth: 0);
            CheckGroup(reader, page.Dictionary, resources, $"the transparency group of {where}", profile, intentComponents, unsatisfied);

            foreach (var annotation in reader.ResolveArray(page.Dictionary.Get("Annots"))?.Items ?? [])
            {
                CheckAppearance(reader, annotation, where, profile, intentComponents, unsatisfied, visited);
            }
        }
    }

    private static void CheckAppearance(
        PdfFileReader reader,
        PdfObject annotation,
        string where,
        PdfAProfile profile,
        int intentComponents,
        List<string> unsatisfied,
        HashSet<long> visited)
    {
        if (reader.ResolveDictionary(reader.ResolveDictionary(annotation)?.Get("AP"))?.Get("N") is not { } normal)
        {
            return;
        }

        // The normal appearance is a stream, or a dictionary of streams, one per state.
        var streams = reader.Resolve(normal) is PdfDictionary states
            ? states.Entries.Select(entry => entry.Value)
            : [normal];

        foreach (var stream in streams)
        {
            CheckForm(reader, stream, $"an annotation of {where}", profile, intentComponents, unsatisfied, visited, depth: 1);
        }
    }

    private static void CheckResources(
        PdfFileReader reader,
        PdfDictionary? resources,
        string where,
        PdfAProfile profile,
        int intentComponents,
        List<string> unsatisfied,
        HashSet<long> visited,
        int depth)
    {
        if (resources is null || depth > MaxDepth)
        {
            return;
        }

        foreach (var entry in reader.ResolveDictionary(resources.Get("XObject"))?.Entries ?? [])
        {
            CheckForm(reader, entry.Value, $"the object '{entry.Key}' of {where}", profile, intentComponents, unsatisfied, visited, depth + 1);
        }

        foreach (var entry in reader.ResolveDictionary(resources.Get("Pattern"))?.Entries ?? [])
        {
            CheckForm(reader, entry.Value, $"the pattern '{entry.Key}' of {where}", profile, intentComponents, unsatisfied, visited, depth + 1);
        }

        foreach (var entry in reader.ResolveDictionary(resources.Get("Shading"))?.Entries ?? [])
        {
            Report(
                FamiliesOf(reader, reader.ResolveDictionary(entry.Value)?.Get("ColorSpace"), resources),
                resources,
                $"the shading '{entry.Key}' of {where}",
                profile,
                intentComponents,
                unsatisfied,
                reader);
        }
    }

    /// <summary>Checks a stream a resource or an appearance names: an image states a space, a form has content of its own.</summary>
    private static void CheckForm(
        PdfFileReader reader,
        PdfObject? value,
        string where,
        PdfAProfile profile,
        int intentComponents,
        List<string> unsatisfied,
        HashSet<long> visited,
        int depth)
    {
        if (depth > MaxDepth || reader.Resolve(value) is not PdfStream stream)
        {
            return;
        }

        if (value is PdfReference reference && !visited.Add(reference.ObjectNumber))
        {
            return;
        }

        var dictionary = stream.Dictionary;
        var resources = reader.ResolveDictionary(dictionary.Get("Resources"));

        if (string.Equals(dictionary.GetName("Subtype"), "Image", StringComparison.Ordinal))
        {
            Report(FamiliesOf(reader, dictionary.Get("ColorSpace"), resources), resources, where, profile, intentComponents, unsatisfied, reader);
            return;
        }

        CheckContent(reader, value, resources, $"the content of {where}", profile, intentComponents, unsatisfied);
        CheckResources(reader, resources, where, profile, intentComponents, unsatisfied, visited, depth);
        CheckGroup(reader, dictionary, resources, $"the transparency group of {where}", profile, intentComponents, unsatisfied);
    }

    /// <summary>
    /// Checks the colour space a page or a form states for its transparency group, which the reference validator
    /// counts as a use of that space: a group in DeviceRGB under a CMYK output intent breaks the same rule as a fill.
    /// </summary>
    private static void CheckGroup(
        PdfFileReader reader,
        PdfDictionary owner,
        PdfDictionary? resources,
        string where,
        PdfAProfile profile,
        int intentComponents,
        List<string> unsatisfied)
    {
        if (reader.ResolveDictionary(owner.Get("Group")) is { } group)
        {
            Report(FamiliesOf(reader, group.Get("CS"), resources), resources, where, profile, intentComponents, unsatisfied, reader);
        }
    }

    private static void CheckContent(
        PdfFileReader reader,
        PdfObject? contents,
        PdfDictionary? resources,
        string where,
        PdfAProfile profile,
        int intentComponents,
        List<string> unsatisfied)
    {
        if (!TryRead(reader, contents, out var data))
        {
            return;
        }

        var used = Families.None;
        var undefined = new SortedSet<string>(StringComparer.Ordinal);
        var colourSpaces = reader.ResolveDictionary(resources?.Get("ColorSpace"));

        foreach (var operation in PdfContentScanner.Scan(data, reader.SourceName))
        {
            var name = operation.Operator;

            if (!Operators.Contains(name) && !Literals.Contains(name))
            {
                undefined.Add(name);
                continue;
            }

            switch (name)
            {
                case "rg" or "RG":
                    used |= Families.Rgb;
                    break;

                case "k" or "K":
                    used |= Families.Cmyk;
                    break;

                case "cs" or "CS" when operation.Names.Count > 0:
                    used |= FamiliesOfName(reader, operation.Names[0], colourSpaces);
                    break;

                case "BI" when operation.InlineImage is { } image:
                    used |= FamiliesOf(reader, image.Get("ColorSpace") ?? image.Get("CS"), resources);
                    break;
            }
        }

        foreach (var undefinedOperator in undefined)
        {
            unsatisfied.Add(
                $"{where} uses the operator '{undefinedOperator}', which ISO 32000 does not define; {PdfAProfileRules.Name(profile)} forbids it even between BX and EX.");
        }

        Report(used, resources, where, profile, intentComponents, unsatisfied, reader);
    }

    /// <summary>Reports the device spaces a stream painted in that the output intent and the resources do not cover.</summary>
    private static void Report(
        Families used,
        PdfDictionary? resources,
        string where,
        PdfAProfile profile,
        int intentComponents,
        List<string> unsatisfied,
        PdfFileReader reader)
    {
        var defaults = reader.ResolveDictionary(resources?.Get("ColorSpace"));

        if (used.HasFlag(Families.Cmyk) && intentComponents != 4 && defaults?.ContainsKey("DefaultCMYK") != true)
        {
            unsatisfied.Add(
                $"{where} paints in DeviceCMYK, which {PdfAProfileRules.Name(profile)} allows only with a CMYK output intent or a DefaultCMYK colour space; the output intent of the result has {intentComponents} colour components. Supply a CMYK output intent, or convert the colours.");
        }

        if (used.HasFlag(Families.Rgb) && intentComponents != 3 && defaults?.ContainsKey("DefaultRGB") != true)
        {
            unsatisfied.Add(
                $"{where} paints in DeviceRGB, which {PdfAProfileRules.Name(profile)} allows only with an RGB output intent or a DefaultRGB colour space; the output intent of the result has {intentComponents} colour components.");
        }
    }

    private static Families FamiliesOfName(PdfFileReader reader, string name, PdfDictionary? colourSpaces) => name switch
    {
        "DeviceRGB" or "RGB" => Families.Rgb,
        "DeviceCMYK" or "CMYK" => Families.Cmyk,
        "DeviceGray" or "G" or "Pattern" => Families.None,
        _ => colourSpaces?.Get(name) is { } entry ? FamiliesOf(reader, entry, resources: null) : Families.None,
    };

    /// <summary>Names the device spaces a colour space value is, or is built on.</summary>
    private static Families FamiliesOf(PdfFileReader reader, PdfObject? value, PdfDictionary? resources)
    {
        switch (reader.Resolve(value))
        {
            case PdfName name:
                return FamiliesOfName(reader, name.Value, reader.ResolveDictionary(resources?.Get("ColorSpace")));

            case PdfArray { Items.Count: > 1 } array when reader.Resolve(array.Items[0]) is PdfName { Value: "Indexed" or "I" }:
                return FamiliesOf(reader, array.Items[1], resources);

            // A Separation or DeviceN space is painted through its alternate when the colorant is not available,
            // and the reference validator counts the alternate as a use of that device space.
            case PdfArray { Items.Count: > 2 } array when reader.Resolve(array.Items[0]) is PdfName { Value: "Separation" or "DeviceN" }:
                return FamiliesOf(reader, array.Items[2], resources) | ColorantFamilies(reader, array, resources);

            default:
                return Families.None;
        }
    }

    /// <summary>Names the device spaces the Separation colorants of a DeviceN space are built on.</summary>
    private static Families ColorantFamilies(PdfFileReader reader, PdfArray deviceN, PdfDictionary? resources)
    {
        // The attributes dictionary is the fifth element; its /Colorants map each name to a Separation array.
        if (deviceN.Items.Count < 5
            || reader.ResolveDictionary(reader.ResolveDictionary(deviceN.Items[4])?.Get("Colorants")) is not { } colorants)
        {
            return Families.None;
        }

        var families = Families.None;

        foreach (var entry in colorants.Entries)
        {
            families |= FamiliesOf(reader, entry.Value, resources);
        }

        return families;
    }

    private static bool TryRead(PdfFileReader reader, PdfObject? value, out byte[] data)
    {
        data = [];

        try
        {
            switch (reader.Resolve(value))
            {
                case PdfStream stream:
                    data = PdfFilters.Decode(stream, reader.SourceName, reader.Options.MaxDecodedStreamBytes);
                    return true;

                case PdfArray array:
                    using (var joined = new MemoryStream())
                    {
                        foreach (var item in array.Items)
                        {
                            if (reader.Resolve(item) is PdfStream part)
                            {
                                joined.Write(PdfFilters.Decode(part, reader.SourceName, reader.Options.MaxDecodedStreamBytes));
                                joined.WriteByte((byte)'\n');
                            }
                        }

                        data = joined.ToArray();
                    }

                    return true;

                default:
                    return false;
            }
        }
        catch (PdfSourceFormatException)
        {
            return false;
        }
        catch (PdfUnsupportedFeatureException)
        {
            return false;
        }
    }
}
