using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the identity of a merge participant: the values it refuses, the uniqueness of a generated one,
/// and the comparison callers rely on when they keep a reference to a source.
/// </summary>
/// <remarks>
/// Two sources may read the same file, so an identity can never be derived from a location; that decision is
/// only visible in tests that build two sources from one fixture.
/// </remarks>
public sealed class PdfSourceIdTests
{
    /// <summary>A generated identity is never empty and never repeats.</summary>
    [Fact]
    public void Creates_a_unique_identity()
    {
        var first = PdfSourceId.New();
        var second = PdfSourceId.New();

        Assert.False(string.IsNullOrWhiteSpace(first.Value));
        Assert.NotEqual(first, second);
        Assert.Equal(first.Value, first.ToString());
    }

    /// <summary>An identity that carries no value is refused.</summary>
    /// <param name="value">The value that is not usable as an identity.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_an_empty_value(string value) =>
        Assert.Throws<ArgumentException>(() => new PdfSourceId(value));

    /// <summary>Nothing at all is not an identity either.</summary>
    [Fact]
    public void Refuses_a_missing_value() => Assert.Throws<ArgumentNullException>(() => new PdfSourceId(null!));

    /// <summary>Identities compare by the value they carry.</summary>
    [Fact]
    public void Compares_by_its_value()
    {
        Assert.Equal(new PdfSourceId("source-1"), new PdfSourceId("source-1"));
        Assert.NotEqual(new PdfSourceId("source-1"), new PdfSourceId("source-2"));
        Assert.NotEqual(PdfSourceId.New(), PdfSourceId.New());
    }
}
