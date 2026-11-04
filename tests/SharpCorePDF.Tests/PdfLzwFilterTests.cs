using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the LZW filter: the codes the specification defines, the table a stream builds while it is
/// read, the widths the codes grow to, and how a damaged stream is reported.
/// </summary>
/// <remarks>
/// LZW is the compressor a scanned page most often arrives in, and the one whose decoder carries state
/// that can be wrong in ways a single sample never shows: a code that names the entry it is about to
/// define, the moment the field widens, and the clear that resets both.
/// </remarks>
public sealed class PdfLzwFilterTests
{
    /// <summary>The example the specification gives decodes to the text it states.</summary>
    /// <remarks>
    /// ISO 32000-1, 7.4.4.2: the codes 256 45 258 258 65 259 66 257 stand for <c>-----A---B</c>. The
    /// second 258 is the case a table filled up front gets wrong — it names the entry the stream defined
    /// one code earlier — so this vector checks that the table is grown as the codes arrive.
    /// </remarks>
    [Fact]
    public void Decodes_the_example_the_specification_gives()
    {
        int[] codes = [256, 45, 258, 258, 65, 259, 66, 257];

        Assert.Equal("-----A---B", Text(Decode(TestLzw.Pack(codes.Select(code => (code, 9))))));
    }

    /// <summary>
    /// A stream whose codes outgrow nine bits is read at the wider field, which is the boundary a decoder
    /// that fixed the width gets wrong.
    /// </summary>
    [Fact]
    public void Reads_a_code_at_ten_bits_once_the_table_outgrows_nine()
    {
        var codes = new List<(int Code, int Width)> { (256, 9) };

        // 254 literals add 253 entries, so the next free slot is 511 and the field widens one code early.
        for (var value = 0; value < 254; value++)
        {
            codes.Add((value, 9));
        }

        // The first entry the stream defined, which is the two bytes 0 and 1, then the end-of-data code.
        codes.Add((258, 10));
        codes.Add((257, 10));

        var expected = new byte[256];

        for (var value = 0; value < 254; value++)
        {
            expected[value] = (byte)value;
        }

        expected[255] = 1;

        Assert.Equal(expected, Decode(TestLzw.Pack(codes)));
    }

    /// <summary>A payload large enough to fill the table decodes to exactly the bytes that were encoded.</summary>
    /// <remarks>
    /// Nine thousand bytes of pattern reach twelve-bit codes and the full table, which is where an encoder
    /// has to send a clear and a decoder has to honour it rather than keep writing past the last slot.
    /// </remarks>
    [Fact]
    public void Round_trips_a_payload_that_fills_the_table()
    {
        var original = Pattern(9000);

        Assert.Equal(original, Decode(TestLzw.Encode(original, earlyChange: 1)));
    }

    /// <summary>A stream that states <c>EarlyChange 0</c> widens its codes one code later.</summary>
    [Fact]
    public void Round_trips_a_stream_that_does_not_widen_early()
    {
        var original = Pattern(3000);

        Assert.Equal(original, Decode(TestLzw.Encode(original, earlyChange: 0), earlyChange: 0));

        // The same bytes encoded the other way are not the same stream, which is what makes the parameter
        // worth reading rather than assuming: decoding one with the other's convention fails or drifts.
        Assert.NotEqual(TestLzw.Encode(original, earlyChange: 1), TestLzw.Encode(original, earlyChange: 0));
    }

    /// <summary>A stream that stops without its end-of-data code still yields the bytes it defined.</summary>
    [Fact]
    public void Decodes_a_stream_that_stops_before_its_end_of_data_code()
    {
        var codes = new (int Code, int Width)[] { (256, 9), (65, 9), (66, 9) };

        Assert.Equal("AB", Text(Decode(TestLzw.Pack(codes))));
    }

    /// <summary>A code the table never held is refused rather than guessed at.</summary>
    [Fact]
    public void Refuses_a_code_the_table_never_held()
    {
        // The table holds only the literals at this point, so 300 names nothing.
        var codes = new (int Code, int Width)[] { (256, 9), (65, 9), (300, 9) };

        Assert.False(PdfFilters.TryDecodeLzw(TestLzw.Pack(codes), 1, null, out var length));
        Assert.Equal(0, length);
    }

    /// <summary>A stream written by an encoder this repository did not write decodes to the same bytes.</summary>
    /// <remarks>
    /// The fixture is a TIFF strip libtiff wrote for 200 x 8 grey samples, and TIFF and PDF share the LZW
    /// coding, so the strip is what an image with <c>LZWDecode</c> and no predictor holds. A round trip
    /// through this repository's own encoder cannot catch a rule it got wrong on both sides — the width
    /// at which the field grows is exactly such a rule — which is why this case is here.
    /// </remarks>
    [Fact]
    public void Decodes_a_stream_written_by_another_encoder()
    {
        Assert.Equal(Pattern(200 * 8), Decode(File.ReadAllBytes(TestAssets.LzwStrip)));
    }

    /// <summary>An image that declares LZW is decoded, and its samples are exposed as pixels.</summary>
    [Fact]
    public void Decodes_the_pixels_of_an_lzw_image()
    {
        byte[] pixels = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0];

        var payload = PdfImageFilters.Decode(
            ImageStream(
                TestLzw.Encode(pixels),
                ("Filter", new PdfName("LZWDecode")),
                ("Width", new PdfInteger(2)),
                ("Height", new PdfInteger(2)),
                ("BitsPerComponent", new PdfInteger(8)),
                ("ColorSpace", new PdfName("DeviceRGB"))),
            1 << 20,
            "sample.pdf");

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal(pixels.Length, payload.Length);
        Assert.Equal(pixels, payload.Data!.Value.ToArray());

        var (format, decoded) = ImagePixels.Of(
            payload.IsDecodable,
            payload.Filter,
            new PdfSampleSpace(PdfSampleKind.Rgb, 3, false, null, 0),
            2,
            2,
            8,
            payload.Data,
            1 << 20);

        Assert.Equal(ImagePixels.Rgb24, format);
        Assert.Equal(pixels, decoded!.Value.ToArray());
    }

    /// <summary>A predictor that follows an LZW stream is undone the same way it follows Flate.</summary>
    [Fact]
    public void Undoes_a_predictor_after_lzw()
    {
        // Two rows of three samples, encoded with PNG's Sub filter: every sample is stored as its
        // difference from the sample one byte to its left.
        byte[] predicted = [1, 10, 10, 10, 1, 40, 10, 10];

        var parameters = new PdfDictionary()
            .Set("Predictor", new PdfInteger(15))
            .Set("Colors", new PdfInteger(1))
            .Set("BitsPerComponent", new PdfInteger(8))
            .Set("Columns", new PdfInteger(3));

        var payload = PdfImageFilters.Decode(
            ImageStream(
                TestLzw.Encode(predicted),
                ("Filter", new PdfName("LZWDecode")),
                ("DecodeParms", parameters),
                ("Width", new PdfInteger(3)),
                ("Height", new PdfInteger(2)),
                ("BitsPerComponent", new PdfInteger(8)),
                ("ColorSpace", new PdfName("DeviceGray"))),
            1 << 20,
            "sample.pdf");

        Assert.True(payload.IsDecodable);
        Assert.Equal([10, 20, 30, 40, 50, 60], payload.Data!.Value.ToArray());
    }

    /// <summary>A decoded payload above the budget is measured but not held.</summary>
    [Fact]
    public void Reports_an_lzw_payload_above_the_budget_without_buffering_it()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

        var payload = PdfImageFilters.Decode(
            ImageStream(
                TestLzw.Encode(pixels),
                ("Filter", new PdfName("LZWDecode")),
                ("Width", new PdfInteger(2)),
                ("Height", new PdfInteger(2)),
                ("BitsPerComponent", new PdfInteger(8)),
                ("ColorSpace", new PdfName("DeviceRGB"))),
            4,
            "sample.pdf");

        Assert.True(payload.IsDecodable);
        Assert.Equal(pixels.Length, payload.Length);
        Assert.Null(payload.Data);
    }

    /// <summary>Builds a stream with the entries a test needs.</summary>
    /// <param name="raw">The raw bytes of the stream.</param>
    /// <param name="entries">The entries of the stream dictionary.</param>
    /// <returns>The stream.</returns>
    private static PdfStream ImageStream(byte[] raw, params (string Key, PdfObject Value)[] entries)
    {
        var dictionary = new PdfDictionary();

        foreach (var (key, value) in entries)
        {
            dictionary.Set(key, value);
        }

        return new PdfStream(dictionary, raw);
    }

    /// <summary>Decodes a stream and returns the bytes it produced.</summary>
    /// <param name="encoded">The bytes the document stores.</param>
    /// <param name="earlyChange">Whether the code width grows one code early.</param>
    /// <returns>The decoded bytes.</returns>
    private static byte[] Decode(byte[] encoded, int earlyChange = 1)
    {
        var output = new List<byte>();

        Assert.True(PdfFilters.TryDecodeLzw(
            encoded,
            earlyChange,
            sequence => output.AddRange(sequence.ToArray()),
            out var length));

        Assert.Equal(output.Count, (int)length);

        return [.. output];
    }

    /// <summary>Builds a deterministic pattern of bytes that does not compress.</summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <returns>The pattern.</returns>
    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        var state = 2463534242u;

        for (var index = 0; index < length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[index] = (byte)(state & 0xFF);
        }

        return bytes;
    }

    /// <summary>Decodes bytes as Latin-1 text, which is how a PDF stores them.</summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <returns>The text.</returns>
    private static string Text(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
