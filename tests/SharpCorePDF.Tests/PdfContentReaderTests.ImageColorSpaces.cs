using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the colour spaces an image may state: which of them the read model is willing to state as
/// pixels, what a sample of each one means, and what happens when the samples and the space disagree.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>A palette is applied, so the indices of a palette image become the colours it states.</summary>
    [Fact]
    public async Task Exposes_a_palette_image_as_the_colours_of_its_table()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 2, 2);

        Assert.Equal("Indexed DeviceRGB 3 reference", image.ColorSpace);
        Assert.Equal("Rgb24", image.PixelFormat);
        Assert.Equal(PdfContentFixtures.PaletteLookup, image.DecodedPixels!.Value.ToArray());

        // The palette is applied to the pixels, not to the payload: what the document stores stays what
        // the record hands over as Data.
        Assert.Equal(PdfContentFixtures.PaletteSamples, image.Data!.Value.ToArray());
    }

    /// <summary>A lookup table stored as a string is read the same way as one stored in a stream.</summary>
    [Fact]
    public async Task Exposes_a_palette_whose_table_is_a_string()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 3, 1);

        Assert.Equal("Gray8", image.PixelFormat);
        Assert.Equal(PdfContentFixtures.GrayPaletteLookup[..3], image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A one-bit image is widened to a byte per sample, and each row is read at its own padded length, so
    /// the second row does not continue inside the first.
    /// </summary>
    [Fact]
    public async Task Exposes_bilevel_samples_as_one_byte_per_sample()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 5, 2);

        Assert.Equal("Gray8", image.PixelFormat);
        Assert.Equal(PdfContentFixtures.BilevelSamples, image.Data!.Value.ToArray());
        Assert.Equal([255, 0, 255, 0, 0, 0, 255, 0, 255, 0], image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A four-bit sample is a level of sixteen, so widening it to eight bits steps by seventeen rather
    /// than by one, and each row is read at its own padded length so the second does not continue inside
    /// the first.
    /// </summary>
    [Fact]
    public async Task Exposes_four_bit_samples_at_the_scale_of_their_depth()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 3, 2);

        Assert.Equal("Gray8", image.PixelFormat);
        Assert.Equal(PdfContentFixtures.FourBitSamples, image.Data!.Value.ToArray());
        Assert.Equal([0, 255, 170, 17, 34, 51], image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A two-bit image stores five samples in the ten bits its row uses, and the six bits of padding are
    /// not samples: the pixels are the five the width states.
    /// </summary>
    [Fact]
    public async Task Exposes_two_bit_samples_at_the_scale_of_their_depth()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 5, 1);

        Assert.Equal("Gray8", image.PixelFormat);
        Assert.Equal(PdfContentFixtures.TwoBitSamples, image.Data!.Value.ToArray());
        Assert.Equal([0, 85, 170, 255, 85], image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// Device CMYK samples are exposed as they are stored: four device values per pixel and no ink
    /// conversion, because how cyan, magenta, yellow and black become light is a rendering decision.
    /// </summary>
    [Fact]
    public async Task Exposes_a_cmyk_image_as_four_device_samples_per_pixel()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 2, 1);

        Assert.Equal("Cmyk32", image.PixelFormat);
        Assert.Equal(PdfContentFixtures.CmykSamples, image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>An ICCBased image is read by the component count its profile states.</summary>
    [Fact]
    public async Task Exposes_the_samples_of_an_iccbased_image()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 1, 1);

        Assert.Equal("ICCBased reference", image.ColorSpace);
        Assert.Equal("Rgb24", image.PixelFormat);
        Assert.Equal(PdfContentFixtures.IccSamples, image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A Separation image states its colour through a tint transform rather than storing it, so its
    /// samples stay a payload: the record names the space instead of evaluating the function for it.
    /// </summary>
    [Fact]
    public async Task Does_not_expose_the_samples_of_a_separation_space()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 1, 2);

        Assert.Equal("Separation Spot DeviceCMYK reference", image.ColorSpace);
        Assert.True(image.IsDecodable);
        Assert.NotNull(image.Data);
        Assert.Null(image.PixelFormat);
        Assert.Null(image.DecodedPixels);
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.ImageDataUnreadable);
    }

    /// <summary>
    /// An index past the end of the lookup table means the samples and the space disagree, which is
    /// reported by leaving the pixels out rather than by painting an undefined colour.
    /// </summary>
    [Fact]
    public async Task Does_not_expose_a_palette_image_whose_index_is_outside_its_table()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 4, 1);

        Assert.True(image.IsDecodable);
        Assert.NotNull(image.Data);
        Assert.Null(image.PixelFormat);
        Assert.Null(image.DecodedPixels);
    }

    /// <summary>
    /// Widening samples multiplies the bytes, so an expansion that would pass the inline budget is left
    /// out of the record while the payload that fits it is still carried.
    /// </summary>
    [Fact]
    public async Task Leaves_an_expansion_above_the_budget_out_of_the_record()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var result = await Reader(new PdfEngineOptions { MaxInlineContentBytes = 8 })
            .ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 5, 2);

        Assert.Equal(2, image.Data!.Value.Length);
        Assert.Null(image.PixelFormat);
        Assert.Null(image.DecodedPixels);
    }

    /// <summary>
    /// The budget is on the bytes produced, so an expansion that lands exactly on it is kept and one byte
    /// less refuses it — and the count is every channel of every pixel, not the pixels divided by them.
    /// </summary>
    [Fact]
    public async Task Keeps_an_expansion_that_fits_the_budget_exactly()
    {
        var source = Source(PdfContentFixtures.ColorSpaces(), "colorspaces.pdf");

        var exact = await Reader(new PdfEngineOptions { MaxInlineContentBytes = 12 })
            .ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var pixels = Image(exact, 2, 2);

        Assert.Equal("Rgb24", pixels.PixelFormat);
        Assert.Equal(PdfContentFixtures.PaletteLookup, pixels.DecodedPixels!.Value.ToArray());

        var short0 = await Reader(new PdfEngineOptions { MaxInlineContentBytes = 11 })
            .ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var none = Image(short0, 2, 2);

        Assert.Equal(PdfContentFixtures.PaletteSamples, none.Data!.Value.ToArray());
        Assert.Null(none.PixelFormat);
        Assert.Null(none.DecodedPixels);
    }

    /// <summary>
    /// The calibrated forms of grey and RGB are the device spaces with a mapping applied, so their samples
    /// are exposed the way a device space's are.
    /// </summary>
    [Fact]
    public async Task Resolves_the_calibrated_forms_of_the_device_spaces()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var gray = Image(result, 2, 2);
        var rgb = Image(result, 3, 1);

        Assert.Equal("Gray8", gray.PixelFormat);
        Assert.Equal([0, 64, 128, 255], gray.DecodedPixels!.Value.ToArray());

        Assert.Equal("Rgb24", rgb.PixelFormat);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], rgb.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A palette over a palette, or over a space that states its colours through a function, would be an
    /// index into something the read model cannot state, so neither is followed and the payload stays one.
    /// </summary>
    [Fact]
    public async Task Does_not_build_a_palette_over_a_palette_or_over_a_function()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var overPalette = Image(result, 1, 1);
        var overFunction = Image(result, 2, 1);

        Assert.Null(overPalette.PixelFormat);
        Assert.Null(overPalette.DecodedPixels);
        Assert.NotNull(overPalette.Data);

        Assert.Null(overFunction.PixelFormat);
        Assert.Null(overFunction.DecodedPixels);
        Assert.NotNull(overFunction.Data);
    }

    /// <summary>An index of zero declares a table of one entry, which is still a table.</summary>
    [Fact]
    public async Task Reads_a_palette_whose_declared_index_is_zero()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 4, 1);

        Assert.Equal("Gray8", image.PixelFormat);
        Assert.Equal([0, 0, 0, 0], image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A table that declares 255 entries and holds five is read by what it holds: an index inside the table
    /// is the colour it states, and the index past the table is refused rather than read.
    /// </summary>
    [Fact]
    public async Task Clamps_a_declared_index_to_the_entries_the_table_holds()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var within = Image(result, 3, 2);
        var past = Image(result, 4, 2);

        Assert.Equal("Gray8", within.PixelFormat);
        Assert.Equal([0, 255, 0, 128, 0, 255], within.DecodedPixels!.Value.ToArray());

        Assert.Null(past.PixelFormat);
        Assert.Null(past.DecodedPixels);
        Assert.NotNull(past.Data);
    }

    /// <summary>
    /// An <c>ICCBased</c> array with no profile in it states no component count, so its samples are a
    /// payload rather than pixels — and the read does not fail over the missing item.
    /// </summary>
    [Fact]
    public async Task Does_not_read_an_icc_space_that_states_no_profile()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 6, 1);

        Assert.Null(image.PixelFormat);
        Assert.Null(image.DecodedPixels);
        Assert.NotNull(image.Data);
    }

    /// <summary>
    /// An inline image states its colour space with the abbreviations the specification defines for it, so
    /// <c>/G</c>, <c>/RGB</c> and <c>/CMYK</c> are read as the spaces they name rather than as unknown.
    /// </summary>
    [Fact]
    public async Task Exposes_the_pixels_of_an_inline_image_stated_by_an_abbreviation()
    {
        var abbreviations = await Reader().ReadImagesAsync(
            Source(PdfContentFixtures.InlineAbbreviations(), "inline.pdf"),
            PageRange.All,
            cancellationToken: Token);

        var gray = Assert.Single(abbreviations.Items, image => image.IsInline && image.ColorSpace == "G");
        var cmyk = Assert.Single(abbreviations.Items, image => image.IsInline && image.Width == 1);

        Assert.Equal("Gray8", gray.PixelFormat);
        Assert.Equal([0x10, 0x20, 0x30, 0x40], gray.DecodedPixels!.Value.ToArray());

        Assert.Equal("Cmyk32", cmyk.PixelFormat);
        Assert.Equal([1, 2, 3, 4], cmyk.DecodedPixels!.Value.ToArray());

        var inline = Assert.Single(
            (await Reader().ReadImagesAsync(
                Source(PdfContentFixtures.Images(), "images.pdf"), PageRange.All, cancellationToken: Token))
            .Items,
            image => image.IsInline);

        Assert.Equal("Rgb24", inline.PixelFormat);
        Assert.Equal(PdfContentFixtures.InlineSamples, inline.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A profile states what its samples mean through <c>/N</c>, so a one-component profile is a grey image
    /// and a four-component one is ink: each is read by that count rather than by the family name.
    /// </summary>
    [Fact]
    public async Task Exposes_the_samples_of_a_profile_by_the_components_it_states()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var gray = Image(result, 5, 1);
        var cmyk = Image(result, 1, 2);

        Assert.Equal("Gray8", gray.PixelFormat);
        Assert.Equal([7, 8, 9, 10, 11], gray.DecodedPixels!.Value.ToArray());

        Assert.Equal("Cmyk32", cmyk.PixelFormat);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], cmyk.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// A lookup table that holds nothing states no colour for any index, so the samples stay a payload
    /// rather than becoming colours the table cannot name.
    /// </summary>
    [Fact]
    public async Task Does_not_read_a_palette_whose_table_is_empty()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 7, 1);

        Assert.True(image.IsDecodable);
        Assert.NotNull(image.Data);
        Assert.Null(image.PixelFormat);
        Assert.Null(image.DecodedPixels);
    }

    /// <summary>
    /// Samples narrower than a byte are not confined to one channel: four-bit RGB packs three nibbles per
    /// pixel, and each row starts at its own padded length.
    /// </summary>
    [Fact]
    public async Task Exposes_four_bit_samples_of_a_three_channel_image()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Image(result, 2, 3);

        Assert.Equal("Rgb24", image.PixelFormat);
        Assert.Equal(
            [17, 34, 51, 68, 85, 102, 119, 136, 153, 170, 187, 204, 0, 17, 34, 51, 68, 85],
            image.DecodedPixels!.Value.ToArray());
    }

    /// <summary>
    /// An image that states no width or no height draws nothing, so its samples are a payload rather than
    /// pixels — an empty payload is not enough to make one a picture.
    /// </summary>
    [Fact]
    public async Task Does_not_expose_an_image_that_states_no_extent()
    {
        var source = Source(PdfContentFixtures.ColorSpaceEdges(), "edges.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var flat = Image(result, 0, 3);
        var thin = Image(result, 3, 0);

        Assert.Equal(0, flat.ByteLength);
        Assert.Null(flat.PixelFormat);
        Assert.Null(flat.DecodedPixels);

        Assert.Equal(0, thin.ByteLength);
        Assert.Null(thin.PixelFormat);
        Assert.Null(thin.DecodedPixels);
    }

    /// <summary>
    /// Resolving a colour space without a document is a programming error rather than a document problem,
    /// so it is refused by name instead of surfacing as a null dereference further in.
    /// </summary>
    [Fact]
    public void Refuses_to_resolve_a_colour_space_without_a_document()
    {
        var error = Assert.Throws<ArgumentNullException>(
            () => PdfSampleSpaces.Resolve(null!, new PdfName("DeviceGray"), "colorspaces.pdf"));

        Assert.Equal("reader", error.ParamName);
    }

    /// <summary>Finds the one image of a read whose geometry is the given one.</summary>
    /// <param name="result">The images of the read.</param>
    /// <param name="width">The width the image must state.</param>
    /// <param name="height">The height the image must state.</param>
    /// <returns>The image.</returns>
    private static PdfImageInfo Image(PdfReadResult<PdfImageInfo> result, int width, int height) =>
        Assert.Single(result.Items, image => image.Width == width && image.Height == height);
}
