using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// An ordered selection of pages taken from a single source document, expressed as one or more
/// <see cref="PageRange"/> values.
/// </summary>
/// <remarks>
/// <para>
/// A selection is an immutable value object: <see cref="Add"/> and <see cref="Concat"/> return new
/// instances, which makes a selection safe to share between plans and across threads.
/// </para>
/// <para>
/// The order of the ranges is the order in which the pages appear in the merged document, so
/// <c>3,1</c> puts page 3 before page 1. Duplicates are collapsed on first occurrence.
/// </para>
/// </remarks>
public sealed class PageSelection : IEquatable<PageSelection>
{
    private readonly ImmutableArray<PageRange> ranges;

    private PageSelection(ImmutableArray<PageRange> ranges) => this.ranges = ranges;

    /// <summary>Gets a selection that contains every page of a document.</summary>
    public static PageSelection All { get; } = new([PageRange.All]);

    /// <summary>Gets the ranges that make up this selection, in merge order.</summary>
    public IReadOnlyList<PageRange> Ranges => ranges;

    /// <summary>Gets the number of ranges in this selection.</summary>
    public int RangeCount => ranges.Length;

    /// <summary>Gets a value indicating whether this selection covers the whole document in its natural order.</summary>
    public bool IsAll => ranges.Length == 1 && ranges[0] == PageRange.All;

    /// <summary>Creates a selection from one or more ranges.</summary>
    /// <param name="ranges">The ranges, in merge order.</param>
    /// <returns>The created selection.</returns>
    /// <exception cref="ArgumentException">No range was supplied.</exception>
    public static PageSelection Of(params PageRange[] ranges) => Of((IEnumerable<PageRange>)ranges);

    /// <summary>Creates a selection from one or more ranges.</summary>
    /// <param name="ranges">The ranges, in merge order.</param>
    /// <returns>The created selection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ranges"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No range was supplied.</exception>
    public static PageSelection Of(IEnumerable<PageRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var materialized = ImmutableArray.CreateRange(ranges);

        return materialized.Length == 0
            ? throw new ArgumentException(
                "A page selection must contain at least one range. Use PageSelection.All to select every page.",
                nameof(ranges))
            : new PageSelection(materialized);
    }

    /// <summary>Creates a selection for a single page.</summary>
    /// <param name="page">The one-based page number.</param>
    /// <returns>The created selection.</returns>
    public static PageSelection Single(int page) => new([PageRange.Single(page)]);

    /// <summary>Creates a selection for a closed range such as <c>2-7</c>.</summary>
    /// <param name="from">The first page, inclusive.</param>
    /// <param name="to">The last page, inclusive.</param>
    /// <returns>The created selection.</returns>
    public static PageSelection FromTo(int from, int to) => new([PageRange.FromTo(from, to)]);

    /// <summary>Creates an open ended selection such as <c>3-</c>.</summary>
    /// <param name="from">The first page, inclusive.</param>
    /// <returns>The created selection.</returns>
    public static PageSelection FromPage(int from) => new([PageRange.FromPage(from)]);

    /// <summary>Parses a selection such as <c>"1,3-5,8-"</c>.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed selection.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> is not a valid selection.</exception>
    public static PageSelection Parse(string? text) =>
        TryParse(text, out var selection)
            ? selection
            : throw new FormatException(
                $"'{text}' is not a valid page selection. Expected a comma separated list such as '1,3-5,8-'.");

    /// <summary>Attempts to parse a selection such as <c>"1,3-5,8-"</c>.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="selection">The parsed selection when parsing succeeded.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a valid selection.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out PageSelection? selection)
    {
        selection = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<PageRange>();

        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0 || !PageRange.TryParse(part, out var range))
            {
                return false;
            }

            builder.Add(range);
        }

        if (builder.Count == 0)
        {
            return false;
        }

        selection = new PageSelection(builder.ToImmutable());
        return true;
    }

    /// <summary>Returns a new selection with <paramref name="range"/> appended.</summary>
    /// <param name="range">The range to append.</param>
    /// <returns>The new selection.</returns>
    public PageSelection Add(PageRange range) => new(ranges.Add(range));

    /// <summary>Returns a new selection that appends the ranges of <paramref name="other"/>.</summary>
    /// <param name="other">The selection to append.</param>
    /// <returns>The new selection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public PageSelection Concat(PageSelection other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(ranges.AddRange(other.ranges));
    }

    /// <summary>Resolves this selection into concrete, one-based page numbers for a document.</summary>
    /// <param name="documentPageCount">The number of pages in the source document.</param>
    /// <param name="sourceName">The display name of the source, used in error messages.</param>
    /// <returns>The selected page numbers, in merge order and without duplicates.</returns>
    /// <exception cref="MergePlanValidationException">A range falls outside the document, or the document has no pages.</exception>
    public IReadOnlyList<int> Resolve(int documentPageCount, string sourceName)
    {
        if (documentPageCount < 1)
        {
            throw new MergePlanValidationException($"'{sourceName}' does not contain any pages.");
        }

        if (IsAll)
        {
            var everyPage = new int[documentPageCount];

            for (var index = 0; index < documentPageCount; index++)
            {
                everyPage[index] = index + 1;
            }

            return everyPage;
        }

        // The ranges are read in the caller's order and a page that appears twice keeps its first
        // position, which is exactly what Distinct preserves over a single enumeration.
        var pages = ranges
            .SelectMany(range => range.Enumerate(documentPageCount, sourceName))
            .Distinct()
            .ToList();

        if (pages.Count == 0)
        {
            throw new MergePlanValidationException(
                $"the selection '{this}' does not select any page of '{sourceName}'.");
        }

        return pages;
    }

    /// <summary>Counts how many pages this selection contributes for a document.</summary>
    /// <param name="documentPageCount">The number of pages in the source document.</param>
    /// <param name="sourceName">The display name of the source, used in error messages.</param>
    /// <returns>The number of contributed pages.</returns>
    public int ResolveCount(int documentPageCount, string sourceName) =>
        Resolve(documentPageCount, sourceName).Count;

    /// <summary>Determines whether <paramref name="other"/> selects exactly the same pages in the same order.</summary>
    /// <param name="other">The selection to compare with.</param>
    /// <returns><see langword="true"/> when both selections are identical.</returns>
    public bool Equals(PageSelection? other) => other is not null && ranges.SequenceEqual(other.ranges);

    /// <summary>Determines whether <paramref name="obj"/> is an identical selection.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an identical selection.</returns>
    public override bool Equals(object? obj) => obj is PageSelection other && Equals(other);

    /// <summary>Returns a hash code consistent with <see cref="Equals(PageSelection?)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var range in ranges)
        {
            hash.Add(range);
        }

        return hash.ToHashCode();
    }

    /// <summary>Formats the selection as a comma separated list.</summary>
    /// <returns>The formatted selection, for example <c>1,3-5,8-</c>.</returns>
    public override string ToString() => string.Join(",", ranges.Select(range => range.ToString()));
}
