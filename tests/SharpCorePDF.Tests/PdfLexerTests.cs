using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the token layer: the scanner that turns document bytes into tokens, the scanners it uses for
/// strings and names, and what a token reports when it is printed in a diagnostic.
/// </summary>
/// <remarks>
/// The lexer is the first part of the engine a real file meets, so these tests feed it the bytes a
/// producer may leave behind: stray whitespace, escapes the specification does not define, strings that
/// are never closed and hexadecimal strings with an odd number of digits.
/// </remarks>
public sealed class PdfLexerTests
{
    /// <summary>An integer, a real number and an exponent all come out as numbers.</summary>
    /// <param name="text">The bytes of the token.</param>
    /// <param name="value">The value the token must report.</param>
    /// <param name="isInteger">Whether the token must report itself as an integer.</param>
    [Theory]
    [InlineData("42", 42d, true)]
    [InlineData("0", 0d, true)]
    [InlineData("-7", -7d, true)]
    [InlineData("+8", 8d, true)]
    [InlineData("3.25", 3.25d, false)]
    [InlineData(".5", 0.5d, false)]
    [InlineData("5.", 5d, false)]
    [InlineData("1e3", 1000d, false)]
    [InlineData("1.5e-2", 0.015d, false)]
    [InlineData("-2E+2", -200d, false)]
    public void Reads_numbers(string text, double value, bool isInteger)
    {
        var token = Read(text);

        Assert.Equal(PdfTokenType.Number, token.Type);
        Assert.Equal(isInteger, token.IsInteger);
        Assert.Equal(value, token.RealValue, 12);

        if (isInteger)
        {
            Assert.Equal((long)value, token.IntegerValue);
        }
    }

    /// <summary>A token reports where it starts and where it ends, so the reader can continue behind it.</summary>
    [Fact]
    public void Reports_the_offsets_of_a_token()
    {
        var token = Read("%comment\n 42 ");

        Assert.Equal(10, token.Start);
        Assert.Equal(12, token.End);
    }

    /// <summary>A name is decoded: a hash escape becomes the byte it stands for.</summary>
    /// <param name="text">The bytes of the name.</param>
    /// <param name="expected">The decoded name.</param>
    [Theory]
    [InlineData("/Name", "Name")]
    [InlineData("/A#20B", "A B")]
    [InlineData("/A#23B", "A#B")]
    [InlineData("/a#2fb", "a/b")]
    [InlineData("/Name#", "Name#")]
    [InlineData("/A#zz", "A#zz")]
    [InlineData("/", "")]
    public void Decodes_names(string text, string expected)
    {
        var token = Read(text);

        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal(expected, token.Text);
    }

    /// <summary>A name stops at the first whitespace or delimiter.</summary>
    [Fact]
    public void Stops_a_name_at_a_delimiter()
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes("/Name[1]"));
        var name = lexer.ReadToken();
        var array = lexer.ReadToken();

        Assert.Equal("Name", name.Text);
        Assert.Equal(PdfTokenType.ArrayOpen, array.Type);
    }

    /// <summary>Every escape the specification defines for a literal string is undone.</summary>
    /// <param name="text">The bytes of the string.</param>
    /// <param name="expected">The decoded text.</param>
    [Theory]
    [InlineData("(a\\nb)", "a\nb")]
    [InlineData("(a\\rb)", "a\rb")]
    [InlineData("(a\\tb)", "a\tb")]
    [InlineData("(a\\bb)", "a\bb")]
    [InlineData("(a\\fb)", "a\fb")]
    [InlineData("(a\\(b\\)c)", "a(b)c")]
    [InlineData("(a\\\\b)", "a\\b")]
    [InlineData("(a\\qb)", "aqb")]
    [InlineData("(\\101\\102)", "AB")]
    [InlineData("(\\53)", "+")]
    [InlineData("(\\1018)", "A8")]
    public void Decodes_literal_string_escapes(string text, string expected)
    {
        var token = Read(text);

        Assert.Equal(PdfTokenType.LiteralString, token.Type);
        Assert.Equal(Encoding.Latin1.GetBytes(expected), token.Bytes);
    }

    /// <summary>An unescaped end of line inside a string is one line feed, also when it is a CRLF pair.</summary>
    [Fact]
    public void Converts_an_unescaped_end_of_line_inside_a_string()
    {
        Assert.Equal(Encoding.Latin1.GetBytes("a\nb"), Read("(a\nb)").Bytes);
        Assert.Equal(Encoding.Latin1.GetBytes("a\nb"), Read("(a\r\nb)").Bytes);
    }

    /// <summary>A backslash before an end of line continues the string without adding a byte.</summary>
    [Fact]
    public void Continues_a_string_behind_a_backslash_at_the_end_of_a_line()
    {
        Assert.Equal(Encoding.Latin1.GetBytes("ab"), Read("(a\\\nb)").Bytes);
        Assert.Equal(Encoding.Latin1.GetBytes("ab"), Read("(a\\\r\nb)").Bytes);
    }

    /// <summary>Parentheses inside a string are kept; a string that is never closed ends with the data.</summary>
    /// <param name="text">The bytes of the string.</param>
    /// <param name="expected">The decoded text.</param>
    [Theory]
    [InlineData("(a(b)c)", "a(b)c")]
    [InlineData("(a(b(c))d)", "a(b(c))d")]
    [InlineData("(a)b)", "a")]
    [InlineData("(abc", "abc")]
    [InlineData("(abc\\", "abc")]
    [InlineData("()", "")]
    public void Decodes_a_literal_string(string text, string expected) =>
        Assert.Equal(Encoding.Latin1.GetBytes(expected), Read(text).Bytes);

    /// <summary>Hexadecimal strings may be spaced out and may carry an odd number of digits.</summary>
    /// <param name="text">The bytes of the string.</param>
    /// <param name="expected">The decoded text.</param>
    [Theory]
    [InlineData("<74657874>", "text")]
    [InlineData("<74 65 78 74>", "text")]
    [InlineData("<74\t65>", "te")]
    [InlineData("<74>65>", "t")]
    [InlineData("<746>", "t`")]
    [InlineData("<7465", "te")]
    [InlineData("<>", "")]
    [InlineData("<zz>", "")]
    public void Decodes_hexadecimal_strings(string text, string expected)
    {
        var token = Read(text);

        Assert.Equal(PdfTokenType.HexString, token.Type);
        Assert.Equal(Encoding.Latin1.GetBytes(expected), token.Bytes);
    }

    /// <summary>The brackets and keywords of the syntax are read one by one.</summary>
    /// <param name="text">The bytes of the token.</param>
    /// <param name="expected">The kind that must be read.</param>
    [Theory]
    [InlineData("[", (int)PdfTokenType.ArrayOpen)]
    [InlineData("]", (int)PdfTokenType.ArrayClose)]
    [InlineData("<<", (int)PdfTokenType.DictionaryOpen)]
    [InlineData(">>", (int)PdfTokenType.DictionaryClose)]
    [InlineData("true", (int)PdfTokenType.Keyword)]
    [InlineData("obj", (int)PdfTokenType.Keyword)]
    [InlineData("stream", (int)PdfTokenType.Keyword)]
    [InlineData(">", (int)PdfTokenType.Keyword)]
    [InlineData("{", (int)PdfTokenType.Keyword)]
    [InlineData("", (int)PdfTokenType.None)]
    public void Reads_the_tokens_of_the_syntax(string text, int expected) =>
        Assert.Equal((PdfTokenType)expected, Read(text).Type);

    /// <summary>A single angle bracket that does not open a hexadecimal string is a keyword of one byte.</summary>
    [Fact]
    public void Reads_a_lone_angle_bracket_as_a_keyword()
    {
        var token = Read(">");

        Assert.Equal(">", token.Text);
        Assert.Equal(1, token.End);
    }

    /// <summary>Comments and whitespace are skipped before every token.</summary>
    [Fact]
    public void Skips_whitespace_and_comments()
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes("% leading comment\n 42 % trailing"));
        var number = lexer.ReadToken();
        var none = lexer.ReadToken();

        Assert.Equal(42L, number.IntegerValue);
        Assert.Equal(PdfTokenType.None, none.Type);
        Assert.True(lexer.AtEnd);
    }

    /// <summary>An integer is read only when an integer is next, and a refusal leaves the reader alone.</summary>
    [Fact]
    public void Reads_an_integer_only_when_an_integer_is_next()
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes("3.5 7"));

        Assert.False(lexer.TryReadInteger(out var rejected));
        Assert.Equal(0L, rejected);
        Assert.Equal(0, lexer.Position);

        var integers = new PdfLexer(Encoding.Latin1.GetBytes("7"));

        Assert.True(integers.TryReadInteger(out var accepted));
        Assert.Equal(7L, accepted);
        Assert.Equal(1, integers.Position);
    }

    /// <summary>A keyword is consumed as a whole, and only when it is a keyword of its own.</summary>
    [Fact]
    public void Reads_a_keyword_when_it_is_next()
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes("stream\nendstream"));

        Assert.True(lexer.PeekKeyword("stream"));
        Assert.Equal(0, lexer.Position);
        Assert.False(lexer.TryReadKeyword("end"));
        Assert.Equal(0, lexer.Position);
        Assert.True(lexer.TryReadKeyword("stream"));
        Assert.True(lexer.TryReadKeyword("endstream"));
        Assert.True(lexer.AtEnd);
    }

    /// <summary>A keyword that does not fit in the remaining data is not a keyword.</summary>
    [Fact]
    public void Refuses_a_keyword_that_does_not_fit()
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes("ob"));

        Assert.False(lexer.TryReadKeyword("obj"));
        Assert.Throws<ArgumentException>(() => lexer.TryReadKeyword(string.Empty));
    }

    /// <summary>A token prints itself in the syntax it came from.</summary>
    /// <param name="text">The bytes of the token.</param>
    /// <param name="expected">The text a diagnostic must show.</param>
    [Theory]
    [InlineData("42", "42")]
    [InlineData("3.25", "3.25")]
    [InlineData("/Name", "/Name")]
    [InlineData("obj", "obj")]
    [InlineData("<7465>", "<hex>")]
    [InlineData("[", "ArrayOpen")]
    [InlineData("", "None")]
    public void Formats_a_token(string text, string expected) => Assert.Equal(expected, Read(text).ToString());

    /// <summary>The lexer exposes the bytes it reads and where it is.</summary>
    [Fact]
    public void Exposes_the_data_and_the_position()
    {
        var bytes = Encoding.Latin1.GetBytes("abc");
        var lexer = new PdfLexer(bytes);

        Assert.Same(bytes, lexer.Data);
        Assert.Equal(3, lexer.Length);
        Assert.Equal(3, lexer.Memory.Length);
        Assert.Equal(0, lexer.Position);

        lexer.Position = 1;

        Assert.Equal(PdfTokenType.Keyword, lexer.ReadToken().Type);
        Assert.True(lexer.AtEnd);
    }

    /// <summary>A start position outside the data is clamped instead of throwing.</summary>
    /// <param name="position">The position the lexer is asked to start at.</param>
    /// <param name="expected">The position the lexer must hold.</param>
    [Theory]
    [InlineData(99, 3)]
    [InlineData(-5, 0)]
    public void Clamps_the_start_position(int position, int expected)
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes("abc"), position);

        Assert.Equal(expected, lexer.Position);
    }

    /// <summary>A lexer needs bytes.</summary>
    [Fact]
    public void Rejects_null_data() => Assert.Throws<ArgumentNullException>(() => new PdfLexer(null!));

    /// <summary>The six whitespace characters of the specification are recognised.</summary>
    /// <param name="value">The byte to test.</param>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x09)]
    [InlineData(0x0A)]
    [InlineData(0x0C)]
    [InlineData(0x0D)]
    [InlineData(0x20)]
    public void Recognises_whitespace(byte value)
    {
        Assert.True(PdfLexer.IsWhitespace(value));
        Assert.True(PdfLexer.IsWhitespaceOrDelimiter(value));
    }

    /// <summary>The ten delimiter characters of the specification are recognised.</summary>
    [Fact]
    public void Recognises_delimiters()
    {
        foreach (var value in "()<>[]{}/%"u8)
        {
            Assert.True(PdfLexer.IsDelimiter(value));
            Assert.True(PdfLexer.IsWhitespaceOrDelimiter(value));
        }

        Assert.False(PdfLexer.IsDelimiter((byte)'#'));
        Assert.False(PdfLexer.IsWhitespace((byte)'a'));
        Assert.False(PdfLexer.IsWhitespaceOrDelimiter((byte)'x'));
    }

    /// <summary>Reads the first token of <paramref name="text"/>.</summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The first token.</returns>
    private static PdfToken Read(string text) => new PdfLexer(Encoding.Latin1.GetBytes(text)).ReadToken();
}
