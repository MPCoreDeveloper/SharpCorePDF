using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of word-level reading: the letters of a page grouped into words by the strategy the caller
/// selects, with the order of the page as the default and the order of the content as the baseline.
/// </summary>
public sealed partial class PdfLayoutReaderTests
{
    /// <summary>
    /// The default strategy groups a line into words by the geometry of the page and keeps the letters of
    /// every word, so a caller can regroup them without reading the document again.
    /// </summary>
    [Fact]
    public async Task Groups_a_line_into_words_by_the_geometry_of_the_page()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var result = await Reader().ReadWordsAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(["Hello", "World", "Second", "line"], result.Items.Select(word => word.Text));
        Assert.Empty(result.Warnings);

        var hello = result.Items[0];

        Assert.Equal(1, hello.PageNumber);
        Assert.Equal(5, hello.Letters.Count);
        Assert.Equal(5, hello.Glyphs.Count);
        Assert.Equal("Helvetica", hello.FontName);
        Assert.Equal(12d, hello.FontSize, 0.001d);
        Assert.Equal(72d, hello.BoundingBox.Left, 0.001d);
        Assert.Equal(720d, hello.BaselineY, 0.001d);
        Assert.Equal("H", hello.Letters[0].Value);
    }

    /// <summary>
    /// The default strategy takes the order of the page; the raw strategy takes the order of the content,
    /// which is the only order that reproduces the producer exactly.
    /// </summary>
    [Fact]
    public async Task Takes_the_page_order_by_default_and_the_content_order_when_asked()
    {
        var source = Source(PdfContentFixtures.TextOutOfOrder(), "order.pdf");

        var ordered = await Reader().ReadWordsAsync(source, PageRange.All, cancellationToken: Token);
        var raw = await Reader().ReadWordsAsync(
            source,
            PageRange.All,
            new PdfWordExtractionOptions { Strategy = PdfWordExtractionStrategy.Raw },
            cancellationToken: Token);

        Assert.Equal(["Hello", "World", "Second"], ordered.Items.Select(word => word.Text));
        Assert.Equal(["World", "Hello", "Second"], raw.Items.Select(word => word.Text));
        Assert.Equal(108d, ordered.Items[1].BoundingBox.Left, 0.001d);
    }

    /// <summary>
    /// A caller that asks for whitespace gets the space in the word it follows, so the words of a page
    /// concatenate back to the text the page draws.
    /// </summary>
    [Fact]
    public async Task Keeps_the_whitespace_in_the_word_it_follows_when_asked()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var result = await Reader().ReadWordsAsync(
            source,
            PageRange.All,
            new PdfWordExtractionOptions { IncludeWhitespace = true },
            cancellationToken: Token);

        Assert.Equal(["Hello ", "World", "Second ", "line"], result.Items.Select(word => word.Text));
        Assert.Equal("Hello WorldSecond line", string.Concat(result.Items.Select(word => word.Text)));
        Assert.True(result.Items[0].Letters[^1].IsWhitespace);
    }

    /// <summary>
    /// The gap a caller allows decides where a word ends: a gap of a quarter em splits two runs and a
    /// wider allowance keeps them one word.
    /// </summary>
    [Fact]
    public async Task Splits_at_the_gap_the_caller_allows()
    {
        var source = Source(PdfContentFixtures.TextKerning(), "kern.pdf");

        var narrow = await Reader().ReadWordsAsync(source, PageRange.All, cancellationToken: Token);
        var wide = await Reader().ReadWordsAsync(
            source,
            PageRange.All,
            new PdfWordExtractionOptions { MaxHorizontalGap = 3d },
            cancellationToken: Token);

        Assert.Equal(["Kern", "Out", "er"], narrow.Items.Select(word => word.Text));
        Assert.Equal(["Kern", "Outer"], wide.Items.Select(word => word.Text));
    }

    /// <summary>
    /// A run that advances towards the left is grouped into a word read in its own direction, and its box
    /// covers the area between its baseline points rather than nothing.
    /// </summary>
    [Fact]
    public async Task Groups_a_right_to_left_run_in_its_own_direction()
    {
        var source = Source(PdfContentFixtures.TextRightToLeft(), "rtl.pdf");

        var result = await Reader().ReadWordsAsync(source, PageRange.All, cancellationToken: Token);

        var word = Assert.Single(result.Items);

        Assert.Equal("AB", word.Text);
        Assert.Equal(188d, word.BoundingBox.Left, 0.001d);
        Assert.Equal(200d, word.BoundingBox.Right, 0.001d);
        Assert.All(word.Letters, letter => Assert.Equal(PdfTextDirection.RightToLeft, letter.TextDirection));
    }

    /// <summary>Words carry the page they were drawn on, page by page.</summary>
    [Fact]
    public async Task Reads_the_words_of_every_page_in_order()
    {
        var source = Source(PdfContentFixtures.TextPages(), "pages.pdf");

        var result = await Reader().ReadWordsAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(["Page", "one", "Page", "two"], result.Items.Select(word => word.Text));
        Assert.Equal([1, 1, 2, 2], result.Items.Select(word => word.PageNumber));
    }

    /// <summary>A strategy this release does not implement is refused when the options are created.</summary>
    [Fact]
    public void Refuses_a_strategy_it_does_not_implement() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfWordExtractionOptions { Strategy = (PdfWordExtractionStrategy)42 });

    /// <summary>A gap that cannot be a distance is refused when the options are created, before a document is opened.</summary>
    [Fact]
    public void Refuses_a_negative_gap() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfWordExtractionOptions { MaxHorizontalGap = -0.1d });
}
