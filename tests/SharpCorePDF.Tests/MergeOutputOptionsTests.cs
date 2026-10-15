using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the options of a merge: the defaults that make a plain merge need no configuration, and the
/// values a plan refuses before a single byte of a source is read.
/// </summary>
/// <remarks>
/// These are the problems that can be seen without reading a document, so they are reported by
/// <see cref="MergePlan.Validate"/> while the plan is still being assembled.
/// </remarks>
public sealed class MergeOutputOptionsTests
{
    /// <summary>The defaults describe the common merge: keep as much as possible and write PDF 1.7.</summary>
    [Fact]
    public void Has_the_documented_defaults()
    {
        var options = MergeOutputOptions.Default;

        Assert.Null(options.PrimarySourceId);
        Assert.Equal(PdfVersion.V1_7, options.TargetVersion);
        Assert.Equal(MetadataPolicy.FromPrimarySource, options.MetadataPolicy);
        Assert.Null(options.MetadataOverride);
        Assert.Null(options.ModificationDate);
        Assert.Equal(OutlinePolicy.Preserve, options.OutlinePolicy);
        Assert.Equal(PageLabelPolicy.Drop, options.PageLabelPolicy);
        Assert.True(options.PreserveAnnotations);
        Assert.True(options.PreserveAcroForm);
        Assert.True(options.PreserveEmbeddedFiles);
        Assert.StartsWith("SharpCorePDF ", options.Producer, StringComparison.Ordinal);
        Assert.Empty(options.Validate());
    }

    /// <summary>The engine writes cross-reference tables, so a version that predates them is refused.</summary>
    /// <param name="major">The major version to try.</param>
    /// <param name="minor">The minor version to try.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 3)]
    public void Refuses_a_target_version_that_predates_the_engine(int major, int minor)
    {
        var options = MergeOutputOptions.Default with { TargetVersion = new PdfVersion(major, minor) };

        Assert.Contains(options.Validate(), error => error.Contains("is not supported", StringComparison.Ordinal));
    }

    /// <summary>A version the specification never released is refused as well.</summary>
    [Fact]
    public void Refuses_a_target_version_that_is_not_known()
    {
        var options = MergeOutputOptions.Default with { TargetVersion = new PdfVersion(3, 0) };

        Assert.Contains(
            options.Validate(),
            error => error.Contains("not a known PDF version", StringComparison.Ordinal));
    }

    /// <summary>A policy that is not one of the values the engine defines is refused.</summary>
    [Fact]
    public void Refuses_a_policy_that_is_not_defined()
    {
        var metadata = MergeOutputOptions.Default with { MetadataPolicy = (MetadataPolicy)9 };
        var outlines = MergeOutputOptions.Default with { OutlinePolicy = (OutlinePolicy)9 };
        var labels = MergeOutputOptions.Default with { PageLabelPolicy = (PageLabelPolicy)9 };

        Assert.Contains(metadata.Validate(), error => error.Contains("metadata policy", StringComparison.Ordinal));
        Assert.Contains(outlines.Validate(), error => error.Contains("outline policy", StringComparison.Ordinal));
        Assert.Contains(labels.Validate(), error => error.Contains("page label policy", StringComparison.Ordinal));
    }

    /// <summary>A producer that is set but empty is refused; a producer that is not set is fine.</summary>
    [Fact]
    public void Refuses_a_producer_that_is_empty()
    {
        Assert.Contains(
            (MergeOutputOptions.Default with { Producer = "  " }).Validate(),
            error => error.Contains("non-empty string", StringComparison.Ordinal));
        Assert.Empty((MergeOutputOptions.Default with { Producer = null! }).Validate());
        Assert.Empty((MergeOutputOptions.Default with { Producer = "SharpCorePDF" }).Validate());
    }

    /// <summary>A plan reports the problems of its options as its own.</summary>
    [Fact]
    public void Reports_the_problems_of_its_options()
    {
        var plan = MergePlan.Create(MergeOutputOptions.Default with { TargetVersion = PdfVersion.Unknown })
            .AddFile(TestAssets.Modern);

        Assert.Contains(plan.Validate(), error => error.Contains("is not supported", StringComparison.Ordinal));
        Assert.Throws<MergePlanValidationException>(() => plan.EnsureValid());
    }
}
