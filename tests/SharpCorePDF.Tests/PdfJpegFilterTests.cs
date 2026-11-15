using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the baseline JPEG decoder against strips Pillow wrote and decoded itself: the samples the
/// decoder states must be the samples Pillow states, to within the two levels a different IDCT may move.
/// </summary>
public sealed class PdfJpegFilterTests
{
    private static readonly byte[] Rgb = File.ReadAllBytes(Fixture("jpeg-444.jpg"));
    private static readonly byte[] RgbExpected = File.ReadAllBytes(Fixture("jpeg-444-expected.bin"));
    private static readonly byte[] Gray = File.ReadAllBytes(Fixture("jpeg-gray.jpg"));
    private static readonly byte[] GrayExpected = File.ReadAllBytes(Fixture("jpeg-gray-expected.bin"));
    private static readonly byte[] Rgb420 = File.ReadAllBytes(Fixture("jpeg-420.jpg"));
    private static readonly byte[] Rgb420Expected = File.ReadAllBytes(Fixture("jpeg-420-expected.bin"));
    private static readonly byte[] Cmyk = File.ReadAllBytes(Fixture("jpeg-cmyk.jpg"));
    private static readonly byte[] CmykExpected = File.ReadAllBytes(Fixture("jpeg-cmyk-expected.bin"));
    private static readonly byte[] Restart = File.ReadAllBytes(Fixture("jpeg-restart.jpg"));
    private static readonly byte[] RestartExpected = File.ReadAllBytes(Fixture("jpeg-restart-expected.bin"));
    private static readonly byte[] Progressive = File.ReadAllBytes(Fixture("jpeg-progressive.jpg"));
    private static readonly byte[] ProgressiveExpected = File.ReadAllBytes(Fixture("jpeg-progressive-expected.bin"));
    private static readonly byte[] Progressive420 = File.ReadAllBytes(Fixture("jpeg-progressive-420.jpg"));
    private static readonly byte[] Progressive420Expected = File.ReadAllBytes(Fixture("jpeg-progressive-420-expected.bin"));
    private static readonly byte[] ProgressiveGray = File.ReadAllBytes(Fixture("jpeg-progressive-gray.jpg"));
    private static readonly byte[] ProgressiveGrayExpected = File.ReadAllBytes(Fixture("jpeg-progressive-gray-expected.bin"));
    private static readonly byte[] ProgressiveCmyk = File.ReadAllBytes(Fixture("jpeg-progressive-cmyk.jpg"));
    private static readonly byte[] ProgressiveCmykExpected = File.ReadAllBytes(Fixture("jpeg-progressive-cmyk-expected.bin"));
    private static readonly byte[] ProgressiveWide = File.ReadAllBytes(Fixture("jpeg-progressive-wide.jpg"));
    private static readonly byte[] ProgressiveWideExpected = File.ReadAllBytes(Fixture("jpeg-progressive-wide-expected.bin"));
    private static readonly byte[] ProgressiveSparse = File.ReadAllBytes(Fixture("jpeg-progressive-sparse.jpg"));
    private static readonly byte[] ProgressiveSparseExpected = File.ReadAllBytes(Fixture("jpeg-progressive-sparse-expected.bin"));
    private static readonly byte[] ProgressiveRestart = File.ReadAllBytes(Fixture("jpeg-progressive-restart.jpg"));
    private static readonly byte[] ProgressiveRestartExpected = File.ReadAllBytes(Fixture("jpeg-progressive-restart-expected.bin"));

    private static string Fixture(string name) => Path.Combine(TestAssets.Folder, "jpeg", name);

    /// <summary>A 4:4:4 RGB JPEG decodes to the bytes Pillow decodes from the same file.</summary>
    [Fact]
    public void Decodes_a_baseline_rgb_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(Rgb, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.NotNull(pixels);
        Assert.Equal(RgbExpected.Length, pixels!.Length);
        Assert.InRange(MaxDifference(pixels, RgbExpected), 0, 2);
    }

    /// <summary>One component decodes as Gray8, with the same tolerance.</summary>
    [Fact]
    public void Decodes_a_baseline_gray_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(Gray, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Gray8", format);
        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.NotNull(pixels);
        Assert.Equal(GrayExpected.Length, pixels!.Length);
        Assert.InRange(MaxDifference(pixels, GrayExpected), 0, 2);
    }

    /// <summary>
    /// A 4:2:0 JPEG halves the chroma in both directions; the decoder upsamples with the same 3:1 filter
    /// libjpeg (and so Pillow) uses, which keeps the comparison inside the two-level tolerance.
    /// </summary>
    [Fact]
    public void Decodes_a_subsampled_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(Rgb420, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, Rgb420Expected), 0, 2);
    }

    /// <summary>
    /// Four components are CMYK, one byte each. A JPEG stores them inverted — 0 is full ink, which is what
    /// the image's <c>/Decode</c> array states — while Pillow converts them to the conventional 0 = no ink,
    /// so the comparison inverts Pillow's bytes.
    /// </summary>
    [Fact]
    public void Decodes_a_cmyk_jpeg_to_four_components()
    {
        Assert.True(JpegDecoder.TryDecode(Cmyk, 1 << 20, out var pixels, out var format, out var width, out var height));

        var expected = CmykExpected.Select(value => (byte)(255 - value)).ToArray();

        Assert.Equal("Cmyk32", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, expected), 0, 2);
    }

    /// <summary>Restart markers reset the predictors, and the decode continues after each of them.</summary>
    [Fact]
    public void Decodes_a_jpeg_with_restart_markers()
    {
        Assert.True(JpegDecoder.TryDecode(Restart, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, RestartExpected), 0, 2);
    }

    /// <summary>A progressive JPEG states its coefficients across several scans and refines them.</summary>
    [Fact]
    public void Decodes_a_progressive_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(Progressive, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, ProgressiveExpected), 0, 2);
    }

    /// <summary>A progressive 4:2:0 JPEG interleaves the DC scan but not the chroma AC scans.</summary>
    [Fact]
    public void Decodes_a_subsampled_progressive_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(Progressive420, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, Progressive420Expected), 0, 2);
    }

    /// <summary>A single-component progressive JPEG refines one plane.</summary>
    [Fact]
    public void Decodes_a_grayscale_progressive_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(ProgressiveGray, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Gray8", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, ProgressiveGrayExpected), 0, 2);
    }

    /// <summary>Four components travel through the progressive scans and leave as inverted CMYK.</summary>
    [Fact]
    public void Decodes_a_progressive_cmyk_jpeg_to_four_components()
    {
        Assert.True(JpegDecoder.TryDecode(ProgressiveCmyk, 1 << 20, out var pixels, out var format, out var width, out var height));

        var expected = ProgressiveCmykExpected.Select(value => (byte)(255 - value)).ToArray();

        Assert.Equal("Cmyk32", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, expected), 0, 2);
    }

    /// <summary>A non-square image keeps the column and row counts apart in both grids.</summary>
    [Fact]
    public void Decodes_a_non_square_progressive_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(ProgressiveWide, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(32, width);
        Assert.Equal(16, height);
        Assert.InRange(MaxDifference(pixels!, ProgressiveWideExpected), 0, 2);
    }

    /// <summary>Flat areas produce EOB runs and zero runs an AC scan has to skip.</summary>
    [Fact]
    public void Decodes_a_sparse_progressive_jpeg_within_two_levels()
    {
        Assert.True(JpegDecoder.TryDecode(ProgressiveSparse, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, ProgressiveSparseExpected), 0, 2);
    }

    /// <summary>A restart interval inside a progressive scan resets the predictors and the EOB run.</summary>
    [Fact]
    public void Decodes_a_progressive_jpeg_with_restart_markers()
    {
        Assert.True(JpegDecoder.TryDecode(ProgressiveRestart, 1 << 20, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.InRange(MaxDifference(pixels!, ProgressiveRestartExpected), 0, 2);
    }

    /// <summary>A progressive stream that stops inside a scan is refused rather than decoded as far as it got.</summary>
    [Theory]
    [InlineData(50)]
    [InlineData(250)]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(450)]
    [InlineData(560)]
    [InlineData(620)]
    [InlineData(660)]
    [InlineData(700)]
    public void Refuses_a_truncated_progressive_jpeg(int length)
    {
        var truncated = Progressive.AsSpan(0, length).ToArray();

        Assert.False(JpegDecoder.TryDecode(truncated, 1 << 20, out _, out _, out _, out _));
    }

    /// <summary>A pixel buffer above the budget is measured and nothing is kept.</summary>
    [Fact]
    public void Measures_a_pixel_buffer_above_the_budget_without_keeping_it()
    {
        Assert.True(JpegDecoder.TryDecode(Rgb, 16, out var pixels, out var format, out var width, out var height));

        Assert.Equal("Rgb24", format);
        Assert.Equal(16, width);
        Assert.Null(pixels);
    }

    /// <summary>A stream that is not a JPEG is refused.</summary>
    [Fact]
    public void Refuses_a_stream_that_is_not_a_jpeg() =>
        Assert.False(JpegDecoder.TryDecode(new byte[] { 0, 0, 0, 0 }, 1 << 20, out _, out _, out _, out _));

    /// <summary>A JPEG that stops inside a segment is refused rather than decoded as far as it got.</summary>
    [Fact]
    public void Refuses_a_truncated_jpeg()
    {
        var truncated = Rgb.AsSpan(0, 24).ToArray();

        Assert.False(JpegDecoder.TryDecode(truncated, 1 << 20, out _, out _, out _, out _));
    }

    private static int MaxDifference(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected)
    {
        var maximum = 0;

        for (var index = 0; index < actual.Length; index++)
        {
            maximum = Math.Max(maximum, Math.Abs(actual[index] - expected[index]));
        }

        return maximum;
    }
}
