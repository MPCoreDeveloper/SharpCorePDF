using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the reader of a Type1 program's clear-text header: the part of the program that states what its
/// own codes mean when the document states nothing.
/// </summary>
/// <remarks>
/// The header of a real program ends at <c>eexec</c>, after which the bytes are encrypted; every case here
/// states a header and, where it matters, bytes after it that must not be read as text.
/// </remarks>
public sealed class PdfType1EncodingTests
{
    private static byte[] Program(string header, string encrypted = "eexec\n\u0001\u0002\u0003binary")
    {
        var text = "%!PS-AdobeFont-1.0: Test 001.001\n"
            + "/FontName /Test def\n"
            + "/Encoding 256 array\n"
            + "0 1 255 {1 index exch /.notdef put} for\n"
            + header
            + "readonly def\n"
            + "currentfile " + encrypted;

        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>The program's own <c>dup</c> lines are read into a table keyed by code.</summary>
    [Fact]
    public void Reads_the_dup_lines_of_the_header()
    {
        var table = PdfType1Encoding.Parse(Program("dup 65 /A put\ndup 200 /bullet put\n"));

        Assert.NotNull(table);
        Assert.Equal("A", table[65]);
        Assert.Equal("\u2022", table[200]);
        Assert.Null(table[66]);
    }

    /// <summary>
    /// A name this library does not know, and the <c>.notdef</c> filler every program writes, map nothing:
    /// no character is guessed for a glyph whose name says nothing.
    /// </summary>
    [Fact]
    public void Ignores_names_it_does_not_know()
    {
        var table = PdfType1Encoding.Parse(Program("dup 65 /nosuchglyph put\ndup 66 /.notdef put\n"));

        Assert.Null(table);
    }

    /// <summary>A program that says its encoding is <c>StandardEncoding</c> is read as that table.</summary>
    [Fact]
    public void Uses_StandardEncoding_when_the_program_says_so()
    {
        var table = PdfType1Encoding.Parse(Program("/Encoding StandardEncoding def\n"));

        Assert.NotNull(table);
        Assert.Equal("A", table[65]);
        Assert.Equal("a", table[97]);
    }

    /// <summary>A program that states no encoding at all yields nothing, so the caller keeps its own table.</summary>
    [Fact]
    public void States_nothing_for_a_header_with_no_encoding()
    {
        Assert.Null(PdfType1Encoding.Parse(Program(string.Empty)));
    }

    /// <summary>Bytes after <c>eexec</c> are encrypted and are never read as statements.</summary>
    [Fact]
    public void Never_reads_past_the_encrypted_marker()
    {
        var program = Program(
            "dup 65 /A put\n",
            encrypted: "eexec\ndup 66 /B put\n");

        var table = PdfType1Encoding.Parse(program);

        Assert.NotNull(table);
        Assert.Equal("A", table[65]);
        Assert.Null(table[66]);
    }

    /// <summary>A code outside a byte is not a code this table can hold, and is skipped rather than thrown on.</summary>
    [Fact]
    public void Skips_a_code_a_table_cannot_hold()
    {
        var table = PdfType1Encoding.Parse(Program("dup 300 /A put\ndup 66 /B put\n"));

        Assert.NotNull(table);
        Assert.Equal("B", table[66]);
        Assert.Null(table[65]);
    }

    /// <summary>The mathematical names the Computer Modern fonts use are known to the glyph table.</summary>
    [Theory]
    [InlineData("element", '\u2208')]
    [InlineData("greaterequal", '\u2265')]
    [InlineData("summation", '\u2211')]
    [InlineData("radical", '\u221A')]
    [InlineData("beta", '\u03B2')]
    [InlineData("Sigma", '\u03A3')]
    public void Knows_the_mathematical_glyph_names(string name, char expected) =>
        Assert.Equal(expected, PdfGlyphEncodings.FromGlyphName(name));
}
