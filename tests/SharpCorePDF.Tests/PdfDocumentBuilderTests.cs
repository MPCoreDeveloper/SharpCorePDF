using System.Text;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of authoring: a page written from nothing, read back through the same engine that merges it.
/// </summary>
/// <remarks>
/// The assertions go through the public read side — the inspector and the content reader — so a document
/// that only looks right in the bytes it was built from does not pass. A realistic cover, with a logo
/// and French text, is merged in front of a real fixture: that is the use the feature exists for.
/// </remarks>
public sealed partial class PdfDocumentBuilderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfTypeface Helvetica => PdfTypeface.Standard(StandardFont.Helvetica);

    private static PdfTypeface EmbeddedFace =>
        PdfTypeface.FromTrueType(File.ReadAllBytes(TestAssets.EmbeddedFont));

    private static PdfContentReaderService Reader() => new(new PdfPageContentReader());

    private static async Task<PdfDocumentInfo> InspectAsync(byte[] bytes) =>
        await new PdfInspectorService(new PdfDocumentReader()).InspectAsync(
            new PdfByteArraySource(bytes, "authored.pdf"),
            cancellationToken: Token);

    private static async Task<PdfReadResult<PdfTextBlock>> TextAsync(
        byte[] bytes,
        PageRange? range = null) =>
        await Reader().ReadTextAsync(
            new PdfByteArraySource(bytes, "authored.pdf"),
            range ?? PageRange.All,
            cancellationToken: Token);

    private static async Task<PdfReadResult<PdfImageInfo>> ImagesAsync(byte[] bytes) =>
        await Reader().ReadImagesAsync(
            new PdfByteArraySource(bytes, "authored.pdf"),
            PageRange.All,
            cancellationToken: Token);

    private static string PlainText(PdfReadResult<PdfTextBlock> result) =>
        string.Join("\n", result.Items.OrderBy(block => block.ReadingOrder).Select(block => block.Text));

    private static byte[] Logo() => TestPng.Rgba(
        152,
        87,
        (x, y) => ((byte)(x * 255 / 151), (byte)(y * 255 / 86), 200, x < 20 ? (byte)0 : (byte)255));

    /// <summary>A page of A4 has the size the issue states, and reads back with the text it was given.</summary>
    [Fact]
    public async Task Writes_an_a4_page_with_its_text()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText(
                "Veuillez trouver ci-joint le décompte pour l'immeuble 12345",
                150,
                400,
                Helvetica,
                12))
            .ToArray();

        var info = await InspectAsync(bytes);

        Assert.True(info.IsReadable);
        Assert.Equal(1, info.PageCount);
        Assert.Equal(PdfVersion.V1_7, info.Version);
        Assert.Equal(595.276, info.Pages[0].Size.WidthPoints, 0.001);
        Assert.Equal(841.89, info.Pages[0].Size.HeightPoints, 0.001);

        Assert.Equal(
            "Veuillez trouver ci-joint le décompte pour l'immeuble 12345",
            PlainText(await TextAsync(bytes)));
    }

    /// <summary>The accented characters of the issue survive a standard font and are extracted exactly.</summary>
    [Fact]
    public async Task Round_trips_the_accented_characters_of_a_standard_font()
    {
        const string Text = "é è à ç ô ï ' / |";

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText(Text, 72, 200, Helvetica, 12))
            .ToArray();

        Assert.Equal(Text, PlainText(await TextAsync(bytes)));
    }

    /// <summary>An embedded TrueType font is subsetted, carries a ToUnicode map and extracts its text.</summary>
    [Fact]
    public async Task Embeds_a_truetype_font_and_reads_its_text_back()
    {
        const string Text = "Veuillez trouver ci-joint le décompte pour l'immeuble 12345";

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText(Text, 150, 400, EmbeddedFace, 12))
            .ToArray();

        var raw = Encoding.Latin1.GetString(bytes);

        Assert.Contains("/Subtype /CIDFontType2", raw);
        Assert.Contains("/FontFile2", raw);
        Assert.Contains("/ToUnicode", raw);
        Assert.Contains("/Encoding /Identity-H", raw);
        Assert.Equal(Text, PlainText(await TextAsync(bytes)));
    }

    /// <summary>The cover of the issue — logo, address, two message lines and a marker — reads back whole.</summary>
    [Fact]
    public async Task Writes_the_cover_the_issue_describes()
    {
        var png = Logo();

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .DrawImage(png, 40, 30, 152, 87)
                .DrawText("Décomptes SPRL", 330, 180, Helvetica, 12)
                .DrawText("Rue de l'Église 1", 330, 194, Helvetica, 12)
                .DrawText("1000 Bruxelles", 330, 208, Helvetica, 12)
                .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", 150, 400, EmbeddedFace, 12)
                .DrawText("pour la période du 01/01/2025 au 31/12/2025.", 150, 414, EmbeddedFace, 12)
                .DrawText("Sending 1/3", 150, 550, EmbeddedFace, 12))
            .ToArray();

        var text = PlainText(await TextAsync(bytes));

        Assert.Contains("Décomptes SPRL", text);
        Assert.Contains("Rue de l'Église 1", text);
        Assert.Contains("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", text);
        Assert.Contains("pour la période du 01/01/2025 au 31/12/2025.", text);
        Assert.Contains("Sending 1/3", text);

        var image = Assert.Single((await ImagesAsync(bytes)).Items);

        Assert.Equal(152, image.Width);
        Assert.Equal(87, image.Height);
        Assert.Equal(40d, image.Position!.Left, 0.001d);
        Assert.Equal(192d, image.Position.Right, 0.001d);
        Assert.Equal(841.89d - 30d - 87d, image.Position.Bottom, 0.001d);
        Assert.Equal(841.89d - 30d, image.Position.Top, 0.001d);
    }

    /// <summary>An RGBA PNG becomes an image with a soft mask holding its alpha channel.</summary>
    [Fact]
    public async Task Writes_an_rgba_png_with_a_soft_mask()
    {
        var png = TestPng.Rgba(
            152,
            87,
            (x, _) => ((byte)200, (byte)40, (byte)10, x < 10 ? (byte)0 : (byte)255));

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 40, 30, 152, 87))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);
        var mask = Assert.IsType<PdfImageMask>(image.Mask);

        Assert.Equal(152, image.Width);
        Assert.Equal(87, image.Height);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal(PdfImageMaskKind.SoftMask, mask.Kind);
        Assert.Equal(152, mask.Width);
        Assert.Equal(87, mask.Height);
        Assert.Equal("DeviceGray", mask.ColorSpace);
        Assert.NotNull(mask.Data);
        Assert.Equal(0, mask.Data!.Value.Span[0]);
        Assert.Equal(255, mask.Data.Value.Span[10]);
    }

    /// <summary>A tRNS colour key on an RGB image becomes the PDF colour-key mask.</summary>
    [Fact]
    public async Task Writes_an_rgb_png_with_a_colour_key()
    {
        var png = TestPng.RgbWithColorKey(8, 4);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 36, 36, 8, 4))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);
        var mask = Assert.IsType<PdfImageMask>(image.Mask);

        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal(PdfImageMaskKind.ColorKey, mask.Kind);
        Assert.Equal(0, mask.ByteLength);
    }

    /// <summary>An indexed PNG keeps its palette, and a tRNS chunk becomes a soft mask.</summary>
    [Fact]
    public async Task Writes_a_palette_png_with_transparency()
    {
        var png = TestPng.PaletteWithTransparency(8, 4);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 36, 36, 8, 4))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);
        var mask = Assert.IsType<PdfImageMask>(image.Mask);

        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal("DeviceRGB", image.ColorSpace);
        Assert.Equal(PdfImageMaskKind.SoftMask, mask.Kind);
        Assert.NotNull(mask.Data);
        Assert.Equal(255, mask.Data!.Value.Span[0]);
        Assert.Equal(0, mask.Data.Value.Span[1]);

        // The palette was expanded: the first pixel is red, the second green.
        Assert.NotNull(image.Data);
        Assert.Equal([255, 0, 0, 0, 255, 0], image.Data!.Value.Span[..6].ToArray());
    }

    /// <summary>A greyscale PNG with alpha keeps its colour space and gets a soft mask.</summary>
    [Fact]
    public async Task Writes_a_greyscale_png_with_alpha()
    {
        var png = TestPng.GrayAlpha(8, 4, (x, _) => (128, x < 4 ? (byte)0 : (byte)255));

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 36, 36, 8, 4))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);
        var mask = Assert.IsType<PdfImageMask>(image.Mask);

        Assert.Equal("DeviceGray", image.ColorSpace);
        Assert.Equal(PdfImageMaskKind.SoftMask, mask.Kind);
        Assert.NotNull(mask.Data);
        Assert.Equal(0, mask.Data!.Value.Span[0]);
        Assert.Equal(255, mask.Data.Value.Span[4]);
    }

    /// <summary>A PNG without an explicit size is drawn at the resolution its pHYs chunk states.</summary>
    [Fact]
    public async Task Uses_the_resolution_of_a_png_for_its_natural_size()
    {
        // 11811 pixels per metre is 300 dpi, the resolution a logo is usually exported at.
        var png = TestPng.Rgb(152, 87, (_, _) => ((byte)255, (byte)0, (byte)0), pixelsPerMeter: 11811);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 40, 30))
            .ToArray();

        var image = Assert.Single((await ImagesAsync(bytes)).Items);
        var area = Assert.IsType<PdfRectangle>(image.Position);

        Assert.Equal(152, image.Width);
        Assert.Equal(87, image.Height);
        Assert.Equal(152 * 72d / 300d, area.Right - area.Left, 0.01d);
        Assert.Equal(87 * 72d / 300d, area.Top - area.Bottom, 0.01d);
    }

    /// <summary>One logo drawn on two pages is one image object that both pages share.</summary>
    [Fact]
    public async Task Writes_one_image_object_for_a_logo_used_on_two_pages()
    {
        var png = Logo();

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 40, 30, 152, 87))
            .AddPage(PdfPageSize.A4, page => page.DrawImage(png, 40, 30, 152, 87))
            .ToArray();

        var info = await InspectAsync(bytes);
        var image = Assert.Single((await ImagesAsync(bytes)).Items);

        Assert.Equal(2, info.PageCount);
        Assert.Equal([1, 2], image.Pages);
    }

    /// <summary>The authored page can lead a merge without a temporary file.</summary>
    [Fact]
    public async Task Merges_the_authored_page_in_front_of_a_document()
    {
        var cover = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .DrawImage(Logo(), 40, 30, 152, 87)
                .DrawText(
                    "Veuillez trouver ci-joint le décompte pour l'immeuble 12345",
                    150,
                    400,
                    EmbeddedFace,
                    12))
            .Build("cover.pdf");

        var plan = MergePlan.Create()
            .AddSource(PdfSource.Create(cover))
            .AddFile(TestAssets.Modern);

        using var buffer = new MemoryStream();
        var merger = new PdfMergerService(new PdfDocumentReader(), new PdfMergeEngine());

        await merger.MergeAsync(plan, buffer, Token);

        var merged = buffer.ToArray();
        var info = await InspectAsync(merged);

        Assert.Equal(3, info.PageCount);
        Assert.Equal(595.276, info.Pages[0].Size.WidthPoints, 0.001d);
        Assert.Equal(PdfPageSize.A4.HeightPoints, info.Pages[1].Size.HeightPoints, 0.001d);

        Assert.Contains(
            "Veuillez trouver ci-joint le décompte pour l'immeuble 12345",
            PlainText(await TextAsync(merged, PageRange.Single(1))));

        // The font program, its ToUnicode map, the image and its soft mask all survive the copy.
        var image = Assert.Single((await Reader().ReadImagesAsync(
            new PdfByteArraySource(merged, "merged.pdf"),
            PageRange.Single(1),
            cancellationToken: Token)).Items);

        Assert.Equal(152, image.Width);
        Assert.NotNull(image.Mask);
        Assert.Equal(PdfImageMaskKind.SoftMask, image.Mask.Kind);
    }

    /// <summary>A fixed creation date and identifier make the output reproducible, byte for byte.</summary>
    [Fact]
    public void Writes_the_same_bytes_for_the_same_input()
    {
        var options = PdfAuthoringOptions.Default with
        {
            CreationDate = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
            DocumentId = "0123456789ABCDEF0123456789ABCDEF",
        };

        byte[] Build(PdfAuthoringOptions value) => PdfDocumentBuilder.Create(value)
            .AddPage(PdfPageSize.A4, page => page.DrawText("Deterministic", 72, 72, Helvetica, 12))
            .ToArray();

        var first = Build(options);
        var second = Build(options);

        Assert.Equal(first, second);
        Assert.NotEqual(first, Build(options with { CreationDate = options.CreationDate!.Value.AddMinutes(1) }));
        Assert.Contains("0123456789ABCDEF", Encoding.Latin1.GetString(first), StringComparison.OrdinalIgnoreCase);

        // The identifier does not have to be supplied: a fixed date is enough for a repeatable file.
        var derived = options with { DocumentId = null };

        Assert.Equal(Build(derived), Build(derived));
    }

    /// <summary>The document information of the builder reaches the document.</summary>
    [Fact]
    public async Task Records_the_metadata()
    {
        var bytes = PdfDocumentBuilder.Create()
            .WithMetadata(new PdfMetadata { Title = "Cover", Author = "MPCoreDeveloper", Creator = "Tests" })
            .AddPage(PdfPageSize.A4)
            .ToArray();

        var info = await InspectAsync(bytes);

        Assert.Equal("Cover", info.Metadata.Title);
        Assert.Equal("MPCoreDeveloper", info.Metadata.Author);
        Assert.Equal("Tests", info.Metadata.Creator);
        Assert.Contains("SharpCorePDF", info.Metadata.Producer);
        Assert.Equal(1, info.PageCount);
    }

    /// <summary>The writer can save straight to a stream.</summary>
    [Fact]
    public async Task Saves_to_a_stream()
    {
        var builder = PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with
        {
            CreationDate = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
        })
            .AddPage(PdfPageSize.A4, page => page.DrawText("Stream", 72, 72, Helvetica, 12));

        using var stream = new MemoryStream();
        await builder.SaveAsync(stream, Token);

        Assert.Equal(builder.ToArray(), stream.ToArray());
    }

    /// <summary>A colour is three bytes, from hexadecimal or from components.</summary>
    [Fact]
    public void Builds_a_colour_from_hexadecimal_and_components()
    {
        var colour = PdfColor.FromHex("#1F3A5F");

        Assert.Equal((byte)0x1F, colour.Red);
        Assert.Equal((byte)0x3A, colour.Green);
        Assert.Equal((byte)0x5F, colour.Blue);
        Assert.Equal(colour, PdfColor.FromRgb(0x1F, 0x3A, 0x5F));
        Assert.Equal("#1F3A5F", colour.ToString());
        Assert.Equal(PdfColor.Black, PdfColor.FromHex("000000"));
        Assert.Throws<ArgumentException>(() => PdfColor.FromHex("12345"));
    }

    /// <summary>An undefined standard font is refused where it is selected.</summary>
    [Fact]
    public void Refuses_an_undefined_standard_font()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfTypeface.Standard((StandardFont)99));
    }

    /// <summary>A document without a page is refused before anything is written.</summary>
    [Fact]
    public void Refuses_a_document_without_a_page()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PdfDocumentBuilder.Create().ToArray());

        Assert.Contains("at least one page", exception.Message);

        // default(PdfPageSize) bypasses the constructor, so the page itself checks its size.
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocumentBuilder.Create().AddPage(default));
    }

    /// <summary>Text outside Windows-1252 is refused by a standard font, with the reason.</summary>
    [Fact]
    public void Refuses_text_a_standard_font_cannot_encode()
    {
        var builder = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("→", 72, 72, Helvetica, 12));

        var exception = Assert.Throws<ArgumentException>(() => builder.ToArray());

        Assert.Contains("WinAnsiEncoding", exception.Message);
        Assert.Contains("embedded TrueType", exception.Message);
    }

    /// <summary>A line break belongs to a second call, not to one text operation.</summary>
    [Fact]
    public void Refuses_a_line_break_inside_a_text_operation()
    {
        var exception = Assert.Throws<ArgumentException>(() => PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("one\ntwo", 72, 72, Helvetica, 12)));

        Assert.Contains("one line", exception.Message);
    }

    /// <summary>A glyph the embedded font does not cover is named in the refusal.</summary>
    [Fact]
    public void Refuses_a_missing_glyph_of_an_embedded_font()
    {
        var builder = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("→", 72, 72, EmbeddedFace, 12));

        var exception = Assert.Throws<ArgumentException>(() => builder.ToArray());

        Assert.Contains("has no glyph", exception.Message);
    }

    /// <summary>An interlaced PNG is refused when it is handed over, not when the page is written.</summary>
    [Fact]
    public void Refuses_an_interlaced_png()
    {
        var exception = Assert.Throws<NotSupportedException>(() => PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(TestPng.Interlaced(4, 4), 0, 0, 4, 4)));

        Assert.Contains("interlaced", exception.Message);
    }

    /// <summary>A sixteen bit PNG is refused with an explanation rather than decoded as eight bit.</summary>
    [Fact]
    public void Refuses_a_sixteen_bit_png()
    {
        var exception = Assert.Throws<NotSupportedException>(() => PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawImage(TestPng.SixteenBit(4, 4), 0, 0, 4, 4)));

        Assert.Contains("bits per sample", exception.Message);
    }

    /// <summary>An identifier that is not sixteen bytes of hexadecimal is refused.</summary>
    [Fact]
    public void Refuses_an_unusable_identifier()
    {
        var options = PdfAuthoringOptions.Default with { DocumentId = "not-an-identifier" };

        var exception = Assert.Throws<InvalidOperationException>(() => PdfDocumentBuilder.Create(options)
            .AddPage(PdfPageSize.A4)
            .ToArray());

        Assert.Contains("32 hexadecimal", exception.Message);
    }
}
