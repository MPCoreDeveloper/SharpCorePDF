using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the scalar half of the object model: the null, boolean, number, string and name objects, and the
/// number formatting every object of a document is written with.
/// </summary>
/// <remarks>
/// A PDF has no exponent notation and no locale, so the formatting of a number is part of the file format:
/// <c>0.5</c> is a number and <c>,5</c> is not, on every machine.
/// </remarks>
public sealed class PdfObjectModelTests
{
    /// <summary>
    /// The empty name — a solidus on its own — is a key like any other: the dictionary keeps it and every
    /// entry after it, and writing the dictionary back states it as a solidus again.
    /// </summary>
    [Fact]
    public void Keeps_an_entry_whose_key_is_the_empty_name()
    {
        var parser = new PdfObjectParser(Encoding.Latin1.GetBytes("<< /A 1 / 2 /B 3 >>"), "empty-name.pdf", 0, null, 64);
        var dictionary = Assert.IsType<PdfDictionary>(parser.ParseObject());

        Assert.Equal(1L, dictionary.GetInteger("A"));
        Assert.Equal(2L, dictionary.GetInteger(string.Empty));
        Assert.Equal(3L, dictionary.GetInteger("B"));

        using var stream = new MemoryStream();
        var output = new PdfOutputBuffer(stream);
        PdfSerializer.WriteDictionary(dictionary, output);
        output.Flush();
        var written = Encoding.Latin1.GetString(stream.ToArray());

        Assert.Contains("/ 2", written, StringComparison.Ordinal);
    }

    /// <summary>A document whose page dictionary carries an empty-named entry opens and lists its page.</summary>
    [Fact]
    public void Opens_a_document_that_uses_the_empty_name_as_a_key()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] / (empty) >>");
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R / 1 >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        var reader = new PdfFileReader(builder.Build(catalog), "empty-name.pdf", SharpCorePDF.Infrastructure.PdfEngineOptions.Default);

        Assert.Single(reader.Pages);
    }

    /// <summary>The null object is one instance and writes itself as its keyword.</summary>
    [Fact]
    public void Exposes_the_null_object()
    {
        Assert.Same(PdfNull.Instance, PdfNull.Instance);
        Assert.Equal(PdfObjectKind.Null, PdfNull.Instance.Kind);
        Assert.Equal("null", PdfNull.Instance.ToString());
    }

    /// <summary>A boolean is one of two shared instances.</summary>
    [Fact]
    public void Exposes_the_two_booleans()
    {
        Assert.Same(PdfBoolean.True, PdfBoolean.From(true));
        Assert.Same(PdfBoolean.False, PdfBoolean.From(false));
        Assert.True(PdfBoolean.True.Value);
        Assert.False(PdfBoolean.False.Value);
        Assert.Equal(PdfObjectKind.Boolean, PdfBoolean.True.Kind);
        Assert.Equal("true", PdfBoolean.True.ToString());
        Assert.Equal("false", PdfBoolean.False.ToString());
    }

    /// <summary>An integer writes itself in the shortest invariant form.</summary>
    /// <param name="value">The value of the object.</param>
    /// <param name="expected">The text it must write.</param>
    [Theory]
    [InlineData(0L, "0")]
    [InlineData(42L, "42")]
    [InlineData(-42L, "-42")]
    [InlineData(long.MinValue, "-9223372036854775808")]
    public void Formats_an_integer(long value, string expected)
    {
        var integer = new PdfInteger(value);

        Assert.Equal(value, integer.Value);
        Assert.Equal(PdfObjectKind.Number, integer.Kind);
        Assert.Equal(expected, integer.ToString());
    }

    /// <summary>A real number writes itself without an exponent and with a digit before the point.</summary>
    /// <param name="value">The value of the object.</param>
    /// <param name="expected">The text it must write.</param>
    [Theory]
    [InlineData(42d, "42")]
    [InlineData(-42d, "-42")]
    [InlineData(0d, "0")]
    [InlineData(3.25d, "3.25")]
    [InlineData(-3.25d, "-3.25")]
    [InlineData(0.5d, "0.5")]
    [InlineData(-0.5d, "-0.5")]
    [InlineData(1d / 3d, "0.33333")]
    [InlineData(double.NaN, "0")]
    [InlineData(double.PositiveInfinity, "0")]
    [InlineData(double.NegativeInfinity, "0")]
    public void Formats_a_real_number(double value, string expected)
    {
        var real = new PdfReal(value);

        Assert.Equal(value, real.Value);
        Assert.Equal(PdfObjectKind.Number, real.Kind);
        Assert.Equal(expected, real.ToString());
        Assert.Equal(expected, PdfNumberFormat.Format(value));
    }

    /// <summary>A real number is also formatted straight into the buffer of a writer.</summary>
    [Fact]
    public void Formats_a_number_into_a_buffer()
    {
        Span<byte> destination = stackalloc byte[32];

        Assert.True(PdfNumberFormat.TryFormat(3.25d, destination, out var written));
        Assert.Equal("3.25", Encoding.Latin1.GetString(destination[..written]));

        Assert.True(PdfNumberFormat.TryFormat(-42d, destination, out written));
        Assert.Equal("-42", Encoding.Latin1.GetString(destination[..written]));

        Assert.True(PdfNumberFormat.TryFormat(double.NaN, destination, out written));
        Assert.Equal(1, written);
        Assert.Equal("0", Encoding.Latin1.GetString(destination[..written]));

        Assert.False(PdfNumberFormat.TryFormat(double.NaN, Span<byte>.Empty, out written));
        Assert.Equal(0, written);

        // A destination that is too small for the digits is reported instead of written half way.
        Assert.False(PdfNumberFormat.TryFormat(3.25d, destination[..2], out written));
        Assert.Equal(0, written);
    }

    /// <summary>Text becomes bytes, and text outside Latin-1 travels as UTF-16 behind a byte order mark.</summary>
    [Fact]
    public void Creates_a_string_from_text()
    {
        var latin1 = PdfString.FromText("SharpCorePDF");
        var outside = PdfString.FromText("café €");
        var accented = PdfString.FromText("ÄÖÜ");

        Assert.False(latin1.IsHexadecimal);
        Assert.Equal(PdfObjectKind.String, latin1.Kind);
        Assert.Equal("SharpCorePDF", latin1.ToText());
        Assert.Equal("SharpCorePDF", latin1.ToString());

        Assert.True(outside.IsHexadecimal);
        Assert.Equal([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("café €")], outside.Bytes);
        Assert.Equal("café €", outside.ToText());

        Assert.False(accented.IsHexadecimal);
        Assert.Equal("ÄÖÜ", accented.ToText());
        Assert.Throws<ArgumentNullException>(() => PdfString.FromText(null!));
    }

    /// <summary>A hexadecimal string reads back as the text it holds.</summary>
    [Fact]
    public void Creates_a_string_from_bytes()
    {
        var value = new PdfString([0xFE, 0xFF, 0x00, 0x41], isHexadecimal: true);

        Assert.True(value.IsHexadecimal);
        Assert.Equal([0xFE, 0xFF, 0x00, 0x41], value.Bytes);
        Assert.Equal("A", value.ToText());
    }

    /// <summary>A name holds its value decoded, and a name without a value is not a name.</summary>
    [Fact]
    public void Creates_a_name()
    {
        var name = new PdfName("A B");

        Assert.Equal("A B", name.Value);
        Assert.Equal("A B", name.ToString());
        Assert.Equal(PdfObjectKind.Name, name.Kind);

        Assert.Null(PdfName.From(null));
        Assert.Null(PdfName.From(string.Empty));
        Assert.Equal("Catalog", PdfName.From("Catalog")!.Value);
        Assert.Throws<ArgumentNullException>(() => new PdfName(null!));
    }
}
