using System.Text;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the image formats a page can draw beyond PNG: a JPEG passed through, and a BMP or a GIF
/// decoded and written as samples.
/// </summary>
/// <remarks>
/// Every assertion goes through the content reader, so what is checked is what a consumer of the
/// document sees: the filter, the colour space, the mask and the samples after decoding.
/// </remarks>
public sealed partial class PdfDocumentBuilderTests
{
    private static readonly (byte Red, byte Green, byte Blue)[] Swatches =
        [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)];

    /// <summary>Builds a one page document that draws the image at its own pixel size and reads it back.</summary>
    /// <param name="image">The bytes of the image.</param>
    /// <param name="width">The width to draw at, in points.</param>
    /// <param name="height">The height to draw at, in points.</param>
    /// <returns>The image the document stores.</returns>
    private static async Task<PdfImageInfo> DrawImageAsync(byte[] image, double width, double height)
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(image, 36, 36, width, height))
            .ToArray();

        return Assert.Single((await ImagesAsync(bytes)).Items);
    }

    /// <summary>A JPEG keeps its own bytes and is decoded by the DCTDecode filter.</summary>
    /// <remarks>
    /// Nothing is re-encoded, so the stream the document holds is the file byte for byte. The reader
    /// returns it as it is, because a JPEG is already a form a consumer reads.
    /// </remarks>
    [Fact]
    public async Task Writes_a_jpeg_without_re_encoding_it()
    {
        var jpeg = TestJpeg.Baseline(8, 4);

        var image = await DrawImageAsync(jpeg, 8, 4);

        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal("DCTDecode", image.Filter);
        Assert.True(image.IsDecodable);
        Assert.Null(image.Mask);
        Assert.NotNull(image.Data);
        Assert.Equal(jpeg, image.Data!.Value.ToArray());
    }

    /// <summary>Four component samples are inverted in the file, so the image states the flip PDF needs.</summary>
    [Fact]
    public async Task Writes_a_cmyk_jpeg_with_the_flip_pdf_needs()
    {
        var jpeg = TestJpeg.Baseline(8, 4, components: 4, adobeTransform: 2);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(jpeg, 36, 36, 8, 4))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);

        Assert.Equal("DeviceCMYK", image.ColorSpace);
        Assert.Equal("DCTDecode", image.Filter);
        Assert.Contains("/DeviceCMYK", Encoding.Latin1.GetString(bytes));
        Assert.Contains("/Decode [1 0 1 0 1 0 1 0]", Encoding.Latin1.GetString(bytes));
    }

    /// <summary>A greyscale JPEG keeps its one component colour space.</summary>
    [Fact]
    public async Task Writes_a_greyscale_jpeg_as_one_component_image()
    {
        var jpeg = TestJpeg.Baseline(8, 4, components: 1);

        var image = await DrawImageAsync(jpeg, 8, 4);

        Assert.Equal("DeviceGray", image.ColorSpace);
        Assert.Equal("DCTDecode", image.Filter);
    }

    /// <summary>A scale on the file becomes a wider natural size only when no size is given.</summary>
    [Fact]
    public async Task Uses_the_jfif_density_of_a_jpeg_for_its_natural_size()
    {
        var jpeg = TestJpeg.Baseline(152, 87, dpi: 300);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(jpeg, 40, 30))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);
        var area = Assert.IsType<PdfRectangle>(image.Position);

        Assert.Equal(152 * 72d / 300d, area.Right - area.Left, 0.01d);
        Assert.Equal(87 * 72d / 300d, area.Top - area.Bottom, 0.01d);
    }

    /// <summary>A BMP is decoded to RGB samples, and a bottom-up file is turned over.</summary>
    /// <remarks>
    /// A three row image is stored with its last row first, so the samples a reader gets back only come
    /// out in the order the picture states if the reader turned them over.
    /// </remarks>
    [Fact]
    public async Task Writes_a_bmp_with_its_rows_in_order()
    {
        var bmp = TestBmp.TrueColor(2, 3, (_, y) => ((byte)(10 + y), (byte)(20 + y), (byte)(30 + y)));

        var image = await DrawImageAsync(bmp, 2, 3);

        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal("FlateDecode", image.Filter);
        Assert.Null(image.Mask);
        Assert.NotNull(image.Data);
        Assert.Equal(
            [10, 20, 30, 10, 20, 30, 11, 21, 31, 11, 21, 31, 12, 22, 32, 12, 22, 32],
            image.Data!.Value.ToArray());
    }

    /// <summary>A top-down BMP keeps its rows, so both row orders describe the same picture.</summary>
    [Fact]
    public async Task Turns_a_top_down_bmp_the_same_way_up()
    {
        static (byte, byte, byte) Rows(int _, int y) => ((byte)(10 + y), (byte)(20 + y), (byte)(30 + y));

        var bottomUp = await DrawImageAsync(TestBmp.TrueColor(2, 3, Rows), 2, 3);
        var topDown = await DrawImageAsync(TestBmp.TrueColor(2, 3, Rows, topDown: true), 2, 3);

        Assert.Equal(bottomUp.Data!.Value.ToArray(), topDown.Data!.Value.ToArray());
    }

    /// <summary>A thirty-two bit BMP is read as RGB: its fourth byte is padding, not transparency.</summary>
    [Fact]
    public async Task Reads_a_thirty_two_bit_bmp_as_opaque_colour()
    {
        var bmp = TestBmp.TrueColor(2, 1, (x, _) => (x == 0 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255)), bitsPerPixel: 32);

        var image = await DrawImageAsync(bmp, 2, 1);

        Assert.Null(image.Mask);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal([255, 0, 0, 0, 0, 255], image.Data!.Value.ToArray());
    }

    /// <summary>The palette of an eight bit BMP is expanded in the order red, green and blue.</summary>
    [Fact]
    public async Task Expands_the_palette_of_a_bmp_to_rgb()
    {
        var bmp = TestBmp.Indexed(2, 1, (x, _) => (byte)(x == 0 ? 0 : 1), [(255, 0, 0), (0, 255, 0)]);

        var image = await DrawImageAsync(bmp, 2, 1);

        Assert.Equal(2, image.Width);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal([255, 0, 0, 0, 255, 0], image.Data!.Value.ToArray());
    }

    /// <summary>A GIF is decoded to RGB samples in the order the frame stores them.</summary>
    [Fact]
    public async Task Writes_a_gif_and_expands_its_palette()
    {
        var gif = TestGif.Still(2, 2, (x, y) => (byte)((y * 2) + x), Swatches);

        var image = await DrawImageAsync(gif, 2, 2);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal("FlateDecode", image.Filter);
        Assert.Null(image.Mask);
        Assert.Equal(
            [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255],
            image.Data!.Value.ToArray());
    }

    /// <summary>An interlaced GIF is put back into row order, so both storages describe one picture.</summary>
    /// <remarks>
    /// Five rows reach all four passes of the interlace: rows 0 and 4 are stored first, then row 2, then
    /// rows 1 and 3.
    /// </remarks>
    [Fact]
    public async Task Puts_an_interlaced_gif_back_into_row_order()
    {
        static byte Rows(int _, int y) => (byte)(y % 4);

        var still = await DrawImageAsync(TestGif.Still(2, 5, Rows, Swatches), 2, 5);
        var interlaced = await DrawImageAsync(TestGif.Interlaced(2, 5, Rows, Swatches), 2, 5);

        Assert.Equal(still.Data!.Value.ToArray(), interlaced.Data!.Value.ToArray());
        Assert.Equal(5, interlaced.Height);
        Assert.Equal([255, 0, 0], interlaced.Data!.Value.Span[..3].ToArray());
    }

    /// <summary>The transparent palette entry of a GIF becomes a soft mask, not a hole in the samples.</summary>
    [Fact]
    public async Task Turns_the_transparency_of_a_gif_into_a_soft_mask()
    {
        var gif = TestGif.Transparent(2, 1, (x, _) => (byte)(x == 0 ? 0 : 1), Swatches, transparentIndex: 0);

        var image = await DrawImageAsync(gif, 2, 1);
        var mask = Assert.IsType<PdfImageMask>(image.Mask);

        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal([255, 0, 0, 0, 255, 0], image.Data!.Value.ToArray());
        Assert.Equal(PdfImageMaskKind.SoftMask, mask.Kind);
        Assert.Equal("DeviceGray", mask.ColorSpace);
        Assert.NotNull(mask.Data);
        Assert.Equal([0, 255], mask.Data!.Value.ToArray());
    }

    /// <summary>PDF has no animation, so a page draws the first frame and nothing else.</summary>
    [Fact]
    public async Task Draws_only_the_first_frame_of_an_animation()
    {
        static byte Pixels(int x, int y) => (byte)((y * 2) + x);

        var still = await DrawImageAsync(TestGif.Still(2, 2, Pixels, Swatches), 2, 2);
        var animation = await DrawImageAsync(TestGif.Animated(2, 2, Pixels, Swatches, frames: 3), 2, 2);

        Assert.Equal(still.Data!.Value.ToArray(), animation.Data!.Value.ToArray());
        Assert.Equal(2, animation.Width);
        Assert.Equal(2, animation.Height);
    }

    /// <summary>Two opened images that hold the same picture are one object the document embeds once.</summary>
    /// <remarks>
    /// The registry keys the prepared content, so opening the same bytes twice does not double the
    /// document — which matters for a logo that every page of a statement draws.
    /// </remarks>
    [Fact]
    public async Task Embeds_one_image_object_for_a_file_drawn_twice()
    {
        var file = TestGif.Still(2, 2, (x, _) => (byte)(x % 4), Swatches);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(PdfImage.Open(file), 36, 36, 2, 2))
            .AddPage(PdfPageSize.A4, page => page.DrawImage(PdfImage.Open(file), 36, 36, 2, 2))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);

        Assert.Equal([1, 2], image.Pages);
    }

    /// <summary>The pixels-per-metre field of a BMP becomes the natural size in points.</summary>
    [Fact]
    public async Task Uses_the_pixels_per_metre_of_a_bmp_for_its_natural_size()
    {
        var bmp = TestBmp.TrueColor(152, 87, (_, _) => (0, 0, 0), pixelsPerMeter: 11811);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(bmp, 40, 30))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);
        var area = Assert.IsType<PdfRectangle>(image.Position);

        Assert.Equal(152 * 72d / 300d, area.Right - area.Left, 0.01d);
        Assert.Equal(87 * 72d / 300d, area.Top - area.Bottom, 0.01d);
    }

    /// <summary>A profile an <c>iCCP</c> chunk states becomes an ICCBased colour space, not a dropped chunk.</summary>
    /// <remarks>
    /// Leaving the samples as device values would show the picture through the viewer's own profile,
    /// which is a colour shift nobody is told about. The profile is written as its own Flate stream and
    /// the image names it, which is how PDF states colour the samples are measured in.
    /// </remarks>
    [Fact]
    public async Task Declares_the_icc_profile_a_png_states()
    {
        var profile = TestPng.Profile();
        var png = TestPng.Rgb(2, 2, (x, y) => ((byte)(40 + x), (byte)(90 + y), 160), iccChunk: TestPng.IccChunk(profile));

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 36, 36, 2, 2))
            .ToArray();

        var raw = Encoding.Latin1.GetString(bytes);

        Assert.Contains("/ICCBased", raw);
        Assert.Contains("/N 3", raw);
        Assert.Contains("/Alternate /DeviceRGB", raw);
        Assert.Contains(Encoding.Latin1.GetString(EmbeddedImage.Compress(profile)), raw);

        var image = Assert.Single((await ImagesAsync(bytes)).Items);

        Assert.Equal(2, image.Width);
        Assert.Equal("FlateDecode", image.Filter);
    }

    /// <summary>The reader keeps the profile the chunk carries, byte for byte.</summary>
    [Fact]
    public void Reads_the_profile_out_of_the_icc_chunk()
    {
        var profile = TestPng.Profile();
        var png = TestPng.Rgb(1, 1, (_, _) => (1, 2, 3), iccChunk: TestPng.IccChunk(profile));

        var image = PngImage.Parse(png);

        Assert.NotNull(image.IccProfile);
        Assert.Equal(profile, image.IccProfile!.Data);
        Assert.Equal(3, image.IccProfile.Components);
        Assert.Equal("DeviceRGB", image.IccProfile.Alternate);
        Assert.Equal(PdfImageColorSpace.IccBased, PdfImage.Open(png).ColorSpace);
    }

    /// <summary>A grey sample with a grey profile declares one component.</summary>
    [Fact]
    public void Reads_a_grey_profile_as_one_component()
    {
        var profile = TestPng.Profile("GRAY");
        var png = TestPng.Gray(1, 1, (_, _) => 64, iccChunk: TestPng.IccChunk(profile));

        var image = PngImage.Parse(png);

        Assert.NotNull(image.IccProfile);
        Assert.Equal(1, image.IccProfile!.Components);
        Assert.Equal("DeviceGray", image.IccProfile.Alternate);
    }

    /// <summary>A profile that describes another colour space than the samples is refused by name.</summary>
    [Fact]
    public void Refuses_a_profile_that_does_not_describe_the_samples()
    {
        var png = TestPng.Gray(1, 1, (_, _) => 64, iccChunk: TestPng.IccChunk(TestPng.Profile()));

        var exception = Assert.Throws<NotSupportedException>(() => PngImage.Parse(png));

        Assert.Contains("RGB", exception.Message);
    }

    /// <summary>An <c>iCCP</c> chunk that does not hold a profile is refused, not ignored.</summary>
    [Fact]
    public void Refuses_an_icc_chunk_that_is_not_a_profile()
    {
        var png = TestPng.Rgb(
            1,
            1,
            (_, _) => (1, 2, 3),
            iccChunk: TestPng.IccChunk(Encoding.ASCII.GetBytes("not a profile at all")));

        var exception = Assert.Throws<ArgumentException>(() => PngImage.Parse(png));

        Assert.Contains("acsp", exception.Message);
    }
}
