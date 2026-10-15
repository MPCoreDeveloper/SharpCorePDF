using System.IO.Compression;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the prediction filters: the row wise schemes that make a table of offsets compact and that a
/// reader has to undo before it can see those offsets at all.
/// </summary>
/// <remarks>
/// The expectations are written as the hexadecimal form of the bytes the filter has to produce, so the
/// arithmetic of every scheme — including the wrap around at 256 — is visible in the test itself.
/// </remarks>
public sealed class PdfPredictorTests
{
    /// <summary>Every PNG filter type the specification defines is undone.</summary>
    /// <param name="encoded">The predicted rows, as hexadecimal.</param>
    /// <param name="expected">The rows the filter must produce, as hexadecimal.</param>
    [Theory]
    [InlineData("000102", "0102")]
    [InlineData("010202", "0204")]
    [InlineData("00010202FEFD", "0102FFFF")]
    [InlineData("000A14030505", "0A140A14")]
    [InlineData("000A14040202", "0A140C16")]
    public void Undoes_every_png_filter_type(string encoded, string expected) =>
        Assert.Equal(Convert.FromHexString(expected), Decode(encoded, Parameters(12, columns: 2)));

    /// <summary>A filter type that is not one of the five PNG filters is a damaged stream.</summary>
    [Fact]
    public void Reports_an_undefined_png_filter_type()
    {
        var exception = Assert.Throws<PdfSourceFormatException>(
            () => Decode("050102", Parameters(12, columns: 2)));

        Assert.Contains("undefined PNG predictor filter type 5", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A predictor value the specification does not define is refused instead of assumed.</summary>
    /// <param name="predictor">The value the stream declares.</param>
    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(16)]
    public void Reports_a_predictor_that_is_not_defined(int predictor)
    {
        var exception = Assert.Throws<PdfUnsupportedFeatureException>(
            () => Decode("000102", Parameters(predictor, columns: 2)));

        Assert.Equal("Predictor", exception.Feature);
        Assert.Contains($"the predictor value {predictor}", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A row that arrives without its filter byte is handed over as it is.</summary>
    [Fact]
    public void Returns_a_row_without_a_filter_byte() =>
        Assert.Equal(Convert.FromHexString("00"), Decode("00", Parameters(12, columns: 1)));

    /// <summary>A colour count or a column count that is not positive is treated as one.</summary>
    /// <remarks>
    /// A stream that declares neither leaves both at their default of one as well, which is why the third
    /// assertion stands for a document that omits both entries.
    /// </remarks>
    [Fact]
    public void Treats_a_count_that_is_not_positive_as_one()
    {
        Assert.Equal(Convert.FromHexString("0102"), Decode("000102", Parameters(12, columns: 2, colors: 0)));
        Assert.Equal(Convert.FromHexString("07"), Decode("0007", Parameters(12, columns: 0)));
        Assert.Equal(Convert.FromHexString("07"), Decode("0007", Parameters(12)));
    }

    /// <summary>The colour count may be written as a colour space entry, which producers do.</summary>
    /// <remarks>
    /// A row is as long as its columns and its colours together, and a sub filter steps one pixel at a time,
    /// so the same encoded row decodes to four bytes with two colours and to two bytes with the default of
    /// one. Both are asserted so the entry is proven to be read rather than falling back to the default.
    /// </remarks>
    [Fact]
    public void Reads_the_colour_count_from_a_colour_space_entry()
    {
        var colourSpace = new PdfDictionary()
            .SetInteger("Predictor", 12)
            .SetInteger("Columns", 2)
            .SetInteger("ColorSpace", 2);

        Assert.Equal(Convert.FromHexString("01020406"), Decode("0101020304", colourSpace));
        Assert.Equal(Convert.FromHexString("0103"), Decode("0101020304", Parameters(12, columns: 2)));
    }

    /// <summary>TIFF prediction, the older scheme, is undone as well.</summary>
    [Fact]
    public void Undoes_tiff_prediction() =>
        Assert.Equal(
            Convert.FromHexString("01020406"),
            Decode("01020304", Parameters(2, columns: 2, colors: 2)));

    /// <summary>TIFF prediction with a component size other than eight is reported.</summary>
    [Fact]
    public void Reports_tiff_prediction_that_is_not_eight_bit()
    {
        var exception = Assert.Throws<PdfUnsupportedFeatureException>(
            () => Decode("01020304", Parameters(2, columns: 2, colors: 2, bitsPerComponent: 16)));

        Assert.Contains("16 bits per component", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Inflates predicted rows and decodes them as a cross-reference stream would be decoded.</summary>
    /// <param name="encoded">The predicted rows, as hexadecimal.</param>
    /// <param name="parameters">The decode parameters of the stream.</param>
    /// <returns>The decoded rows.</returns>
    private static byte[] Decode(string encoded, PdfDictionary parameters) =>
        PdfFilters.Decode(
            Stream(Compress(Convert.FromHexString(encoded)), parameters),
            "predicted.pdf");

    /// <summary>Builds a Flate compressed stream with the given decode parameters.</summary>
    /// <param name="raw">The compressed bytes.</param>
    /// <param name="parameters">The decode parameters.</param>
    /// <returns>The stream.</returns>
    private static PdfStream Stream(byte[] raw, PdfDictionary parameters) =>
        new(
            new PdfDictionary()
                .Set("Filter", new PdfName("FlateDecode"))
                .Set("DecodeParms", parameters),
            raw);

    /// <summary>Builds the decode parameters of a predictor.</summary>
    /// <param name="predictor">The predictor value.</param>
    /// <param name="columns">The number of columns, when the stream declares one.</param>
    /// <param name="colors">The number of colours, when the stream declares one.</param>
    /// <param name="bitsPerComponent">The size of a component, when the stream declares one.</param>
    /// <returns>The decode parameters.</returns>
    private static PdfDictionary Parameters(
        int predictor,
        int? columns = null,
        int? colors = null,
        int? bitsPerComponent = null)
    {
        var parameters = new PdfDictionary().SetInteger("Predictor", predictor);

        if (columns is { } declaredColumns)
        {
            parameters.SetInteger("Columns", declaredColumns);
        }

        if (colors is { } declaredColors)
        {
            parameters.SetInteger("Colors", declaredColors);
        }

        if (bitsPerComponent is { } declaredSize)
        {
            parameters.SetInteger("BitsPerComponent", declaredSize);
        }

        return parameters;
    }

    /// <summary>Compresses bytes with a zlib wrapper, as a Flate stream is written.</summary>
    /// <param name="data">The bytes to compress.</param>
    /// <returns>The compressed bytes.</returns>
    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }
}
