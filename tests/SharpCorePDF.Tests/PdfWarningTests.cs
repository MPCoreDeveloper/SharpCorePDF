using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the remark a document or a merge reports instead of failing: what identifies it, and how
/// two of them compare.
/// </summary>
/// <remarks>
/// The kind is the contract and the text is not, which is the decision AR-C5 asked for: a caller may
/// depend on <see cref="PdfWarning.Kind"/>, and the wording may change in any release.
/// </remarks>
public sealed class PdfWarningTests
{
    /// <summary>A remark keeps the kind it was created with and formats as its text.</summary>
    [Fact]
    public void Carries_its_kind_and_its_text()
    {
        var warning = new PdfWarning(PdfWarningKind.CrossReferenceRebuilt, "'damaged.pdf': rebuilt.");

        Assert.Equal(PdfWarningKind.CrossReferenceRebuilt, warning.Kind);
        Assert.Equal("'damaged.pdf': rebuilt.", warning.Message);
        Assert.Equal(warning.Message, warning.ToString());
    }

    /// <summary>Two remarks are the same only when both the kind and the text agree.</summary>
    [Fact]
    public void Compares_by_kind_and_text()
    {
        var warning = new PdfWarning(PdfWarningKind.PageTreeCycle, "'damaged.pdf': cycle.");

        Assert.Equal(warning, new PdfWarning(PdfWarningKind.PageTreeCycle, "'damaged.pdf': cycle."));
        Assert.NotEqual(warning, new PdfWarning(PdfWarningKind.PageTreeCycle, "'damaged.pdf': cycle at 4."));
        Assert.NotEqual(warning, new PdfWarning(PdfWarningKind.PageTreeMissing, "'damaged.pdf': cycle."));
    }

    /// <summary>A remark without text says nothing, so it is refused.</summary>
    /// <param name="message">The text that is not usable.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_an_empty_message(string message) =>
        Assert.Throws<ArgumentException>(() => new PdfWarning(PdfWarningKind.OutputVerified, message));

    /// <summary>Nothing at all is not a remark either.</summary>
    [Fact]
    public void Refuses_a_missing_message() =>
        Assert.Throws<ArgumentNullException>(() => new PdfWarning(PdfWarningKind.OutputVerified, null!));
}
