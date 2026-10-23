using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// Projects a document the engine has parsed into the read-only object graph an inspector reports.
/// </summary>
/// <remarks>
/// <para>
/// One node is produced per object the cross-reference table lists as in use, in ascending object number,
/// so two reads of the same file report the same graph in the same order. The projection copies what the
/// parser read into records that expose no setter, so holding a graph does not hold the document open for
/// change (NFR-7.7).
/// </para>
/// <para>
/// A reference is projected as a reference and never followed: the graph states the number and the
/// generation it names and whether the document holds them. That keeps the graph finite even when objects
/// point at each other, and it keeps a reference to a missing object visible instead of dropping it; the
/// hole is reported once per target as <see cref="PdfWarningKind.DanglingReference"/>.
/// </para>
/// </remarks>
internal static class ObjectGraphView
{
    /// <summary>Projects the objects of a document.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="warnings">The list that receives a remark per reference that does not resolve.</param>
    /// <returns>The nodes of the graph, in ascending object number.</returns>
    public static IReadOnlyList<PdfObjectView> Project(PdfFileReader reader, List<PdfWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(warnings);

        var entries = reader.CrossReferenceTable.Entries.Values
            .Where(entry => entry.IsInUse)
            .OrderBy(entry => entry.ObjectNumber);

        var nodes = new List<PdfObjectView>();
        var reported = new HashSet<long>();

        foreach (var entry in entries)
        {
            var value = reader.Resolve(entry.ObjectNumber);

            // An object the table lists but the file does not hold is reported where it should have been,
            // with IsResolved cleared: a caller reading the graph sees the gap at the number it belongs to
            // instead of an object that quietly went missing.
            nodes.Add(value is null
                ? new PdfObjectView
                {
                    Kind = PdfObjectViewKind.Null,
                    ObjectNumber = entry.ObjectNumber,
                    Generation = entry.Generation,
                    IsResolved = false,
                }
                : ProjectNode(value, null, reader, warnings, reported) with
                {
                    ObjectNumber = entry.ObjectNumber,
                    Generation = entry.Generation,
                });
        }

        return nodes;
    }

    private static PdfObjectView ProjectNode(
        PdfObject value,
        string? key,
        PdfFileReader reader,
        List<PdfWarning> warnings,
        HashSet<long> reported)
    {
        switch (value)
        {
            case PdfBoolean boolean:
                return new PdfObjectView { Kind = PdfObjectViewKind.Boolean, Key = key, Scalar = boolean.Value };

            case PdfInteger integer:
                return new PdfObjectView { Kind = PdfObjectViewKind.Integer, Key = key, Scalar = integer.Value };

            case PdfReal real:
                return new PdfObjectView { Kind = PdfObjectViewKind.Real, Key = key, Scalar = real.Value };

            case PdfString text:
                return new PdfObjectView { Kind = PdfObjectViewKind.String, Key = key, Scalar = text.ToText() };

            case PdfName name:
                return new PdfObjectView { Kind = PdfObjectViewKind.Name, Key = key, Scalar = name.Value };

            case PdfArray array:
                return new PdfObjectView
                {
                    Kind = PdfObjectViewKind.Array,
                    Key = key,
                    Children = [.. array.Items.Select(item => ProjectNode(item, null, reader, warnings, reported))],
                };

            case PdfDictionary dictionary:
                return new PdfObjectView
                {
                    Kind = PdfObjectViewKind.Dictionary,
                    Key = key,
                    Children = [.. ProjectEntries(dictionary, reader, warnings, reported)],
                };

            case PdfStream stream:
                return new PdfObjectView
                {
                    Kind = PdfObjectViewKind.Stream,
                    Key = key,
                    Children = [.. ProjectEntries(stream.Dictionary, reader, warnings, reported)],
                    StreamLength = stream.Raw.Length,
                    Filter = FilterName(stream.Dictionary, reader),
                };

            case PdfReference reference:
                return ProjectReference(reference, key, reader, warnings, reported);

            default:
                return new PdfObjectView { Kind = PdfObjectViewKind.Null, Key = key };
        }
    }

    private static IEnumerable<PdfObjectView> ProjectEntries(
        PdfDictionary dictionary,
        PdfFileReader reader,
        List<PdfWarning> warnings,
        HashSet<long> reported) =>
        dictionary.Entries.Select(entry => ProjectNode(entry.Value, entry.Key, reader, warnings, reported));

    private static PdfObjectView ProjectReference(
        PdfReference reference,
        string? key,
        PdfFileReader reader,
        List<PdfWarning> warnings,
        HashSet<long> reported)
    {
        var resolved = reader.Resolve(reference.ObjectNumber) is not null;

        // One remark per target is enough: a page that names the same missing object five times is one
        // hole in the document, and repeating it five times would bury the rest of the warnings.
        if (!resolved && reported.Add(reference.ObjectNumber))
        {
            warnings.Add(new PdfWarning(
                PdfWarningKind.DanglingReference,
                key is null
                    ? $"'{reader.SourceName}': the reference to object {reference.ObjectNumber} {reference.Generation} R does not resolve; the document holds no such object."
                    : $"'{reader.SourceName}': the reference to object {reference.ObjectNumber} {reference.Generation} R in entry '{key}' does not resolve; the document holds no such object."));
        }

        return new PdfObjectView
        {
            Kind = PdfObjectViewKind.Reference,
            Key = key,
            Reference = new PdfObjectReference(reference.ObjectNumber, reference.Generation),
            IsResolved = resolved,
        };
    }

    /// <summary>Names the filters a stream declares, in the order they are applied.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="reader">The document, so a filter written as a reference can be read.</param>
    /// <returns>The filter or filters joined by a space, or <see langword="null"/> when none is declared.</returns>
    private static string? FilterName(PdfDictionary dictionary, PdfFileReader reader)
    {
        var value = reader.Resolve(dictionary.Get("Filter"));

        if (value is PdfName name)
        {
            return name.Value;
        }

        if (value is not PdfArray array)
        {
            return null;
        }

        var names = array.Items
            .Select(item => reader.Resolve(item))
            .OfType<PdfName>()
            .Select(name => name.Value)
            .ToArray();

        return names.Length == 0 ? null : string.Join(" ", names);
    }
}
