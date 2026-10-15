using System.Diagnostics.CodeAnalysis;
using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// An inclusive, one-based range of pages, optionally open ended.
/// </summary>
/// <remarks>
/// Page numbers in SharpCorePDF are always one-based, matching how a reader displays them and how the
/// <c>1-3</c>, <c>4-</c> and <c>-2</c> notations are usually written. PDF's internal zero-based page
/// indices never surface in this API.
/// </remarks>
public readonly record struct PageRange
{
    /// <summary>The sentinel used by an open ended range: <c>3-</c> selects page 3 through the last page.</summary>
    public const int OpenEnd = int.MaxValue;

    private PageRange(int from, int to)
    {
        From = from;
        To = to;
    }

    /// <summary>Gets the first page of the range, inclusive.</summary>
    public int From { get; }

    /// <summary>Gets the last page of the range, inclusive, or <see cref="OpenEnd"/>.</summary>
    public int To { get; }

    /// <summary>Gets a value indicating whether the range runs through the last page of the document.</summary>
    public bool IsOpenEnded => To == OpenEnd;

    /// <summary>Gets a range that selects every page, equivalent to <c>1-</c>.</summary>
    public static PageRange All => new(1, OpenEnd);

    /// <summary>Creates a range that selects exactly one page.</summary>
    /// <param name="page">The one-based page number.</param>
    /// <returns>The created range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is less than one.</exception>
    public static PageRange Single(int page)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return new PageRange(page, page);
    }

    /// <summary>Creates a closed range such as <c>2-7</c>.</summary>
    /// <param name="from">The first page, inclusive.</param>
    /// <param name="to">The last page, inclusive; must not be smaller than <paramref name="from"/>.</param>
    /// <returns>The created range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A page number is less than one or <paramref name="to"/> is smaller than <paramref name="from"/>.</exception>
    public static PageRange FromTo(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(from, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        return new PageRange(from, to);
    }

    /// <summary>Creates an open ended range such as <c>3-</c>.</summary>
    /// <param name="from">The first page, inclusive.</param>
    /// <returns>The created range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> is less than one.</exception>
    public static PageRange FromPage(int from)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(from, 1);
        return new PageRange(from, OpenEnd);
    }

    /// <summary>Creates a range from the first page such as <c>-2</c>.</summary>
    /// <param name="to">The last page, inclusive.</param>
    /// <returns>The created range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/> is less than one.</exception>
    public static PageRange ToPage(int to)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(to, 1);
        return new PageRange(1, to);
    }

    /// <summary>Parses a single range in the <c>5</c>, <c>2-7</c>, <c>3-</c> or <c>-4</c> notation.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed range.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> is not a valid range.</exception>
    public static PageRange Parse(string? text) =>
        TryParse(text, out var range)
            ? range
            : throw new FormatException($"'{text}' is not a valid page range. Expected 5, 2-7, 3- or -4.");

    /// <summary>Attempts to parse a single range in the <c>5</c>, <c>2-7</c>, <c>3-</c> or <c>-4</c> notation.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="range">The parsed range when parsing succeeded.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a valid range.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out PageRange range)
    {
        range = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var separator = trimmed.IndexOf('-', StringComparison.Ordinal);

        if (separator < 0)
        {
            return int.TryParse(trimmed, out var single) && single >= 1 && TryCreate(single, single, out range);
        }

        if (trimmed.IndexOf('-', separator + 1) >= 0)
        {
            return false;
        }

        var left = trimmed[..separator].Trim();
        var right = trimmed[(separator + 1)..].Trim();

        if (left.Length == 0 && right.Length == 0)
        {
            return false;
        }

        if (left.Length == 0)
        {
            return int.TryParse(right, out var to) && to >= 1 && TryCreate(1, to, out range);
        }

        if (right.Length == 0)
        {
            return int.TryParse(left, out var from) && from >= 1 && TryCreate(from, OpenEnd, out range);
        }

        return int.TryParse(left, out var start)
            && int.TryParse(right, out var end)
            && start >= 1
            && end >= start
            && TryCreate(start, end, out range);
    }

    /// <summary>Resolves the last page of this range against a concrete document.</summary>
    /// <param name="documentPageCount">The number of pages in the source document.</param>
    /// <param name="sourceName">The display name of the source, used in error messages.</param>
    /// <returns>The resolved, inclusive last page.</returns>
    /// <exception cref="MergePlanValidationException">The range starts beyond the last page of the document.</exception>
    public int ResolveTo(int documentPageCount, string sourceName)
    {
        if (From > documentPageCount)
        {
            throw new MergePlanValidationException(
                $"page range '{this}' starts at page {From} but '{sourceName}' only has {documentPageCount} page(s).");
        }

        return IsOpenEnded ? documentPageCount : To;
    }

    /// <summary>Counts the pages this range selects in a document with <paramref name="documentPageCount"/> pages.</summary>
    /// <param name="documentPageCount">The number of pages in the source document.</param>
    /// <param name="sourceName">The display name of the source, used in error messages.</param>
    /// <returns>The number of selected pages.</returns>
    public int Count(int documentPageCount, string sourceName) =>
        ResolveTo(documentPageCount, sourceName) - From + 1;

    /// <summary>Determines whether this range contains <paramref name="page"/>.</summary>
    /// <param name="page">A one-based page number.</param>
    /// <returns><see langword="true"/> when the page number falls inside the range.</returns>
    public bool Contains(int page) => page >= From && (IsOpenEnded || page <= To);

    /// <summary>Enumerates the page numbers this range selects in a concrete document.</summary>
    /// <param name="documentPageCount">The number of pages in the source document.</param>
    /// <param name="sourceName">The display name of the source, used in error messages.</param>
    /// <returns>The selected, ascending page numbers.</returns>
    public IEnumerable<int> Enumerate(int documentPageCount, string sourceName)
    {
        var last = ResolveTo(documentPageCount, sourceName);

        for (var page = From; page <= last; page++)
        {
            yield return page;
        }
    }

    /// <summary>Formats the range in the <c>5</c>, <c>2-7</c>, <c>3-</c> or <c>-4</c> notation.</summary>
    /// <returns>The formatted range.</returns>
    public override string ToString() => this switch
    {
        { From: 1, IsOpenEnded: true } => "1-",
        { IsOpenEnded: true } => $"{From}-",
        { From: 1 } => $"-{To}",
        var r when r.From == r.To => r.From.ToString(System.Globalization.CultureInfo.InvariantCulture),
        var r => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{r.From}-{r.To}"),
    };

    private static bool TryCreate(int from, int to, out PageRange range)
    {
        if (from < 1 || to < from)
        {
            range = default;
            return false;
        }

        range = new PageRange(from, to);
        return true;
    }
}
