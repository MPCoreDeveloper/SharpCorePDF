using System.IO.Compression;
using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Infrastructure.Pdf;

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
        var stream = Stream([1, 2, 3], ("Filter", new PdfName("LZWDecode")));

        var exception = Assert.Throws<PdfUnsupportedFeatureException>(() => PdfFilters.Decode(stream, "sample.pdf"));

        Assert.Equal("sample.pdf", exception.SourceName);
        Assert.Equal("LZWDecode", exception.Feature);
        Assert.Contains("LZWDecode", exception.Message, StringComparison.Ordinal);
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
