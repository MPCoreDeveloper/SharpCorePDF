using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the page detail: one record per page, yielded as each page is read, carrying what the other
/// readers found on it and the remarks that belong to it.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>Every page of the range is yielded, in order, with what it carries.</summary>
    [Fact]
    public async Task Yields_one_detail_per_page()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var pages = await Details(source, PageRange.All);

        Assert.Equal(2, pages.Count);
        Assert.Equal([1, 2], pages.Select(page => page.PageNumber));
        Assert.Equal(3, pages[0].Images.Count);
        Assert.Equal(4, pages[1].Images.Count);
        Assert.All(pages, page => Assert.Equal(PdfPageSize.FromPoints(200d, 200d), page.Size));
        Assert.All(pages, page => Assert.Equal(PageRotation.None, page.Rotation));
    }

    /// <summary>A page with nothing on it is still yielded, with empty collections.</summary>
    [Fact]
    public async Task Yields_a_page_with_nothing_on_it_with_empty_collections()
    {
        var source = Source(PdfContentFixtures.WithoutMetadata(), "plain.pdf");

        var pages = await Details(source, PageRange.All);
        var page = Assert.Single(pages);

        Assert.Empty(page.Images);
        Assert.Empty(page.Annotations);
        Assert.Empty(page.Links);
        Assert.Empty(page.FormFields);
        Assert.Empty(page.Warnings);
    }

    /// <summary>A damaged image costs the image, not the page: the detail is still yielded.</summary>
    [Fact]
    public async Task Yields_a_detail_for_a_damaged_image()
    {
        var source = Source(PdfContentFixtures.DamagedImage(), "damaged.pdf");

        var pages = await Details(source, PageRange.All);
        var page = Assert.Single(pages);

        Assert.Single(page.Images);
        Assert.False(page.Images[0].IsDecodable);
        Assert.Equal(PdfPageSize.FromPoints(200d, 200d), page.Size);
        AssertWarning(page.Warnings, PdfWarningKind.ImageDataUnreadable);
    }

    /// <summary>The widgets of a form field appear on the page that carries them.</summary>
    [Fact]
    public async Task Carries_the_form_widgets_of_the_page()
    {
        var source = Source(PdfContentFixtures.Form(), "form.pdf");

        var pages = await Details(source, PageRange.All);
        var page = Assert.Single(pages);

        Assert.Equal(["group", "person.name", "sig"], page.FormFields.Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.All(page.FormFields, field => Assert.All(field.Widgets, widget => Assert.Equal(1, widget.PageNumber)));
    }

    /// <summary>A range past the last page yields nothing, for the same reason it reads nothing.</summary>
    [Fact]
    public async Task Yields_nothing_for_a_range_past_the_last_page()
    {
        var source = Source(PdfContentFixtures.Images(), "images.pdf");

        var pages = await Details(source, PageRange.FromTo(5, 500));

        Assert.Empty(pages);
    }

    /// <summary>The annotations of a page are gathered into the detail, links included.</summary>
    [Fact]
    public async Task Gathers_annotations_and_links()
    {
        var source = Source(PdfContentFixtures.Annotations(), "annotations.pdf");

        var pages = await Details(source, PageRange.All);
        var page = Assert.Single(pages);

        Assert.Equal(8, page.Annotations.Count);
        Assert.Equal(4, page.Links.Count);
        Assert.All(page.Links, link => Assert.True(link.IsLink));
    }

    /// <summary>
    /// A document whose security handler will not open is still paged out, because the page tree is not
    /// encrypted: each page keeps its number and loses its content to a remark instead of an exception.
    /// </summary>
    [Fact]
    public async Task Reports_pages_of_a_document_whose_content_could_not_be_decrypted()
    {
        var source = PdfFileSource.From(TestAssets.Encrypted);
        var pages = new List<PdfPageDetail>();

        await foreach (var detail in Reader().ReadPageDetailsAsync(source, PageRange.All, cancellationToken: Token))
        {
            pages.Add(detail);
        }

        var page = Assert.Single(pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Empty(page.Images);
        AssertWarning(page.Warnings, PdfWarningKind.ContentStreamUnreadable);
        Assert.DoesNotContain(page.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionNotOpened);
    }

    private static async Task<List<PdfPageDetail>> Details(IPdfContentSource source, PageRange range)
    {
        var pages = new List<PdfPageDetail>();

        await foreach (var page in Reader().ReadPageDetailsAsync(source, range, cancellationToken: Token))
        {
            pages.Add(page);
        }

        return pages;
    }
}
