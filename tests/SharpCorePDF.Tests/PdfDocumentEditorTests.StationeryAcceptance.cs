using System.Text;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// One test per acceptance criterion of issue #6, so the checklist can be read against a named
/// assertion rather than inferred from the behaviour tests.
/// </summary>
public sealed partial class PdfDocumentEditorTests
{
    /// <summary>Reads the XObjects a page's resources name, resolving each through the reader.</summary>
    /// <param name="reader">The document to read.</param>
    /// <param name="pageIndex">The zero-based index of the page.</param>
    /// <returns>The XObject streams, in resource order.</returns>
    private static List<PdfStream> XObjectsOf(PdfFileReader reader, int pageIndex)
    {
        var resources = reader.ResolveDictionary(reader.Pages[pageIndex].Dictionary.Get("Resources"));

        if (resources is null || reader.ResolveDictionary(resources.Get("XObject")) is not { } xobjects)
        {
            return [];
        }

        return xobjects.Entries
            .Select(entry => reader.Resolve(entry.Value))
            .OfType<PdfStream>()
            .ToList();
    }

    /// <summary>Criterion 1: different stationery PDFs apply to selected pages and page ranges.</summary>
    [Fact]
    public async Task Acceptance_criterion_1_applies_different_stationery_to_pages_and_ranges()
    {
        var stationery = PdfStationery.Create()
            .Assign(Template("letterhead"), PageRange.Single(1))
            .Assign(Template("footer"), PageRange.FromTo(2, 3));

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(stationery),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("letterhead", Compact(texts[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("footer", Compact(texts[0]), StringComparison.Ordinal);
        Assert.Contains("footer", Compact(texts[1]), StringComparison.Ordinal);
        Assert.Contains("footer", Compact(texts[2]), StringComparison.Ordinal);
    }

    /// <summary>Criterion 2: the default stationery covers every page without an assignment.</summary>
    [Fact]
    public async Task Acceptance_criterion_2_applies_default_stationery_to_the_remaining_pages()
    {
        var stationery = PdfStationery.Create()
            .Assign(Template("letterhead"), PageRange.Single(1))
            .AssignDefault(Template("footer"));

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(stationery),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("letterhead", Compact(texts[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("footer", Compact(texts[0]), StringComparison.Ordinal);
        Assert.Contains("footer", Compact(texts[1]), StringComparison.Ordinal);
        Assert.Contains("footer", Compact(texts[2]), StringComparison.Ordinal);
    }

    /// <summary>Criterion 3: a multi-page content PDF is supported.</summary>
    [Fact]
    public async Task Acceptance_criterion_3_supports_a_multi_page_content_pdf()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(
                PdfStationery.Create().AssignDefault(Template("letterhead"))),
            cancellationToken: Token);

        var info = await InspectAsync(edited);
        var texts = await TextsAsync(edited, 3);

        Assert.Equal(3, info.PageCount);
        Assert.Contains("one", texts[0], StringComparison.Ordinal);
        Assert.Contains("two", texts[1], StringComparison.Ordinal);
        Assert.Contains("three", texts[2], StringComparison.Ordinal);
    }

    /// <summary>Criterion 4: page dimensions and the position of the content are preserved.</summary>
    [Fact]
    public async Task Acceptance_criterion_4_preserves_page_dimensions_and_content_positioning()
    {
        var source = ThreePages();
        var before = await InspectAsync(source);

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(source, "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(
                PdfStationery.Create().AssignDefault(Template("letterhead"))),
            cancellationToken: Token);

        var after = await InspectAsync(edited);

        Assert.Equal(before.PageCount, after.PageCount);

        // The three content pages are A5, A4 and Letter; stationery must not resize any of them.
        for (var page = 0; page < before.PageCount; page++)
        {
            Assert.Equal(before.Pages[page].Size, after.Pages[page].Size);
        }

        // The content is untouched, byte for byte, so every letter keeps its baseline. The template's
        // text is 24 points and the content's is 12, which is how the two are told apart.
        for (var page = 1; page <= before.PageCount; page++)
        {
            var expected = await BaselinesAsync(source, page, letter => letter.FontSize <= 20);
            var actual = await BaselinesAsync(edited, page, letter => letter.FontSize <= 20);

            Assert.NotEmpty(expected);
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>Criterion 5: vector graphics and selectable text are preserved.</summary>
    [Fact]
    public async Task Acceptance_criterion_5_preserves_vector_graphics_and_selectable_text()
    {
        var template = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .DrawLine(
                    new PdfPoint(72, 72),
                    new PdfPoint(523, 72),
                    new PdfStroke(PdfPaint.Black, width: 0.5))
                .DrawText("letterhead", 72, 120, Helvetica, 18))
            .Build("letterhead.pdf");

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(
                PdfStationery.Create().AssignDefault(template)),
            cancellationToken: Token);

        // Both the content and the template are still selectable text.
        var texts = await TextsAsync(edited, 3);

        Assert.Contains("letterhead", Compact(texts[0]), StringComparison.Ordinal);
        Assert.Contains("one", Compact(texts[0]), StringComparison.Ordinal);

        // The template is a form XObject whose stream still holds the path and the text operators:
        // it was copied, not rasterized.
        var reader = Read(edited);
        var form = XObjectsOf(reader, 0)
            .Single(xobject => xobject.Dictionary.GetName("Subtype") == "Form");
        var operators = Encoding.ASCII.GetString(PdfFilters.Decode(form, reader.SourceName));

        Assert.Contains(" m", operators, StringComparison.Ordinal);
        Assert.Contains(" l", operators, StringComparison.Ordinal);
        Assert.True(
            operators.Contains("Tj", StringComparison.Ordinal) || operators.Contains("TJ", StringComparison.Ordinal),
            "the template's text operators are missing from its form XObject.");
    }

    /// <summary>Criterion 6: the result is a valid PDF whose content was not rasterized.</summary>
    [Fact]
    public async Task Acceptance_criterion_6_produces_a_valid_pdf_without_rasterization()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(
                PdfStationery.Create().AssignDefault(Template("letterhead"))),
            cancellationToken: Token);

        // Reading the whole document back proves the cross-reference table and every object it names.
        var reader = Read(edited);
        var info = await InspectAsync(edited);

        Assert.Equal(3, reader.Pages.Count);
        Assert.Equal(3, info.PageCount);

        for (var page = 0; page < reader.Pages.Count; page++)
        {
            var xobjects = XObjectsOf(reader, page);

            // The background is drawn from a form XObject, which is vector content.
            Assert.Contains(xobjects, xobject => xobject.Dictionary.GetName("Subtype") == "Form");

            // Neither the content nor the template carries an image, so an image XObject would be the
            // fingerprint of rasterizing them together. There is none.
            Assert.DoesNotContain(xobjects, xobject => xobject.Dictionary.GetName("Subtype") == "Image");
        }
    }
}
