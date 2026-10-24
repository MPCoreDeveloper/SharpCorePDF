using System.Globalization;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The numbering style of one range of page labels.</summary>
public enum PageLabelStyle
{
    /// <summary>Arabic numerals: 1, 2, 3. The default.</summary>
    Decimal = 0,

    /// <summary>Upper-case Roman numerals: I, II, III.</summary>
    UpperRoman = 1,

    /// <summary>Lower-case Roman numerals: i, ii, iii.</summary>
    LowerRoman = 2,

    /// <summary>Upper-case letters: A, B, C, … AA.</summary>
    UpperLetters = 3,

    /// <summary>Lower-case letters: a, b, c, … aa.</summary>
    LowerLetters = 4,

    /// <summary>The range carries a prefix or a fixed label and no page number at all.</summary>
    NoNumber = 5,
}

/// <summary>
/// One run of pages that share a numbering style, starting at <see cref="StartPage"/> and lasting until
/// the next range begins.
/// </summary>
public sealed record PdfPageLabelRange
{
    /// <summary>Gets the one-based page the range starts at.</summary>
    public required int StartPage { get; init; }

    /// <summary>Gets the numbering style of the range.</summary>
    public PageLabelStyle Style { get; init; } = PageLabelStyle.Decimal;

    /// <summary>Gets the text written before the number of every page in the range, if any.</summary>
    public string? Prefix { get; init; }

    /// <summary>Gets the number the first page of the range is given.</summary>
    public int StartNumber { get; init; } = 1;

    /// <summary>Formats the range for diagnostics.</summary>
    /// <returns>The start page and the label it produces.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"pages from {StartPage}: '{Prefix}{StartNumber}' ({Style})");
}

/// <summary>
/// The page labels an edit writes: one range per style, in ascending page order.
/// </summary>
/// <remarks>
/// <para>
/// Page labels are the numbers a viewer shows in its page box and prints in its page headers; they are
/// independent of the physical page index. A caller states them explicitly because only the caller
/// knows that the third page of a report is the first page of chapter one.
/// </para>
/// <para>
/// <see cref="None"/> writes no labels at all, which is how an edit removes the labels of a source
/// instead of carrying them across a reordering that would make them wrong.
/// </para>
/// </remarks>
public sealed record PdfPageLabeling
{
    /// <summary>Gets the value that writes no page labels, so a viewer numbers the pages sequentially.</summary>
    public static PdfPageLabeling None { get; } = new();

    /// <summary>Gets the ranges in ascending page order.</summary>
    public IReadOnlyList<PdfPageLabelRange> Ranges { get; init; } = [];

    /// <summary>Creates labeling that numbers every page from <paramref name="startPage"/> in Arabic numerals.</summary>
    /// <param name="startPage">The one-based page the numbering starts at.</param>
    /// <param name="prefix">The text written before every number, if any.</param>
    /// <returns>The labeling.</returns>
    public static PdfPageLabeling Decimal(int startPage = 1, string? prefix = null) =>
        From(new PdfPageLabelRange { StartPage = startPage, Prefix = prefix });

    /// <summary>Creates labeling from explicit ranges.</summary>
    /// <param name="ranges">The ranges, in ascending page order.</param>
    /// <returns>The labeling.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ranges"/> is <see langword="null"/>.</exception>
    public static PdfPageLabeling From(params PdfPageLabelRange[] ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        return new() { Ranges = [.. ranges] };
    }

    /// <summary>Validates these labels.</summary>
    /// <returns>Every problem found, or an empty list when the labels are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var previous = 0;

        for (var index = 0; index < Ranges.Count; index++)
        {
            if (Ranges[index] is not { } range)
            {
                errors.Add($"page label range {index} is null.");
                continue;
            }

            if (range.StartPage < 1)
            {
                errors.Add($"page label range {index}: start page {range.StartPage} is not one-based.");
            }
            else if (range.StartPage <= previous)
            {
                errors.Add(
                    $"page label range {index}: start page {range.StartPage} does not follow page {previous}.");
            }

            if (range.StartNumber < 1)
            {
                errors.Add(
                    $"page label range {index}: number {range.StartNumber} is not one-based.");
            }

            if (!Enum.IsDefined(range.Style))
            {
                errors.Add($"page label range {index}: style '{(int)range.Style}' is not one PDF defines.");
            }

            previous = Math.Max(previous, range.StartPage);
        }

        return errors;
    }
}
