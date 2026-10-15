using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of how a stream body is delimited: a stored length is trusted only after the <c>endstream</c>
/// keyword really follows it, and a file that gets its length wrong is read by scanning instead.
/// </summary>
/// <remarks>
/// Producers get the stored length wrong often enough that the specification itself tells readers to find
/// the keyword when the length cannot be trusted; these tests pin both halves of that rule, and the bytes the
/// reader ends up with either way.
/// </remarks>
public sealed class PdfStreamParsingTests
{
    /// <summary>A stored length that is followed by the keyword is used as it is.</summary>
    [Fact]
    public void Reads_a_stream_with_the_length_it_stores() =>
        Assert.Equal("abc", Read("<< /Length 3 >>\nstream\nabc\nendstream"));

    /// <summary>A stored length that hides the keyword behind it is ignored and the body is scanned for.</summary>
    /// <param name="length">The length the stream stores.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(-1)]
    public void Scans_for_a_body_when_the_stored_length_is_wrong(int length) =>
        Assert.Equal("abc", Read($"<< /Length {length} >>\nstream\nabc\nendstream"));

    /// <summary>A stream without a length at all is scanned for.</summary>
    [Fact]
    public void Scans_for_a_body_when_no_length_is_stored() => Assert.Equal("abc", Read("<< >>\nstream\nabc\nendstream"));

    /// <summary>A length that is an indirect reference is resolved, and scanning takes over without a resolver.</summary>
    [Fact]
    public void Resolves_a_length_that_is_an_indirect_reference()
    {
        const string Text = "<< /Length 4 0 R >>\nstream\nabc\nendstream";

        Assert.Equal("abc", Read(Text, reference => reference == 4 ? 3 : null));
        Assert.Equal("abc", Read(Text, reference => null));
        Assert.Equal("abc", Read(Text));
    }

    /// <summary>The end of line behind the <c>stream</c> keyword may be a LF, a CRLF or a lone CR.</summary>
    /// <param name="text">The bytes of the stream, with the line break the test uses.</param>
    [Theory]
    [InlineData("<< /Length 3 >>\nstream\nabc\nendstream")]
    [InlineData("<< /Length 3 >>\r\nstream\r\nabc\r\nendstream")]
    [InlineData("<< /Length 3 >>\rstream\rabc\rendstream")]
    [InlineData("<< >>\nstream\nabc\r\nendstream")]
    [InlineData("<< >>\nstream\nabc\rendstream")]
    public void Skips_the_end_of_line_behind_the_stream_keyword(string text) => Assert.Equal("abc", Read(text));

    /// <summary>A body that runs straight into the keyword is not a body at all.</summary>
    /// <remarks>
    /// <c>endstream</c> has to be preceded by whitespace to be the keyword; text that only ends in those
    /// letters is part of the body, so the reader would be guessing if it cut there. It returns no bytes.
    /// </remarks>
    [Fact]
    public void Returns_no_bytes_when_the_keyword_is_not_where_it_must_be() =>
        Assert.Equal(string.Empty, Read("<< >>\nstream\nabcendstream"));

    /// <summary>A compressed stream whose first candidate does not inflate is kept as it was read.</summary>
    [Fact]
    public void Keeps_the_bytes_it_found_when_a_compressed_candidate_does_not_inflate() =>
        Assert.Equal("not compressed", Read("<< /Filter /FlateDecode >>\nstream\nnot compressed\nendstream"));

    /// <summary>A stream is read behind an indirect object header.</summary>
    [Fact]
    public void Reads_a_stream_behind_an_object_header()
    {
        var bytes = Encoding.Latin1.GetBytes("12 0 obj\n<< /Length 3 >>\nstream\nabc\nendstream\nendobj\n");
        var parser = new PdfObjectParser(bytes, "damaged.pdf", 0, null, 64);

        Assert.True(parser.TryReadIndirectHeader(out var objectNumber, out var generation));

        var stream = Assert.IsType<PdfStream>(parser.ParseObject());

        Assert.Equal(12L, objectNumber);
        Assert.Equal(0, generation);
        Assert.Equal("abc", Encoding.Latin1.GetString(stream.Raw.Span));
    }

    /// <summary>Reads the body of the only stream of <paramref name="text"/>.</summary>
    /// <param name="text">The text of the stream, starting at its dictionary.</param>
    /// <param name="resolver">The resolver for an indirect length, when the test supplies one.</param>
    /// <returns>The text of the body.</returns>
    private static string Read(string text, Func<long, long?>? resolver = null)
    {
        var parser = new PdfObjectParser(Encoding.Latin1.GetBytes(text), "damaged.pdf", 0, resolver, 64);
        var stream = Assert.IsType<PdfStream>(parser.ParseObject());

        return Encoding.Latin1.GetString(stream.Raw.Span);
    }
}
