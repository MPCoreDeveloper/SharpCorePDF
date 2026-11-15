using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the CCITT Group 3 and Group 4 decoder, against strips Pillow wrote through libtiff: the
/// bitmap Pillow decodes is the bitmap this decoder must state.
/// </summary>
public sealed class PdfCcittFilterTests
{
    private const int Width = 16;
    private const int Height = 16;

    /// <summary>Group 4, as Pillow wrote the 16 x 16 fixture (a diagonal and a block).</summary>
    private static readonly byte[] Group4 = File.ReadAllBytes(Fixture("ccitt-g4.bin"));

    /// <summary>Group 3 one-dimensional, as Pillow wrote the same bitmap.</summary>
    private static readonly byte[] Group3 = File.ReadAllBytes(Fixture("ccitt-g3.bin"));

    /// <summary>The bitmap Pillow decodes: 1 is white, 0 is black, rows padded to a byte.</summary>
    private static readonly byte[] Expected = File.ReadAllBytes(Fixture("ccitt-expected.bin"));

    private static string Fixture(string name) => Path.Combine(TestAssets.Folder, "ccitt", name);

    /// <summary>Group 4 codes every line relative to the one above it.</summary>
    [Fact]
    public void Decodes_a_group_4_strip_to_the_bitmap_the_fixture_states()
    {
        Assert.Equal(Expected, Decode(Group4, Params(k: -1, blackIs1: true)));
    }

    /// <summary>Group 3 codes each line on its own after its end-of-line code.</summary>
    [Fact]
    public void Decodes_a_group_3_strip_to_the_bitmap_the_fixture_states()
    {
        Assert.Equal(Expected, Decode(Group3, Params(k: 0, blackIs1: true)));
    }

    /// <summary>
    /// The raw stream is the complement of the mode-1 pixels, so the default <c>BlackIs1</c> inverts it.
    /// </summary>
    [Fact]
    public void Inverts_the_bitmap_when_black_is_zero()
    {
        var expected = Expected.Select(value => (byte)~value).ToArray();

        Assert.Equal(expected, Decode(Group4, Params(k: -1)));
    }

    /// <summary>The width and height come from the image when the parameters omit them.</summary>
    [Fact]
    public void Reads_the_geometry_from_the_image_when_the_parameters_omit_it()
    {
        var payload = PdfImageFilters.Decode(
            Stream(Group4, Params(k: -1, blackIs1: true, geometry: false)),
            1 << 20,
            "ccitt.pdf");

        Assert.True(payload.IsDecodable);
        Assert.Equal(Expected, payload.Data!.Value.ToArray());
    }

    /// <summary>A stream that is not CCITT data is a payload the filter refuses.</summary>
    [Fact]
    public void Refuses_a_stream_it_cannot_decode()
    {
        var payload = PdfImageFilters.Decode(Stream([0, 0, 0, 0], Params(k: -1)), 1 << 20, "ccitt.pdf");

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
    }

    /// <summary>
    /// A stream that stops mid-line is refused rather than written as far as it got: the image would be
    /// missing rows the document says it has.
    /// </summary>
    [Fact]
    public void Refuses_a_group_4_stream_that_stops_mid_line()
    {
        var payload = PdfImageFilters.Decode(Stream(Group4[..10], Params(k: -1)), 1 << 20, "ccitt.pdf");

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
    }

    /// <summary>Without a row count the decoder stops when the data does, which is how a fax stream ends.</summary>
    [Fact]
    public void Decodes_a_stream_without_a_row_count()
    {
        var payload = PdfImageFilters.Decode(
            Stream(Group4, Params(k: -1, blackIs1: true, geometry: false), height: false),
            1 << 20,
            "ccitt.pdf");

        Assert.True(payload.IsDecodable);
        Assert.Equal(Expected, payload.Data!.Value.ToArray());
    }

    /// <summary>A bitmap above the budget is measured and nothing is kept.</summary>
    [Fact]
    public void Measures_a_bitmap_above_the_budget_without_keeping_it()
    {
        var payload = PdfImageFilters.Decode(Stream(Group4, Params(k: -1)), 16, "ccitt.pdf");

        Assert.True(payload.IsDecodable);
        Assert.Equal(Width * Height / 8, payload.Length);
        Assert.Null(payload.Data);
    }

    private static byte[] Decode(byte[] encoded, PdfDictionary parameters)
    {
        var payload = PdfImageFilters.Decode(Stream(encoded, parameters), 1 << 20, "ccitt.pdf");

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal(Width * Height / 8, payload.Length);

        return payload.Data!.Value.ToArray();
    }

    private static PdfDictionary Params(int k, bool geometry = true, bool blackIs1 = false)
    {
        var parameters = new PdfDictionary().SetInteger("K", k);

        if (geometry)
        {
            parameters.SetInteger("Columns", Width).SetInteger("Rows", Height);
        }

        if (blackIs1)
        {
            parameters.Set("BlackIs1", PdfBoolean.From(true));
        }

        return parameters;
    }

    private static PdfStream Stream(byte[] encoded, PdfDictionary? parameters, bool height = true)
    {
        var dictionary = new PdfDictionary()
            .SetName("Filter", "CCITTFaxDecode")
            .SetInteger("Width", Width);

        if (height)
        {
            dictionary.SetInteger("Height", Height);
        }

        if (parameters is not null)
        {
            dictionary.Set("DecodeParms", parameters);
        }

        return new PdfStream(dictionary, encoded);
    }
}
