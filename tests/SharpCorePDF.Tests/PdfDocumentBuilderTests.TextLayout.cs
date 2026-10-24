using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Tests;

/// <summary>Tests of measuring text and of laying it out into wrapped, aligned lines.</summary>
/// <remarks>
/// The expected numbers are the Helvetica AFM widths and metrics in 1/1000 em — <c>H</c> is 722,
/// <c>e</c> is 556, a space is 278, the ascent is 718 and the descent is -207 — so an assertion that
/// passes here passes because the engine read the published data, not because it agreed with itself.
/// </remarks>
public sealed partial class PdfDocumentBuilderTests
{
    /// <summary>A standard font reports the metrics its AFM file states, in 1/1000 em.</summary>
    [Fact]
    public void Reports_the_metrics_of_a_standard_font()
    {
        var metrics = Helvetica.Metrics;

        Assert.Equal(718d, metrics.Ascent);
        Assert.Equal(-207d, metrics.Descent);
        Assert.Equal(0d, metrics.LineGap);
        Assert.Equal(718d, metrics.CapHeight);
        Assert.Equal(523d, metrics.XHeight);
        Assert.Equal(1000, metrics.UnitsPerEm);
        Assert.Equal(11.1, metrics.LineHeight(12), 6);
    }

    /// <summary>A measurement is the sum of the advance widths of the characters, at the size asked for.</summary>
    [Fact]
    public void Measures_a_standard_font_line_with_the_afm_widths()
    {
        Assert.Equal(27.336, Helvetica.Measure("Hello", 12), 6);
        Assert.Equal(42.012, Helvetica.Measure("Veuillez", 12), 6);
        Assert.Equal(3.336, Helvetica.Measure(" ", 12), 6);
        Assert.Equal(0d, Helvetica.Measure(string.Empty, 12));
        Assert.Equal(54.672, Helvetica.Measure("Hello", 24), 6);
    }

    /// <summary>Character and word spacing are added the way the writer sets them.</summary>
    [Fact]
    public void Adds_the_text_state_spacing_to_a_measurement()
    {
        var plain = Helvetica.Measure("a b", 12);

        Assert.Equal(plain + 2.5, Helvetica.Measure("a b", 12, 0.5, 1), 6);
        Assert.Equal(plain, Helvetica.Measure("a b", 12, 0d, 0d), 6);
    }

    /// <summary>A character the encoding cannot express is refused rather than measured as a guess.</summary>
    [Fact]
    public void Refuses_to_measure_text_a_standard_font_cannot_encode()
    {
        var thrown = Assert.Throws<ArgumentException>(() => Helvetica.Measure("\u2603", 12));

        Assert.Contains("WinAnsiEncoding", thrown.Message);
        Assert.Contains("U+2603", thrown.Message);
    }

    /// <summary>A size or a spacing that is not a number is refused before a width is computed.</summary>
    [Fact]
    public void Refuses_a_measurement_at_a_size_that_is_not_a_size()
    {
        Assert.Throws<ArgumentNullException>(() => Helvetica.Measure(null!, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => Helvetica.Measure("a", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Helvetica.Measure("a", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Helvetica.Measure("a", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Helvetica.Measure("a", double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Helvetica.Measure("a", 12, double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Helvetica.Measure("a", 12, 0, double.NaN));
    }

    /// <summary>An embedded face reports its own metrics and units, and covers what its program covers.</summary>
    /// <remarks>
    /// The numbers are what fontTools reads from the same file — <c>head.unitsPerEm</c> is 2048,
    /// <c>hhea.ascent</c> 1901, <c>hhea.descent</c> -483, <c>hhea.lineGap</c> 0, and the widths of
    /// "Veuillez" add up to 8001 units — so a parse that drifted from the program would fail here.
    /// The subset carries no <c>OS/2</c> cap or x height, so those come from the outline of H and x.
    /// </remarks>
    [Fact]
    public void Measures_an_embedded_face_with_its_own_metrics()
    {
        var metrics = EmbeddedFace.Metrics;

        Assert.Equal(2048, metrics.UnitsPerEm);
        Assert.Equal(1901d, metrics.Ascent);
        Assert.Equal(-483d, metrics.Descent);
        Assert.Equal(0d, metrics.LineGap);
        Assert.InRange(metrics.CapHeight, 1400d, 1500d);
        Assert.InRange(metrics.XHeight, 1080d, 1150d);
        Assert.Equal(8001d / 2048 * 12, EmbeddedFace.Measure("Veuillez", 12), 9);
        Assert.Equal(0d, EmbeddedFace.Measure(string.Empty, 12));

        var thrown = Assert.Throws<ArgumentException>(() => EmbeddedFace.Measure("\u2603", 12));
        Assert.Contains("no glyph", thrown.Message);
    }

    /// <summary>Words are added to a line until the next one would overflow, and no line exceeds the width.</summary>
    [Fact]
    public void Wraps_a_paragraph_greedily_to_the_width()
    {
        var style = new PdfTextStyle(Helvetica, 12);
        var width = Helvetica.Measure("alpha beta", 12) + 0.001;

        var layout = PdfTextLayouter.Layout("alpha beta gamma delta", style, width);

        Assert.Equal(3, layout.LineCount);
        Assert.Equal(["alpha beta", "gamma", "delta"], layout.Lines.Select(line => line.Text).ToArray());
        Assert.All(layout.Lines, line => Assert.True(line.Width <= width, $"{line.Text} is {line.Width} wide."));
        Assert.Equal(width, layout.Bounds.Width, 6);
    }

    /// <summary>A line break is kept, an empty paragraph is a line of its own, and runs of spaces collapse.</summary>
    [Fact]
    public void Keeps_line_breaks_and_empty_paragraphs()
    {
        var layout = PdfTextLayouter.Layout("one two\r\n\r\nthree   four", new PdfTextStyle(Helvetica, 12), 200);

        Assert.Equal(3, layout.LineCount);
        Assert.Equal("one two", layout.Lines[0].Text);
        Assert.Equal(string.Empty, layout.Lines[1].Text);
        Assert.Equal(0d, layout.Lines[1].Width);
        Assert.Equal("three four", layout.Lines[2].Text);
    }

    /// <summary>An empty string is one empty line, not a layout without lines.</summary>
    [Fact]
    public void Lays_out_an_empty_string_as_one_line()
    {
        var layout = PdfTextLayouter.Layout(string.Empty, new PdfTextStyle(Helvetica, 12), 200);

        Assert.Equal(1, layout.LineCount);
        Assert.Equal(string.Empty, layout.Lines[0].Text);
        Assert.Equal(200d, layout.Bounds.Width, 6);
    }

    /// <summary>Each alignment starts the line where the width left over puts it.</summary>
    /// <param name="alignment">The alignment the line is placed at.</param>
    /// <param name="start">The expected x coordinate of the line in the box.</param>
    [Theory]
    [InlineData(PdfTextAlignment.Left, 0d)]
    [InlineData(PdfTextAlignment.Center, 21.98d)]
    [InlineData(PdfTextAlignment.Right, 43.96d)]
    public void Places_a_line_at_the_alignment_it_was_given(PdfTextAlignment alignment, double start)
    {
        var style = new PdfTextStyle(Helvetica, 12);
        var options = new PdfTextLayoutOptions(100, alignment);

        var layout = PdfTextLayouter.Layout("alpha beta", style, options);

        Assert.Equal(1, layout.LineCount);
        Assert.Equal(56.04, layout.Lines[0].Width, 6);
        Assert.Equal(start, layout.Lines[0].Start.X, 6);
    }

    /// <summary>A justified line is stretched to the width with the slack in its gaps; the last one is not.</summary>
    [Fact]
    public void Stretches_the_lines_of_a_justified_paragraph()
    {
        var style = new PdfTextStyle(Helvetica, 12);
        var options = new PdfTextLayoutOptions(100, PdfTextAlignment.Justify);

        var layout = PdfTextLayouter.Layout("alpha beta gamma delta", style, options);

        Assert.Equal(2, layout.LineCount);

        var stretched = layout.Lines[0];
        Assert.Equal("alpha beta gamma", stretched.Text);
        Assert.Equal(100d, stretched.Width, 6);
        Assert.Equal(3, stretched.Runs.Count);
        Assert.Equal("alpha ", stretched.Runs[0].Text);
        Assert.Equal(0d, stretched.Runs[0].Start.X, 6);
        Assert.Equal(32.996, stretched.Runs[1].Start.X, 6);
        Assert.Equal(100d, stretched.Runs[2].Start.X + stretched.Runs[2].Width, 6);

        var last = layout.Lines[1];
        Assert.Equal("delta", last.Text);
        Assert.Equal(26.016, last.Width, 6);
        Assert.Single(last.Runs);
    }

    /// <summary>The first baseline is one ascent down, and every later one a line height below it.</summary>
    [Fact]
    public void Steps_the_baselines_by_the_line_height()
    {
        var style = new PdfTextStyle(Helvetica, 12, leading: 20);

        var layout = PdfTextLayouter.Layout("one\ntwo\nthree", style, 200);

        Assert.Equal(20d, layout.LineHeight, 6);
        Assert.Equal(8.616, layout.Baseline, 6);
        Assert.Equal([8.616, 28.616, 48.616], layout.Lines.Select(line => Math.Round(line.Baseline, 3)).ToArray());
        Assert.Equal(51.1, layout.Bounds.Height, 6);
    }

    /// <summary>Without a leading, the height of a line is the font's ascent, descent and line gap.</summary>
    [Fact]
    public void Defaults_the_line_height_to_the_font_metrics()
    {
        var layout = PdfTextLayouter.Layout("one\ntwo\nthree", new PdfTextStyle(Helvetica, 12), 200);

        Assert.Equal(11.1, layout.LineHeight, 6);
        Assert.Equal(30.816, layout.Lines[2].Baseline, 6);
        Assert.Equal(33.3, layout.Bounds.Height, 6);
    }

    /// <summary>An explicit line height wins over the leading the style states.</summary>
    [Fact]
    public void Takes_the_line_height_from_the_options_when_it_states_one()
    {
        var style = new PdfTextStyle(Helvetica, 12, leading: 20);
        var options = new PdfTextLayoutOptions(200, lineHeight: 15);

        var layout = PdfTextLayouter.Layout("one\ntwo", style, options);

        Assert.Equal(15d, layout.LineHeight, 6);
        Assert.Equal(23.616, layout.Lines[1].Baseline, 6);
    }

    /// <summary>A word that cannot fit is refused with the width it needed and the width it had.</summary>
    [Fact]
    public void Refuses_a_word_that_cannot_fit()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => PdfTextLayouter.Layout("alpha", new PdfTextStyle(Helvetica, 12), 10));

        Assert.Contains("alpha", thrown.Message);
        Assert.Contains("wider than the", thrown.Message);
    }

    /// <summary>Words are broken into pieces that fit when the options allow it.</summary>
    [Fact]
    public void Breaks_a_word_into_pieces_that_fit()
    {
        var options = new PdfTextLayoutOptions(10, breakWords: true);

        var layout = PdfTextLayouter.Layout("alpha", new PdfTextStyle(Helvetica, 12), options);

        Assert.Equal(["al", "p", "h", "a"], layout.Lines.Select(line => line.Text).ToArray());
        Assert.Equal("alpha", string.Concat(layout.Lines.Select(line => line.Text)));
        Assert.All(layout.Lines, line => Assert.True(line.Width <= 10));
    }

    /// <summary>Without breaking, a word too wide is cut at the width or left to overflow.</summary>
    [Fact]
    public void Clips_or_continues_a_word_too_wide_to_break()
    {
        var style = new PdfTextStyle(Helvetica, 12);
        var clip = new PdfTextLayoutOptions(10, overflow: PdfTextOverflow.Clip);
        var keep = new PdfTextLayoutOptions(10, overflow: PdfTextOverflow.Continue);

        Assert.Equal("al", PdfTextLayouter.Layout("alpha", style, clip).Lines[0].Text);
        Assert.Equal("alpha", PdfTextLayouter.Layout("alpha", style, keep).Lines[0].Text);
    }

    /// <summary>Breaking a word cannot help when a single character is wider than the line.</summary>
    [Fact]
    public void Refuses_a_character_wider_than_the_line()
    {
        var options = new PdfTextLayoutOptions(5, breakWords: true);

        var thrown = Assert.Throws<ArgumentException>(
            () => PdfTextLayouter.Layout("aaa", new PdfTextStyle(Helvetica, 12), options));

        Assert.Contains("'a'", thrown.Message);
        Assert.Contains("wider than the", thrown.Message);
    }

    /// <summary>Arguments that describe no box are refused before any text is measured.</summary>
    [Fact]
    public void Refuses_a_layout_without_a_box()
    {
        var style = new PdfTextStyle(Helvetica, 12);

        Assert.Throws<ArgumentNullException>(() => PdfTextLayouter.Layout(null!, style, 100));
        Assert.Throws<ArgumentNullException>(() => PdfTextLayouter.Layout("text", null!, 100));
        Assert.Throws<ArgumentNullException>(() => PdfTextLayouter.Layout("text", style, (PdfTextLayoutOptions)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfTextLayouter.Layout("text", style, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfTextLayouter.Layout("text", style, double.NaN));
    }

    /// <summary>A right-to-left paragraph is refused rather than drawn backwards.</summary>
    [Fact]
    public void Refuses_a_right_to_left_paragraph()
    {
        var options = new PdfTextLayoutOptions(200, direction: PdfTextDirection.RightToLeft);

        var thrown = Assert.Throws<PdfUnsupportedFeatureException>(
            () => PdfTextLayouter.Layout("text", new PdfTextStyle(Helvetica, 12), options));

        Assert.Contains("right-to-left", thrown.Message);
    }

    /// <summary>A layout built here is a layout the page can draw, and the lines read back.</summary>
    [Fact]
    public async Task Draws_the_layout_the_layouter_built()
    {
        var style = new PdfTextStyle(Helvetica, 12, leading: 15);
        var layout = PdfTextLayouter.Layout("alpha beta gamma delta", style, 100);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawTextLayout(layout, new PdfPoint(72, 100)))
            .ToArray();

        var text = PlainText(await TextAsync(bytes));

        Assert.Contains("alpha beta gamma", text);
        Assert.Contains("delta", text);
    }

    /// <summary>
    /// The origin is the top-left corner of the box: the first baseline sits one ascent below it and each
    /// later line a line height below that, so a laid-out block lands exactly where the same baselines
    /// drawn one by one with <see cref="PdfPageCanvas.DrawText"/> land, rather than a box height away.
    /// </summary>
    [Fact]
    public async Task Draws_the_layout_from_the_top_left_of_its_box()
    {
        var style = new PdfTextStyle(Helvetica, 12, leading: 15);
        var layout = PdfTextLayouter.Layout("one\ntwo\nthree", style, 60);

        var laidOut = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawTextLayout(layout, new PdfPoint(72, 100)))
            .ToArray();

        var byHand = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page =>
            {
                foreach (var line in layout.Lines)
                {
                    page.DrawText(line.Text, 72, 100 + line.Baseline, Helvetica, 12);
                }
            })
            .ToArray();

        var actual = await TextAsync(laidOut);
        var expected = await TextAsync(byHand);

        Assert.Equal(expected.Items.Select(block => block.Text), actual.Items.Select(block => block.Text));
        Assert.Equal(expected.Items.Count, actual.Items.Count);

        for (var index = 0; index < expected.Items.Count; index++)
        {
            Assert.Equal(expected.Items[index].BoundingBox.Left, actual.Items[index].BoundingBox.Left, 0.001d);
            Assert.Equal(expected.Items[index].BoundingBox.Bottom, actual.Items[index].BoundingBox.Bottom, 0.001d);
            Assert.Equal(expected.Items[index].BoundingBox.Right, actual.Items[index].BoundingBox.Right, 0.001d);
            Assert.Equal(expected.Items[index].BoundingBox.Top, actual.Items[index].BoundingBox.Top, 0.001d);
        }

        // The reader states page space, y up, so the block starts just under 100 points below the top of
        // the page. The defect this guards against put the text a whole box height, 71 points, higher.
        var top = actual.Items.Max(block => block.BoundingBox.Top);
        Assert.InRange(PdfPageSize.A4.HeightPoints - top, 95d, 105d);
    }
}
