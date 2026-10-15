using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the content sources: the name and the size they report, the guards of their constructors, and the
/// promise that every read is independent of the one before it.
/// </summary>
/// <remarks>
/// A source is what the rest of the library reads a document through, so the promise that matters here is
/// that opening a source twice yields two whole documents, whichever way the content is stored.
/// </remarks>
public sealed class PdfSourceContentTests
{
    /// <summary>A file source is named after its file and reports the size of that file.</summary>
    [Fact]
    public async Task Reads_a_document_from_a_file()
    {
        var source = new PdfFileSource(TestAssets.Modern);
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = File.ReadAllBytes(TestAssets.Modern);

        Assert.Equal("Modern.pdf", source.DisplayName);
        Assert.Equal(Path.GetFullPath(TestAssets.Modern), source.Path);
        Assert.Equal(expected.LongLength, source.Length);
        Assert.Equal($"Modern.pdf ({source.Path})", source.ToString());

        await using var first = await source.OpenReadAsync(cancellationToken);
        await using var second = await source.OpenReadAsync(cancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(expected, await DrainAsync(first));
        Assert.Equal(0, second.Position);
    }

    /// <summary>A file source may be renamed in messages, and a file that is not there has no size.</summary>
    [Fact]
    public void Reports_the_name_and_the_size_it_knows()
    {
        var named = PdfFileSource.From(TestAssets.Outlined, "appendix.pdf");
        var absent = new PdfFileSource(Path.Combine(Path.GetTempPath(), "SharpCorePDF.Tests", "absent.pdf"));

        Assert.Equal("appendix.pdf", named.DisplayName);
        Assert.Equal("Outlined.pdf", new PdfFileSource(TestAssets.Outlined).DisplayName);
        Assert.Equal($"appendix.pdf ({named.Path})", named.ToString());
        Assert.Null(absent.Length);
    }

    /// <summary>A file that cannot be read is reported when the source is opened.</summary>
    [Fact]
    public async Task Reports_a_file_that_is_not_there()
    {
        var absent = new PdfFileSource(Path.Combine(Path.GetTempPath(), "SharpCorePDF.Tests", "absent.pdf"));

        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await absent.OpenReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A file source refuses a path that says nothing.</summary>
    [Fact]
    public void Refuses_a_file_without_a_path()
    {
        Assert.Throws<ArgumentException>(() => new PdfFileSource(" "));
        Assert.Throws<ArgumentNullException>(() => new PdfFileSource(null!));
    }

    /// <summary>Bytes in memory are served as independent streams over the same array.</summary>
    [Fact]
    public async Task Reads_a_document_from_bytes()
    {
        var bytes = File.ReadAllBytes(TestAssets.Modern);
        var source = new PdfByteArraySource(bytes, "modern.pdf");
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal("modern.pdf", source.DisplayName);
        Assert.Equal(bytes.LongLength, source.Length);
        Assert.Equal($"modern.pdf ({bytes.Length} bytes)", source.ToString());
        Assert.Equal(bytes, source.Bytes.ToArray());

        await using var first = await source.OpenReadAsync(cancellationToken);
        await using var second = await source.OpenReadAsync(cancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(bytes, await DrainAsync(first));
        Assert.Equal(bytes, await DrainAsync(second));

        Assert.Throws<ArgumentException>(() => new PdfByteArraySource(bytes, " "));
        Assert.Throws<ArgumentNullException>(() => new PdfByteArraySource(null!, "modern.pdf"));
    }

    /// <summary>A stream is captured once and kept, so its size stays known and every read is whole.</summary>
    [Fact]
    public async Task Reads_a_document_from_a_stream()
    {
        var bytes = File.ReadAllBytes(TestAssets.Modern);
        var content = new MemoryStream(bytes, writable: false);
        var source = new PdfStreamSource(content, "streamed.pdf");

        Assert.Equal("streamed.pdf", source.DisplayName);
        Assert.Equal(bytes.LongLength, source.Length);
        Assert.Equal($"streamed.pdf ({bytes.Length} bytes)", source.ToString());

        await using var read = await source.OpenReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await DrainAsync(read));
        Assert.Equal(bytes.LongLength, source.Length);
        Assert.Equal(bytes.Length, content.Position);

        Assert.Throws<ArgumentException>(() => new PdfStreamSource(content, " "));
        Assert.Throws<ArgumentNullException>(() => new PdfStreamSource(null!, "streamed.pdf"));
    }

    /// <summary>Every source reports a cancelled read instead of a stream that is quietly broken.</summary>
    [Fact]
    public async Task Reports_a_cancelled_read()
    {
        var bytes = File.ReadAllBytes(TestAssets.Modern);
        IPdfContentSource[] sources =
        [
            new PdfFileSource(TestAssets.Modern),
            new PdfByteArraySource(bytes, "modern.pdf"),
            new PdfStreamSource(new MemoryStream(bytes, writable: false), "streamed.pdf"),
        ];

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        foreach (var source in sources)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await source.OpenReadAsync(cancellation.Token));
        }
    }

    /// <summary>Reads a stream to its end.</summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The bytes that were read.</returns>
    private static async Task<byte[]> DrainAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);

        return buffer.ToArray();
    }
}
