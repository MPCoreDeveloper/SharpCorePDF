using System.Text;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the budget a page has for the forms it paints: how many paints, so that a small document cannot ask
/// for billions of them, and that the budget belongs to one page and not to the read.
/// </summary>
/// <remarks>
/// The depth limit and the guard against a form that paints itself bound how deep a page goes, not how much it
/// does. A form that paints the next form twenty times, four levels down, is under both and asks for eight
/// thousand paints (1 + 20 + 400 + 8,000); six levels of forty is four billion, from a document of a few kilobytes.
/// </remarks>
public sealed class PdfFormBudgetTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A page that paints forms past the limit is read as far as the limit and the caller is told.</summary>
    [Fact]
    public async Task Stops_reading_the_text_of_a_page_that_paints_more_forms_than_the_limit_and_says_so()
    {
        var document = FanOut(pages: 1, levels: 4, width: 20);

        var result = await Reader(maxPaints: 1_000).ReadTextAsync(Source(document), PageRange.All, cancellationToken: Token);

        var warning = Assert.Single(result.Warnings, w => w.Kind == PdfWarningKind.FormPaintLimitExceeded);

        Assert.Contains("page 1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("1000", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>The walk that looks for images keeps the same budget, and what it found before the limit is kept.</summary>
    [Fact]
    public async Task Stops_reading_the_images_of_a_page_that_paints_more_forms_than_the_limit_and_says_so()
    {
        var document = FanOut(pages: 1, levels: 4, width: 20);

        var result = await Reader(maxPaints: 1_000).ReadImagesAsync(Source(document), PageRange.All, cancellationToken: Token);

        Assert.Single(result.Warnings, w => w.Kind == PdfWarningKind.FormPaintLimitExceeded);
    }

    /// <summary>A page that stays within the limit is read completely and nothing is reported.</summary>
    [Fact]
    public async Task Reads_a_page_that_paints_fewer_forms_than_the_limit_without_a_remark()
    {
        var document = FanOut(pages: 1, levels: 2, width: 10);
        var reader = Reader(maxPaints: 1_000);

        var text = await reader.ReadTextAsync(Source(document), PageRange.All, cancellationToken: Token);
        var images = await reader.ReadImagesAsync(Source(document), PageRange.All, cancellationToken: Token);

        Assert.DoesNotContain(text.Warnings, w => w.Kind == PdfWarningKind.FormPaintLimitExceeded);
        Assert.DoesNotContain(images.Warnings, w => w.Kind == PdfWarningKind.FormPaintLimitExceeded);
    }

    /// <summary>
    /// The budget is a page's own: two pages that each stay within the limit are not charged together, so a long
    /// document is not cut off for painting forms on every page.
    /// </summary>
    [Fact]
    public async Task Gives_every_page_its_own_budget()
    {
        // 31 paints a page (the form, and the thirty paints it makes), 62 over the two: under 40 each, over 40 together.
        var document = FanOut(pages: 2, levels: 2, width: 30);
        var reader = Reader(maxPaints: 40);

        var text = await reader.ReadTextAsync(Source(document), PageRange.All, cancellationToken: Token);
        var images = await reader.ReadImagesAsync(Source(document), PageRange.All, cancellationToken: Token);

        Assert.DoesNotContain(text.Warnings, w => w.Kind == PdfWarningKind.FormPaintLimitExceeded);
        Assert.DoesNotContain(images.Warnings, w => w.Kind == PdfWarningKind.FormPaintLimitExceeded);
    }

    /// <summary>The limit is an option with a default that real pages sit far below, and zero is not a limit.</summary>
    [Fact]
    public void States_the_limit_as_an_option_a_page_cannot_ask_to_be_without()
    {
        Assert.Equal(100_000, PdfEngineOptions.Default.MaxFormPaintsPerPage);
        Assert.Contains(
            (PdfEngineOptions.Default with { MaxFormPaintsPerPage = 0 }).Validate(),
            error => error.Contains("MaxFormPaintsPerPage", StringComparison.Ordinal));
        Assert.Empty((PdfEngineOptions.Default with { MaxFormPaintsPerPage = 1 }).Validate());
    }

    private static PdfContentReaderService Reader(int maxPaints) =>
        new(new PdfPageContentReader(PdfEngineOptions.Default with { MaxFormPaintsPerPage = maxPaints }));

    private static PdfByteArraySource Source(byte[] bytes) => new(bytes, "fan-out.pdf");

    /// <summary>
    /// Builds a document whose every page paints one form, which paints the next <paramref name="width"/> times,
    /// <paramref name="levels"/> forms deep. No form paints itself, so no guard against a cycle applies.
    /// </summary>
    private static byte[] FanOut(int pages, int levels, int width)
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var tree = builder.Reserve();
        var firstPage = 3;
        var firstContent = firstPage + pages;
        var firstForm = firstContent + pages;
        var kids = string.Join(' ', Enumerable.Range(0, pages).Select(page => $"{firstPage + page} 0 R"));

        for (var page = 0; page < pages; page++)
        {
            builder.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /XObject << /F0 {firstForm} 0 R >> >> /Contents {firstContent + page} 0 R >>");
        }

        for (var page = 0; page < pages; page++)
        {
            builder.AddStream(string.Empty, Encoding.Latin1.GetBytes("/F0 Do"));
        }

        for (var level = 0; level < levels; level++)
        {
            var last = level == levels - 1;
            var content = last ? "0 0 m 1 1 l S\n" : string.Concat(Enumerable.Repeat($"q /F{level + 1} Do Q\n", width));
            var resources = last ? "<< >>" : $"<< /XObject << /F{level + 1} {firstForm + level + 1} 0 R >> >>";

            builder.AddStream($"/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources {resources}", Encoding.Latin1.GetBytes(content));
        }

        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(tree, $"<< /Type /Pages /Kids [{kids}] /Count {pages} >>");

        return builder.Build(catalog);
    }
}
