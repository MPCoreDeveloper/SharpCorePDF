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
/// Tests of annotations: what the editor writes on a page, what a read of the result reports, and what
/// it refuses before the source is opened.
/// </summary>
public sealed partial class PdfDocumentEditorTests
{
    private static readonly PdfColor Yellow = new(0xFF, 0xF1, 0x76);

    /// <summary>Reads every annotation of an edited document.</summary>
    /// <param name="bytes">The document.</param>
    /// <returns>The annotations, in page order.</returns>
    private static async Task<IReadOnlyList<PdfAnnotationInfo>> AnnotationsAsync(byte[] bytes)
    {
        var result = await Content.ReadAnnotationsAsync(
            new PdfByteArraySource(bytes, "edited.pdf"),
            PageRange.All,
            cancellationToken: Token);

        return result.Items;
    }

    /// <summary>Reads the operators of the appearance stream of one annotation of a page.</summary>
    /// <param name="bytes">The document.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="index">The zero-based position of the annotation in the page's array.</param>
    /// <returns>The decoded operators, or an empty string when the annotation has no appearance.</returns>
    private static string AppearanceOf(byte[] bytes, int page, int index = 0)
    {
        var reader = Read(bytes);
        var annots = reader.ResolveArray(reader.Pages[page - 1].Dictionary.Get("Annots"));
        var annotation = reader.ResolveDictionary(annots?.Items[index]);
        var appearance = reader.Resolve(reader.ResolveDictionary(annotation?.Get("AP"))?.Get("N")) as PdfStream;

        return appearance is null
            ? string.Empty
            : Encoding.ASCII.GetString(PdfFilters.Decode(appearance, "edited.pdf"));
    }

    /// <summary>A highlight is written with its quad, its colour and an appearance the engine drew.</summary>
    [Fact]
    public async Task Adds_a_highlight_and_reads_it_back_with_an_appearance()
    {
        var quad = new PdfRectangle(72, 500, 220, 514);
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().AddAnnotation(1, PdfAnnotations.Highlight([quad], Yellow)),
            cancellationToken: Token);

        var annotations = await AnnotationsAsync(edited);

        var highlight = Assert.Single(annotations);
        Assert.Equal("Highlight", highlight.Subtype);
        Assert.Equal(1, highlight.PageNumber);
        Assert.Equal(quad, highlight.Rectangle);
        Assert.Equal("scpdf-1", highlight.Name);
        Assert.True(highlight.HasAppearance);
        Assert.Contains(" re", AppearanceOf(edited, 1), StringComparison.Ordinal);
    }

    /// <summary>A link names a URL or a page, and the read of the result resolves both.</summary>
    [Fact]
    public async Task Adds_links_to_a_url_and_to_a_page()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .AddAnnotation(1, PdfAnnotations.Link(new PdfRectangle(72, 440, 300, 460), "https://example.com/spec"))
                .AddAnnotation(1, PdfAnnotations.Link(new PdfRectangle(72, 400, 300, 420), new PdfDestination { PageNumber = 3 })),
            cancellationToken: Token);

        var annotations = await AnnotationsAsync(edited);

        Assert.Equal(2, annotations.Count);
        Assert.Equal("https://example.com/spec", annotations[0].Target?.Uri);
        Assert.Equal(3, annotations[1].Target?.PageNumber);
        Assert.False(annotations[0].HasAppearance);
    }

    /// <summary>An update replaces the annotation it names and a removal takes another one away.</summary>
    [Fact]
    public async Task Updates_one_annotation_and_removes_another()
    {
        var box = new PdfRectangle(72, 500, 90, 518);

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .AddAnnotation(1, PdfAnnotations.Text(box, "first") with { Name = "note-a" })
                .AddAnnotation(1, PdfAnnotations.Text(box, "second") with { Name = "note-b" })
                .UpdateAnnotation(1, "note-a", PdfAnnotations.Text(box, "revised") with { Name = "note-a" })
                .RemoveAnnotation(1, "note-b"),
            cancellationToken: Token);

        var annotation = Assert.Single(await AnnotationsAsync(edited));

        Assert.Equal("Text", annotation.Subtype);
        Assert.Equal("note-a", annotation.Name);
        Assert.Equal("revised", annotation.Contents);
    }

    /// <summary>An update of an identifier no annotation carries is refused before a byte is written.</summary>
    [Fact]
    public async Task Refuses_an_update_of_an_annotation_the_page_does_not_carry()
    {
        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().UpdateAnnotation(
                1,
                "missing",
                PdfAnnotations.Text(new PdfRectangle(72, 500, 90, 518), "revised")),
            destination,
            cancellationToken: Token));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, destination.Length);
    }

    /// <summary>A link that names both a page and a URL cannot be resolved, so the plan is refused.</summary>
    [Fact]
    public void Refuses_a_link_with_both_a_page_and_a_url()
    {
        var plan = PdfEditPlan.Create().AddAnnotation(
            1,
            PdfAnnotations.Link(
                new PdfRectangle(72, 440, 300, 460),
                new PdfDestination { PageNumber = 2, Uri = "https://example.com" }));

        var problems = plan.Validate();

        Assert.Contains(problems, problem => problem.Contains("exactly one", StringComparison.Ordinal));
    }

    /// <summary>A rectangle that reaches past the page is clipped to it, and the clip is reported.</summary>
    [Fact]
    public async Task Clips_an_annotation_to_the_page_and_reports_it()
    {
        var page = PdfPageSize.A5;
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().AddAnnotation(
                1,
                PdfAnnotations.Square(new PdfRectangle(page.WidthPoints - 20, 100, page.WidthPoints + 80, 160), PdfColor.Black)),
            destination,
            cancellationToken: Token);

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.AnnotationClipped);
    }

    /// <summary>The annotations a page already carried are kept when another one is added.</summary>
    [Fact]
    public async Task Keeps_the_annotation_a_page_already_carried()
    {
        var first = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().AddAnnotation(2, PdfAnnotations.Text(new PdfRectangle(72, 500, 90, 518), "first")),
            cancellationToken: Token);

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(first, "annotated.pdf"),
            PdfEditPlan.Create().AddAnnotation(2, PdfAnnotations.Text(new PdfRectangle(72, 660, 90, 678), "second")),
            cancellationToken: Token);

        var annotations = await AnnotationsAsync(edited);

        Assert.Equal(2, annotations.Count);
        Assert.All(annotations, annotation => Assert.Equal(2, annotation.PageNumber));
        Assert.Equal(["first", "second"], annotations.Select(annotation => annotation.Contents));
    }

    /// <summary>A duplicate made after the annotation was added carries it, as one object.</summary>
    [Fact]
    public async Task Carries_an_annotation_into_a_duplicate_of_its_page()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .AddAnnotation(1, PdfAnnotations.Text(new PdfRectangle(72, 500, 90, 518), "note"))
                .DuplicatePage(1, 2),
            cancellationToken: Token);

        var annotations = await AnnotationsAsync(edited);

        Assert.Equal(2, annotations.Count);
        Assert.Equal([1, 2], annotations.Select(annotation => annotation.PageNumber));
        Assert.All(annotations, annotation => Assert.Equal("note", annotation.Contents));
    }

    /// <summary>A link to a page the same plan removes is dropped, and the removal is reported.</summary>
    [Fact]
    public async Task Drops_a_link_whose_destination_the_edit_removes()
    {
        using var destination = new MemoryStream();

        var result = await Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .AddAnnotation(1, PdfAnnotations.Link(new PdfRectangle(72, 400, 300, 420), new PdfDestination { PageNumber = 3 }))
                .DeletePage(3),
            destination,
            cancellationToken: Token);

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.AnnotationDestinationUnresolved);
        Assert.Empty(await AnnotationsAsync(destination.ToArray()));
    }

    /// <summary>The appearance of a free-text annotation draws the text the caller stated.</summary>
    [Fact]
    public async Task Draws_the_text_of_a_free_text_annotation_into_its_appearance()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().AddAnnotation(
                1,
                PdfAnnotations.FreeText(
                    new PdfRectangle(72, 500, 320, 530),
                    "Reviewed (and approved)",
                    new PdfStandardTextStyle(StandardFont.Helvetica, 10))),
            cancellationToken: Token);

        var operators = AppearanceOf(edited, 1);

        Assert.Contains("Reviewed", operators, StringComparison.Ordinal);
        Assert.Contains("and approved", operators, StringComparison.Ordinal);
        Assert.Contains(" Tf", operators, StringComparison.Ordinal);
        Assert.Contains(" Tf", operators, StringComparison.Ordinal);
    }

    /// <summary>An annotation that states its own appearance is drawn with those operators and no others.</summary>
    [Fact]
    public async Task Writes_the_operators_of_a_supplied_annotation_appearance()
    {
        const string operators = "0 0 1 rg 10 5 40 20 re f";

        var annotation = PdfAnnotations.Square(new PdfRectangle(72, 500, 320, 560), Yellow) with
        {
            Appearance = new PdfAnnotationAppearance(
                operators,
                new PdfRectangle(0, 0, 60, 30),
                StandardFont.CourierBold),
        };

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().AddAnnotation(1, annotation),
            cancellationToken: Token);

        // The operators are the appearance, verbatim: the engine's own `q`, its rectangle and its `Q` would
        // all be visible here, because a caller that states its own stream is not asking for a second one.
        Assert.Equal(operators, AppearanceOf(edited, 1).Trim());

        // The font the record names still reaches the resources of the form XObject, under /Helv.
        var reader = Read(edited);
        var annots = reader.ResolveArray(reader.Pages[0].Dictionary.Get("Annots"));
        var written = reader.ResolveDictionary(annots?.Items[0]);
        var appearance = reader.Resolve(reader.ResolveDictionary(written?.Get("AP"))?.Get("N")) as PdfStream;
        var resources = reader.ResolveDictionary(appearance?.Dictionary.Get("Resources"));
        var fonts = reader.ResolveDictionary(resources?.Get("Font"));
        var face = reader.ResolveDictionary(fonts?.Get("Helv"));

        Assert.Equal("Courier-Bold", (face?.Get("BaseFont") as PdfName)?.Value);
    }

    /// <summary>Every kind the editor offers is written with an appearance, except a link.</summary>
    [Fact]
    public async Task Writes_an_appearance_for_every_kind_that_draws_something()
    {
        var box = new PdfRectangle(72, 400, 200, 440);
        var quad = new PdfRectangle(72, 500, 220, 514);

        var annotations = new[]
        {
            PdfAnnotations.Text(box, "note"),
            PdfAnnotations.FreeText(box, "text", PdfStandardTextStyle.Default),
            PdfAnnotations.Stamp(box, "Approved"),
            PdfAnnotations.Highlight([quad], Yellow),
            PdfAnnotations.Underline([quad], Yellow),
            PdfAnnotations.StrikeOut([quad], Yellow),
            PdfAnnotations.Square(box, PdfColor.Black),
            PdfAnnotations.Circle(box, PdfColor.Black),
            PdfAnnotations.Line(new PdfPoint(72, 400), new PdfPoint(200, 440), PdfColor.Black),
            PdfAnnotations.Ink([[new PdfPoint(72, 400), new PdfPoint(120, 420), new PdfPoint(200, 440)]], PdfColor.Black),
        };

        var plan = annotations
            .Select((annotation, index) => (annotation, index))
            .Aggregate(PdfEditPlan.Create(), (current, item) => current.AddAnnotation(1, item.annotation));

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"), plan, cancellationToken: Token);

        var written = await AnnotationsAsync(edited);

        Assert.Equal(annotations.Length, written.Count);
        Assert.All(written, annotation => Assert.True(annotation.HasAppearance));
    }

    /// <summary>PDF/A-1b has no transparency, so a translucent annotation is refused instead of written.</summary>
    [Fact]
    public async Task Refuses_a_translucent_annotation_for_pdfa_1b()
    {
        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => Editor.ApplyAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create()
                .WithOutput(PdfEditOutputOptions.Default with { PdfAProfile = PdfAProfile.PdfA1B })
                .AddAnnotation(
                    1,
                    PdfAnnotations.Highlight([new PdfRectangle(72, 500, 220, 514)], Yellow) with { Opacity = 0.4 }),
            destination,
            cancellationToken: Token));

        Assert.Contains("PDF/A-1b", exception.Message, StringComparison.Ordinal);
    }
}
