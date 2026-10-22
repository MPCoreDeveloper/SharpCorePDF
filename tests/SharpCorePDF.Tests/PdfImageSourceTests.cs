using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the image value: the metadata a writer needs and the validation that keeps a source from
/// claiming a shape its bytes cannot have.
/// </summary>
public sealed class PdfImageSourceTests
{
    private static readonly byte[] Bytes = [0xFF, 0xD8, 0xFF, 0xD9];

    /// <summary>A JPEG keeps its encoded bytes and names the filter that reads them.</summary>
    [Fact]
    public void Describes_a_jpeg_that_can_be_embedded_unchanged()
    {
        var image = new PdfJpegImage(
            Bytes,
            120,
            80,
            naturalWidthPoints: 60,
            naturalHeightPoints: 40);

        Assert.Equal(PdfImageFormat.Jpeg, image.Format);
        Assert.Equal(120, image.WidthPixels);
        Assert.Equal(80, image.HeightPixels);
        Assert.Equal(PdfImageColorSpace.Rgb, image.ColorSpace);
        Assert.Equal(8, image.BitsPerComponent);
        Assert.Equal("DCTDecode", image.Filter);
        Assert.True(image.IsPassthrough);
        Assert.False(image.HasAlpha);
        Assert.Equal(60, image.NaturalWidthPoints);
        Assert.Equal(40, image.NaturalHeightPoints);
        Assert.Equal(Bytes, image.Encoded.ToArray());
    }

    /// <summary>PNG, BMP and GIF are decoded before they are written, so they are not pass-through by default.</summary>
    [Fact]
    public void Describes_the_formats_pdf_cannot_embed_directly()
    {
        var png = new PdfPngImage(Bytes, 10, 20, hasAlpha: true);
        var bmp = new PdfBmpImage(Bytes, 30, 40);
        var gif = new PdfGifImage(Bytes, 50, 60);

        Assert.Equal(PdfImageFormat.Png, png.Format);
        Assert.Equal("FlateDecode", png.Filter);
        Assert.True(png.HasAlpha);
        Assert.False(png.IsPassthrough);

        Assert.Equal(PdfImageFormat.Bmp, bmp.Format);
        Assert.Equal("FlateDecode", bmp.Filter);

        Assert.Equal(PdfImageFormat.Gif, gif.Format);
        Assert.Equal("FlateDecode", gif.Filter);
        Assert.Null(gif.NaturalWidthPoints);
    }

    /// <summary>A source refuses dimensions, samples and sizes that PDF cannot represent.</summary>
    [Fact]
    public void Refuses_metadata_that_cannot_describe_an_image()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPngImage(Bytes, 0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPngImage(Bytes, 10, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPngImage(Bytes, 10, 20, bitsPerComponent: 3));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfPngImage(Bytes, 10, 20, colorSpace: (PdfImageColorSpace)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfPngImage(Bytes, 10, 20, naturalWidthPoints: double.NaN));
        Assert.Throws<ArgumentException>(() => new PdfGifImage(ReadOnlyMemory<byte>.Empty, 10, 20));
    }
}
