using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the one rule every payload follows: a record carries its bytes only within the inline
/// budget, says how large the payload is either way, and offers a bounded route for the rest.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>A payload above the budget is described, not carried, and no warning is raised.</summary>
    [Fact]
    public async Task Leaves_a_payload_above_the_budget_out_of_the_record()
    {
        var source = Source(PdfContentFixtures.LargeImage(), "large.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.Null(image.Data);
        Assert.False(image.IsBuffered);
        Assert.Equal(400_000, image.ByteLength);
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Kind is PdfWarningKind.ImageDataUnreadable or PdfWarningKind.ImageFilterUnsupported);
    }

    /// <summary>A payload exactly at the budget is carried, and its length equals its size.</summary>
    [Fact]
    public async Task Carries_a_payload_exactly_at_the_budget()
    {
        var source = Source(PdfContentFixtures.LargeImage(), "large.pdf");
        var reader = Reader(new PdfEngineOptions { MaxInlineContentBytes = 400_000 });

        var result = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.True(image.IsBuffered);
        Assert.NotNull(image.Data);
        Assert.Equal(400_000, image.Data.Value.Length);
        Assert.Equal(image.ByteLength, image.Data.Value.Length);
    }

    /// <summary>A budget of zero makes every entry metadata only, and keeps every length truthful.</summary>
    [Fact]
    public async Task Carries_nothing_when_the_budget_is_zero()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");
        var reader = Reader(new PdfEngineOptions { MaxInlineContentBytes = 0 });

        var result = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: Token);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, image => Assert.Null(image.Data));
        Assert.All(result.Items, image => Assert.False(image.IsBuffered));
        Assert.All(result.Items, image => Assert.True(image.ByteLength > 0));
    }

    /// <summary>The copy route writes exactly the bytes the read would have carried.</summary>
    [Fact]
    public async Task Copies_an_unbuffered_payload_through_the_copy_route()
    {
        var source = Source(PdfContentFixtures.LargeImage(), "large.pdf");
        var reader = Reader();
        var read = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(read.Items);

        using var destination = new MemoryStream();

        await reader.CopyImageDataAsync(source, image, destination, cancellationToken: Token);

        var materialised = await reader.ReadImageDataAsync(source, image, cancellationToken: Token);

        Assert.Equal(image.ByteLength, destination.Length);
        Assert.Equal(materialised.ToArray(), destination.ToArray());
        Assert.True(materialised.Length == 400_000);
        Assert.True(destination.CanWrite);
    }

    /// <summary>A damaged image is returned with what is known, and reported rather than dropped.</summary>
    [Fact]
    public async Task Returns_a_damaged_image_with_a_warning()
    {
        var source = Source(PdfContentFixtures.DamagedImage(), "damaged.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.False(image.IsDecodable);
        Assert.Equal(3, image.ByteLength);
        Assert.Equal([1, 2, 3], image.Data!.Value.ToArray());
        AssertWarning(result.Warnings, PdfWarningKind.ImageDataUnreadable);
    }

    /// <summary>A negative budget is refused by the options rather than by a read.</summary>
    [Fact]
    public void Rejects_a_negative_inline_budget()
    {
        var problems = new PdfEngineOptions { MaxInlineContentBytes = -1 }.Validate();

        Assert.Single(problems);
        Assert.Contains("MaxInlineContentBytes", problems[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A predicted payload above the budget is counted row by row but never carried, and counting it is
    /// not an error: the record is still decodable.
    /// </summary>
    [Fact]
    public async Task Leaves_a_predicted_payload_above_the_budget_out_of_the_record()
    {
        var source = Source(PdfContentFixtures.LargePredictorImage(), "predictor.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.Null(image.Data);
        Assert.False(image.IsBuffered);
        Assert.True(image.IsDecodable);
        Assert.Equal(400_000, image.ByteLength);
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Kind is PdfWarningKind.ImageDataUnreadable or PdfWarningKind.ImageFilterUnsupported);
    }

    /// <summary>A predicted payload copied without being carried equals the bytes a read would have carried.</summary>
    [Fact]
    public async Task Copies_a_predicted_payload_identical_to_the_read()
    {
        var source = Source(PdfContentFixtures.LargePredictorImage(), "predictor.pdf");
        var reader = Reader();
        var read = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(read.Items);

        Assert.Null(image.Data);

        using var destination = new MemoryStream();

        await reader.CopyImageDataAsync(source, image, destination, cancellationToken: Token);

        var carried = await Reader(new PdfEngineOptions { MaxInlineContentBytes = (int)image.ByteLength })
            .ReadImageDataAsync(source, image, cancellationToken: Token);

        Assert.Equal(image.ByteLength, destination.Length);
        Assert.Equal(carried.ToArray(), destination.ToArray());
    }

    /// <summary>
    /// A mask follows the same rule as the image it belongs to: above the budget it is described but not
    /// carried, while the small image that owns it still is.
    /// </summary>
    [Fact]
    public async Task Leaves_a_mask_above_the_budget_out_of_the_record()
    {
        var source = Source(PdfContentFixtures.LargeMaskedImage(), "masked.pdf");

        var result = await Reader().ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var image = Assert.Single(result.Items);

        Assert.NotNull(image.Data);
        Assert.NotNull(image.Mask);
        Assert.Null(image.Mask.Data);
        Assert.False(image.Mask.IsBuffered);
        Assert.True(image.Mask.IsDecodable);
        Assert.Equal(400_000, image.Mask.ByteLength);
        Assert.Equal(image.Id, image.Mask.ImageId);
    }

    /// <summary>An unbuffered mask is copied through the copy route, byte for byte and bounded in memory.</summary>
    [Fact]
    public async Task Copies_an_unbuffered_mask_byte_for_byte()
    {
        var source = Source(PdfContentFixtures.LargeMaskedImage(), "masked.pdf");
        var reader = Reader();
        var read = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: Token);
        var mask = Assert.Single(read.Items).Mask!;

        Assert.Null(mask.Data);

        using var destination = new MemoryStream();

        await reader.CopyImageMaskDataAsync(source, mask, destination, cancellationToken: Token);

        Assert.Equal(PdfContentFixtures.LargeMaskSamples, destination.ToArray());
        Assert.Equal(mask.ByteLength, destination.Length);
    }
}
