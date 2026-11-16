using System.IO.Compression;
using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the stream filters the engine understands: which filter it applies, in which order, what it
/// does with the parameters of a filter, and how it reports data it cannot decode.
/// </summary>
/// <remarks>
/// Only object streams and cross-reference streams are decoded, so these are the filters a document has
/// to keep working when it was written by a producer that chose a different compression than the one the
/// engine would have chosen.
/// </remarks>
public sealed class PdfFilterTests
{
    /// <summary>An ASCII hexadecimal stream is decoded, including an odd trailing digit.</summary>
    /// <param name="body">The bytes of the stream.</param>
    /// <param name="expected">The text the stream must decode to.</param>
    [Theory]
    [InlineData("48656C6C6F>", "Hello")]
    [InlineData("48 65 6C 6C 6F>", "Hello")]
    [InlineData("48\t65\r\n6C6C6F>", "Hello")]
    [InlineData("414", "A@")]
    [InlineData("zz>", "")]
    [InlineData(">", "")]
    public void Decodes_an_ascii_hexadecimal_stream(string body, string expected) =>
        Assert.Equal(
            expected,
            Text(PdfFilters.Decode(
                Stream(Encoding.Latin1.GetBytes(body), ("Filter", new PdfName("ASCIIHexDecode"))),
                "sample.pdf")));

    /// <summary>An ASCII85 stream is decoded, including the compact groups a producer may use.</summary>
    /// <param name="body">The bytes of the stream.</param>
    /// <param name="expected">The text the stream must decode to.</param>
    /// <remarks>
    /// A group of <c>n</c> characters stands for <c>n - 1</c> bytes, so the last group of a stream that does
    /// not fill it is the case worth pinning: four characters give three bytes and three give two.
    /// </remarks>
    [Theory]
    [InlineData("87cURDj~>", "Hello")]
    [InlineData("87cUR~>", "Hell")]
    [InlineData("87cU~>", "Hel")]
    [InlineData("87c~>", "He")]
    [InlineData("87c\nU~>", "Hel")]
    [InlineData("\u000187cU~>", "Hel")]
    [InlineData("zz~>", "\0\0\0\0\0\0\0\0")]
    [InlineData("z~>", "\0\0\0\0")]
    [InlineData("~>", "")]
    public void Decodes_an_ascii85_compressed_stream(string body, string expected) =>
        Assert.Equal(
            expected,
            Text(PdfFilters.Decode(
                Stream(Encoding.Latin1.GetBytes(body), ("Filter", new PdfName("ASCII85Decode"))),
                "sample.pdf")));

    /// <summary>The abbreviated names of both ASCII filters are accepted.</summary>
    [Fact]
    public void Accepts_the_abbreviated_names_of_the_ascii_filters()
    {
        var hexadecimal = Stream(Encoding.Latin1.GetBytes("48 69>"), ("Filter", new PdfName("AHx")));
        var ascii85 = Stream(Encoding.Latin1.GetBytes("87cURDj~>"), ("Filter", new PdfName("A85")));

        Assert.Equal("Hi", Text(PdfFilters.Decode(hexadecimal, "sample.pdf")));
        Assert.Equal("Hello", Text(PdfFilters.Decode(ascii85, "sample.pdf")));
    }

    /// <summary>A stream that declares several filters is decoded by applying them in the declared order.</summary>
    [Fact]
    public void Applies_every_filter_of_a_filter_array_in_order()
    {
        // Flate first, then the hexadecimal text around it: the order of the array is the order in which
        // the bytes have to be undone.
        var compressed = Encoding.Latin1.GetBytes(Convert.ToHexString(Deflate(Encoding.Latin1.GetBytes("Hello"))) + ">");
        var stream = Stream(
            compressed,
            ("Filter", new PdfArray([new PdfName("ASCIIHexDecode"), new PdfName("FlateDecode")])));

        Assert.Equal("Hello", Text(PdfFilters.Decode(stream, "sample.pdf")));
    }

    /// <summary>A filter the engine does not implement is reported instead of guessed at.</summary>
    [Fact]
    public void Reports_a_filter_it_does_not_implement()
    {
        var stream = Stream([1, 2, 3], ("Filter", new PdfName("RunLengthDecode")));

        var exception = Assert.Throws<PdfUnsupportedFeatureException>(() => PdfFilters.Decode(stream, "sample.pdf"));

        Assert.Equal("sample.pdf", exception.SourceName);
        Assert.Equal("RunLengthDecode", exception.Feature);
        Assert.Contains("RunLengthDecode", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>An LZW stream is decompressed, under its full name and under its abbreviation.</summary>
    /// <remarks>
    /// An object stream is stored behind a filter like any other stream, and a producer that chose LZW for
    /// one is not doing anything the specification forbids, so this path has to read it too.
    /// </remarks>
    [Fact]
    public void Decodes_an_lzw_stream()
    {
        var encoded = TestLzw.Encode(Encoding.Latin1.GetBytes("Hello"));

        var spelled = Stream(encoded, ("Filter", new PdfName("LZWDecode")));
        var abbreviated = Stream(encoded, ("Filter", new PdfName("LZW")));

        Assert.Equal("Hello", Text(PdfFilters.Decode(spelled, "sample.pdf")));
        Assert.Equal("Hello", Text(PdfFilters.Decode(abbreviated, "sample.pdf")));
    }

    /// <summary>A stream without a filter is handed over exactly as it is stored.</summary>
    [Fact]
    public void Copies_a_stream_without_a_filter()
    {
        var raw = Encoding.Latin1.GetBytes("plain bytes of a stream");
        var stream = Stream(raw);

        Assert.Equal(raw, PdfFilters.Decode(stream, "sample.pdf"));
        Assert.False(PdfFilters.HasFlateCompression(stream.Dictionary));
    }

    /// <summary>Decoding a stream needs a stream.</summary>
    [Fact]
    public void Rejects_a_missing_stream() =>
        Assert.Throws<ArgumentNullException>(() => PdfFilters.Decode(null!, "sample.pdf"));

    /// <summary>Flate data is inflated both with and without the zlib wrapper some producers omit.</summary>
    [Fact]
    public void Inflates_flate_data_however_it_was_written()
    {
        var text = Encoding.Latin1.GetBytes("SharpCorePDF");

        Assert.Equal(text, PdfFilters.Inflate(Deflate(text), "sample.pdf"));
        Assert.Equal(text, PdfFilters.Inflate(RawDeflate(text), "sample.pdf"));

        Assert.True(PdfFilters.TryInflate(Deflate(text), out var wrapped));
        Assert.Equal(text, wrapped);

        Assert.True(PdfFilters.TryInflate(RawDeflate(text), out var raw));
        Assert.Equal(text, raw);
    }

    /// <summary>
    /// A Flate stream that decodes past the limit is refused rather than inflated: zeros deflate about a
    /// thousand to one, so the input limit alone lets a small document ask for a gigabyte.
    /// </summary>
    [Fact]
    public void Refuses_a_flate_stream_that_decodes_past_the_limit()
    {
        var bomb = Deflate(new byte[2 * 1024 * 1024]);

        Assert.True(bomb.Length < 16 * 1024, "the fixture has to be small for the test to say anything");

        var error = Assert.Throws<PdfSourceFormatException>(() => PdfFilters.Inflate(bomb, "bomb.pdf", 1024 * 1024));

        Assert.Contains("MaxDecodedStreamBytes", error.Message, StringComparison.Ordinal);
        Assert.Equal(2 * 1024 * 1024, PdfFilters.Inflate(bomb, "bomb.pdf", 2 * 1024 * 1024).Length);
        Assert.True(PdfFilters.TryInflate(Deflate(new byte[1024]), out var small));
        Assert.Equal(1024, small.Length);
    }

    /// <summary>An LZW stream is held to the same limit: one 12-bit code can stand for 4,096 bytes.</summary>
    [Fact]
    public void Refuses_an_lzw_stream_that_decodes_past_the_limit()
    {
        var encoded = TestLzw.Encode(new byte[100_000]);

        var error = Assert.Throws<PdfSourceFormatException>(() => PdfFilters.ApplyLzw(encoded, null, "lzw.pdf", 99_999));

        Assert.Contains("MaxDecodedStreamBytes", error.Message, StringComparison.Ordinal);
        Assert.Contains("LZW", error.Message, StringComparison.Ordinal);
        Assert.Equal(100_000, PdfFilters.ApplyLzw(encoded, null, "lzw.pdf", 100_000).Length);
    }

    /// <summary>A RunLength stream is held to the limit too: two bytes stand for 128.</summary>
    [Fact]
    public void Refuses_a_run_length_stream_that_decodes_past_the_limit()
    {
        var runs = Enumerable.Repeat(new byte[] { 0x81, 0x00 }, 1000).SelectMany(run => run).ToArray();

        var error = Assert.Throws<PdfSourceFormatException>(() => PdfFilters.DecodeRunLength(runs, "runs.pdf", 127_999));

        Assert.Contains("MaxDecodedStreamBytes", error.Message, StringComparison.Ordinal);
        Assert.Contains("RunLength", error.Message, StringComparison.Ordinal);
        Assert.Equal(128_000, PdfFilters.DecodeRunLength(runs, "runs.pdf", 128_000).Length);
    }

    /// <summary>Literal runs are held to the limit on the same exact boundary as repeated ones.</summary>
    [Fact]
    public void Refuses_literal_runs_that_decode_past_the_limit()
    {
        byte[] run = [0x7F, .. Enumerable.Range(0, 128).Select(value => (byte)value)];
        var runs = Enumerable.Repeat(run, 100).SelectMany(bytes => bytes).ToArray();

        var error = Assert.Throws<PdfSourceFormatException>(() => PdfFilters.DecodeRunLength(runs, "runs.pdf", 12_799));

        Assert.Contains("RunLength", error.Message, StringComparison.Ordinal);
        Assert.Equal(12_800, PdfFilters.DecodeRunLength(runs, "runs.pdf", 12_800).Length);
    }

    /// <summary>
    /// A page whose content stream decodes past the limit is reported as unreadable, the way any other
    /// undecodable content is, and the read finishes: the limit turns a bomb into a warning.
    /// </summary>
    [Fact]
    public async Task Reports_a_content_stream_past_the_limit_as_unreadable_instead_of_inflating_it()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>");
        builder.AddStream("/Filter /FlateDecode", PdfBuilder.Compress(new byte[1024 * 1024]));
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var document = builder.Build(catalog);

        var limited = new PdfContentReaderService(
            new PdfPageContentReader(PdfEngineOptions.Default with { MaxDecodedStreamBytes = 64 * 1024 }));
        var text = await limited.ReadTextAsync(
            new PdfByteArraySource(document, "bomb.pdf"),
            PageRange.All,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(text.Warnings, warning => warning.Kind == PdfWarningKind.ContentStreamUnreadable);

        var unlimited = await new PdfContentReaderService(new PdfPageContentReader()).ReadTextAsync(
            new PdfByteArraySource(document, "bomb.pdf"),
            PageRange.All,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(unlimited.Warnings, warning => warning.Kind == PdfWarningKind.ContentStreamUnreadable);
    }

    /// <summary>Flate data that cannot be decompressed is refused and reported as a damaged source.</summary>
    [Fact]
    public void Reports_data_that_cannot_be_decompressed()
    {
        var garbage = Encoding.Latin1.GetBytes("this is certainly not compressed");

        Assert.False(PdfFilters.TryInflate(garbage, out var nothing));
        Assert.Empty(nothing);
        Assert.False(PdfFilters.TryInflate(ReadOnlyMemory<byte>.Empty, out _));

        var stream = Stream(garbage, ("Filter", new PdfName("FlateDecode")));
        var exception = Assert.Throws<PdfSourceFormatException>(() => PdfFilters.Decode(stream, "damaged.pdf"));

        Assert.Equal("damaged.pdf", exception.SourceName);
        Assert.Contains("could not be decompressed", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Decode parameters may be written behind their abbreviated key, and junk is ignored.</summary>
    [Fact]
    public void Reads_decode_parameters_that_do_not_always_match()
    {
        var compressed = Deflate(Encoding.Latin1.GetBytes("Hi"));
        var abbreviated = Stream(
            compressed,
            ("Filter", new PdfName("FlateDecode")),
            ("DP", new PdfDictionary().SetInteger("Predictor", 1)));
        var single = Stream(
            compressed,
            ("Filter", new PdfName("FlateDecode")),
            ("DecodeParms", new PdfDictionary().SetInteger("Predictor", 1)));
        var junk = Stream(
            Encoding.Latin1.GetBytes("48 69>"),
            ("Filter", new PdfArray([new PdfName("ASCIIHexDecode")])),
            ("DecodeParms", new PdfArray([new PdfInteger(42)])));

        Assert.Equal("Hi", Text(PdfFilters.Decode(abbreviated, "sample.pdf")));
        Assert.Equal("Hi", Text(PdfFilters.Decode(single, "sample.pdf")));
        Assert.Equal("Hi", Text(PdfFilters.Decode(junk, "sample.pdf")));
    }

    /// <summary>Flate compression is recognised by name, by abbreviation and inside a filter array.</summary>
    /// <param name="filter">The filter name the stream declares.</param>
    /// <param name="expected">Whether the stream must be reported as Flate compressed.</param>
    [Theory]
    [InlineData("FlateDecode", true)]
    [InlineData("Fl", true)]
    [InlineData("ASCIIHexDecode", false)]
    [InlineData("LZWDecode", false)]
    public void Recognises_flate_compression_by_name(string filter, bool expected)
    {
        var dictionary = new PdfDictionary().Set("Filter", new PdfName(filter));

        Assert.Equal(expected, PdfFilters.HasFlateCompression(dictionary));
    }

    /// <summary>Inside an array only the first filter decides, and entries that are not names are skipped.</summary>
    [Fact]
    public void Recognises_flate_compression_inside_a_filter_array()
    {
        var first = new PdfDictionary().Set("Filter", new PdfArray([new PdfName("Fl"), new PdfName("ASCIIHexDecode")]));
        var second = new PdfDictionary().Set("Filter", new PdfArray([new PdfName("ASCIIHexDecode"), new PdfName("Fl")]));
        var skipped = new PdfDictionary().Set("Filter", new PdfArray([new PdfInteger(1), new PdfName("ASCIIHexDecode")]));
        var promoted = new PdfDictionary().Set("Filter", new PdfArray([new PdfInteger(1), new PdfName("FlateDecode")]));

        Assert.True(PdfFilters.HasFlateCompression(first));
        Assert.False(PdfFilters.HasFlateCompression(second));
        Assert.False(PdfFilters.HasFlateCompression(skipped));

        // An entry that is not a name does not become the first filter, so the name behind it does.
        Assert.True(PdfFilters.HasFlateCompression(promoted));
        Assert.False(PdfFilters.HasFlateCompression(new PdfDictionary()));
    }

    /// <summary>Builds a stream with the entries a test needs.</summary>
    /// <param name="raw">The raw bytes of the stream.</param>
    /// <param name="entries">The entries of the stream dictionary.</param>
    /// <returns>The stream.</returns>
    private static PdfStream Stream(byte[] raw, params (string Key, PdfObject Value)[] entries)
    {
        var dictionary = new PdfDictionary();

        foreach (var (key, value) in entries)
        {
            dictionary.Set(key, value);
        }

        return new PdfStream(dictionary, raw);
    }

    /// <summary>Compresses bytes the way the specification prescribes, with a zlib wrapper.</summary>
    /// <param name="data">The bytes to compress.</param>
    /// <returns>The compressed bytes.</returns>
    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();

        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Compresses bytes as a raw deflate stream, without the zlib wrapper.</summary>
    /// <param name="data">The bytes to compress.</param>
    /// <returns>The compressed bytes.</returns>
    private static byte[] RawDeflate(byte[] data)
    {
        using var output = new MemoryStream();

        using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Decodes bytes as Latin-1 text, which is how a PDF stores them.</summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <returns>The text.</returns>
    private static string Text(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
