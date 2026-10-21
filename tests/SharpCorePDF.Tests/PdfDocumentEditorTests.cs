using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the editor through the shape a caller uses: a source, an immutable plan, a result.
/// </summary>
/// <remarks>
/// The engine options enable <see cref="PdfEngineOptions.VerifyOutput"/>, so every edit also proves
/// that the document it wrote can be read back with the page count and rotations it claims. The
/// assertions then go through the public read side — the inspector and the content reader — so a
/// document that only looks right in the bytes it was built from does not pass.
/// </remarks>
public sealed class PdfDocumentEditorTests
{
    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default with { VerifyOutput = true };
    private static readonly PdfDocumentEditorService Editor = new(new PdfDocumentReader(Options), new PdfEditEngine(Options));
    private static readonly PdfInspectorService Inspector = new(new PdfDocumentReader(Options));
    private static readonly PdfContentReaderService Content = new(new PdfPageContentReader(Options));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfTypeface Helvetica => PdfTypeface.Standard(StandardFont.Helvetica);

    private static byte[] ThreePages() => PdfDocumentBuilder.Create()
        .AddPage(PdfPageSize.A5, page => page.DrawText("one", 72, 100, Helvetica, 12))
        .AddPage(PdfPageSize.A4, page => page.DrawText("two", 72, 100, Helvetica, 12))
        .AddPage(PdfPageSize.Letter, page => page.DrawText("three", 72, 100, Helvetica, 12))
        .ToArray();

    private static async Task<PdfDocumentInfo> InspectAsync(byte[] bytes) =>
        await Inspector.InspectAsync(new PdfByteArraySource(bytes, "edited.pdf"), cancellationToken: Token);

    /// <summary>Reads the page number the first top-level bookmark points at.</summary>
    private static int? FirstBookmarkPage(byte[] bytes)
    {
        var reader = new PdfFileReader(bytes, "bookmarks.pdf", Options);
        var outlines = reader.ResolveDictionary(reader.Catalog?.Get("Outlines"));
        var first = reader.ResolveDictionary(outlines?.Get("First"));

        if (reader.ResolveArray(first?.Get("Dest")) is not { Count: > 0 } destination
            || destination.Items[0] is not PdfReference target)
        {
            return null;
        }

        var page = reader.Pages.FirstOrDefault(candidate => candidate.ObjectNumber == target.ObjectNumber);
        return page?.Index + 1;
    }

    private static async Task<string[]> TextsAsync(byte[] bytes, int pageCount)
    {
        var source = new PdfByteArraySource(bytes, "edited.pdf");
        var texts = new string[pageCount];

        for (var page = 1; page <= pageCount; page++)
        {
            var result = await Content.ReadTextAsync(source, page, cancellationToken: Token);
            texts[page - 1] = string.Join(" ", result.Items.Select(block => block.Text));
        }

        return texts;
    }

    /// <summary>A move, a delete and a duplicate are applied in plan order, one-based after each step.</summary>
    [Fact]
    public async Task Reorders_deletes_and_duplicates_pages()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .MovePage(1, 3)
                .DeletePage(1)
                .DuplicatePage(2, 2),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal(3, info.PageCount);
        Assert.Equal(["three", "one", "one"], await TextsAsync(edited, info.PageCount));
    }

    /// <summary>Rotation is additive and normalised, and the read-back verification checks it.</summary>
    [Fact]
    public async Task Adds_rotations_to_the_rotation_a_page_already_has()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .RotatePage(1, PageRotation.Clockwise90)
                .RotatePage(1, PageRotation.Clockwise90)
                .RotatePage(3, PageRotation.Clockwise270),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal(PageRotation.Clockwise180, info.Pages[0].Rotation);
        Assert.Equal(PageRotation.None, info.Pages[1].Rotation);
        Assert.Equal(PageRotation.Clockwise270, info.Pages[2].Rotation);
        Assert.Equal(PdfPageSize.Letter.Transpose(), info.Pages[2].DisplaySize);
    }

    /// <summary>Metadata overrides the fields it sets and leaves the rest of the source values alone.</summary>
    [Fact]
    public async Task Overrides_the_metadata_it_sets_and_preserves_the_rest()
    {
        var source = PdfDocumentBuilder.Create()
            .WithMetadata(new PdfMetadata { Title = "Old title", Author = "Old author" })
            .AddPage(PdfPageSize.A5, page => page.DrawText("body", 72, 100, Helvetica, 12))
            .ToArray();

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(source, "with-metadata.pdf"),
            PdfEditPlan.Create().SetMetadata(new PdfMetadata { Title = "New title" }),
            cancellationToken: Token);

        var info = await InspectAsync(edited);

        Assert.Equal("New title", info.Metadata.Title);
        Assert.Equal("Old author", info.Metadata.Author);
        Assert.StartsWith("SharpCorePDF", info.Metadata.Producer, StringComparison.Ordinal);
        Assert.NotNull(info.Metadata.ModificationDate);
    }

    /// <summary>An operation beyond the last page is refused before the destination is written.</summary>
    [Fact]
    public async Task Refuses_a_page_that_does_not_exist()
    {
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().DeletePage(4),
            destination,
            cancellationToken: Token));

        Assert.Equal(0, destination.Length);
    }

    /// <summary>An edit may not delete every page.</summary>
    [Fact]
    public async Task Refuses_to_write_a_document_without_pages()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().DeletePage(1).DeletePage(1).DeletePage(1),
            cancellationToken: Token));
    }

    /// <summary>A fixed modification date and document identifier make repeated edits byte-identical.</summary>
    [Fact]
    public async Task Writes_the_same_bytes_for_the_same_input()
    {
        var output = PdfEditOutputOptions.Default with
        {
            ModificationDate = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
            DocumentId = "reproducible-edit",
        };

        var plan = PdfEditPlan.Create(output)
            .RotatePage(1, PageRotation.Clockwise90)
            .DeletePage(2);

        var first = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            plan,
            cancellationToken: Token);

        var second = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            plan,
            cancellationToken: Token);

        Assert.Equal(first, second);
    }

    /// <summary>Page-independent document features survive an edit; bookmarks still point at real pages.</summary>
    [Fact]
    public async Task Preserves_bookmarks_of_the_source()
    {
        var source = await File.ReadAllBytesAsync(TestAssets.Outlined, Token);
        var before = await InspectAsync(source);
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(source, "outlined.pdf"),
            PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90),
            destination,
            cancellationToken: Token);

        var after = await InspectAsync(destination.ToArray());

        Assert.True(before.HasOutlines);
        Assert.True(after.HasOutlines);
        Assert.Equal(before.OutlineItemCount, after.OutlineItemCount);
        Assert.Equal(before.PageCount, after.PageCount);
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.PageLabelsDropped);
    }

    /// <summary>A bookmark whose page was deleted points at the first page that survived.</summary>
    [Fact]
    public async Task Redirects_a_bookmark_whose_page_was_deleted()
    {
        var source = await File.ReadAllBytesAsync(TestAssets.Outlined, Token);
        using var destination = new MemoryStream();

        await Editor.ApplyAsync(
            new PdfByteArraySource(source, "outlined.pdf"),
            PdfEditPlan.Create().DeletePage(1),
            destination,
            cancellationToken: Token);

        Assert.Equal(1, FirstBookmarkPage(destination.ToArray()));
    }

    /// <summary>Changing the layout drops page labels, whose numbers no longer match the pages.</summary>
    [Fact]
    public async Task Drops_page_labels_when_the_page_order_changes()
    {
        var source = await File.ReadAllBytesAsync(TestAssets.Outlined, Token);
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(source, "outlined.pdf"),
            PdfEditPlan.Create().DeletePage(2),
            destination,
            cancellationToken: Token);

        var info = await InspectAsync(destination.ToArray());

        Assert.Equal(2, info.PageCount);
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PageLabelsDropped);
    }

    /// <summary>Overriding information drops a stale XMP packet, whether the override is an operation or an option.</summary>
    [Fact]
    public async Task Drops_xmp_when_metadata_overrides_the_information_dictionary()
    {
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(PdfContentFixtures.Metadata(), "with-xmp.pdf"),
            PdfEditPlan.Create().SetMetadata(new PdfMetadata { Title = "New title" }),
            destination,
            cancellationToken: Token);

        var edited = destination.ToArray();
        var info = await InspectAsync(edited);

        Assert.Equal("New title", info.Metadata.Title);
        Assert.Equal(string.Empty, TestAssets.ReadMetadata(edited));
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.XmpPacketDropped);
    }

    /// <summary>An edit that leaves document information alone keeps the XMP packet.</summary>
    [Fact]
    public async Task Keeps_xmp_when_metadata_is_not_overridden()
    {
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(PdfContentFixtures.Metadata(), "with-xmp.pdf"),
            PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90),
            destination,
            cancellationToken: Token);

        Assert.Equal(PdfContentFixtures.XmpText, TestAssets.ReadMetadata(destination.ToArray()));
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.XmpPacketDropped);
    }

    /// <summary>Output options are validated as part of the plan, before a source is opened.</summary>
    [Fact]
    public void Validates_output_options_without_a_document()
    {
        var plan = PdfEditPlan.Create(PdfEditOutputOptions.Default with
        {
            TargetVersion = new PdfVersion(1, 3),
            Producer = " ",
            DocumentId = string.Empty,
        });

        var errors = plan.Validate();

        Assert.Contains(errors, error => error.Contains("1.3", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("producer", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("identifier", StringComparison.Ordinal));
        Assert.Throws<PdfEditPlanValidationException>(() => plan.EnsureValid());
    }
}
