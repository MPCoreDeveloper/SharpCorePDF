using System.Runtime.InteropServices;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of image extraction: identity, payload, geometry, masks, the inline budget, and the copy route
/// that exists so a payload larger than the budget never has to be held in memory.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>One object drawn twice and shared by two pages is one entry carrying both pages.</summary>
    [Fact]
    public async Task Returns_each_image_once_with_every_page_that_draws_it()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(5, result.Count);
        Assert.Equal("DCTDecode", result.Items[0].Filter);

        var jpeg = Assert.Single(result.Items, image => image.Filter == "DCTDecode");
        var flate = Assert.Single(result.Items, image => image.Filter == "FlateDecode" && image.Mask is null);

        Assert.Equal([1, 2], jpeg.Pages);
        Assert.Equal([1, 2], flate.Pages);
        Assert.Equal([2], Assert.Single(result.Items, image => image.IsInline).Pages);
    }

    /// <summary>A page range reads only the pages it names, even when an image is also drawn outside it.</summary>
    [Fact]
    public async Task Reads_only_the_pages_of_the_range()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.Single(2), cancellationToken: Token);

        Assert.Equal(4, result.Count);
        Assert.All(result.Items, image => Assert.Equal([2], image.Pages));
    }

    /// <summary>A range that starts past the last page reads the pages that exist, which is none.</summary>
    [Fact]
    public async Task Does_not_throw_for_a_range_past_the_last_page()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.FromTo(5, 500), cancellationToken: Token);

        Assert.Empty(result.Items);
    }

    /// <summary>A JPEG is returned byte for byte, as a view of the source rather than a copy.</summary>
    [Fact]
    public async Task Returns_a_jpeg_untouched()
    {
        var bytes = PdfContentFixtures.Images();
        var source = Source(bytes, "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var jpeg = Assert.Single(result.Items, image => image.Filter == "DCTDecode");

        Assert.True(jpeg.IsDecodable);
        Assert.True(jpeg.Data.HasValue);
        Assert.Equal(PdfContentFixtures.JpegData.Length, jpeg.ByteLength);
        Assert.Equal(PdfContentFixtures.JpegData, jpeg.Data.Value.ToArray());

        Assert.True(MemoryMarshal.TryGetArray(jpeg.Data.Value, out var segment));
        Assert.NotNull(segment.Array);
        Assert.True(segment.Array!.Length > segment.Count);
        Assert.Equal(PdfContentFixtures.JpegData.Length, segment.Count);
    }

    /// <summary>A payload survives the document, the source and a full collection.</summary>
    [Fact]
    public async Task Keeps_the_jpeg_alive_after_everything_else_is_gone()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");
        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var payload = Assert.Single(result.Items, image => image.Filter == "DCTDecode").Data;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(payload.HasValue);
        Assert.Equal(PdfContentFixtures.JpegData, payload.Value.ToArray());
    }

    /// <summary>A Flate image is decoded to its samples, and its soft mask is returned beside it.</summary>
    [Fact]
    public async Task Decodes_the_samples_and_returns_the_soft_mask_separately()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var masked = Assert.Single(result.Items, image => image.Mask is not null);

        Assert.Equal(PdfContentFixtures.ImageSamples, masked.Data!.Value.ToArray());
        Assert.Equal(PdfImageMaskKind.SoftMask, masked.Mask!.Kind);
        Assert.Equal(PdfContentFixtures.MaskSamples, masked.Mask.Data!.Value.ToArray());
        Assert.True(masked.Mask.IsBuffered);
    }

    /// <summary>A filter the library does not decode is named, kept raw, and reported once.</summary>
    [Fact]
    public async Task Reports_an_unsupported_filter_with_exactly_one_warning()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var jpx = Assert.Single(result.Items, image => image.Filter == "JPXDecode");

        Assert.False(jpx.IsDecodable);
        Assert.Equal(PdfContentFixtures.JpxData, jpx.Data!.Value.ToArray());
        Assert.Equal(PdfContentFixtures.JpxData.Length, jpx.ByteLength);
        Assert.Single(result.Warnings, warning => warning.Kind == PdfWarningKind.ImageFilterUnsupported);
    }

    /// <summary>An inline image has no object number, so it carries a synthetic identifier and a position.</summary>
    [Fact]
    public async Task Returns_an_inline_image_with_a_synthetic_identifier()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var inline = Assert.Single(result.Items, image => image.IsInline);

        Assert.Equal("inline:2:1", inline.Id);
        Assert.Null(inline.ObjectNumber);
        Assert.Null(inline.Generation);
        Assert.Equal(PdfContentFixtures.InlineSamples, inline.Data!.Value.ToArray());
        Assert.Equal([2], inline.Pages);
        Assert.NotNull(inline.Position);
        Assert.Equal(0d, inline.Position!.Left);
        Assert.Equal(10d, inline.Position.Right);
    }

    /// <summary>The position of an image is the area its first draw paints it in.</summary>
    [Fact]
    public async Task Reports_the_position_of_the_first_draw()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var jpeg = Assert.Single(result.Items, image => image.Filter == "DCTDecode");

        Assert.Equal(new PdfRectangle(10d, 20d, 60d, 70d), jpeg.Position);
    }

    /// <summary>Two calls over the same source produce the same records, bytes and remarks.</summary>
    [Fact]
    public async Task Returns_the_same_result_twice()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var first = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var second = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Equal(first.Count, second.Count);

        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first.Items[index].Id, second.Items[index].Id);
            Assert.Equal(first.Items[index].ByteLength, second.Items[index].ByteLength);
            Assert.Equal(first.Items[index].Data!.Value.ToArray(), second.Items[index].Data!.Value.ToArray());
        }

        Assert.Equal(
            first.Warnings.Select(warning => warning.Kind),
            second.Warnings.Select(warning => warning.Kind));
    }

    /// <summary>Reading never modifies the document it reads.</summary>
    [Fact]
    public async Task Leaves_the_source_untouched()
    {
        var bytes = PdfContentFixtures.Images();
        var before = Digest(bytes);

        await Reader().ReadImagesAsync(Source(bytes, "images.pdf"), PageRange.All, cancellationToken: Token);

        Assert.Equal(before, Digest(bytes));
    }

    /// <summary>An encrypted document is read with its password, and reports no encryption problem.</summary>
    [Fact]
    public async Task Reads_an_encrypted_document_with_its_password()
    {
        var source = PdfFileSource.From(TestAssets.Encrypted);

        var result = await Reader().ReadImagesAsync(
            source,
            PageRange.All,
            TestAssets.EncryptedUserPassword,
            Token);

        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>A document that stays closed yields nothing, and says why instead of throwing.</summary>
    [Fact]
    public async Task Reports_an_encrypted_document_it_cannot_open()
    {
        var source = PdfFileSource.From(TestAssets.Encrypted);

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);

        Assert.Empty(result.Items);
        AssertWarning(result.Warnings, PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>
    /// A predicted image is undone one row at a time, with the filter type of every row honoured — the
    /// fixture cycles through all five PNG filters.
    /// </summary>
    [Fact]
    public async Task Undoes_every_png_filter_type_row_by_row()
    {
        var source = Source(PdfContentFixtures.PredictorImage(), "predictor.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.Equal("FlateDecode", image.Filter);
        Assert.True(image.IsDecodable);
        Assert.Equal(PdfContentFixtures.PredictedSamples.Length, image.ByteLength);
        Assert.Equal(PdfContentFixtures.PredictedSamples, image.Data!.Value.ToArray());
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Kind is PdfWarningKind.ImageDataUnreadable or PdfWarningKind.ImageFilterUnsupported);
    }

    /// <summary>A chain of two filters the library decodes is applied in the order the document states.</summary>
    [Fact]
    public async Task Decodes_a_chain_of_two_filters()
    {
        var source = Source(PdfContentFixtures.FilterChains(), "chains.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var chained = Assert.Single(result.Items, image => image.Filter == "ASCII85Decode FlateDecode");

        Assert.True(chained.IsDecodable);
        Assert.Equal(PdfContentFixtures.ChainSamples, chained.Data!.Value.ToArray());
        Assert.Equal(PdfContentFixtures.ChainSamples.Length, chained.ByteLength);
    }

    /// <summary>An ASCII hexadecimal image is decoded to its samples.</summary>
    [Fact]
    public async Task Decodes_an_ascii_hexadecimal_image()
    {
        var source = Source(PdfContentFixtures.FilterChains(), "chains.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var hexadecimal = Assert.Single(result.Items, image => image.Filter == "ASCIIHexDecode");

        Assert.True(hexadecimal.IsDecodable);
        Assert.Equal(PdfContentFixtures.ChainSamples, hexadecimal.Data!.Value.ToArray());
    }

    /// <summary>A filter the library does not decode is named and left raw, one entry per filter.</summary>
    [Theory]
    [InlineData("LZWDecode")]
    [InlineData("RunLengthDecode")]
    public async Task Keeps_a_filter_it_cannot_decode_untouched(string filter)
    {
        var source = Source(PdfContentFixtures.FilterChains(), "chains.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items, candidate => candidate.Filter == filter);

        Assert.False(image.IsDecodable);
        Assert.Equal(PdfContentFixtures.ChainSamples, image.Data!.Value.ToArray());
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.ImageFilterUnsupported);
    }

    /// <summary>
    /// An inline image states its own metadata, and a name that follows a name is the value of the entry
    /// rather than a second key: "/CS /G" is a colour space, not two names.
    /// </summary>
    [Fact]
    public async Task Reports_the_metadata_of_an_inline_image()
    {
        var source = Source(PdfContentFixtures.InlineVariants(), "inline.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var inline = Assert.Single(result.Items, image => image.Id == "inline:1:1");

        Assert.True(inline.IsInline);
        Assert.Null(inline.ObjectNumber);
        Assert.Equal(2, inline.Width);
        Assert.Equal(2, inline.Height);
        Assert.Equal(8, inline.BitsPerComponent);
        Assert.Equal("G", inline.ColorSpace);
        Assert.Null(inline.Filter);
        Assert.Equal(PdfContentFixtures.InlineTerminatorSamples.Length, inline.ByteLength);
    }

    /// <summary>
    /// An inline image is as long as its header states, so a space followed by the letters "EI" inside
    /// the samples is data rather than the end of the image.
    /// </summary>
    [Fact]
    public async Task Keeps_an_inline_image_whose_samples_contain_a_terminator()
    {
        var source = Source(PdfContentFixtures.InlineVariants(), "inline.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var inline = Assert.Single(result.Items, image => image.Id == "inline:1:1");

        Assert.Equal(PdfContentFixtures.InlineTerminatorSamples, inline.Data!.Value.ToArray());
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.ImageDataUnreadable);
    }

    /// <summary>An inline image whose terminator is followed directly by a delimiter ends there too.</summary>
    [Fact]
    public async Task Reads_an_inline_image_that_ends_at_a_delimiter()
    {
        var source = Source(PdfContentFixtures.InlineVariants(), "inline.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var inline = Assert.Single(result.Items, image => image.Id == "inline:1:2");

        Assert.Equal([1, 2, 3, 4], inline.Data!.Value.ToArray());
        Assert.Single(result.Items, image => image.Filter == "DCTDecode");
    }

    /// <summary>An inline image with no EI keeps what it read and says the terminator was missing.</summary>
    [Fact]
    public async Task Warns_when_an_inline_image_is_not_terminated()
    {
        var source = Source(PdfContentFixtures.UnterminatedInline(), "inline.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var inline = Assert.Single(result.Items);

        // Nothing was lost and nothing was invented: the payload is exactly the bytes the page holds.
        Assert.Equal(PdfContentFixtures.UnterminatedSamples, inline.Data!.Value.ToArray());
        Assert.Equal(PdfContentFixtures.UnterminatedSamples.Length, inline.ByteLength);
        AssertWarning(result.Warnings, PdfWarningKind.ImageDataUnreadable);
    }

    /// <summary>A colour key mask is a list of sample ranges: it names its image and has no payload.</summary>
    [Fact]
    public async Task Reports_a_colour_key_mask_without_a_payload()
    {
        var source = Source(PdfContentFixtures.ColorKeyMaskedImage(), "keyed.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.NotNull(image.Mask);
        Assert.Equal(PdfImageMaskKind.ColorKey, image.Mask.Kind);
        Assert.Equal(image.Id, image.Mask.ImageId);
        Assert.Equal(PdfContentFixtures.ColorKey, image.Mask.ColorKey);
        Assert.Equal(0, image.Mask.ByteLength);
        Assert.Null(image.Mask.Data);
    }

    /// <summary>A mask is resolved by its image identifier and its kind, and copied through the copy route.</summary>
    [Fact]
    public async Task Copies_a_mask_through_the_copy_route()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");
        var reader = Reader();
        var read = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(read.Items, candidate => candidate.Mask is not null);

        Assert.Equal(image.Id, image.Mask!.ImageId);

        using var destination = new MemoryStream();

        await reader.CopyImageMaskDataAsync(source, image.Mask, destination, cancellationToken: Token);

        Assert.Equal(PdfContentFixtures.MaskSamples, destination.ToArray());
    }

    /// <summary>A colour key mask is not an image, so asking for its payload is refused by name.</summary>
    [Fact]
    public async Task Rejects_copying_a_colour_key_mask()
    {
        var source = Source(PdfContentFixtures.ColorKeyMaskedImage(), "keyed.pdf");
        var reader = Reader();
        var read = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var mask = Assert.Single(read.Items).Mask!;

        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => reader.CopyImageMaskDataAsync(source, mask, destination, cancellationToken: Token));

        Assert.Equal("mask", exception.ParamName);
    }

    /// <summary>A mask whose image is not in the document it is asked of is refused rather than guessed.</summary>
    [Fact]
    public async Task Rejects_a_mask_that_does_not_belong_to_the_document()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");
        var foreign = new PdfImageMask { ImageId = "99 0", Kind = PdfImageMaskKind.SoftMask };

        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Reader().CopyImageMaskDataAsync(source, foreign, destination, cancellationToken: Token));
    }

    /// <summary>
    /// A page's streams are one instruction sequence: the separator between them keeps the last keyword of
    /// one and the first keyword of the next apart, so <c>Do</c> and <c>Q</c> stay two operators.
    /// </summary>
    [Fact]
    public async Task Joins_the_content_streams_of_a_page_with_a_separator()
    {
        var source = Source(PdfContentFixtures.SplitContent(), "split.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.Equal([1], image.Pages);
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new PdfRectangle(0d, 0d, 10d, 10d), image.Position);
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.XObjectResourceMissing);
    }
}


