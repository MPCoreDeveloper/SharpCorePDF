using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of a merge participant: the name it is reported under, the identity that tells two sources apart,
/// the pages it contributes, and the password it may carry.
/// </summary>
/// <remarks>
/// A source is an entity, so every change returns a new instance that keeps the identity of the original;
/// that is what lets a caller build a plan from a source and still recognise it in the result.
/// </remarks>
public sealed class PdfSourceTests
{
    /// <summary>A source that is handed no name takes the one its content reports.</summary>
    [Fact]
    public void Creates_a_source_that_uses_the_name_of_its_content()
    {
        var source = PdfSource.Create(new PdfByteArraySource([1, 2, 3], "report.pdf"));

        Assert.False(string.IsNullOrWhiteSpace(source.Id.Value));
        Assert.Equal("report.pdf", source.DisplayName);
        Assert.True(source.Pages.IsAll);
        Assert.Null(source.Password);
        Assert.Equal("report.pdf [1-]", source.ToString());
        Assert.Equal(3L, source.Content.Length);
    }

    /// <summary>A name that is handed in wins over the name of the content.</summary>
    [Fact]
    public void Creates_a_source_with_the_name_a_caller_gives() =>
        Assert.Equal(
            "Q1 report",
            PdfSource.Create(new PdfByteArraySource([1], "report.pdf"), "Q1 report").DisplayName);

    /// <summary>A source may reuse an identity that was handed out earlier.</summary>
    [Fact]
    public void Creates_a_source_with_a_known_identity()
    {
        var id = new PdfSourceId("source-1");
        var source = PdfSource.Create(id, new PdfFileSource(TestAssets.Modern));

        Assert.Equal(id, source.Id);
        Assert.Equal("Modern.pdf", source.DisplayName);
        Assert.True(source.Pages.IsAll);
    }

    /// <summary>Content without a name and no name handed in is refused, because messages would be useless.</summary>
    [Fact]
    public void Refuses_a_source_without_a_usable_name()
    {
        var exception = Assert.Throws<ArgumentException>(() => PdfSource.Create(new AnonymousContent()));

        Assert.Contains("needs a display name", exception.Message, StringComparison.Ordinal);
        Assert.Equal("displayName", exception.ParamName);
        Assert.Throws<ArgumentException>(() => PdfSource.Create(new AnonymousContent(), "   "));
    }

    /// <summary>A source needs content to read from.</summary>
    [Fact]
    public void Refuses_a_missing_content() => Assert.Throws<ArgumentNullException>(() => PdfSource.Create(null!));

    /// <summary>Narrowing the pages keeps the identity and every other value of the source.</summary>
    [Fact]
    public void Returns_a_source_that_contributes_other_pages()
    {
        var source = PdfSource.Create(new PdfByteArraySource([1], "report.pdf"));
        var narrowed = source.WithPages(PageSelection.Parse("2-3"));

        Assert.Equal("2-3", narrowed.Pages.ToString());
        Assert.Equal(source.Id, narrowed.Id);
        Assert.Equal("report.pdf", narrowed.DisplayName);
        Assert.Null(narrowed.Password);
        Assert.True(source.Pages.IsAll);
        Assert.Throws<ArgumentNullException>(() => source.WithPages(null!));
    }

    /// <summary>A password travels with the source and may be taken away again.</summary>
    [Fact]
    public void Returns_a_source_that_carries_a_password()
    {
        var source = PdfSource.Create(new PdfByteArraySource([1], "report.pdf")).WithPassword("s3cret");

        Assert.Equal("s3cret", source.Password);
        Assert.Null(source.WithPassword(null).Password);
        Assert.Equal("report.pdf", source.DisplayName);
        Assert.True(source.Pages.IsAll);
    }

    /// <summary>A source may be renamed, but not to nothing.</summary>
    [Fact]
    public void Renames_a_source()
    {
        var source = PdfSource.Create(new PdfByteArraySource([1], "report.pdf"));
        var renamed = source.Rename("Q1 report");

        Assert.Equal("Q1 report", renamed.DisplayName);
        Assert.Equal("Q1 report [1-]", renamed.ToString());
        Assert.Equal(source.Id, renamed.Id);
        Assert.Equal("report.pdf", source.DisplayName);
        Assert.Throws<ArgumentException>(() => source.Rename(" "));
    }

    /// <summary>Content that reports no name at all, which none of the shipped sources do.</summary>
    private sealed class AnonymousContent : IPdfContentSource
    {
        /// <inheritdoc />
        public string DisplayName => " ";

        /// <inheritdoc />
        public long? Length => 0L;

        /// <inheritdoc />
        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Stream>(new MemoryStream([], writable: false));
        }
    }
}
