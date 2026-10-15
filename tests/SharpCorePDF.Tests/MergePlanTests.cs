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
}
