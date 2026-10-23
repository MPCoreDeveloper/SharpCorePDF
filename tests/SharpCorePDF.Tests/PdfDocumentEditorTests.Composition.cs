using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the edits that combine documents: a page imported from another document, a page drawn by
/// the caller, appended pages, an explicit page order, an extraction, and a split into several
/// documents.
/// </summary>
public sealed partial class PdfDocumentEditorTests
{
    /// <summary>A two-page document whose pages are labelled so they can be told apart.</summary>
    private static byte[] Appendix() => PdfDocumentBuilder.Create()
        .AddPage(PdfPageSize.A5, page => page.DrawText("appendix", 72, 100, Helvetica, 12))
        .AddPage(PdfPageSize.A5, page => page.DrawText("tail", 72, 100, Helvetica, 12))
        .ToArray();

    /// <summary>A page of another document is copied in at the position the plan names.</summary>
    [Fact]
    public async Task Imports_a_page_of_another_document()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ImportPage(new PdfByteArraySource(Appendix(), "appendix.pdf"), 2, 2),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal(4, info.PageCount);
        Assert.Equal(["one", "tail", "two", "three"], await TextsAsync(edited, info.PageCount));
    }

    /// <summary>An import that names a page the other document does not have writes nothing.</summary>
    [Fact]
    public async Task Refuses_to_import_a_page_the_other_document_does_not_have()
    {
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ImportPage(new PdfByteArraySource(Appendix(), "appendix.pdf"), 3, 1),
            destination,
            cancellationToken: Token));

        Assert.Equal(0, destination.Length);
    }

    /// <summary>A page the caller draws is inserted with the size and the content it was drawn with.</summary>
    [Fact]
    public async Task Inserts_a_page_that_a_callback_draws()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().InsertPage(
                4,
                PdfPageSize.A4,
                page => page.DrawText("inserted", 72, 100, Helvetica, 12)),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal(4, info.PageCount);
        Assert.Equal(PdfPageSize.A4.WidthPoints, info.Pages[3].Size.WidthPoints, 3);
        Assert.Equal(PdfPageSize.A4.HeightPoints, info.Pages[3].Size.HeightPoints, 3);
        Assert.Equal(["one", "two", "three", "inserted"], await TextsAsync(edited, info.PageCount));
    }

    /// <summary>The document a page is inserted from must hold that page alone.</summary>
    [Fact]
    public async Task Refuses_to_insert_a_document_that_has_more_than_one_page()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().InsertPage(1, PdfPageSize.A4, new PdfByteArraySource(Appendix(), "appendix.pdf")),
            cancellationToken: Token));
    }

    /// <summary>Appending copies every page of the other document after the last page.</summary>
    [Fact]
    public async Task Appends_every_page_of_another_document()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().Append(new PdfByteArraySource(Appendix(), "appendix.pdf")),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal(5, info.PageCount);
        Assert.Equal(["one", "two", "three", "appendix", "tail"], await TextsAsync(edited, info.PageCount));
    }

    /// <summary>An appended range stops at the last page of the other document instead of failing.</summary>
    [Fact]
    public async Task Appends_a_range_that_reaches_past_the_end_of_the_other_document()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().Append(
                new PdfByteArraySource(Appendix(), "appendix.pdf"),
                PageRange.FromTo(2, 9)),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal(4, info.PageCount);
        Assert.Equal(["one", "two", "three", "tail"], await TextsAsync(edited, info.PageCount));
    }

    /// <summary>A range that begins past the other document has no page to append, so it is refused.</summary>
    [Fact]
    public async Task Refuses_to_append_a_range_that_begins_past_the_other_document()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().Append(
                new PdfByteArraySource(Appendix(), "appendix.pdf"),
                PageRange.FromTo(3, 4)),
            cancellationToken: Token));
    }

    /// <summary>The pages are rearranged to the order the plan lists, which is resolved one-based.</summary>
    [Fact]
    public async Task Puts_the_pages_in_the_listed_order()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ReorderPages(3, 1, 2),
            cancellationToken: Token);

        Assert.Equal(["three", "one", "two"], await TextsAsync(edited, 3));
    }

    /// <summary>An order that leaves a page out is not a permutation, so the plan is refused.</summary>
    [Fact]
    public async Task Refuses_an_order_that_leaves_a_page_out()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ReorderPages(1, 2),
            cancellationToken: Token));
    }

    /// <summary>An extraction keeps the pages of its range, in their existing order.</summary>
    [Fact]
    public async Task Keeps_only_the_pages_of_the_range()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ExtractPages(PageRange.FromTo(2, 3)),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal(2, info.PageCount);
        Assert.Equal(["two", "three"], await TextsAsync(edited, info.PageCount));
    }

    /// <summary>An extraction that selects no page would leave nothing to write, so it is refused.</summary>
    [Fact]
    public async Task Refuses_a_range_that_keeps_no_page()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ExtractPages(PageRange.FromTo(4, 5)),
            cancellationToken: Token));
    }

    /// <summary>A split cuts the edited document into parts of the requested size, numbered from one.</summary>
    [Fact]
    public async Task Splits_the_document_into_parts_of_the_requested_size()
    {
        var result = await Editor.SplitAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().SplitDocument(2),
            cancellationToken: Token);

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(3, result.PageCount);
        Assert.Equal([1, 2], result.Parts.Select(part => part.PartNumber).ToArray());
        Assert.Equal([2, 1], result.Parts.Select(part => part.PageCount).ToArray());
        Assert.Equal(PageRange.FromTo(1, 2), result.Parts[0].Pages);
        Assert.Equal(PageRange.FromTo(3, 3), result.Parts[1].Pages);
        Assert.Equal(["one", "two"], await TextsAsync(result.Parts[0].Content, 2));
        Assert.Equal(["three"], await TextsAsync(result.Parts[1].Content, 1));
        Assert.All(result.Parts, part =>
            Assert.Contains(part.Result.Warnings, warning => warning.Kind == PdfWarningKind.OutputVerified));
    }

    /// <summary>Every other operation runs before the split, so the parts are cut from the edited document.</summary>
    [Fact]
    public async Task Splits_the_edited_document_after_the_other_operations()
    {
        var result = await Editor.SplitAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .ReorderPages(3, 2, 1)
                .RotatePage(1, PageRotation.Clockwise90)
                .SplitDocument(2),
            cancellationToken: Token);

        Assert.Equal(["three", "two"], await TextsAsync(result.Parts[0].Content, 2));
        Assert.Equal(["one"], await TextsAsync(result.Parts[1].Content, 1));

        var first = await InspectAsync(result.Parts[0].Content);

        Assert.Equal(PageRotation.Clockwise90, first.Pages[0].Rotation);
    }

    /// <summary>A split produces several documents, so it cannot be written to a single destination.</summary>
    [Fact]
    public async Task Refuses_to_apply_a_split_to_a_single_destination()
    {
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().SplitDocument(2),
            destination,
            cancellationToken: Token));

        Assert.Equal(0, destination.Length);
    }

    /// <summary>A split needs exactly one split operation: none has nothing to cut, two are ambiguous.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Refuses_a_split_plan_that_does_not_hold_exactly_one_split(int splits)
    {
        var plan = PdfEditPlan.Create();

        for (var index = 0; index < splits; index++)
        {
            plan = plan.SplitDocument(2);
        }

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.SplitAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            plan,
            cancellationToken: Token));
    }

    /// <summary>A split that gives each part no page is a plan error, refused before the source is read.</summary>
    [Fact]
    public async Task Refuses_a_split_that_gives_each_part_no_page()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.SplitAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().SplitDocument(0),
            cancellationToken: Token));
    }
}
