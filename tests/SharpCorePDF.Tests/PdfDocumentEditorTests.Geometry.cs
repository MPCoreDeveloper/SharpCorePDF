using System.Text;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the geometry edits: cropping sets an explicit visible area inside the media box, and
/// resizing changes the page size while the original content keeps its bytes.
/// </summary>
public sealed partial class PdfDocumentEditorTests
{
    private static PdfRectangle? BoxOf(PdfFileReader reader, PdfObject? value)
    {
        var array = reader.ResolveArray(value);

        if (array is not { Count: >= 4 })
        {
            return null;
        }

        var numbers = new double[4];

        for (var index = 0; index < numbers.Length; index++)
        {
            numbers[index] = reader.Resolve(array.Items[index]) switch
            {
                PdfInteger integer => integer.Value,
                PdfReal real => real.Value,
                _ => double.NaN,
            };
        }

        return double.IsNaN(numbers[0])
            ? null
            : PdfRectangle.FromCorners(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    private static List<byte[]> ContentStreams(PdfFileReader reader, SourcePage page)
    {
        var resolved = reader.Resolve(page.Dictionary.Get("Contents"));

        if (resolved is null)
        {
            return [];
        }

        List<PdfObject> items = resolved is PdfArray array ? array.Items : [resolved];
        var streams = new List<byte[]>();

        foreach (var item in items)
        {
            if (reader.Resolve(item) is PdfStream stream)
            {
                streams.Add(stream.Raw.ToArray());
            }
        }

        return streams;
    }

    private static async Task<(byte[] Bytes, PdfEditResult Result)> ApplyWithResultAsync(
        byte[] source,
        PdfEditPlan plan)
    {
        var destination = new MemoryStream();
        var result = await new PdfEditEngine(Options).ApplyAsync(
            "geometry.pdf",
            new MemoryStream(source),
            plan,
            password: null,
            destination,
            Token);

        Assert.True(destination.Length > 0);
        return (destination.ToArray(), result);
    }

    /// <summary>A crop box that reaches outside the media box is clamped and reported.</summary>
    [Fact]
    public async Task Crop_is_clamped_to_the_media_box_and_reported()
    {
        var applied = await ApplyWithResultAsync(
            ThreePages(),
            PdfEditPlan.Create().CropPage(2, new PdfRectangle(-100d, -100d, 2000d, 2000d)));

        var reader = new PdfFileReader(applied.Bytes, "geometry.pdf", Options);
        var page = reader.Pages[1];
        var crop = BoxOf(reader, page.Dictionary.Get("CropBox"));
        var media = BoxOf(reader, page.Dictionary.Get("MediaBox"));

        Assert.NotNull(media);
        Assert.NotNull(crop);
        Assert.Equal(media.Left, crop.Left, 3);
        Assert.Equal(media.Bottom, crop.Bottom, 3);
        Assert.Equal(media.Right, crop.Right, 3);
        Assert.Equal(media.Top, crop.Top, 3);
        Assert.Contains(applied.Result.Warnings, warning => warning.Kind == PdfWarningKind.PageGeometryNormalized);
    }

    /// <summary>Every surviving page states its own media box instead of inheriting one.</summary>
    [Fact]
    public async Task Crop_writes_an_explicit_media_box_on_every_page()
    {
        var source = ThreePages();

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(source, "three-pages.pdf"),
            PdfEditPlan.Create().CropPage(2, new PdfRectangle(36d, 36d, 300d, 400d)),
            cancellationToken: Token);

        var reader = new PdfFileReader(edited, "geometry.pdf", Options);
        Assert.Equal(3, reader.Pages.Count);

        foreach (var page in reader.Pages)
        {
            Assert.NotNull(page.Dictionary.Get("MediaBox"));
        }

        var crop = BoxOf(reader, reader.Pages[1].Dictionary.Get("CropBox"));

        Assert.NotNull(crop);
        Assert.Equal(36d, crop.Left, 3);
        Assert.Equal(36d, crop.Bottom, 3);
        Assert.Equal(300d, crop.Right, 3);
        Assert.Equal(400d, crop.Top, 3);

        var original = new PdfFileReader(source, "three-pages.pdf", Options);
        Assert.Equal(
            ContentStreams(original, original.Pages[1])[0],
            ContentStreams(reader, reader.Pages[1])[0]);
    }

    /// <summary>A crop box that misses the media box entirely is refused before anything is written.</summary>
    [Fact]
    public async Task A_crop_box_outside_the_media_box_is_refused()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(
            () => ApplyWithResultAsync(
                ThreePages(),
                PdfEditPlan.Create().CropPage(1, new PdfRectangle(1000d, 1000d, 1100d, 1100d))));
    }

    /// <summary>Scale changes the media box and frames the untouched original content with a cm transform.</summary>
    [Fact]
    public async Task Resize_scale_changes_the_media_box_and_frames_the_content()
    {
        var source = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("one", 72, 100, Helvetica, 12))
            .ToArray();

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(source, "one-page.pdf"),
            PdfEditPlan.Create().ResizePage(1, PdfPageSize.A5),
            cancellationToken: Token);

        var reader = new PdfFileReader(edited, "resized.pdf", Options);
        var page = reader.Pages[0];
        var media = BoxOf(reader, page.Dictionary.Get("MediaBox"));

        Assert.NotNull(media);
        Assert.Equal(PdfPageSize.A5.WidthPoints, media.Width, 3);
        Assert.Equal(PdfPageSize.A5.HeightPoints, media.Height, 3);
        Assert.Equal(0d, media.Left, 3);
        Assert.Equal(0d, media.Bottom, 3);

        var streams = ContentStreams(reader, page);

        Assert.Equal(3, streams.Count);
        Assert.StartsWith("q\n", Encoding.Latin1.GetString(streams[0]));
        Assert.Contains("cm", Encoding.Latin1.GetString(streams[0]));
        Assert.Equal("Q\n", Encoding.Latin1.GetString(streams[2]));

        var original = new PdfFileReader(source, "one-page.pdf", Options);
        Assert.Equal(ContentStreams(original, original.Pages[0])[0], streams[1]);
    }

    /// <summary>Preserve keeps the content at its own size and centres it on the new page.</summary>
    [Fact]
    public async Task Resize_preserve_offsets_the_content_without_scaling_it()
    {
        var source = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A5, page => page.DrawText("one", 72, 100, Helvetica, 12))
            .ToArray();

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(source, "one-page.pdf"),
            PdfEditPlan.Create().ResizePage(1, PdfPageSize.A4, PdfPageResizeMode.Preserve),
            cancellationToken: Token);

        var reader = new PdfFileReader(edited, "resized.pdf", Options);
        var prefix = Encoding.Latin1.GetString(ContentStreams(reader, reader.Pages[0])[0]);
        var operands = prefix.Split(['\n', ' '], StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("q", operands[0]);
        Assert.Equal(1d, Parse(operands[1]), 3);
        Assert.Equal(0d, Parse(operands[2]), 3);
        Assert.Equal(0d, Parse(operands[3]), 3);
        Assert.Equal(1d, Parse(operands[4]), 3);
        Assert.Equal((PdfPageSize.A4.WidthPoints - PdfPageSize.A5.WidthPoints) / 2d, Parse(operands[5]), 3);
        Assert.Equal((PdfPageSize.A4.HeightPoints - PdfPageSize.A5.HeightPoints) / 2d, Parse(operands[6]), 3);
        Assert.Equal("cm", operands[7]);

        static double Parse(string value) =>
            double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A crop box set before a resize travels with the content through the same transform.</summary>
    [Fact]
    public async Task Resize_maps_a_crop_box_that_was_set_before_it()
    {
        var source = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("one", 72, 100, Helvetica, 12))
            .ToArray();

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(source, "one-page.pdf"),
            PdfEditPlan.Create()
                .CropPage(1, new PdfRectangle(100d, 100d, 300d, 400d))
                .ResizePage(1, PdfPageSize.A5),
            cancellationToken: Token);

        var reader = new PdfFileReader(edited, "resized.pdf", Options);
        var crop = BoxOf(reader, reader.Pages[0].Dictionary.Get("CropBox"));
        var scaleX = PdfPageSize.A5.WidthPoints / PdfPageSize.A4.WidthPoints;
        var scaleY = PdfPageSize.A5.HeightPoints / PdfPageSize.A4.HeightPoints;

        Assert.NotNull(crop);
        Assert.Equal(100d * scaleX, crop.Left, 3);
        Assert.Equal(100d * scaleY, crop.Bottom, 3);
        Assert.Equal(300d * scaleX, crop.Right, 3);
        Assert.Equal(400d * scaleY, crop.Top, 3);
    }

    /// <summary>An empty crop box is refused before the source is opened.</summary>
    [Fact]
    public void An_empty_crop_box_is_refused_by_validation()
    {
        var plan = PdfEditPlan.Create().CropPage(1, new PdfRectangle(10d, 10d, 10d, 20d));

        Assert.Contains(plan.Validate(), problem => problem.Contains("encloses no area", StringComparison.Ordinal));
    }

    /// <summary>An undefined resize mode is refused when the plan is built.</summary>
    [Fact]
    public void An_undefined_resize_mode_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfEditPlan.Create().ResizePage(1, PdfPageSize.A4, (PdfPageResizeMode)99));
    }
}
