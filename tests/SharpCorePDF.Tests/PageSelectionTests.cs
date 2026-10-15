using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of a page selection: the comma separated notation, the page numbers it resolves to, the order and
/// the duplicates of those pages, and the selections that are refused.
/// </summary>
/// <remarks>
/// The order of a selection is the order the pages appear in the merged document, so resolving a selection
/// is where a caller's <c>3,1</c> becomes a decision about the output.
/// </remarks>
public sealed class PageSelectionTests
{
    /// <summary>Every notation of the API produces the selection it describes.</summary>
    [Fact]
    public void Creates_a_selection_from_every_notation()
    {
        Assert.Equal("5", PageSelection.Single(5).ToString());
        Assert.Equal("2-7", PageSelection.FromTo(2, 7).ToString());
        Assert.Equal("3-", PageSelection.FromPage(3).ToString());
        Assert.Equal("1-", PageSelection.All.ToString());

        Assert.Equal([PageRange.FromTo(2, 7)], PageSelection.FromTo(2, 7).Ranges.ToArray());
        Assert.Equal(1, PageSelection.FromTo(2, 7).RangeCount);
        Assert.True(PageSelection.All.IsAll);

        // "3-" is not the whole document, while "1-" is, also when it was built from the first page.
        Assert.False(PageSelection.FromPage(3).IsAll);
        Assert.True(PageSelection.FromPage(1).IsAll);
        Assert.False(PageSelection.Single(1).IsAll);
    }

    /// <summary>A selection can be built from a list of ranges as well.</summary>
    [Fact]
    public void Creates_a_selection_from_ranges()
    {
        var selection = PageSelection.Of(PageRange.Single(3), PageRange.FromTo(5, 6));
        var fromList = PageSelection.Of((IEnumerable<PageRange>)[PageRange.Single(3), PageRange.FromTo(5, 6)]);

        Assert.Equal(2, selection.RangeCount);
        Assert.Equal("3,5-6", selection.ToString());
        Assert.Equal(selection, fromList);
    }

    /// <summary>A selection needs at least one range to select from.</summary>
    [Fact]
    public void Refuses_a_selection_without_ranges()
    {
        var exception = Assert.Throws<ArgumentException>(() => PageSelection.Of());

        Assert.Contains("at least one range", exception.Message, StringComparison.Ordinal);
        Assert.Equal("ranges", exception.ParamName);
        Assert.Throws<ArgumentNullException>(() => PageSelection.Of((IEnumerable<PageRange>)null!));
    }

    /// <summary>The comma separated notation is parsed, spaces and all.</summary>
    /// <remarks>
    /// The first range of the second text starts at the first page and ends at the first page, which the
    /// notation writes as <c>-1</c>: both spellings select the same page, so the parsed range formats as the
    /// one the notation canonically uses.
    /// </remarks>
    [Fact]
    public void Parses_the_notation()
    {
        Assert.True(PageSelection.TryParse("3,5-7,9-", out var selection));

        Assert.Equal(3, selection!.RangeCount);
        Assert.Equal("3,5-7,9-", selection.ToString());
        Assert.Equal("3,5-7,9-", PageSelection.Parse(" 3 , 5-7 ,9- ").ToString());
        Assert.Equal("-1", PageSelection.Parse("1").ToString());
        Assert.Equal(PageSelection.Single(1), PageSelection.Parse("-1"));
    }

    /// <summary>Text that does not describe a selection is refused instead of being guessed at.</summary>
    /// <param name="text">The text that is not a selection.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData("1,,2")]
    [InlineData("1,")]
    [InlineData("first")]
    [InlineData("1,second")]
    [InlineData("0-2")]
    public void Refuses_text_that_is_not_a_selection(string text)
    {
        Assert.False(PageSelection.TryParse(text, out _));
        Assert.Throws<FormatException>(() => PageSelection.Parse(text));
    }

    /// <summary>Nothing at all is not a selection either.</summary>
    [Fact]
    public void Refuses_a_missing_selection() => Assert.False(PageSelection.TryParse(null, out _));

    /// <summary>The selected pages are resolved in merge order and without duplicates.</summary>
    [Fact]
    public void Resolves_the_selected_pages()
    {
        Assert.Equal([3, 1], PageSelection.Parse("3,1").Resolve(4, "sample.pdf"));
        Assert.Equal([3, 1], PageSelection.Parse("3,1,3").Resolve(4, "sample.pdf"));
        Assert.Equal([1, 3, 4], PageSelection.Parse("1,3-4").Resolve(4, "sample.pdf"));
        Assert.Equal([1, 2, 3], PageSelection.All.Resolve(3, "sample.pdf"));
        Assert.Equal([8, 9, 10], PageSelection.FromPage(8).Resolve(10, "sample.pdf"));
        Assert.Equal([1, 2], PageSelection.FromTo(1, 2).Resolve(10, "sample.pdf"));
        Assert.Equal(3, PageSelection.FromTo(2, 4).ResolveCount(10, "sample.pdf"));
    }

    /// <summary>A document without pages cannot be selected from at all.</summary>
    [Fact]
    public void Refuses_a_document_without_pages()
    {
        var exception = Assert.Throws<MergePlanValidationException>(() => PageSelection.All.Resolve(0, "empty.pdf"));

        Assert.Contains("'empty.pdf' does not contain any pages", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A selection grows by adding a range or by appending another selection.</summary>
    [Fact]
    public void Adds_ranges_and_appends_selections()
    {
        var added = PageSelection.Single(3).Add(PageRange.FromTo(5, 6));
        var appended = added.Concat(PageSelection.Single(9));

        Assert.Equal("3", PageSelection.Single(3).ToString());
        Assert.Equal("3,5-6", added.ToString());
        Assert.Equal("3,5-6,9", appended.ToString());
        Assert.Equal(3, appended.RangeCount);
        Assert.Throws<ArgumentNullException>(() => added.Concat(null!));
    }

    /// <summary>Two selections are equal when they select the same pages in the same order.</summary>
    [Fact]
    public void Compares_by_the_pages_it_selects()
    {
        var left = PageSelection.Parse("1,3-5");
        var right = PageSelection.Of(PageRange.Single(1), PageRange.FromTo(3, 5));

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left.Equals(PageSelection.Parse("3-5,1")));
        Assert.False(left.Equals(PageSelection.Parse("1,3-6")));
        Assert.False(left.Equals(PageSelection.Parse("1-")));
        Assert.False(left.Equals((PageSelection?)null));
        Assert.False(left.Equals(new object()));
    }
}
