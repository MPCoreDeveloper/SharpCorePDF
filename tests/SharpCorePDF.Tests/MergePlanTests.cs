using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the page selection notation and of the invariants of a merge plan.
/// </summary>
public sealed class MergePlanTests
{
    /// <summary>The <c>5</c>, <c>2-7</c>, <c>3-</c>, <c>-4</c> notation resolves against the document.</summary>
    [Theory]
    [InlineData("3", 10, new[] { 3 })]
    [InlineData("2-4", 10, new[] { 2, 3, 4 })]
    [InlineData("3-", 5, new[] { 3, 4, 5 })]
    [InlineData("-2", 5, new[] { 1, 2 })]
    [InlineData("1,3-4,8-", 9, new[] { 1, 3, 4, 8, 9 })]
    [InlineData("3,1", 4, new[] { 3, 1 })]
    [InlineData("1-", 2, new[] { 1, 2 })]
    public void Resolves_the_page_selection_notation(string text, int pageCount, int[] expected)
    {
        var selection = PageSelection.Parse(text);

        Assert.Equal(expected, selection.Resolve(pageCount, "sample.pdf"));
    }

    /// <summary>A selection that reaches past the last page is an error, not an empty result.</summary>
    [Fact]
    public void Refuses_a_selection_beyond_the_document()
    {
        var exception = Assert.Throws<MergePlanValidationException>(
            () => PageSelection.Parse("4-").Resolve(3, "sample.pdf"));

        Assert.Contains("sample.pdf", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Text that is not a selection at all is refused while parsing.</summary>
    [Fact]
    public void Refuses_text_that_is_not_a_selection() =>
        Assert.Throws<FormatException>(() => PageSelection.Parse("first page"));

    /// <summary>A plan without sources reports every problem it can see.</summary>
    [Fact]
    public void Reports_an_empty_plan()
    {
        var plan = MergePlan.Create();
        var errors = plan.Validate();

        Assert.True(plan.IsEmpty);
        Assert.Contains(errors, error => error.Contains("no sources", StringComparison.Ordinal));
        Assert.Throws<MergePlanValidationException>(() => plan.EnsureValid());
    }

    /// <summary>A plan is immutable: adding a source returns a new plan and leaves the old one alone.</summary>
    [Fact]
    public void Keeps_the_plan_immutable()
    {
        var empty = MergePlan.Create();
        var filled = empty.AddFile(TestAssets.Outlined, pages: "1-2");

        Assert.True(empty.IsEmpty);
        Assert.Equal(1, filled.SourceCount);
        Assert.NotSame(empty, filled);
        Assert.Equal("Outlined.pdf", filled.PrimarySource.DisplayName);
        Assert.Equal("-2", filled.PrimarySource.Pages.ToString());
        Assert.Empty(filled.Validate());
    }

    /// <summary>Two sources cannot share an identity.</summary>
    [Fact]
    public void Refuses_a_duplicate_identity()
    {
        var source = PdfSource.Create(new PdfFileSource(TestAssets.Modern));
        var plan = MergePlan.Create().AddSource(source);

        Assert.Throws<MergePlanValidationException>(() => plan.AddSource(source));
    }

    /// <summary>A plan can be built from a set of sources at once, and the default options apply.</summary>
    [Fact]
    public void Builds_a_plan_from_a_set_of_sources()
    {
        var plan = MergePlan.For(PdfSource.Create(new PdfFileSource(TestAssets.Modern)));

        Assert.Equal(1, plan.SourceCount);
        Assert.False(plan.IsEmpty);
        Assert.Equal(MergeOutputOptions.Default, plan.Output);
        Assert.Equal("Modern.pdf", plan.PrimarySource.DisplayName);
        Assert.True(MergePlan.For().IsEmpty);
    }

    /// <summary>Several sources are appended in the order they are handed in.</summary>
    [Fact]
    public void Appends_several_sources_at_once()
    {
        var plan = MergePlan.Create().AddSources(
        [
            PdfSource.Create(new PdfFileSource(TestAssets.Modern)),
            PdfSource.Create(new PdfFileSource(TestAssets.Outlined)),
        ]);

        Assert.Equal(2, plan.SourceCount);
        Assert.Equal(["Modern.pdf", "Outlined.pdf"], plan.Sources.Select(source => source.DisplayName).ToArray());
        Assert.Throws<ArgumentNullException>(() => plan.AddSources(null!));
    }

    /// <summary>A source can be taken out of a plan again, by the identity it carries.</summary>
    [Fact]
    public void Removes_a_source_by_its_identity()
    {
        var source = PdfSource.Create(new PdfFileSource(TestAssets.Modern));
        var plan = MergePlan.Create().AddSource(source).AddFile(TestAssets.Outlined);
        var removed = plan.RemoveSource(source.Id);

        Assert.Equal(2, plan.SourceCount);
        Assert.Equal(1, removed.SourceCount);
        Assert.Equal("Outlined.pdf", removed.PrimarySource.DisplayName);
        Assert.Same(removed, removed.RemoveSource(source.Id));
    }

    /// <summary>The options of a plan can be replaced or adjusted, and its sources stay where they are.</summary>
    [Fact]
    public void Replaces_and_adjusts_the_output_options()
    {
        var plan = MergePlan.Create().AddFile(TestAssets.Modern);
        var replaced = plan.WithOutput(MergeOutputOptions.Default with { TargetVersion = PdfVersion.V2_0 });
        var adjusted = plan.WithOutput(options => options with { OutlinePolicy = OutlinePolicy.Drop });
        var primary = plan.WithPrimarySource(plan.PrimarySource.Id);

        Assert.Equal(PdfVersion.V2_0, replaced.Output.TargetVersion);
        Assert.Equal(PdfVersion.V1_7, plan.Output.TargetVersion);
        Assert.Equal(OutlinePolicy.Drop, adjusted.Output.OutlinePolicy);
        Assert.Equal(plan.PrimarySource.Id, primary.Output.PrimarySourceId);
        Assert.Equal(plan.PrimarySource.Id, primary.PrimarySource.Id);
        Assert.Equal(1, replaced.SourceCount);
        Assert.Throws<ArgumentNullException>(() => plan.WithOutput((MergeOutputOptions)null!));
        Assert.Throws<ArgumentNullException>(() => plan.WithOutput((Func<MergeOutputOptions, MergeOutputOptions>)null!));
    }

    /// <summary>A source is found by the identity it carries.</summary>
    [Fact]
    public void Finds_a_source_by_its_identity()
    {
        var source = PdfSource.Create(new PdfFileSource(TestAssets.Modern));
        var plan = MergePlan.Create().AddSource(source);

        Assert.Same(source, plan.FindSource(source.Id));
        Assert.Null(plan.FindSource(new PdfSourceId("absent")));
    }

    /// <summary>A primary source that is not part of the plan is reported before anything is merged.</summary>
    [Fact]
    public void Reports_a_primary_source_that_is_not_part_of_the_plan()
    {
        var plan = MergePlan.Create().AddFile(TestAssets.Modern).WithPrimarySource(new PdfSourceId("absent"));

        Assert.Contains(plan.Validate(), error => error.Contains("not part of the plan", StringComparison.Ordinal));
        Assert.Throws<MergePlanValidationException>(() => plan.PrimarySource);
        Assert.Throws<MergePlanValidationException>(() => plan.EnsureValid());
    }

    /// <summary>A plan without sources has no primary source to lead the merge.</summary>
    [Fact]
    public void Refuses_a_primary_source_of_an_empty_plan() =>
        Assert.Throws<MergePlanValidationException>(() => MergePlan.Create().PrimarySource);

    /// <summary>Two plans are equal when they carry the same sources and the same options.</summary>
    [Fact]
    public void Compares_plans_by_their_sources_and_options()
    {
        var source = PdfSource.Create(new PdfFileSource(TestAssets.Modern));
        var left = MergePlan.Create().AddSource(source);
        var right = MergePlan.Create().AddSource(source);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, right.WithOutput(MergeOutputOptions.Default with { TargetVersion = PdfVersion.V2_0 }));
        Assert.NotEqual(left, MergePlan.Create());
        Assert.False(left.Equals((MergePlan?)null));
        Assert.Contains("1 source(s) to PDF 1.7", left.ToString(), StringComparison.Ordinal);
        Assert.Contains("0 source(s) to PDF 1.7", MergePlan.Create().ToString(), StringComparison.Ordinal);
    }
}
