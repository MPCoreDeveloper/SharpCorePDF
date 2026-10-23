using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of letter-level reading: one entry per glyph, in the order the content stream draws them, with the
/// baseline, the font and the direction a caller needs to place text back on the page.
/// </summary>
public sealed partial class PdfLayoutReaderTests
{
    /// <summary>
    /// A simple font is read glyph by glyph: the content order is kept, the baseline is the one the text
    /// matrix states, the space is reported as whitespace and the box follows the size of the em.
    /// </summary>
    [Fact]
    public async Task Reads_one_letter_per_glyph_in_content_order()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var result = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(22, result.Count);
        Assert.Equal("Hello WorldSecond line", string.Concat(result.Items.Select(letter => letter.Value)));
        Assert.Empty(result.Warnings);

        var first = result.Items[0];

        Assert.Equal("H", first.Value);
        Assert.Equal(1, first.PageNumber);
        Assert.Equal(72d, first.StartBaseLine.X, 0.001d);
        Assert.Equal(720d, first.StartBaseLine.Y, 0.001d);
        Assert.Equal(78d, first.EndBaseLine.X, 0.001d);
        Assert.Equal(720d, first.EndBaseLine.Y, 0.001d);
        Assert.Equal(6d, first.Width, 0.001d);
        Assert.Equal(12d, first.FontSize, 0.001d);
        Assert.Equal("Helvetica", first.FontName);
        Assert.Equal(PdfTextDirection.LeftToRight, first.TextDirection);
        Assert.False(first.IsWhitespace);
        Assert.Equal(72d, first.BoundingBox.Left, 0.001d);
        Assert.Equal(717.6d, first.BoundingBox.Bottom, 0.001d);
        Assert.Equal(729.6d, first.BoundingBox.Top, 0.001d);

        var space = result.Items[5];

        Assert.Equal(" ", space.Value);
        Assert.True(space.IsWhitespace);

        var second = result.Items[11];

        Assert.Equal("S", second.Value);
        Assert.Equal(706d, second.StartBaseLine.Y, 0.001d);
    }

    /// <summary>
    /// The order of the letters is the order of the content, even when the page reads the other way round:
    /// the footer is drawn first here and still comes first.
    /// </summary>
    [Fact]
    public async Task Keeps_the_content_order_rather_than_the_reading_order()
    {
        var source = Source(PdfContentFixtures.TextBlocks(), "blocks.pdf");

        var result = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(
            "Later paragraphEarlier paragraph",
            string.Concat(result.Items.Select(letter => letter.Value)));
        Assert.True(result.Items[0].StartBaseLine.Y < result.Items[^1].StartBaseLine.Y);
    }

    /// <summary>
    /// A composite font is read through its <c>/ToUnicode</c> map: a code that maps to two characters
    /// becomes one letter with both, because the document draws them as one glyph.
    /// </summary>
    [Fact]
    public async Task Reads_a_composite_font_through_its_ToUnicode_map()
    {
        var source = Source(PdfContentFixtures.TextComposite(), "composite.pdf");

        var result = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(["H", "i", "fi", "a", "b", "c"], result.Items.Select(letter => letter.Value));
        Assert.Equal("TestCID-Identity-H", result.Items[0].FontName);
        Assert.Empty(result.Warnings);
    }

    /// <summary>A simple font's <c>/Differences</c> name the character a code stands for.</summary>
    [Fact]
    public async Task Reads_a_letter_named_by_the_differences_of_an_encoding()
    {
        var source = Source(PdfContentFixtures.TextDifferences(), "differences.pdf");

        var result = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal("éZ", string.Concat(result.Items.Select(letter => letter.Value)));
    }

    /// <summary>
    /// A page whose font cannot be mapped contributes no letter and one warning naming the font, so silence
    /// and unreadable text stay different answers.
    /// </summary>
    [Fact]
    public async Task Skips_a_glyph_no_font_can_map_and_says_so()
    {
        var source = Source(PdfContentFixtures.TextUnreadable(), "unreadable.pdf");

        var result = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal("Readable", string.Concat(result.Items.Select(letter => letter.Value)));
        Assert.All(result.Items, letter => Assert.Equal(2, letter.PageNumber));
        AssertWarning(result.Warnings, PdfWarningKind.TextUnreadable);
    }

    /// <summary>A page that draws no text contributes no letter and no warning.</summary>
    [Fact]
    public async Task Reads_a_page_with_no_text_as_no_letters()
    {
        var source = Source(PdfContentFixtures.WithoutMetadata(), "plain.pdf");

        var result = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Empty(result.Items);
        Assert.Empty(result.Warnings);
    }

    /// <summary>A range selects the pages it names and the letters carry the page they were drawn on.</summary>
    [Fact]
    public async Task Reads_the_letters_of_one_page_of_a_range()
    {
        var source = Source(PdfContentFixtures.TextPages(), "pages.pdf");

        var result = await Reader().ReadLettersAsync(source, PageRange.Single(2), cancellationToken: Token);

        Assert.Equal("Page two", string.Concat(result.Items.Select(letter => letter.Value)));
        Assert.All(result.Items, letter => Assert.Equal(2, letter.PageNumber));
    }

    /// <summary>Two reads of the same document produce the same letters, because nothing here is mutable.</summary>
    [Fact]
    public async Task Reads_the_same_letters_twice()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var first = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);
        var second = await Reader().ReadLettersAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(first.Items, second.Items);
        Assert.Equal(first.Warnings, second.Warnings);
    }

    /// <summary>
    /// A letter whose end sits to the left of its start runs right to left, and its width is negative: the
    /// direction is derived from the baseline, not from the character.
    /// </summary>
    [Fact]
    public void Derives_the_direction_from_the_baseline()
    {
        var letter = new PdfLetter
        {
            Value = "א",
            PageNumber = 1,
            FontSize = 12d,
            BoundingBox = new PdfRectangle(90d, 700d, 100d, 712d),
            StartBaseLine = new PdfPoint(100d, 702d),
            EndBaseLine = new PdfPoint(90d, 702d),
        };

        Assert.Equal(PdfTextDirection.RightToLeft, letter.TextDirection);
        Assert.Equal(-10d, letter.Width, 0.001d);
        Assert.False(letter.IsWhitespace);
    }

    /// <summary>A missing source is a programming error and is reported as one.</summary>
    [Fact]
    public async Task Refuses_a_missing_source()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Reader().ReadLettersAsync(null!, PageRange.All, cancellationToken: Token));
    }
}
