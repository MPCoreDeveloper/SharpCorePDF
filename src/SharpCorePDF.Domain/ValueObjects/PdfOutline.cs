using System.Globalization;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The bookmarks an edit writes, as a tree of <see cref="PdfOutlineItem"/>.
/// </summary>
/// <remarks>
/// <para>
/// The same shape is read back by the outline reader as <see cref="PdfOutlineItem"/>, so a caller can
/// read an outline out of one document and write it into another without a second model. A bookmark
/// points at a page of the document being edited — numbered one-based in the order the edit leaves the
/// pages in — or at a URL, or at a destination that does not resolve at all, in which case it is
/// written without one.
/// </para>
/// <para>
/// <see cref="Empty"/> writes an outline with no bookmarks, which removes the outline a source
/// carried. An edit that does not set an outline keeps the one of the source, with the destinations
/// that named a deleted page removed and reported.
/// </para>
/// </remarks>
public sealed record PdfOutline
{
    /// <summary>Gets the outline that writes no bookmarks.</summary>
    public static PdfOutline Empty { get; } = new();

    /// <summary>Gets the top-level bookmarks, in document order.</summary>
    public IReadOnlyList<PdfOutlineItem> Items { get; init; } = [];

    /// <summary>The depth an outline may reach before it is refused.</summary>
    /// <remarks>
    /// A viewer shows a handful of levels at most, and an outline deeper than this is a sign of a
    /// builder that walked a cycle rather than of a document somebody wrote.
    /// </remarks>
    public const int MaximumDepth = 8;

    /// <summary>Creates an outline from top-level bookmarks.</summary>
    /// <param name="items">The bookmarks, in document order.</param>
    /// <returns>The outline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public static PdfOutline From(params PdfOutlineItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new() { Items = [.. items] };
    }

    /// <summary>Validates this outline.</summary>
    /// <returns>Every problem found, or an empty list when the outline is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Check(Items, 1, errors);
        return errors;
    }

    private static void Check(IReadOnlyList<PdfOutlineItem> items, int depth, List<string> errors)
    {
        if (depth > MaximumDepth)
        {
            errors.Add($"the outline is deeper than {MaximumDepth} levels.");
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not { } item)
            {
                errors.Add($"bookmark {index} at depth {depth} is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Title))
            {
                errors.Add($"bookmark {index} at depth {depth} has no title.");
            }

            if (item.Destination is null)
            {
                errors.Add(
                    $"bookmark '{item.Title}' has no destination; a bookmark must point at a page or a URL.");
            }
            else if (item.Destination.PageNumber is < 1)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"bookmark '{item.Title}' points at page {item.Destination.PageNumber}, which is not one-based."));
            }

            Check(item.Children, depth + 1, errors);
        }
    }
}
