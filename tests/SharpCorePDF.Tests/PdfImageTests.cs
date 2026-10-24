using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of <see cref="PdfImage"/>: the format a signature states, what the reader reports about a
/// file, and the files it refuses rather than showing wrongly.
/// </summary>
/// <remarks>
/// An image is opened before a page draws it, so every assertion here is about the report a caller
/// sees first. What the writer makes of the prepared content is in
/// <c>PdfDocumentBuilderTests.Images.cs</c>.
/// </remarks>
public sealed class PdfImageTests
{
    private static readonly (byte Red, byte Green, byte Blue)[] Palette =
        [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)];

    /// <summary>A JPEG is reported with the size, the colour space and the sample depth of its frame.</summary>
    [Fact]
    public void Reads_a_jpeg_and_reports_its_frame()
    {
        var image = PdfImage.Open(TestJpeg.Baseline(40, 30));

        Assert.Equal(PdfImageFormat.Jpeg, image.Format);
        Assert.Equal(40, image.WidthPixels);
        Assert.Equal(30, image.HeightPixels);
        Assert.Equal(PdfImageColorSpace.Rgb, image.ColorSpace);
        Assert.Equal(8, image.BitsPerComponent);
        Assert.Equal(1, image.FrameCount);
        Assert.False(image.HasAlpha);

        // A JPEG without a density is drawn at one point per pixel.
        Assert.Equal(40d, image.NaturalWidth);
        Assert.Equal(30d, image.NaturalHeight);
    }

    /// <summary>A BMP is reported as RGB samples, whatever depth and row order the file states.</summary>
    [Fact]
    public void Reads_a_bmp_and_reports_its_samples()
    {
        var image = PdfImage.Open(TestBmp.TrueColor(4, 3, (_, _) => (10, 20, 30), bitsPerPixel: 32));

        Assert.Equal(PdfImageFormat.Bmp, image.Format);
        Assert.Equal(4, image.WidthPixels);
        Assert.Equal(3, image.HeightPixels);
        Assert.Equal(PdfImageColorSpace.Rgb, image.ColorSpace);
        Assert.Equal(8, image.BitsPerComponent);
        Assert.False(image.HasAlpha);
    }

    /// <summary>A GIF is reported as RGB samples with no resolution, because the format states none.</summary>
    [Fact]
    public void Reads_a_gif_and_reports_its_samples()
    {
        var image = PdfImage.Open(TestGif.Still(4, 3, (x, y) => (byte)((x + y) % 4), Palette));

        Assert.Equal(PdfImageFormat.Gif, image.Format);
        Assert.Equal(4, image.WidthPixels);
        Assert.Equal(3, image.HeightPixels);
        Assert.Equal(PdfImageColorSpace.Rgb, image.ColorSpace);
        Assert.Equal(8, image.BitsPerComponent);
        Assert.Equal(1, image.FrameCount);
        Assert.False(image.HasAlpha);
        Assert.Equal(4d, image.NaturalWidth);
        Assert.Equal(3d, image.NaturalHeight);
    }

    /// <summary>A PNG keeps its own format, so the report names the file the caller opened.</summary>
    [Fact]
    public void Reads_a_png_and_names_its_format()
    {
        var image = PdfImage.Open(TestPng.Rgb(6, 5, (_, _) => ((byte)1, (byte)2, (byte)3)));

        Assert.Equal(PdfImageFormat.Png, image.Format);
        Assert.Equal(6, image.WidthPixels);
        Assert.Equal(5, image.HeightPixels);
        Assert.Equal(PdfImageColorSpace.Rgb, image.ColorSpace);
    }

    /// <summary>An animation states how many frames it holds, of which one is drawn.</summary>
    [Fact]
    public void Reports_how_many_frames_a_gif_holds()
    {
        var still = PdfImage.Open(TestGif.Still(4, 3, (_, _) => (byte)0, Palette));
        var animation = PdfImage.Open(TestGif.Animated(4, 3, (_, _) => (byte)0, Palette, frames: 3));

        Assert.Equal(1, still.FrameCount);
        Assert.Equal(3, animation.FrameCount);
        Assert.Equal(4, animation.WidthPixels);
    }

    /// <summary>A graphic control that names a transparent palette entry reports transparency.</summary>
    [Fact]
    public void Reports_the_transparency_a_gif_states()
    {
        var opaque = PdfImage.Open(TestGif.Still(4, 3, (_, _) => (byte)0, Palette));
        var transparent = PdfImage.Open(
            TestGif.Transparent(4, 3, (_, _) => (byte)0, Palette, transparentIndex: 0));

        Assert.False(opaque.HasAlpha);
        Assert.True(transparent.HasAlpha);
    }

    /// <summary>A JFIF density in dots per inch becomes the natural size in points.</summary>
    [Fact]
    public void Uses_the_jfif_density_of_a_jpeg_for_its_natural_size()
    {
        var image = PdfImage.Open(TestJpeg.Baseline(152, 87, dpi: 300));

        Assert.Equal(152 * 72d / 300d, image.NaturalWidth, 0.001d);
        Assert.Equal(87 * 72d / 300d, image.NaturalHeight, 0.001d);
    }

    /// <summary>The pixels-per-metre field of a BMP becomes the natural size in points.</summary>
    [Fact]
    public void Uses_the_pixels_per_metre_of_a_bmp_for_its_natural_size()
    {
        var image = PdfImage.Open(TestBmp.TrueColor(152, 87, (_, _) => (0, 0, 0), pixelsPerMeter: 11811));

        Assert.Equal(152 * 72d / 300d, image.NaturalWidth, 0.01d);
        Assert.Equal(87 * 72d / 300d, image.NaturalHeight, 0.01d);
    }

    /// <summary>Bytes of no known format are refused by naming the formats that would be read.</summary>
    [Fact]
    public void Refuses_bytes_whose_signature_is_no_known_format()
    {
        var error = Assert.Throws<ArgumentException>(
            () => PdfImage.Open(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }));

        Assert.Contains("PNG, JPEG, BMP or GIF", error.Message);
    }

    /// <summary>A file read as a format its signature contradicts is refused rather than guessed at.</summary>
    [Fact]
    public void Refuses_a_file_that_is_not_the_format_the_caller_stated()
    {
        var png = TestPng.Rgb(2, 2, (_, _) => ((byte)0, (byte)0, (byte)0));
        var jpeg = TestJpeg.Baseline(2, 2);

        Assert.Throws<ArgumentException>(() => PdfImage.FromJpeg(png));
        Assert.Throws<ArgumentException>(() => PdfImage.FromBmp(jpeg));
        Assert.Throws<ArgumentException>(() => PdfImage.FromGif(jpeg));
        Assert.Throws<ArgumentException>(() => PdfImage.FromPng(jpeg));
    }

    /// <summary>Two opens of one file share their key, and a different picture does not.</summary>
    [Fact]
    public void Keys_two_opens_of_one_file_alike()
    {
        var file = TestGif.Still(4, 3, (x, _) => (byte)(x % 4), Palette);
        var other = TestGif.Still(4, 3, (_, _) => (byte)1, Palette);

        Assert.Equal(64, PdfImage.Open(file).Key.Length);
        Assert.Equal(PdfImage.Open(file).Key, PdfImage.Open(file).Key);
        Assert.NotEqual(PdfImage.Open(file).Key, PdfImage.Open(other).Key);
    }

    /// <summary>A JPEG whose EXIF orientation is not the identity is refused, because the bytes cannot be turned.</summary>
    [Fact]
    public void Refuses_a_jpeg_stored_with_an_exif_orientation()
    {
        var error = Assert.Throws<PdfUnsupportedFeatureException>(
            () => PdfImage.Open(TestJpeg.WithOrientation(6, 40, 30)));

        Assert.Equal("JPEG EXIF orientation", error.Feature);
        Assert.Contains("rotated by 90 degrees clockwise", error.Message);
    }

    /// <summary>A frame that DCTDecode does not read is refused by naming the coding it states.</summary>
    [Fact]
    public void Refuses_a_jpeg_coded_in_a_frame_pdf_does_not_decode()
    {
        var error = Assert.Throws<PdfUnsupportedFeatureException>(
            () => PdfImage.Open(TestJpeg.WithFrame(0xC9, 40, 30)));

        Assert.Equal("JPEG frame type", error.Feature);
        Assert.Contains("arithmetic coded", error.Message);
    }

    /// <summary>Sixteen bit samples would be shown as a mess, so the precision is refused rather than truncated.</summary>
    [Fact]
    public void Refuses_a_jpeg_that_is_not_eight_bits()
    {
        var error = Assert.Throws<PdfUnsupportedFeatureException>(
            () => PdfImage.Open(TestJpeg.WithPrecision(12, 40, 30)));

        Assert.Equal("JPEG sample precision", error.Feature);
        Assert.Contains("12 bits per sample", error.Message);
    }

    /// <summary>PDF names one, three and four component JPEGs; anything else is refused.</summary>
    [Fact]
    public void Refuses_a_jpeg_with_more_components_than_pdf_names()
    {
        var error = Assert.Throws<NotSupportedException>(
            () => PdfImage.Open(TestJpeg.Baseline(40, 30, components: 5)));

        Assert.Contains("5 colour components", error.Message);
    }

    /// <summary>Four components are only readable as CMYK or YCCK, which an Adobe segment states.</summary>
    [Fact]
    public void Refuses_a_four_component_jpeg_that_states_no_adobe_transform()
    {
        var error = Assert.Throws<PdfUnsupportedFeatureException>(
            () => PdfImage.Open(TestJpeg.Baseline(40, 30, components: 4)));

        Assert.Equal("JPEG colour transform", error.Feature);
    }

    /// <summary>The component count of a frame decides the colour space the report names.</summary>
    [Fact]
    public void Reports_the_colour_space_of_a_one_or_four_component_jpeg()
    {
        var grey = PdfImage.Open(TestJpeg.Baseline(40, 30, components: 1));
        var cmyk = PdfImage.Open(TestJpeg.Baseline(40, 30, components: 4, adobeTransform: 2));

        Assert.Equal(PdfImageColorSpace.Gray, grey.ColorSpace);
        Assert.Equal(PdfImageColorSpace.Cmyk, cmyk.ColorSpace);
    }

    /// <summary>A depth the reader does not expand is refused by name, before any sample is read.</summary>
    [Fact]
    public void Refuses_a_bmp_that_states_a_depth_it_does_not_read()
    {
        var bmp = TestBmp.TrueColor(2, 2, (_, _) => (0, 0, 0));

        // The depth is the sixteen bit field of the information header, at offset 28 of the file.
        bmp[28] = 16;

        var error = Assert.Throws<PdfUnsupportedFeatureException>(() => PdfImage.Open(bmp));

        Assert.Equal("BMP pixel depth", error.Feature);
        Assert.Contains("16 bits per pixel", error.Message);
    }

    /// <summary>A coded BMP is refused by naming the coding, because none of them is decoded.</summary>
    [Fact]
    public void Refuses_a_bmp_that_states_a_compression_it_does_not_read()
    {
        var bmp = TestBmp.TrueColor(2, 2, (_, _) => (0, 0, 0));

        // The compression method is the thirty-two bit field at offset 30, and one states BI_RLE8.
        bmp[30] = 1;

        var error = Assert.Throws<PdfUnsupportedFeatureException>(() => PdfImage.Open(bmp));

        Assert.Equal("BMP compression", error.Feature);
        Assert.Contains("BI_RLE8", error.Message);
    }
}
