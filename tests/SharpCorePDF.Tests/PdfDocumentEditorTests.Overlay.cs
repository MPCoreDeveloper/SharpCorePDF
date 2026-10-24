using System.Text;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the layers: a page of another document recorded over or under a page of the edited one,
/// with the order, the clipping and the resources the contract states.
/// </summary>
public sealed partial class PdfDocumentEditorTests
{
    /// <summary>Builds a one-page document, which is the shape an overlay or underlay takes.</summary>
    /// <param name="text">The text the page draws.</param>
    /// <returns>The bytes of the document.</returns>
    private static byte[] Stamped(string text = "stamp") => PdfDocumentBuilder.Create()
        .AddPage(PdfPageSize.A5, page => page.DrawText(text, 72, 300, Helvetica, 24))
        .ToArray();

    /// <summary>Reads the operators of one content stream of a page.</summary>
    /// <param name="reader">The document the page belongs to.</param>
    /// <param name="stream">The stream.</param>
    /// <returns>The decoded operators.</returns>
    private static string OperatorsOf(PdfFileReader reader, PdfObject? stream) =>
        reader.Resolve(stream) is PdfStream decoded
            ? Encoding.ASCII.GetString(PdfFilters.Decode(decoded, reader.SourceName))
            : string.Empty;

    /// <summary>
    /// Removes the spaces a text read of a layer page inserts between the glyphs it draws, so the words
    /// of the layer can be found in the text of the page it was applied to.
    /// </summary>
    /// <param name="text">The text a page read returned.</param>
    /// <returns>The text without any whitespace.</returns>
    private static string Compact(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character)));

    /// <summary>An overlay is drawn last, so the text of the layer and of the page are both read.</summary>
    [Fact]
    public async Task Draws_an_overlay_page_over_the_page_it_is_applied_to()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().Overlay(new PdfByteArraySource(Stamped(), "stamp.pdf"), 1),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("one", texts[0], StringComparison.Ordinal);
        Assert.Contains("stamp", Compact(texts[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("stamp", Compact(texts[1]), StringComparison.Ordinal);
    }

    /// <summary>An underlay is the first stream of the page, and the page's own streams follow it.</summary>
    [Fact]
    public async Task Draws_an_underlay_page_before_the_page_it_is_applied_to()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().Underlay(new PdfByteArraySource(Stamped("backdrop"), "backdrop.pdf"), 2),
            cancellationToken: Token);

        var reader = Read(edited);
        var contents = reader.ResolveArray(reader.Pages[1].Dictionary.Get("Contents"))!;

        Assert.True(contents.Count >= 2);
        Assert.Contains(" Do", OperatorsOf(reader, contents.Items[0]), StringComparison.Ordinal);
        Assert.DoesNotContain(" Do", OperatorsOf(reader, contents.Items[^1]), StringComparison.Ordinal);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("backdrop", Compact(texts[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("backdrop", Compact(texts[0]), StringComparison.Ordinal);
    }

    /// <summary>The opacity and the blend mode of a layer travel through a graphics state of its own.</summary>
    [Fact]
    public async Task Writes_the_opacity_and_blend_mode_of_a_layer_through_a_graphics_state()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().Overlay(
                new PdfByteArraySource(Stamped(), "stamp.pdf"),
                1,
                new PdfOverlayOptions
                {
                    Opacity = 0.5,
                    BlendMode = PdfOverlayBlendMode.Multiply,
                    Placement = PdfOverlayPlacement.Stretch,
                }),
            cancellationToken: Token);

        var reader = Read(edited);
        var resources = reader.ResolveDictionary(reader.Pages[0].Dictionary.Get("Resources"))!;
        var states = reader.ResolveDictionary(resources.Get("ExtGState"))!;
        var state = reader.ResolveDictionary(states.Get(states.Keys[0]))!;

        Assert.Equal(0.5, state.GetNumber("ca"));
        Assert.Equal(0.5, state.GetNumber("CA"));
        Assert.Equal("Multiply", state.GetName("BM"));
    }

    /// <summary>The layer travels with the page it was applied to when the pages are reordered.</summary>
    [Fact]
    public async Task Keeps_the_layer_on_the_page_it_was_applied_to()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .Overlay(new PdfByteArraySource(Stamped(), "stamp.pdf"), 2)
                .MovePage(2, 1),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("stamp", Compact(texts[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("stamp", Compact(texts[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("stamp", Compact(texts[2]), StringComparison.Ordinal);
    }

    /// <summary>A layer is one page, so a document with more than one page is refused.</summary>
    [Fact]
    public async Task Refuses_a_layer_document_that_has_more_than_one_page()
    {
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().Overlay(new PdfByteArraySource(ThreePages(), "layer.pdf"), 1),
            destination,
            cancellationToken: Token));

        Assert.Equal(0, destination.Length);
    }

    /// <summary>A layer and a resize coexist: the frame wraps the page and the layer is drawn around it.</summary>
    [Fact]
    public async Task Draws_a_layer_on_a_page_the_edit_resized()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .ResizePage(1, PdfPageSize.A4)
                .Overlay(new PdfByteArraySource(Stamped(), "stamp.pdf"), 1),
            cancellationToken: Token);

        var reader = Read(edited);
        var contents = reader.ResolveArray(reader.Pages[0].Dictionary.Get("Contents"))!;

        Assert.Equal(4, contents.Count);
        Assert.Contains(" cm", OperatorsOf(reader, contents.Items[0]), StringComparison.Ordinal);
        Assert.Contains(" Do", OperatorsOf(reader, contents.Items[^1]), StringComparison.Ordinal);

        var info = await InspectAsync(edited);

        Assert.Equal(PdfPageSize.A4.WidthPoints, info.Pages[0].Size.WidthPoints, 2);
        Assert.Equal(PdfPageSize.A4.HeightPoints, info.Pages[0].Size.HeightPoints, 2);
    }

    /// <summary>A duplicate draws the layer of the page it copies, and the form is written once.</summary>
    [Fact]
    public async Task Shares_the_layer_of_a_page_with_its_duplicate()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .Overlay(new PdfByteArraySource(Stamped(), "stamp.pdf"), 1)
                .DuplicatePage(1, 2),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 4);

        Assert.Contains("stamp", Compact(texts[0]), StringComparison.Ordinal);
        Assert.Contains("stamp", Compact(texts[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("stamp", Compact(texts[2]), StringComparison.Ordinal);
        Assert.DoesNotContain("stamp", Compact(texts[3]), StringComparison.Ordinal);
    }

    /// <summary>PDF/A-1b has no transparency, so a translucent layer is refused instead of written.</summary>
    [Fact]
    public async Task Refuses_a_translucent_layer_on_a_document_written_for_pdfa_1b()
    {
        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .WithOutput(PdfEditOutputOptions.Default with { PdfAProfile = PdfAProfile.PdfA1B })
                .Overlay(
                    new PdfByteArraySource(Stamped(), "stamp.pdf"),
                    1,
                    new PdfOverlayOptions { Opacity = 0.5 }),
            destination,
            cancellationToken: Token));

        Assert.Contains("PDF/A-1b", exception.Message, StringComparison.Ordinal);
    }
}
