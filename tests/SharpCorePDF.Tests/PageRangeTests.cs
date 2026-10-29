using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of a single page range: the notation it is written in, the ranges that are refused while parsing,
/// and how a range resolves against the real page count of a document.
/// </summary>
/// <remarks>
/// Page numbers in this library are one based, so the interesting cases are the open ended notations and the
/// ranges that reach past the end of the document they are resolved against.
/// </remarks>
public sealed class PageRangeTests
{
    /// <summary>Every notation of the API produces the range it describes.</summary>
    [Fact]
    public void Creates_a_range_from_every_notation()
    {
        Assert.Equal("5", PageRange.Single(5).ToString());
        Assert.Equal("3", PageRange.FromTo(3, 3).ToString());
        Assert.Equal("2-7", PageRange.FromTo(2, 7).ToString());
        Assert.Equal("3-", PageRange.FromPage(3).ToString());
        Assert.Equal("-4", PageRange.ToPage(4).ToString());
        Assert.Equal("1-", PageRange.All.ToString());

        Assert.Equal(1, PageRange.All.From);
        Assert.Equal(PageRange.OpenEnd, PageRange.All.To);
        Assert.True(PageRange.All.IsOpenEnded);
        Assert.False(PageRange.FromTo(2, 7).IsOpenEnded);
        Assert.True(PageRange.FromPage(3).IsOpenEnded);
    }

    /// <summary>A page number that cannot exist is a programmer error.</summary>
    [Fact]
    public void Refuses_a_page_number_below_one()
    {
        Assert.Equal("page", Assert.Throws<ArgumentOutOfRangeException>(() => PageRange.Single(0)).ParamName);
        Assert.Equal("from", Assert.Throws<ArgumentOutOfRangeException>(() => PageRange.FromTo(0, 3)).ParamName);
        Assert.Equal("to", Assert.Throws<ArgumentOutOfRangeException>(() => PageRange.FromTo(5, 4)).ParamName);
        Assert.Equal("from", Assert.Throws<ArgumentOutOfRangeException>(() => PageRange.FromPage(0)).ParamName);
        Assert.Equal("to", Assert.Throws<ArgumentOutOfRangeException>(() => PageRange.ToPage(0)).ParamName);
    }

    /// <summary>
    /// Parsing understands the four notations, with or without spaces around the dash, and it reads a range
    /// whose two ends are the same page as that closed range rather than as a single page.
    /// </summary>
    /// <remarks>
    /// <c>3-3</c> is the one spelling that tells <c>end &gt;= start</c> from <c>end &gt; start</c>: every
    /// other closed range in this file has a larger end than its start, so a parser that refused a range
    /// ending where it begins went unnoticed until mutation testing asked.
    /// </remarks>
    /// <param name="text">The text to parse.</param>
    /// <param name="from">The expected first page.</param>
    /// <param name="to">The expected last page.</param>
    [Theory]
    [InlineData("5", 5, 5)]
    [InlineData("2-7", 2, 7)]
    [InlineData(" 2 - 7 ", 2, 7)]
    [InlineData("3-3", 3, 3)]
    [InlineData("3-", 3, PageRange.OpenEnd)]
    [InlineData("1-", 1, PageRange.OpenEnd)]
    [InlineData("-4", 1, 4)]
    public void Parses_the_notation(string text, int from, int to)
    {
        Assert.True(PageRange.TryParse(text, out var range));

        Assert.Equal(from, range.From);
        Assert.Equal(to, range.To);
        Assert.Equal(range, PageRange.Parse(text));
    }

    /// <summary>A range that starts at the first page is formatted with the leading dash.</summary>
    /// <remarks>
    /// <c>-4</c> and <c>4</c> are different ranges, and <c>-1</c> and <c>1</c> select the same page: the
    /// notation omits the first page when it is one, so both spellings parse back to the same range.
    /// </remarks>
    [Fact]
    public void Formats_a_range_that_starts_at_the_first_page()
    {
        Assert.Equal("-4", PageRange.FromTo(1, 4).ToString());
        Assert.Equal("-1", PageRange.Single(1).ToString());
        Assert.Equal("1-", PageRange.All.ToString());
        Assert.Equal(PageRange.Single(1), PageRange.Parse("-1"));
        Assert.Equal(PageRange.All, PageRange.Parse("1-"));
    }

    /// <summary>Text that does not describe a range is refused instead of being guessed at.</summary>
    /// <param name="text">The text that is not a range.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("first")]
    [InlineData("3.5")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("0-2")]
    [InlineData("5-2")]
    [InlineData("1-2-3")]
    [InlineData("-")]
    [InlineData("2-x")]
    [InlineData("1-99999999999")]
    public void Refuses_text_that_is_not_a_range(string text)
    {
        Assert.False(PageRange.TryParse(text, out _));
        Assert.Throws<FormatException>(() => PageRange.Parse(text));
    }

    /// <summary>Refusing a range says which text was refused, in the four notations a caller could use.</summary>
    /// <remarks>
    /// The message is what a caller turns into an error beside a command line, so it is asserted whole
    /// rather than by the exception type alone: a blank message still throws where a test only asks for
    /// <see cref="FormatException"/>, and a message a user cannot act on is not a diagnosis.
    /// </remarks>
    [Fact]
    public void Says_what_a_range_looks_like_when_it_refuses()
    {
        var exception = Assert.Throws<FormatException>(() => PageRange.Parse("first"));

        Assert.Equal("'first' is not a valid page range. Expected 5, 2-7, 3- or -4.", exception.Message);
    }

    /// <summary>Nothing at all is not a range either.</summary>
    [Fact]
    public void Refuses_a_missing_range() => Assert.False(PageRange.TryParse(null, out _));

    /// <summary>A range counts and enumerates the pages it selects in a document.</summary>
    [Fact]
    public void Counts_and_enumerates_the_pages_it_selects()
    {
        Assert.Equal(3, PageRange.FromTo(2, 4).Count(10, "sample.pdf"));
        Assert.Equal([2, 3, 4], PageRange.FromTo(2, 4).Enumerate(10, "sample.pdf").ToArray());
        Assert.Equal([3, 4, 5], PageRange.FromPage(3).Enumerate(5, "sample.pdf").ToArray());
        Assert.Equal(3, PageRange.FromPage(3).Count(5, "sample.pdf"));
        Assert.Equal(4, PageRange.All.Count(4, "sample.pdf"));
        Assert.Equal([1, 2, 3, 4], PageRange.All.Enumerate(4, "sample.pdf").ToArray());
        Assert.Equal(4, PageRange.ToPage(4).ResolveTo(10, "sample.pdf"));
        Assert.Equal(7, PageRange.FromTo(7, 7).ResolveTo(10, "sample.pdf"));
    }

    /// <summary>A range knows which pages fall inside it, also when it is open ended.</summary>
    [Fact]
    public void Reports_whether_it_contains_a_page()
    {
        Assert.True(PageRange.All.Contains(1));
        Assert.True(PageRange.All.Contains(9999));
        Assert.True(PageRange.FromTo(2, 4).Contains(2));
        Assert.True(PageRange.FromTo(2, 4).Contains(4));
        Assert.False(PageRange.FromTo(2, 4).Contains(1));
        Assert.False(PageRange.FromTo(2, 4).Contains(5));
        Assert.True(PageRange.FromPage(3).Contains(1000));
        Assert.False(PageRange.FromPage(3).Contains(2));
    }

    /// <summary>A range that starts behind the last page names the document it does not fit.</summary>
    [Fact]
    public void Refuses_a_range_that_starts_behind_the_last_page()
    {
        var range = PageRange.FromPage(4);

        var exception = Assert.Throws<MergePlanValidationException>(() => range.ResolveTo(3, "sample.pdf"));

        Assert.Contains("sample.pdf", exception.Message, StringComparison.Ordinal);
        Assert.Contains("only has 3 page(s)", exception.Message, StringComparison.Ordinal);
        Assert.Throws<MergePlanValidationException>(() => range.Count(3, "sample.pdf"));
        Assert.Throws<MergePlanValidationException>(() => range.Enumerate(3, "sample.pdf").ToArray());
    }
}
