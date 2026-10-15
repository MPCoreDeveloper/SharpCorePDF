using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the document information: what an empty set of values is, how two sets are combined, and how
/// the additional entries a document may carry survive that combination.
/// </summary>
/// <remarks>
/// Merging documents means merging their descriptions, and the part that is easy to get wrong is the
/// additional entries: they are a dictionary of their own and they have to be combined rather than replaced.
/// </remarks>
public sealed class PdfMetadataTests
{
    /// <summary>A set of values without any value is empty, and any value makes it non-empty.</summary>
    [Fact]
    public void Reports_whether_it_carries_a_value()
    {
        Assert.True(PdfMetadata.Empty.IsEmpty);
        Assert.True(new PdfMetadata().IsEmpty);
        Assert.Null(PdfMetadata.Empty.Title);
        Assert.Null(PdfMetadata.Empty.Author);
        Assert.Null(PdfMetadata.Empty.Subject);
        Assert.Null(PdfMetadata.Empty.Keywords);
        Assert.Null(PdfMetadata.Empty.Creator);
        Assert.Null(PdfMetadata.Empty.Producer);
        Assert.Null(PdfMetadata.Empty.CreationDate);
        Assert.Null(PdfMetadata.Empty.ModificationDate);
        Assert.Empty(PdfMetadata.Empty.CustomProperties);

        Assert.False((PdfMetadata.Empty with { Title = "Report" }).IsEmpty);
        Assert.False((PdfMetadata.Empty with { CreationDate = DateTimeOffset.UnixEpoch }).IsEmpty);
        Assert.False((PdfMetadata.Empty with { CustomProperties = new Dictionary<string, string> { ["Company"] = "MPCore" } }).IsEmpty);
        Assert.True((PdfMetadata.Empty with { CustomProperties = new Dictionary<string, string>() }).IsEmpty);
    }

    /// <summary>The values of the overlay win, and everything the overlay leaves unset is kept.</summary>
    [Fact]
    public void Merges_two_sets_of_values()
    {
        var baseline = new PdfMetadata
        {
            Title = "Annual report",
            Author = "The finance team",
            CreationDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        };
        var overlay = new PdfMetadata { Author = "MPCoreDeveloper", Producer = "SharpCorePDF tests" };

        var merged = PdfMetadata.Merge(baseline, overlay);

        Assert.Equal("Annual report", merged.Title);
        Assert.Equal("MPCoreDeveloper", merged.Author);
        Assert.Equal("SharpCorePDF tests", merged.Producer);
        Assert.Equal(baseline.CreationDate, merged.CreationDate);
        Assert.Null(merged.Subject);
        Assert.Null(merged.ModificationDate);
    }

    /// <summary>The additional entries are combined key by key instead of being replaced.</summary>
    [Fact]
    public void Merges_the_additional_entries()
    {
        var baseline = PdfMetadata.Empty with
        {
            CustomProperties = new Dictionary<string, string> { ["Company"] = "MPCoreDeveloper", ["Department"] = "Platform" },
        };
        var overlay = PdfMetadata.Empty with
        {
            CustomProperties = new Dictionary<string, string> { ["Department"] = "PDF", ["Project"] = "SharpCorePDF" },
        };

        var merged = PdfMetadata.Merge(baseline, overlay);

        Assert.Equal(3, merged.CustomProperties.Count);
        Assert.Equal("MPCoreDeveloper", merged.CustomProperties["Company"]);
        Assert.Equal("PDF", merged.CustomProperties["Department"]);
        Assert.Equal("SharpCorePDF", merged.CustomProperties["Project"]);
    }

    /// <summary>When there is nothing to combine, the entries of the baseline are kept as they are.</summary>
    [Fact]
    public void Reuses_the_additional_entries_when_there_is_nothing_to_merge()
    {
        var baseline = PdfMetadata.Empty with { Title = "Annual report" };

        var merged = PdfMetadata.Merge(baseline, new PdfMetadata { Author = "The finance team" });

        Assert.Same(baseline.CustomProperties, merged.CustomProperties);
    }

    /// <summary>Both halves of a merge are required.</summary>
    [Fact]
    public void Rejects_a_missing_operand()
    {
        Assert.Throws<ArgumentNullException>(() => PdfMetadata.Merge(null!, PdfMetadata.Empty));
        Assert.Throws<ArgumentNullException>(() => PdfMetadata.Merge(PdfMetadata.Empty, null!));
    }

    /// <summary>Two sets of values are equal when every value, including the entries, is equal.</summary>
    [Fact]
    public void Compares_by_value()
    {
        var left = new PdfMetadata
        {
            Title = "Report",
            Author = "MPCoreDeveloper",
            CustomProperties = new Dictionary<string, string> { ["Company"] = "MPCoreDeveloper", ["Department"] = "PDF" },
        };
        var right = new PdfMetadata
        {
            Title = "Report",
            Author = "MPCoreDeveloper",
            CustomProperties = new Dictionary<string, string> { ["Department"] = "PDF", ["Company"] = "MPCoreDeveloper" },
        };

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left.Equals((PdfMetadata?)null));
        Assert.False(left.Equals(left with { Title = "Another report" }));
        Assert.False(left.Equals(left with { Author = null }));
        Assert.False(left.Equals(left with { CustomProperties = new Dictionary<string, string> { ["Company"] = "MPCoreDeveloper" } }));
        Assert.False(left.Equals(
            left with { CustomProperties = new Dictionary<string, string> { ["Company"] = "Another company", ["Department"] = "PDF" } }));
        Assert.False(left.Equals(left with { ModificationDate = DateTimeOffset.UnixEpoch }));
    }

    /// <summary>An instance is copied instead of changed when a value is replaced.</summary>
    [Fact]
    public void Copies_an_instance_when_a_value_is_replaced()
    {
        var original = PdfMetadata.Empty with { Title = "Report" };
        var changed = original with { Title = "Another report" };

        Assert.Equal("Report", original.Title);
        Assert.Equal("Another report", changed.Title);
        Assert.NotEqual(original, changed);
    }
}
