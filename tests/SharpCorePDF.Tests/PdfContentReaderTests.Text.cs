using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of text reading: the glyphs a page draws, the words they make and the blocks those make, in the
/// order a reader would take them.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>
    /// A simple font is read through its encoding, the widths it states and the width it leaves to the
    /// reader, and the words of one line and the line under it make one block.
    /// </summary>
    [Fact]
    public async Task Reads_a_simple_font_into_words_and_a_block()
    {
        var source = Source(PdfContentFixtures.Text(), "text.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        var block = Assert.Single(result.Items);

        Assert.Equal(1, block.PageNumber);
        Assert.Equal(1, block.ReadingOrder);
        Assert.Equal("Hello World\nSecond line", block.Text);
        Assert.Equal(2, block.LineCount);
        Assert.Equal(["Hello", "World", "Second", "line"], block.Words.Select(word => word.Text));
        Assert.Equal(72d, block.BoundingBox.Left, 0.001d);
        Assert.Equal(143.328d, block.BoundingBox.Right, 0.001d);
        Assert.Equal(703.6d, block.BoundingBox.Bottom, 0.001d);
        Assert.Equal(729.6d, block.BoundingBox.Top, 0.001d);

        var world = block.Words[1];

        Assert.Equal(108d, world.BoundingBox.Left, 0.001d);
        Assert.Equal(11.328d, world.Glyphs[0].Width, 0.001d);
        Assert.Equal("Helvetica", world.Glyphs[0].BaseFont);
        Assert.Empty(result.Warnings);
    }

    /// <summary>Blocks follow the order of the page, not the order the content draws them in.</summary>
    [Fact]
    public async Task Orders_blocks_by_the_page_not_by_the_content()
    {
        var source = Source(PdfContentFixtures.TextBlocks(), "blocks.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(
            ["Earlier paragraph", "Later paragraph"],
            result.Items.Select(block => block.Text));
        Assert.Equal([1, 2], result.Items.Select(block => block.ReadingOrder));
        Assert.True(result.Items[0].BoundingBox.Top > result.Items[1].BoundingBox.Top);
    }

    /// <summary>
    /// A <c>TJ</c> adjustment tightens a pair of glyphs without splitting their word, and another one
    /// leaves a gap wide enough that the word is split.
    /// </summary>
    [Fact]
    public async Task Applies_the_adjustments_of_a_TJ_array()
    {
        var source = Source(PdfContentFixtures.TextKerning(), "kern.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(2, result.Count);

        var kerned = Assert.Single(result.Items[0].Words);

        Assert.Equal("Kern", kerned.Text);
        Assert.Equal(72d, kerned.Glyphs[0].X, 0.001d);
        Assert.Equal(82.56d, kerned.Glyphs[2].X, 0.001d);
        Assert.Equal(94.56d, kerned.BoundingBox.Right, 0.001d);

        var spaced = result.Items[1];

        Assert.Equal(["Out", "er"], spaced.Words.Select(word => word.Text));
        Assert.Equal(114d, spaced.Words[1].Glyphs[0].X, 0.001d);
    }

    /// <summary>A composite font is read through its ToUnicode map and its sheet of widths.</summary>
    [Fact]
    public async Task Reads_a_composite_font_through_its_ToUnicode_map()
    {
        var source = Source(PdfContentFixtures.TextComposite(), "composite.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        var word = Assert.Single(Assert.Single(result.Items).Words);

        Assert.Equal("Hifiabc", word.Text);
        Assert.Equal(["H", "i", "fi", "a", "b", "c"], word.Glyphs.Select(glyph => glyph.Text));
        Assert.Equal(6d, word.Glyphs[0].Width, 0.001d);
        Assert.Equal(8.4d, word.Glyphs[1].Width, 0.001d);
        Assert.Equal(3.6d, word.Glyphs[2].Width, 0.001d);
        Assert.Equal(86.4d, word.Glyphs[2].X, 0.001d);
        Assert.Equal(111.6d, word.BoundingBox.Right, 0.001d);
        Assert.Equal("F1", word.FontName);
        Assert.Equal("TestCID-Identity-H", word.Glyphs[0].BaseFont);
    }

    /// <summary>A glyph name in <c>/Differences</c> replaces the character the base encoding states.</summary>
    [Fact]
    public async Task Reads_a_glyph_named_by_the_Differences_array()
    {
        var source = Source(PdfContentFixtures.TextDifferences(), "differences.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        var word = Assert.Single(Assert.Single(result.Items).Words);

        Assert.Equal("éZ", word.Text);
        Assert.Equal(["é", "Z"], word.Glyphs.Select(glyph => glyph.Text));
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// A composite font that states no <c>/ToUnicode</c> map costs the text of its page and one warning,
    /// and the pages that can be read are still read.
    /// </summary>
    [Fact]
    public async Task Reports_a_composite_font_that_states_no_ToUnicode_map()
    {
        var source = Source(PdfContentFixtures.TextUnreadable(), "unreadable.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        var block = Assert.Single(result.Items);

        Assert.Equal(2, block.PageNumber);
        Assert.Equal("Readable", block.Text);
        Assert.Single(result.Warnings);
        AssertWarning(result.Warnings, PdfWarningKind.TextUnreadable);
    }

    /// <summary>Only the pages of the range are read, and each of them starts its own reading order.</summary>
    [Fact]
    public async Task Reads_the_text_of_the_pages_of_the_range()
    {
        var source = Source(PdfContentFixtures.TextPages(), "pages.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.Single(2), cancellationToken: Token);

        var block = Assert.Single(result.Items);

        Assert.Equal(2, block.PageNumber);
        Assert.Equal(1, block.ReadingOrder);
        Assert.Equal("Page two", block.Text);
    }

    /// <summary>A page that draws no text contributes no block and no warning.</summary>
    [Fact]
    public async Task Reads_a_page_with_no_content_as_no_text()
    {
        var source = Source(PdfContentFixtures.WithoutMetadata(), "plain.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Empty(result.Items);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// Text drawn by a form is read where the form's own <c>/Matrix</c> puts it, with the font and the size
    /// the form inherits from the page that paints it.
    /// </summary>
    [Fact]
    public async Task Reads_text_drawn_by_a_form_through_the_matrix_of_the_form()
    {
        var source = Source(PdfContentFixtures.TextForm(), "form.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(["Page text", "Form text"], result.Items.Select(block => block.Text));

        var form = result.Items[1];

        Assert.Equal("Form text", form.Text);
        Assert.Equal(["Form", "text"], form.Words.Select(word => word.Text));
        Assert.Equal(60d, form.BoundingBox.Left, 0.001d);
        Assert.Equal(114d, form.BoundingBox.Right, 0.001d);
        Assert.Equal(70d, form.Words[0].BaselineY, 0.001d);
        Assert.Equal(12d, form.Words[0].FontSize, 0.001d);
        Assert.Equal("F1", form.Words[0].FontName);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// The text state is part of the graphics state, so the size a line selects inside <c>q</c> and
    /// <c>Q</c> does not reach the lines drawn after the <c>Q</c>.
    /// </summary>
    [Fact]
    public async Task Restores_the_text_state_the_graphics_state_saved()
    {
        var source = Source(PdfContentFixtures.TextSavedState(), "state.pdf");

        var result = await Reader().ReadTextAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(["Outer", "Inner", "After"], result.Items.Select(block => block.Text));
        Assert.Equal(
            [12d, 48d, 12d],
            result.Items.Select(block => block.Words[0].FontSize));
        Assert.Equal(192d, result.Items[1].BoundingBox.Right, 0.001d);
    }

    /// <summary>The page overload of the extension reads one page through the same machinery.</summary>
    [Fact]
    public async Task Reads_one_page_through_the_extension()
    {
        var source = Source(PdfContentFixtures.TextPages(), "pages.pdf");

        var result = await ((IPdfContentReader)Reader()).ReadTextAsync(source, 2, cancellationToken: Token);

        Assert.Equal("Page two", Assert.Single(result.Items).Text);
    }
}
