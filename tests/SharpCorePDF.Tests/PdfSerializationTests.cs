using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the two serialisation details that are easy to get wrong and impossible to see in a
/// merged document without validating it with another implementation: binary strings and dates.
/// </summary>
public sealed class PdfSerializationTests
{
    /// <summary>A hexadecimal string is written with hex digits, not with name escapes.</summary>
    /// <remarks>
    /// A hexadecimal string carries binary data — a file identifier, a font program, an image mask.
    /// Writing <c>#20</c> where <c>20</c> was meant produces a string that a reader either rejects or
    /// silently reads as something else, which is why this is asserted byte for byte.
    /// </remarks>
    [Fact]
    public void Writes_a_hexadecimal_string_as_hex_digits()
    {
        var output = Write(new PdfString([0x01, 0x20, 0xAB, 0xFF], isHexadecimal: true));

        Assert.Equal("<0120ABFF>", output);
    }

    /// <summary>A literal string keeps its bytes, escaping only what PDF syntax requires.</summary>
    [Fact]
    public void Writes_a_literal_string_with_the_required_escapes()
    {
        var output = Write(PdfString.FromText("a(b)c\\d"));

        Assert.Equal("(a\\(b\\)c\\\\d)", output);
    }

    /// <summary>A name that contains a delimiter or a hash is escaped, because PDF requires it.</summary>
    [Fact]
    public void Escapes_names_that_contain_delimiters()
    {
        var output = Write(new PdfName("A#B/C"));

        Assert.Equal("/A#23B#2FC", output);
    }

    /// <summary>The date format is read field by field, including the optional parts.</summary>
    [Theory]
    [InlineData("D:20260329074926+02'00'", 2026, 3, 29, 7, 49, 26, 120)]
    [InlineData("D:20260401120000Z", 2026, 4, 1, 12, 0, 0, 0)]
    [InlineData("D:2026", 2026, 1, 1, 0, 0, 0, 0)]
    [InlineData("20260329074926", 2026, 3, 29, 7, 49, 26, 0)]
    [InlineData("D:20260329074926-05'30'", 2026, 3, 29, 7, 49, 26, -330)]
    public void Reads_the_pdf_date_format(
        string text,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int offsetMinutes)
    {
        Assert.True(PdfDate.TryParse(text, out var value));

        Assert.Equal(
            new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(offsetMinutes)),
            value);
    }

    /// <summary>What the engine writes is what it can read back, offset and all.</summary>
    [Fact]
    public void Writes_a_date_that_round_trips()
    {
        var local = new DateTimeOffset(2026, 3, 29, 7, 49, 26, TimeSpan.FromHours(2));

        Assert.Equal("D:20260329074926+02'00'", PdfDate.Format(local));
        Assert.True(PdfDate.TryParse(PdfDate.Format(local), out var parsed));
        Assert.Equal(local, parsed);

        Assert.Equal("D:20260401120000Z", PdfDate.Format(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>Text that is not a date at all is rejected instead of being invented.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yesterday")]
    [InlineData("D:00000000")]
    public void Rejects_text_that_is_not_a_date(string text) =>
        Assert.False(PdfDate.TryParse(text, out _));

    /// <summary>Formats a value the way the writer does.</summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The text that was written.</returns>
    private static string Write(PdfObject value)
    {
        using var buffer = new MemoryStream();
        var output = new PdfOutputBuffer(buffer);
        PdfSerializer.Write(value, output);
        output.Flush();

        return Encoding.Latin1.GetString(buffer.ToArray());
    }
}
