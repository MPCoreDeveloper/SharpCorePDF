using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the limits of the engine: the defaults a caller gets, and the values that are refused before a
/// single byte of a document is read.
/// </summary>
/// <remarks>
/// These limits are what keep a damaged or hostile file from exhausting the process, so a limit that can be
/// configured into something unusable has to be reported rather than obeyed.
/// </remarks>
public sealed class PdfEngineOptionsTests
{
    /// <summary>The defaults are the limits a real document fits in.</summary>
    [Fact]
    public void Has_the_documented_defaults()
    {
        var options = PdfEngineOptions.Default;

        Assert.Equal(512L * 1024 * 1024, options.MaxSourceBytes);
        Assert.Equal(64, options.MaxNestingDepth);
        Assert.Equal(100_000, options.MaxPagesPerSource);
        Assert.True(options.ReconstructCrossReferenceOnFailure);
        Assert.False(options.VerifyOutput);
        Assert.True(options.WriteBinaryMarker);
        Assert.Empty(options.Validate());
    }

    /// <summary>A limit that cannot be honoured is reported.</summary>
    [Fact]
    public void Reports_a_limit_that_cannot_be_honoured()
    {
        Assert.Contains(
            (PdfEngineOptions.Default with { MaxSourceBytes = 1023 }).Validate(),
            error => error.Contains("MaxSourceBytes", StringComparison.Ordinal));
        Assert.Contains(
            (PdfEngineOptions.Default with { MaxNestingDepth = 3 }).Validate(),
            error => error.Contains("MaxNestingDepth", StringComparison.Ordinal));
        Assert.Contains(
            (PdfEngineOptions.Default with { MaxNestingDepth = 1025 }).Validate(),
            error => error.Contains("MaxNestingDepth", StringComparison.Ordinal));
        Assert.Contains(
            (PdfEngineOptions.Default with { MaxPagesPerSource = 0 }).Validate(),
            error => error.Contains("MaxPagesPerSource", StringComparison.Ordinal));

        Assert.Empty((PdfEngineOptions.Default with { MaxSourceBytes = 1024 }).Validate());
        Assert.Empty((PdfEngineOptions.Default with { MaxNestingDepth = 1024 }).Validate());
        Assert.Empty((PdfEngineOptions.Default with { MaxPagesPerSource = 1 }).Validate());
    }
}
