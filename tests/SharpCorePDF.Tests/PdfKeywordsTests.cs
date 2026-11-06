using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the keyword cache: a known operator comes back as one shared instance, and anything the
/// specification does not name comes back as itself.
/// </summary>
public sealed class PdfKeywordsTests
{
    /// <summary>A known operator is one instance, and two that differ by case are two.</summary>
    [Fact]
    public void Returns_the_same_instance_for_a_known_operator()
    {
        Assert.Same(PdfKeywords.Read("Tj"u8), PdfKeywords.Read("Tj"u8));
        Assert.Same(PdfKeywords.Read("TJ"u8), PdfKeywords.Read("TJ"u8));
        Assert.NotSame(PdfKeywords.Read("Tj"u8), PdfKeywords.Read("TJ"u8));
        Assert.NotSame(PdfKeywords.Read("q"u8), PdfKeywords.Read("Q"u8));
    }

    /// <summary>Every keyword comes back exactly as it was written, cached or not.</summary>
    /// <param name="keyword">The bytes of the keyword.</param>
    [Theory]
    [InlineData("q")]
    [InlineData("d0")]
    [InlineData("\"")]
    [InlineData("T*")]
    [InlineData("scn")]
    [InlineData("unknownKeyword")]
    [InlineData("endstreamx")]
    public void Returns_the_keyword_it_was_given(string keyword)
    {
        Assert.Equal(keyword, PdfKeywords.Read(System.Text.Encoding.Latin1.GetBytes(keyword)));
    }
}
