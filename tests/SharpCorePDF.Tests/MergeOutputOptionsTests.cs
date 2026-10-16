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
        Assert.Null(options.Encryption);
        Assert.StartsWith("SharpCorePDF ", options.Producer, StringComparison.Ordinal);
        Assert.Empty(options.Validate());
    }

    /// <summary>
    /// The crypt filters AES is written with arrived with PDF 1.5, and AES-256 followed in PDF 1.7, so a
    /// version that predates them is refused rather than written with an entry it does not define.
    /// </summary>
    /// <param name="algorithm">The algorithm to write with.</param>
    /// <param name="major">The major version of the target.</param>
    /// <param name="minor">The minor version of the target.</param>
    [Theory]
    [InlineData(PdfEncryptionAlgorithm.AesWith128BitKey, 1, 4)]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, 1, 4)]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, 1, 5)]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, 1, 6)]
    public void Refuses_an_encryption_the_target_version_is_too_old_for(
        PdfEncryptionAlgorithm algorithm,
        int major,
        int minor)
    {
        var options = MergeOutputOptions.Default with
        {
            TargetVersion = new PdfVersion(major, minor),
            Encryption = PdfEncryptionOptions.Default with { Algorithm = algorithm },
        };

        Assert.Contains(options.Validate(), error => error.Contains("too old", StringComparison.Ordinal));
    }

    /// <summary>Every algorithm is accepted by the versions that define it, RC4 by all of them.</summary>
    /// <param name="algorithm">The algorithm to write with.</param>
    /// <param name="major">The major version of the target.</param>
    /// <param name="minor">The minor version of the target.</param>
    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Rc4With40BitKey, 1, 4)]
    [InlineData(PdfEncryptionAlgorithm.Rc4With128BitKey, 1, 4)]
    [InlineData(PdfEncryptionAlgorithm.AesWith128BitKey, 1, 5)]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, 1, 7)]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, 2, 0)]
    public void Accepts_an_encryption_the_target_version_defines(
        PdfEncryptionAlgorithm algorithm,
        int major,
        int minor)
    {
        var options = MergeOutputOptions.Default with
        {
            TargetVersion = new PdfVersion(major, minor),
            Encryption = PdfEncryptionOptions.Default with
            {
                Algorithm = algorithm,
                UserPassword = "opens-it",
                OwnerPassword = "owns-it",
                Permissions = PdfPermissions.PrintOnly,
            },
        };

        Assert.Empty(options.Validate());
    }

    /// <summary>An algorithm or a permission set that is not one of the defined values is refused.</summary>
    [Fact]
    public void Refuses_encryption_values_that_are_not_defined()
    {
        var algorithm = MergeOutputOptions.Default with
        {
            Encryption = PdfEncryptionOptions.Default with { Algorithm = (PdfEncryptionAlgorithm)9 },
        };

        var permissions = MergeOutputOptions.Default with
        {
            Encryption = PdfEncryptionOptions.Default with { Permissions = (PdfPermissions)1 },
        };

        Assert.Contains(algorithm.Validate(), error => error.Contains("algorithm", StringComparison.Ordinal));
        Assert.Contains(permissions.Validate(), error => error.Contains("permissions", StringComparison.Ordinal));
    }

    /// <summary>
    /// A password that is longer than the revision can use is refused instead of being truncated in
    /// silence, because the document would then need a password other than the one that was set.
    /// </summary>
    [Fact]
    public void Refuses_an_encryption_password_that_would_be_truncated()
    {
        var thirtyThree = new string('a', 33);
        var oneHundredTwentyEight = new string('a', 128);
        var oneHundredTwentySeven = new string('a', 127);

        var rc4 = MergeOutputOptions.Default with
        {
            Encryption = PdfEncryptionOptions.Default with
            {
                Algorithm = PdfEncryptionAlgorithm.Rc4With128BitKey,
                UserPassword = thirtyThree,
            },
        };

        var aes128 = MergeOutputOptions.Default with
        {
            Encryption = PdfEncryptionOptions.Default with
            {
                Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey,
                OwnerPassword = thirtyThree,
            },
        };

        var aes256 = MergeOutputOptions.Default with
        {
            Encryption = PdfEncryptionOptions.Default with { UserPassword = oneHundredTwentyEight },
        };

        var fits = MergeOutputOptions.Default with
        {
            Encryption = PdfEncryptionOptions.Default with { UserPassword = oneHundredTwentySeven },
        };

        Assert.Contains(rc4.Validate(), error => error.Contains("user password", StringComparison.Ordinal));
        Assert.Contains(aes128.Validate(), error => error.Contains("owner password", StringComparison.Ordinal));
        Assert.Contains(aes256.Validate(), error => error.Contains("truncated", StringComparison.Ordinal));
        Assert.Empty(fits.Validate());
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
