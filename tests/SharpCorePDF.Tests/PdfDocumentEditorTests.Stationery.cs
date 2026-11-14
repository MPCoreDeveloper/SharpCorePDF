using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of stationery: a background template per page range, a default for the pages no assignment
/// covers, and the underlay order the contract states.
/// </summary>
public sealed partial class PdfDocumentEditorTests
{
    /// <summary>Builds a one-page template that draws <paramref name="text"/>.</summary>
    /// <param name="text">The text the template page draws.</param>
    /// <returns>The template as a source.</returns>
    private static PdfByteArraySource Template(string text) => new(Stamped(text), $"{text}.pdf");

    /// <summary>Builds a two-page template whose pages draw two different phrases.</summary>
    /// <param name="first">The text of the first template page.</param>
    /// <param name="second">The text of the second template page.</param>
    /// <returns>The template as a source.</returns>
    private static PdfByteArraySource TwoPageTemplate(string first, string second) =>
        new(
            PdfDocumentBuilder.Create()
                .AddPage(PdfPageSize.A5, page => page.DrawText(first, 72, 300, Helvetica, 24))
                .AddPage(PdfPageSize.A5, page => page.DrawText(second, 72, 300, Helvetica, 24))
                .ToArray(),
            "two-page-stationery.pdf");

    /// <summary>A default template fills the pages no assignment covers.</summary>
    [Fact]
    public async Task Applies_assignments_and_a_default_to_the_pages_they_cover()
    {
        var stationery = PdfStationery.Create()
            .Assign(Template("footer"), PageRange.FromTo(2, 3))
            .AssignDefault(Template("letterhead"));

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

    /// <summary>A later, narrower assignment overrides an earlier, broader one.</summary>
    [Fact]
    public async Task A_specific_assignment_overrides_a_broader_one()
    {
        var stationery = PdfStationery.Create()
            .Assign(Template("letterhead"), PageRange.All)
            .Assign(Template("footer"), PageRange.Single(2));

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(stationery),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("letterhead", Compact(texts[0]), StringComparison.Ordinal);
        Assert.Contains("footer", Compact(texts[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("letterhead", Compact(texts[1]), StringComparison.Ordinal);
        Assert.Contains("letterhead", Compact(texts[2]), StringComparison.Ordinal);
    }

    /// <summary>A page assigned no template keeps what it had, even when a default exists.</summary>
    [Fact]
    public async Task AssignNone_leaves_a_page_without_stationery()
    {
        var stationery = PdfStationery.Create()
            .AssignNone(PageRange.Single(2))
            .AssignDefault(Template("letterhead"));

        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(stationery),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("letterhead", Compact(texts[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("letterhead", Compact(texts[1]), StringComparison.Ordinal);
        Assert.Contains("letterhead", Compact(texts[2]), StringComparison.Ordinal);
    }

    /// <summary>A multi-page template maps one page for one page, starting at the range's first page.</summary>
    [Fact]
    public async Task Maps_a_multi_page_template_one_page_for_one_page()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(
                PdfStationery.Create().Assign(TwoPageTemplate("first", "second"), PageRange.FromTo(2, 3))),
            cancellationToken: Token);

        var texts = await TextsAsync(edited, 3);

        Assert.Contains("first", Compact(texts[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("second", Compact(texts[1]), StringComparison.Ordinal);
        Assert.Contains("second", Compact(texts[2]), StringComparison.Ordinal);
        Assert.DoesNotContain("first", Compact(texts[0]), StringComparison.Ordinal);
    }

    /// <summary>A template with fewer pages than its range needs is refused when the plan runs.</summary>
    [Fact]
    public async Task Refuses_a_template_that_cannot_cover_its_range()
    {
        var error = await Assert.ThrowsAsync<PdfEditPlanValidationException>(() =>
            Editor.ApplyToArrayAsync(
                new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
                PdfEditPlan.Create().ApplyStationery(
                    PdfStationery.Create().Assign(TwoPageTemplate("first", "second"), PageRange.FromTo(1, 3))),
                cancellationToken: Token));

        Assert.Contains("cannot cover page 3", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The stationery is the first stream of the page, and the page's own streams follow it.</summary>
    [Fact]
    public async Task Draws_stationery_before_the_page_it_covers()
    {
        var edited = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create().ApplyStationery(
                PdfStationery.Create().AssignDefault(Template("letterhead"))),
            cancellationToken: Token);

        var reader = Read(edited);
        var contents = reader.ResolveArray(reader.Pages[1].Dictionary.Get("Contents"))!;

        Assert.True(contents.Count >= 2);
        Assert.Contains(" Do", OperatorsOf(reader, contents.Items[0]), StringComparison.Ordinal);
        Assert.DoesNotContain(" Do", OperatorsOf(reader, contents.Items[^1]), StringComparison.Ordinal);
    }

    /// <summary>Stationery without an assignment or a default is refused before a document is read.</summary>
    [Fact]
    public void Refuses_stationery_without_a_template()
    {
        var plan = PdfEditPlan.Create().ApplyStationery(PdfStationery.Create());

        Assert.Contains(
            plan.Validate(),
            error => error.Contains("neither a page assignment", StringComparison.Ordinal));
        Assert.Throws<PdfEditPlanValidationException>(() => plan.EnsureValid());
    }

    /// <summary>PDF/A-1b has no transparency, so a translucent stationery is refused.</summary>
    [Fact]
    public void Refuses_translucent_stationery_for_pdf_a_1b()
    {
        var plan = PdfEditPlan
            .Create(PdfEditOutputOptions.Default with { PdfAProfile = PdfAProfile.PdfA1B })
            .ApplyStationery(
                PdfStationery.Create().AssignDefault(
                    Template("letterhead"),
                    new PdfOverlayOptions { Opacity = 0.5 }));

        Assert.Contains(plan.Validate(), error => error.Contains("no transparency", StringComparison.Ordinal));
    }

    /// <summary>A blend mode other than Normal is refused for PDF/A-1b as well.</summary>
    [Fact]
    public void Refuses_a_blended_stationery_for_pdf_a_1b()
    {
        var plan = PdfEditPlan
            .Create(PdfEditOutputOptions.Default with { PdfAProfile = PdfAProfile.PdfA1B })
            .ApplyStationery(
                PdfStationery.Create().AssignDefault(
                    Template("letterhead"),
                    new PdfOverlayOptions { BlendMode = PdfOverlayBlendMode.Multiply }));

        Assert.Contains(plan.Validate(), error => error.Contains("no transparency", StringComparison.Ordinal));
    }

    /// <summary>A later assignment wins, and AssignNone resolves to an assignment with no template.</summary>
    [Fact]
    public void Resolves_the_last_assignment_that_covers_a_page()
    {
        var stationery = PdfStationery.Create()
            .Assign(Template("letterhead"), PageRange.All)
            .Assign(Template("footer"), PageRange.FromTo(2, 3))
            .AssignNone(PageRange.Single(3));

        Assert.Equal("letterhead.pdf", stationery.ResolveForPage(1)?.Source?.DisplayName);
        Assert.Equal("footer.pdf", stationery.ResolveForPage(2)?.Source?.DisplayName);
        Assert.NotNull(stationery.ResolveForPage(3));
        Assert.Null(stationery.ResolveForPage(3)!.Source);
    }

    /// <summary>A template is required: a null one is refused, whether it is assigned or made the default.</summary>
    [Fact]
    public void Refuses_a_null_template()
    {
        Assert.Throws<ArgumentNullException>(() => PdfStationery.Create().Assign(null!, PageRange.All));
        Assert.Throws<ArgumentNullException>(() => PdfStationery.Create().AssignDefault(null!));
    }

    /// <summary>The options a caller supplies are the options the assignment records.</summary>
    [Fact]
    public void Retains_the_supplied_options()
    {
        var options = new PdfOverlayOptions { Opacity = 0.25, BlendMode = PdfOverlayBlendMode.Multiply };
        var stationery = PdfStationery.Create().Assign(Template("footer"), PageRange.All, options, "secret");

        Assert.Same(options, stationery.Assignments[0].Options);
        Assert.Equal("secret", stationery.Assignments[0].Password);
    }

    /// <summary>Two plans built from the same templates compare equal, and a default changes the comparison.</summary>
    [Fact]
    public void Stationery_is_a_value()
    {
        var template = Template("footer");
        var fallback = Template("letterhead");
        var options = new PdfOverlayOptions { Opacity = 0.25 };

        var first = PdfStationery.Create().Assign(template, PageRange.All, options);
        var second = PdfStationery.Create().Assign(template, PageRange.All, options);
        var otherRange = PdfStationery.Create().Assign(template, PageRange.Single(1), options);
        var withDefault = first.AssignDefault(fallback);

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherRange);
        Assert.NotEqual(first, withDefault);

        // The hash names the same inputs as the comparison: the assignments and the default.
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first.GetHashCode(), otherRange.GetHashCode());
        Assert.NotEqual(first.GetHashCode(), withDefault.GetHashCode());
    }

    /// <summary>The diagnostic text says how many assignments exist and whether a default does.</summary>
    [Fact]
    public void Describes_itself()
    {
        var withoutDefault = PdfStationery.Create().Assign(Template("footer"), PageRange.All);
        var withDefault = withoutDefault.AssignDefault(Template("letterhead"));

        Assert.Equal("1 stationery assignment(s)", withoutDefault.ToString());
        Assert.Equal("1 stationery assignment(s) with a default", withDefault.ToString());
    }
}
