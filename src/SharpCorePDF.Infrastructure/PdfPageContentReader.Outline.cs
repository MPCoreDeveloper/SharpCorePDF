using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The outline half of <see cref="PdfPageContentReader"/>: the bookmark tree, with destinations
/// resolved as far as the document allows.
/// </summary>
public sealed partial class PdfPageContentReader
{
    /// <summary>The largest number of bookmarks that are read, as a guard against a damaged tree.</summary>
    private const int MaxOutlineItems = 100_000;

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfOutlineItem>> ReadOutlineAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        NoteClosedEncryption(reader, warnings);

        var items = new List<PdfOutlineItem>();

        if (reader.ResolveDictionary(reader.Catalog?.Get("Outlines")) is { } outline)
        {
            var destinations = new NamedDestinations(reader);
            var pageMap = BuildPageMap(reader);
            var visited = new HashSet<long>();

            var current = outline.Get("First");
            var count = 0;

            while (current is not null && count < MaxOutlineItems)
            {
                var item = ReadOutlineItem(reader, current, destinations, pageMap, warnings, visited, 0);

                if (item is null)
                {
                    break;
                }

                items.Add(item);
                count++;
                current = reader.ResolveDictionary(current)?.Get("Next");
            }
        }

        return Result(reader, items, warnings);
    }

    /// <summary>Reads one bookmark, with its children.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value of the bookmark.</param>
    /// <param name="destinations">The named destinations of the document.</param>
    /// <param name="pageMap">The map from page object numbers to page numbers.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <param name="visited">The object numbers already read, so a damaged tree cannot loop.</param>
    /// <param name="depth">The depth of the bookmark.</param>
    /// <returns>The bookmark, or <see langword="null"/> when it cannot be read.</returns>
    private static PdfOutlineItem? ReadOutlineItem(
        PdfFileReader reader,
        PdfObject? value,
        NamedDestinations destinations,
        Dictionary<long, int> pageMap,
        List<PdfWarning> warnings,
        HashSet<long> visited,
        int depth)
    {
        if (depth > 32 || value is null)
        {
            return null;
        }

        if (value is PdfReference reference && !visited.Add(reference.ObjectNumber))
        {
            warnings.Add(new(
                PdfWarningKind.BookmarkSkipped,
                $"'{reader.SourceName}': the outline repeats object {reference.ObjectNumber}; the branch was skipped."));
            return null;
        }

        var dictionary = reader.ResolveDictionary(value);

        if (dictionary is null)
        {
            warnings.Add(new(
                PdfWarningKind.BookmarkSkipped,
                $"'{reader.SourceName}': a bookmark could not be resolved and was skipped."));
            return null;
        }

        var title = ReadText(reader, dictionary.Get("Title")) ?? "(untitled)";
        var children = new List<PdfOutlineItem>();
        var current = dictionary.Get("First");
        var added = 0;

        while (current is not null && added < MaxOutlineItems)
        {
            var child = ReadOutlineItem(reader, current, destinations, pageMap, warnings, visited, depth + 1);

            if (child is null)
            {
                break;
            }

            children.Add(child);
            added++;
            current = reader.ResolveDictionary(current)?.Get("Next");
        }

        // A count is how many descendants are visible: a negative one means the item is closed.
        var count = dictionary.GetInteger("Count");

        return new PdfOutlineItem
        {
            Title = title,
            Destination = ReadOutlineDestination(reader, dictionary, destinations, pageMap, warnings, title),
            IsOpen = count is null || count >= 0,
            Children = children,
        };
    }

    /// <summary>Reads where a bookmark points.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="item">The bookmark dictionary.</param>
    /// <param name="destinations">The named destinations of the document.</param>
    /// <param name="pageMap">The map from page object numbers to page numbers.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <param name="title">The title of the bookmark, used in diagnostics.</param>
    /// <returns>The destination, or <see langword="null"/>.</returns>
    private static PdfDestination? ReadOutlineDestination(
        PdfFileReader reader,
        PdfDictionary item,
        NamedDestinations destinations,
        Dictionary<long, int> pageMap,
        List<PdfWarning> warnings,
        string title)
    {
        var action = reader.ResolveDictionary(item.Get("A"));
        var actionType = action?.GetName("S");

        if (actionType == "URI" && ReadText(reader, action!.Get("URI")) is { } uri)
        {
            return new PdfDestination { Uri = uri };
        }

        if (actionType is "GoToR" or "Launch" && ReadFileSpecification(reader, action!.Get("F")) is { } file)
        {
            return new PdfDestination { FileName = file };
        }

        if (actionType == "GoTo")
        {
            return ResolveDestination(reader, action!.Get("D"), destinations, pageMap, 0, warnings, 0);
        }

        if (item.Get("Dest") is { } direct)
        {
            return ResolveDestination(reader, direct, destinations, pageMap, 0, warnings, 0);
        }

        if (actionType is not null)
        {
            warnings.Add(new(
                PdfWarningKind.BookmarkDestinationDropped,
                $"'{reader.SourceName}': the bookmark '{title}' uses an action this library does not resolve."));
        }

        return null;
    }
}