using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The annotation half of <see cref="PdfPageContentReader"/>: what a page carries on top of its
/// content, and where a link points.
/// </summary>
public sealed partial class PdfPageContentReader
{
    /// <inheritdoc />
    public async Task<PdfReadResult<PdfAnnotationInfo>> ReadAnnotationsAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        NoteClosedEncryption(reader, warnings);

        var annotations = new List<PdfAnnotationInfo>();
        var destinations = new NamedDestinations(reader);
        var pageMap = BuildPageMap(reader);

        foreach (var page in SelectPages(reader, range))
        {
            annotations.AddRange(ReadPageAnnotations(reader, page, destinations, pageMap, warnings));
        }

        return Result(reader, annotations, warnings);
    }

    /// <summary>Reads the annotations of one page, resolving the targets of the links among them.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="page">The page.</param>
    /// <param name="destinations">The named destinations of the document.</param>
    /// <param name="pageMap">The map from page object numbers to page numbers.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <returns>The annotations of the page, in the order the page states them.</returns>
    private static List<PdfAnnotationInfo> ReadPageAnnotations(
        PdfFileReader reader,
        SourcePage page,
        NamedDestinations destinations,
        Dictionary<long, int> pageMap,
        List<PdfWarning> warnings)
    {
        var pageNumber = page.Index + 1;
        var result = new List<PdfAnnotationInfo>();
        var annots = reader.ResolveArray(page.Dictionary.Get("Annots"));

        if (annots is null)
        {
            return result;
        }

        foreach (var item in annots.Items)
        {
            var dictionary = reader.ResolveDictionary(item);

            if (dictionary is null)
            {
                warnings.Add(new(
                    PdfWarningKind.AnnotationUnreadable,
                    $"'{reader.SourceName}': an annotation of page {pageNumber} could not be resolved and was skipped."));
                continue;
            }

            var subtype = dictionary.GetName("Subtype") ?? "Unknown";

            result.Add(new PdfAnnotationInfo
            {
                PageNumber = pageNumber,
                Subtype = subtype,
                Rectangle = ReadRectangle(reader, dictionary.Get("Rect")),
                Flags = (int)(dictionary.GetInteger("F") ?? 0),
                Contents = ReadText(reader, dictionary.Get("Contents")),
                Author = ReadText(reader, dictionary.Get("T")),
                ModificationDate = ReadDate(reader, dictionary.Get("M")),
                HasAppearance = dictionary.Get("AP") is not null,
                Name = ReadText(reader, dictionary.Get("NM")),
                Target = ReadTarget(reader, dictionary, destinations, pageMap, pageNumber, warnings),
            });
        }

        return result;
    }

    /// <summary>Reads the moment a dictionary states in one of its date entries.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value of the entry.</param>
    /// <returns>The moment, or <see langword="null"/> when it is absent or cannot be read.</returns>
    private static DateTimeOffset? ReadDate(PdfFileReader reader, PdfObject? value) =>
        PdfDate.TryParse(ReadText(reader, value), out var parsed) ? parsed : null;

    /// <summary>Reads where an annotation points.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="annotation">The annotation dictionary.</param>
    /// <param name="destinations">The named destinations of the document.</param>
    /// <param name="pageMap">The map from page object numbers to page numbers.</param>
    /// <param name="pageNumber">The page the annotation belongs to, used in diagnostics.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <returns>The target, or <see langword="null"/> when the annotation points nowhere.</returns>
    private static PdfDestination? ReadTarget(
        PdfFileReader reader,
        PdfDictionary annotation,
        NamedDestinations destinations,
        Dictionary<long, int> pageMap,
        int pageNumber,
        List<PdfWarning> warnings)
    {
        var action = reader.ResolveDictionary(annotation.Get("A"));

        switch (action?.GetName("S"))
        {
            case "URI" when ReadText(reader, action.Get("URI")) is { } uri:
                return new PdfDestination { Uri = uri };

            case "GoTo":
                return ResolveDestination(reader, action.Get("D"), destinations, pageMap, pageNumber, warnings, 0);

            case "GoToR" or "Launch" when ReadFileSpecification(reader, action.Get("F")) is { } file:
                return new PdfDestination { FileName = file };
        }

        return annotation.Get("Dest") is { } destination
            ? ResolveDestination(reader, destination, destinations, pageMap, pageNumber, warnings, 0)
            : null;
    }

    /// <summary>Resolves a destination that may be direct, named, or a reference to either.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The destination as the document states it.</param>
    /// <param name="destinations">The named destinations of the document.</param>
    /// <param name="pageMap">The map from page object numbers to page numbers.</param>
    /// <param name="pageNumber">The page the destination belongs to, used in diagnostics.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <param name="depth">How many names have been followed already.</param>
    /// <returns>The destination, or <see langword="null"/> when the value is not one.</returns>
    private static PdfDestination? ResolveDestination(
        PdfFileReader reader,
        PdfObject? value,
        NamedDestinations destinations,
        Dictionary<long, int> pageMap,
        int pageNumber,
        List<PdfWarning> warnings,
        int depth)
    {
        switch (value)
        {
            case PdfName or PdfString when depth < 4:
            {
                var name = ReadText(reader, value);

                if (name is null)
                {
                    return null;
                }

                if (!destinations.TryGet(name, out var named))
                {
                    return Unresolved(name, reader, pageNumber, warnings);
                }

                // The name is kept beside the page it resolved to: a caller that reports a link wants
                // the name the document wrote, not only where it ended up.
                var resolved = ResolveDestination(reader, named, destinations, pageMap, pageNumber, warnings, depth + 1);

                if (resolved is null)
                {
                    return Unresolved(name, reader, pageNumber, warnings);
                }

                return resolved.NamedDestination is null ? resolved with { NamedDestination = name } : resolved;
            }

            case PdfReference reference when reader.Resolve(reference) is PdfDictionary referenced:
                return ResolveDestination(reader, referenced.Get("D"), destinations, pageMap, pageNumber, warnings, depth);

            case PdfDictionary dictionary:
                return ResolveDestination(reader, dictionary.Get("D"), destinations, pageMap, pageNumber, warnings, depth);
        }

        var array = reader.ResolveArray(value);

        return array is null ? null : FromDestinationArray(reader, array, pageMap);
    }

    /// <summary>Builds the remark and the value of a named destination that did not resolve.</summary>
    /// <param name="name">The name that was written.</param>
    /// <param name="reader">The document.</param>
    /// <param name="pageNumber">The page the destination belongs to, used in diagnostics.</param>
    /// <param name="warnings">The list that receives the remark.</param>
    /// <returns>The unresolved destination.</returns>
    private static PdfDestination Unresolved(
        string name,
        PdfFileReader reader,
        int pageNumber,
        List<PdfWarning> warnings)
    {
        var where = pageNumber > 0 ? $"page {pageNumber}" : "a bookmark";

        warnings.Add(new(
            PdfWarningKind.NamedDestinationUnresolved,
            $"'{reader.SourceName}': {where} names destination '{name}', which does not resolve."));

        return new PdfDestination { NamedDestination = name };
    }

    /// <summary>Builds a destination from the array form, which starts with the page it points at.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="array">The destination array.</param>
    /// <param name="pageMap">The map from page object numbers to page numbers.</param>
    /// <returns>The destination.</returns>
    private static PdfDestination FromDestinationArray(
        PdfFileReader reader,
        PdfArray array,
        Dictionary<long, int> pageMap)
    {
        int? number = array.Count > 0
            ? array.Items[0] switch
            {
                PdfReference reference when pageMap.TryGetValue(reference.ObjectNumber, out var page) => page,
                PdfInteger index when index.Value >= 0 => (int)index.Value + 1,
                _ => null,
            }
            : null;

        return new PdfDestination { PageNumber = number, Area = DestinationArea(reader, array) };
    }

    /// <summary>Reads the coordinates a destination names, when it names any.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="array">The destination array.</param>
    /// <returns>The area or point, or <see langword="null"/>.</returns>
    private static PdfRectangle? DestinationArea(PdfFileReader reader, PdfArray array)
    {
        var kind = array.Count > 1 ? ReadName(reader, array.Items[1]) : null;

        switch (kind)
        {
            case "XYZ" when array.Count >= 4
                && ReadNumber(reader, array.Items[2]) is { } x
                && ReadNumber(reader, array.Items[3]) is { } y:
                return PdfRectangle.FromCorners(x, y, x, y);

            case "FitR" when array.Count >= 6:
                return ReadRectangle(reader, new PdfArray(array.Items.Skip(2).Take(4)));

            default:
                return null;
        }
    }

    /// <summary>Reads the file a file specification names.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value of the entry.</param>
    /// <returns>The file name, or <see langword="null"/>.</returns>
    private static string? ReadFileSpecification(PdfFileReader reader, PdfObject? value) => reader.Resolve(value) switch
    {
        PdfString text => text.ToText(),
        PdfDictionary dictionary => ReadText(reader, dictionary.Get("UF")) ?? ReadText(reader, dictionary.Get("F")),
        _ => null,
    };

    /// <summary>The named destinations of a document, resolved once per read.</summary>
    private sealed class NamedDestinations
    {
        private readonly PdfFileReader reader;
        private readonly Dictionary<string, PdfObject> entries = new(StringComparer.Ordinal);
        private bool loaded;

        /// <summary>Initializes a new instance of the <see cref="NamedDestinations"/> class.</summary>
        /// <param name="reader">The document.</param>
        public NamedDestinations(PdfFileReader reader) => this.reader = reader;

        /// <summary>Looks up a destination by name.</summary>
        /// <param name="name">The name the document used.</param>
        /// <param name="value">The destination as the document states it.</param>
        /// <returns><see langword="true"/> when the name exists.</returns>
        /// <remarks>
        /// Both places a document can keep a named destination are read: the older <c>/Dests</c>
        /// dictionary in the catalog, and the <c>/Dests</c> name tree under <c>/Names</c>. A name that
        /// is in neither is reported by the caller, not by this lookup.
        /// </remarks>
        public bool TryGet(string name, out PdfObject? value)
        {
            Load();

            return entries.TryGetValue(name, out value);
        }

        private void Load()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;

            var catalog = reader.Catalog;

            if (reader.ResolveDictionary(catalog?.Get("Dests")) is { } dictionary)
            {
                foreach (var entry in dictionary.Entries)
                {
                    entries[entry.Key] = entry.Value;
                }
            }

            var names = reader.ResolveDictionary(catalog?.Get("Names"));
            CollectNameTree(
                reader,
                reader.ResolveDictionary(names?.Get("Dests")),
                (name, value) => entries[name] = value,
                0);
        }
    }
}
