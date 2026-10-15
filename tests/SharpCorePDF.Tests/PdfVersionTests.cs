using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the PDF version: how the value in a file header is read, how versions are compared, and which
/// text is refused rather than guessed at.
/// </summary>
/// <remarks>
/// The engine uses a version to decide whether a document may be written with the features it intends to
/// use, so a version that parses to something older than it is would silently change the output.
/// </remarks>
public sealed class PdfVersionTests
{
    /// <summary>A version is the major and the minor number of the header.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="major">The expected major number.</param>
    /// <param name="minor">The expected minor number.</param>
    [Theory]
    [InlineData("1.7", 1, 7)]
    [InlineData("2.0", 2, 0)]
    [InlineData("1", 1, 0)]
    [InlineData(" 1.5 ", 1, 5)]
    [InlineData("1.10", 1, 10)]
    public void Parses_a_version(string text, int major, int minor)
    {
        var version = PdfVersion.Parse(text);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal($"{major}.{minor}", version.ToString());
    }

    /// <summary>Text that is not a version is refused while parsing.</summary>
    /// <param name="text">The text that is not a version.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2.3")]
    [InlineData("-1")]
    [InlineData("1.-2")]
    [InlineData("1.x")]
    [InlineData("x.1")]
    [InlineData(".")]
    public void Rejects_text_that_is_not_a_version(string text)
    {
        Assert.False(PdfVersion.TryParse(text, out _));
        Assert.Throws<FormatException>(() => PdfVersion.Parse(text));
    }

    /// <summary>Nothing at all is not a version either.</summary>
    [Fact]
    public void Rejects_a_missing_version() => Assert.False(PdfVersion.TryParse(null, out _));

    /// <summary>Versions are ordered by their major and then by their minor number.</summary>
    [Fact]
    public void Compares_versions()
    {
        Assert.Equal(0, PdfVersion.V1_7.CompareTo(new PdfVersion(1, 7)));
        Assert.True(PdfVersion.V1_4.CompareTo(PdfVersion.V1_7) < 0);
        Assert.True(PdfVersion.V2_0.CompareTo(PdfVersion.V1_7) > 0);
        Assert.True(PdfVersion.V1_7.CompareTo(PdfVersion.V2_0) < 0);

        Assert.True(PdfVersion.V1_4 < PdfVersion.V1_5);
        Assert.True(PdfVersion.V1_5 <= PdfVersion.V1_5);
        Assert.True(PdfVersion.V2_0 > PdfVersion.V1_7);
        Assert.True(PdfVersion.V2_0 >= PdfVersion.V2_0);
        Assert.False(PdfVersion.V1_4 > PdfVersion.V1_4);

        Assert.True(PdfVersion.V1_7.IsAtLeast(PdfVersion.V1_4));
        Assert.False(PdfVersion.V1_4.IsAtLeast(PdfVersion.V1_7));
    }

    /// <summary>The versions the engine knows are ordered the way the specification released them.</summary>
    [Fact]
    public void Exposes_the_versions_it_knows()
    {
        Assert.True(PdfVersion.Unknown == new PdfVersion(1, 0));
        Assert.True(PdfVersion.V1_4 < PdfVersion.V1_5);
        Assert.True(PdfVersion.V1_5 < PdfVersion.V1_6);
        Assert.True(PdfVersion.V1_6 < PdfVersion.V1_7);
        Assert.True(PdfVersion.V1_7 < PdfVersion.V2_0);
        Assert.True(PdfVersion.Unknown < PdfVersion.V1_4);
    }

    /// <summary>A negative component is a programmer error, not a document.</summary>
    [Fact]
    public void Rejects_a_negative_component()
    {
        Assert.Equal("major", Assert.Throws<ArgumentOutOfRangeException>(() => new PdfVersion(-1, 0)).ParamName);
        Assert.Equal("minor", Assert.Throws<ArgumentOutOfRangeException>(() => new PdfVersion(1, -1)).ParamName);
    }
}
