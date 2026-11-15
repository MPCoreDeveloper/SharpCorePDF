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
