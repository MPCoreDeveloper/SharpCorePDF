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
/// Tests of the document-level edits: the bookmarks, the page labels, the viewer preferences and the
/// XMP packet an edit writes in place of the ones of the source.
/// </summary>
public sealed partial class PdfDocumentEditorTests
{
    /// <summary>Reads the document that was written, as the engine wrote it.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The reader of the result.</returns>
    private static PdfFileReader Read(byte[] bytes) => new(bytes, "edited.pdf", Options);

    /// <summary>The viewer preferences an edit states replace the ones of the source.</summary>
    [Fact]
    public async Task Writes_the_viewer_preferences_it_is_given()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().SetViewerPreferences(new PdfViewerPreferences
            {
                HideToolbar = true,
                HideMenubar = true,
                HideWindowUi = true,
                FitWindow = true,
                CenterWindow = true,
                DisplayDocTitle = true,
                PageLayout = PdfPageLayout.TwoPageLeft,
                PageMode = PdfPageMode.UseOutlines,
                ReadingDirection = PdfReadingDirection.RightToLeft,
                PrintScaling = PdfPrintScaling.None,
            }),
            cancellationToken: Token);

        var reader = Read(edited);
        var catalog = reader.Catalog!;
        var viewer = reader.ResolveDictionary(catalog.Get("ViewerPreferences"))!;

        Assert.Equal("TwoPageLeft", catalog.GetName("PageLayout"));
        Assert.Equal("UseOutlines", catalog.GetName("PageMode"));
        Assert.Equal("R2L", viewer.GetName("Direction"));
        Assert.Equal("None", viewer.GetName("PrintScaling"));
        Assert.True(((PdfBoolean)viewer.Get("HideToolbar")!).Value);
        Assert.True(((PdfBoolean)viewer.Get("HideMenubar")!).Value);
        Assert.True(((PdfBoolean)viewer.Get("HideWindowUI")!).Value);
        Assert.True(((PdfBoolean)viewer.Get("FitWindow")!).Value);
        Assert.True(((PdfBoolean)viewer.Get("CenterWindow")!).Value);
        Assert.True(((PdfBoolean)viewer.Get("DisplayDocTitle")!).Value);
    }

    /// <summary>Each range becomes an entry of the number tree, keyed by the index of its first page.</summary>
    [Fact]
    public async Task Writes_the_page_labels_it_is_given()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().SetPageLabels(PdfPageLabeling.From(
                new PdfPageLabelRange { StartPage = 1, Style = PageLabelStyle.LowerRoman, Prefix = "cover-" },
                new PdfPageLabelRange { StartPage = 2, Style = PageLabelStyle.Decimal, StartNumber = 7 })),
            cancellationToken: Token);

        var reader = Read(edited);
        var labels = reader.ResolveDictionary(reader.Catalog!.Get("PageLabels"))!;
        var nums = reader.ResolveArray(labels.Get("Nums"))!;

        Assert.Equal(4, nums.Count);
        Assert.Equal(0, ((PdfInteger)reader.Resolve(nums.Items[0])!).Value);

        var first = reader.ResolveDictionary(nums.Items[1])!;

        Assert.Equal("r", first.GetName("S"));
        Assert.Equal("cover-", first.GetText("P"));
        Assert.Null(first.GetInteger("St"));

        Assert.Equal(1, ((PdfInteger)reader.Resolve(nums.Items[2])!).Value);

        var second = reader.ResolveDictionary(nums.Items[3])!;

        Assert.Equal("D", second.GetName("S"));
        Assert.Equal(7, second.GetInteger("St"));
    }

    /// <summary>Labels stated explicitly are written even when the edit reordered the pages.</summary>
    [Fact]
    public async Task Writes_the_page_labels_it_is_given_after_the_pages_were_reordered()
    {
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .MovePage(3, 1)
                .SetPageLabels(PdfPageLabeling.Decimal(1, "p.")),
            destination,
            cancellationToken: Token);

        var reader = Read(destination.ToArray());

        Assert.NotNull(reader.Catalog!.Get("PageLabels"));
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.PageLabelsDropped);
    }

    /// <summary>Stating no labels removes the ones of the source instead of dropping them as a surprise.</summary>
    [Fact]
    public async Task Removes_the_page_labels_of_the_source_when_none_are_set()
    {
        var source = await File.ReadAllBytesAsync(TestAssets.Outlined, Token);
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(source, "outlined.pdf"),
            PdfEditPlan.Create().SetPageLabels(PdfPageLabeling.None),
            destination,
            cancellationToken: Token);

        Assert.NotNull(Read(source).Catalog!.Get("PageLabels"));
        Assert.Null(Read(destination.ToArray()).Catalog!.Get("PageLabels"));
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.PageLabelsDropped);
    }

    /// <summary>A range that starts past the last page is refused, as an operation that names a page is.</summary>
    [Fact]
    public async Task Refuses_page_labels_that_start_beyond_the_last_page()
    {
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().SetPageLabels(PdfPageLabeling.Decimal(4)),
            destination,
            cancellationToken: Token));
    }

    /// <summary>An outline replaces the bookmarks of the source, and its pages are the ones the edit leaves.</summary>
    [Fact]
    public async Task Writes_the_bookmarks_it_is_given()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .DeletePage(1)
                .SetOutline(PdfOutline.From(
                    new PdfOutlineItem
                    {
                        Title = "First",
                        Destination = new PdfDestination { PageNumber = 1 },
                        Children =
                        [
                            new PdfOutlineItem
                            {
                                Title = "Detail",
                                Destination = new PdfDestination { PageNumber = 2 },
                            },
                        ],
                    },
                    new PdfOutlineItem
                    {
                        Title = "Site",
                        Destination = new PdfDestination { Uri = "https://example.org" },
                    })),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.True(info.HasOutlines);
        Assert.Equal(3, info.OutlineItemCount);
        Assert.Equal(1, FirstBookmarkPage(edited));
    }

    /// <summary>An outline with no bookmark removes the panel the source carried.</summary>
    [Fact]
    public async Task Removes_the_outline_of_the_source_when_it_is_empty()
    {
        var source = await File.ReadAllBytesAsync(TestAssets.Outlined, Token);
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(source, "outlined.pdf"),
            PdfEditPlan.Create().SetOutline(PdfOutline.Empty),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.True((await InspectAsync(source)).HasOutlines);
        Assert.False(info.HasOutlines);
        Assert.Equal(0, info.OutlineItemCount);
    }

    /// <summary>A bookmark that names a page past the end of the result is refused before anything is written.</summary>
    [Fact]
    public async Task Refuses_a_bookmark_that_names_a_page_the_edited_document_does_not_have()
    {
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().SetOutline(PdfOutline.From(new PdfOutlineItem
            {
                Title = "Nowhere",
                Destination = new PdfDestination { PageNumber = 4 },
            })),
            destination,
            cancellationToken: Token));

        Assert.Equal(0, destination.Length);
    }

    /// <summary>The packet an edit states replaces the one of the source, and is not reported as dropped.</summary>
    [Fact]
    public async Task Replaces_the_xmp_packet_it_is_given()
    {
        const string Packet =
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>";

        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(PdfContentFixtures.Metadata(), "with-xmp.pdf"),
            PdfEditPlan.Create().SetXmp(Encoding.UTF8.GetBytes(Packet)),
            destination,
            cancellationToken: Token);

        Assert.Equal(Packet, TestAssets.ReadMetadata(destination.ToArray()));
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.XmpPacketDropped);
    }
}
