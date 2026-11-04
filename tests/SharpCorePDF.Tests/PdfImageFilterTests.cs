using System.IO.Compression;
using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the decoder for the payload of an image or an attachment: which filter it applies, how much of
/// the result it keeps inside the inline budget, and how it reports what it could not decode.
/// </summary>
/// <remarks>
/// <para>
/// This class answers a different question than <see cref="PdfFilters"/> does — an image the library cannot
/// decode is a warning on a readable document, not a broken one — and it answers it three times: by
/// buffering a payload that fits the budget, by counting one that does not while keeping nothing, and by
/// streaming a copy that never holds the payload at all. The three routes are tested against the same
/// bytes here, because a filter that decodes on one of them and not on the others is a bug an export would
/// find before the suite would.
/// </para>
/// <para>
/// The budget is an inclusive limit, so the tests that turn on it state the boundary in their name and
/// assert both sides of it: a payload of exactly the budget is kept and one byte more is measured only.
/// </para>
/// </remarks>
public sealed class PdfImageFilterTests
{
    /// <summary>A stream that declares no filter is its own payload.</summary>
    [Fact]
    public void Returns_the_payload_of_a_stream_that_declares_no_filter()
    {
        var payload = PdfImageFilters.Decode(Stream([1, 2, 3]), 16, "sample.pdf");

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Null(payload.Filter);
        Assert.Equal(3, payload.Length);
        Assert.Equal([1, 2, 3], payload.Data!.Value.ToArray());
    }

    /// <summary>A decoder without a stream is a programming error, not a document that cannot be read.</summary>
    [Fact]
    public void Refuses_to_decode_without_a_stream() =>
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = PdfImageFilters.Decode(null!, 16, "sample.pdf");
        });

    /// <summary>Flate data, as a producer normally writes it, is inflated and kept within the budget.</summary>
    [Fact]
    public void Inflates_a_flate_payload_that_fits_the_budget()
    {
        byte[] samples = [10, 20, 30, 40, 50, 60];

        var payload = Decode(Deflate(samples), ("Filter", new PdfName("FlateDecode")));

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal("FlateDecode", payload.Filter);
        Assert.Equal(samples.Length, payload.Length);
        Assert.Equal(samples, payload.Data!.Value.ToArray());
    }

    /// <summary>A producer may write Flate without the zlib wrapper, so both wrappers are tried.</summary>
    [Fact]
    public void Inflates_a_flate_payload_written_without_the_zlib_wrapper()
    {
        byte[] samples = [1, 2, 3, 4, 5];

        var payload = Decode(RawDeflate(samples), ("Filter", new PdfName("FlateDecode")));

        Assert.True(payload.IsDecodable);
        Assert.Equal(samples.Length, payload.Length);
        Assert.Equal(samples, payload.Data!.Value.ToArray());
    }

    /// <summary>The abbreviated name of every filter this class decodes is expanded before it is applied.</summary>
    [Fact]
    public void Expands_the_abbreviated_names_of_the_filters_it_decodes()
    {
        Assert.Equal("Hello", Text(Kept(Decode(Deflate(Encoding.Latin1.GetBytes("Hello")), ("Filter", new PdfName("Fl"))))));
        Assert.Equal("Hello", Text(Kept(Decode(Encoding.Latin1.GetBytes("48656C6C6F>"), ("Filter", new PdfName("AHx"))))));
        Assert.Equal("Hello", Text(Kept(Decode(Encoding.Latin1.GetBytes("87cURDj~>"), ("Filter", new PdfName("A85"))))));
        Assert.Equal("Hello", Text(Kept(Decode(TestLzw.Encode(Encoding.Latin1.GetBytes("Hello")), ("Filter", new PdfName("LZW"))))));
    }

    /// <summary>JPEG is handed on as it is, and its abbreviation names the same filter.</summary>
    [Fact]
    public void Hands_back_a_jpeg_behind_its_abbreviated_name()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

        var payload = Decode(jpeg, ("Filter", new PdfName("DCT")));

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal("DCT", payload.Filter);
        Assert.Equal(jpeg, payload.Data!.Value.ToArray());
    }

    /// <summary>A payload a filter refuses is reported, and its stored bytes are handed back.</summary>
    [Fact]
    public void Reports_a_payload_that_a_filter_refuses()
    {
        byte[] damaged = [0xFF, 0xFF, 0xFF, 0xFF];

        var payload = Decode(damaged, ("Filter", new PdfName("FlateDecode")));

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
        Assert.Equal("FlateDecode", payload.Filter);
        Assert.Equal(damaged.Length, payload.Length);
        Assert.Equal(damaged, payload.Data!.Value.ToArray());
    }

    /// <summary>A chain is described by every filter in it, in the order the stream applies them.</summary>
    /// <remarks>
    /// The second filter is JPEG, which ends the walk: a JPEG behind Flate is an image, and the bytes the
    /// read carries are the samples after the Flate stage rather than the stored stream.
    /// </remarks>
    [Fact]
    public void Describes_a_chain_in_the_order_it_is_applied()
    {
        byte[] samples = [7, 8, 9];

        var payload = Decode(
            Deflate(samples),
            ("Filter", new PdfArray([new PdfName("FlateDecode"), new PdfName("DCTDecode")])));

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal("FlateDecode DCTDecode", payload.Filter);
        Assert.Equal(samples.Length, payload.Length);
        Assert.Equal(samples, payload.Data!.Value.ToArray());
    }

    /// <summary>A filter the library knows it does not decode keeps its bytes and is not a data error.</summary>
    [Fact]
    public void Reports_a_filter_it_passes_through()
    {
        byte[] stored = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50];

        var payload = Decode(stored, ("Filter", new PdfName("JPXDecode")));

        Assert.False(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal("JPXDecode", payload.Filter);
        Assert.Equal(stored.Length, payload.Length);
        Assert.Equal(stored, payload.Data!.Value.ToArray());
    }

    /// <summary>
    /// A filter name this library has never seen is an unsupported filter, not an unreadable payload.
    /// </summary>
    /// <remarks>
    /// The contract keeps the two apart: <c>ImageFilterUnsupported</c> says nothing was decoded because the
    /// filter is one this library does not implement, and <c>ImageDataUnreadable</c> says a filter it does
    /// implement refused its data. A producer that invents a filter name has done the first, and reporting
    /// the second tells a caller the document is damaged when it is only newer than the reader.
    /// </remarks>
    [Fact]
    public void Reports_a_filter_name_it_has_never_seen_as_unsupported()
    {
        byte[] stored = [1, 2, 3, 4];

        var payload = Decode(stored, ("Filter", new PdfName("SCPDFDecode")));

        Assert.False(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal("SCPDFDecode", payload.Filter);
        Assert.Equal(stored, payload.Data!.Value.ToArray());
    }

    /// <summary>A payload larger than the budget is measured, and nothing is kept.</summary>
    [Fact]
    public void Measures_a_payload_above_the_budget_without_keeping_it()
    {
        var payload = PdfImageFilters.Decode(Stream([1, 2, 3, 4, 5]), 4, "sample.pdf");

        Assert.True(payload.IsDecodable);
        Assert.Equal(5, payload.Length);
        Assert.Null(payload.Data);
    }

    /// <summary>A payload of exactly the budget is kept: the limit is inclusive.</summary>
    [Fact]
    public void Keeps_a_payload_that_fills_the_budget_exactly()
    {
        var payload = PdfImageFilters.Decode(Stream([1, 2, 3, 4]), 4, "sample.pdf");

        Assert.Equal(4, payload.Length);
        Assert.Equal([1, 2, 3, 4], payload.Data!.Value.ToArray());
    }

    /// <summary>A budget of zero keeps nothing, not even a payload that is itself empty.</summary>
    [Fact]
    public void Keeps_nothing_when_the_budget_is_zero()
    {
        var empty = PdfImageFilters.Decode(Stream([]), 0, "sample.pdf");
        var some = PdfImageFilters.Decode(Stream([1]), 0, "sample.pdf");

        Assert.Equal(0, empty.Length);
        Assert.Null(empty.Data);
        Assert.Equal(1, some.Length);
        Assert.Null(some.Data);
    }

    /// <summary>An ASCII hexadecimal payload is decoded to its length whether or not it is kept.</summary>
    [Fact]
    public void Keeps_an_ascii_hexadecimal_payload_only_within_the_budget()
    {
        var body = Encoding.Latin1.GetBytes("48656C6C6F>");

        var kept = Decode(body, 5, ("Filter", new PdfName("ASCIIHexDecode")));
        var dropped = Decode(body, 4, ("Filter", new PdfName("ASCIIHexDecode")));

        Assert.Equal("Hello", Text(Kept(kept)));
        Assert.Equal(5, dropped.Length);
        Assert.Null(dropped.Data);
    }

    /// <summary>An ASCII85 payload is measured the same way.</summary>
    [Fact]
    public void Keeps_an_ascii85_payload_only_within_the_budget()
    {
        var body = Encoding.Latin1.GetBytes("87cURDj~>");

        var dropped = Decode(body, 4, ("Filter", new PdfName("ASCII85Decode")));

        Assert.Equal(5, dropped.Length);
        Assert.Null(dropped.Data);
    }

    /// <summary>
    /// A predicted payload is undone one row at a time, and the rows that were undone are what is kept.
    /// </summary>
    /// <remarks>
    /// TIFF prediction is horizontal: a sample is stored as its difference from the sample of the same
    /// component one pixel to its left, so the row <c>10 10 10</c> stands for <c>10 20 30</c>. The second
    /// row repeats the same differences, which is what makes it a second row rather than a continuation:
    /// nothing in one row is predicted from the row above it.
    /// </remarks>
    [Fact]
    public void Undoes_a_tiff_prediction_row_by_row()
    {
        byte[] predicted = [10, 10, 10, 1, 1, 1];

        var payload = Decode(
            Deflate(predicted),
            ("Filter", new PdfName("FlateDecode")),
            ("DecodeParms", Predictor(2, columns: 3)));

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal(6, payload.Length);
        Assert.Equal([10, 20, 30, 1, 2, 3], Kept(payload));
    }

    /// <summary>Each row of a PNG prediction names its own filter, and it is undone against the row above.</summary>
    [Fact]
    public void Undoes_a_png_prediction_row_by_row()
    {
        // Row one states "none" (0) and row two states "up" (2), which adds the row above to every byte.
        byte[] predicted = [0, 10, 20, 30, 2, 1, 2, 3];

        var payload = Decode(
            Deflate(predicted),
            ("Filter", new PdfName("FlateDecode")),
            ("DecodeParms", Predictor(12, columns: 3)));

        Assert.True(payload.IsDecodable);
        Assert.Equal(6, payload.Length);
        Assert.Equal([10, 20, 30, 11, 22, 33], Kept(payload));
    }

    /// <summary>An undone predicted payload of exactly the budget is kept, as an unpredicted one is.</summary>
    [Fact]
    public void Keeps_a_predicted_payload_that_fills_the_budget_exactly()
    {
        byte[] predicted = [10, 10, 10];

        var kept = Decode(Deflate(predicted), 3, ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(2, 3)));
        var dropped = Decode(Deflate(predicted), 2, ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(2, 3)));

        Assert.Equal([10, 20, 30], Kept(kept));
        Assert.Equal(3, dropped.Length);
        Assert.Null(dropped.Data);
    }

    /// <summary>A payload too short to form a row is handed back as it was inflated.</summary>
    [Fact]
    public void Hands_back_a_predicted_payload_too_short_to_form_a_row()
    {
        byte[] predicted = [0x42, 0x43];

        var payload = Decode(
            Deflate(predicted),
            ("Filter", new PdfName("FlateDecode")),
            ("DecodeParms", Predictor(12, columns: 3)));

        Assert.True(payload.IsDecodable);
        Assert.Equal(predicted.Length, payload.Length);
        Assert.Equal(predicted, Kept(payload));
    }

    /// <summary>A row that names a predictor the specification does not define is reported, not guessed at.</summary>
    [Fact]
    public void Reports_a_prediction_row_that_names_an_undefined_filter()
    {
        byte[] predicted = [9, 1, 2, 3];

        var payload = Decode(
            Deflate(predicted),
            ("Filter", new PdfName("FlateDecode")),
            ("DecodeParms", Predictor(12, columns: 3)));

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
    }

    /// <summary>A payload of no bytes at all is a payload no filter could decode.</summary>
    /// <remarks>
    /// An empty stream is not an empty image: a Flate stream that ends before its first chunk, an LZW
    /// stream that never states a code, and a predicted payload that never fills a row all leave nothing
    /// to hand back. Reporting them as decodable would hide the damage behind an empty list of samples.
    /// </remarks>
    /// <param name="filter">The filter the stream states.</param>
    /// <param name="predictor">The predictor its decode parameters state, or zero for none.</param>
    [Theory]
    [InlineData("FlateDecode", 0)]
    [InlineData("FlateDecode", 12)]
    [InlineData("LZWDecode", 0)]
    [InlineData("LZWDecode", 2)]
    public void Reports_an_empty_payload_as_unreadable(string filter, int predictor)
    {
        var entries = new List<(string Key, PdfObject Value)> { ("Filter", new PdfName(filter)) };

        if (predictor > 0)
        {
            entries.Add(("DecodeParms", Predictor(predictor, columns: 3)));
        }

        var payload = Decode([], [.. entries]);

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
        Assert.Equal(0, payload.Length);
    }

    /// <summary>An LZW payload is measured as it decodes, and kept only while it fits the budget.</summary>
    [Fact]
    public void Keeps_an_lzw_payload_only_while_it_fits_the_budget()
    {
        byte[] samples = [1, 2, 3, 4, 5, 6];
        var encoded = TestLzw.Encode(samples);

        var kept = Decode(encoded, 6, ("Filter", new PdfName("LZWDecode")));
        var dropped = Decode(encoded, 5, ("Filter", new PdfName("LZWDecode")));
        var nothing = Decode(encoded, 0, ("Filter", new PdfName("LZWDecode")));

        Assert.True(kept.IsDecodable);
        Assert.Equal(samples, Kept(kept));
        Assert.Equal(samples.Length, dropped.Length);
        Assert.Null(dropped.Data);
        Assert.Equal(samples.Length, nothing.Length);
        Assert.Null(nothing.Data);
    }

    /// <summary>An LZW payload with a predictor is undone a row at a time as well.</summary>
    [Fact]
    public void Undoes_an_lzw_payload_with_a_tiff_predictor()
    {
        byte[] predicted = [10, 10, 10, 1, 1, 1];

        var payload = Decode(
            TestLzw.Encode(predicted),
            ("Filter", new PdfName("LZWDecode")),
            ("DecodeParms", Predictor(2, columns: 3)));

        Assert.True(payload.IsDecodable);
        Assert.False(payload.Failed);
        Assert.Equal(6, payload.Length);
        Assert.Equal([10, 20, 30, 1, 2, 3], Kept(payload));
    }

    /// <summary>An undone predicted LZW payload is kept only while the rows fit the budget.</summary>
    /// <remarks>
    /// The first decode primes the pooled buffer this route rents with bytes that are not the ones expected
    /// below, because a rented buffer is not cleared: a route that advances over the buffer without writing
    /// the row hands back those bytes, and the reading that would otherwise pass on them is the one that
    /// says the row was written from itself.
    /// </remarks>
    [Fact]
    public void Keeps_a_predicted_lzw_payload_only_while_it_fits_the_budget()
    {
        byte[] predicted = [10, 10, 10];
        var encoded = TestLzw.Encode(predicted);

        _ = Decode(TestLzw.Encode([20, 20, 20]), 3, ("Filter", new PdfName("LZWDecode")), ("DecodeParms", Predictor(2, 3)));

        var kept = Decode(encoded, 3, ("Filter", new PdfName("LZWDecode")), ("DecodeParms", Predictor(2, 3)));
        var dropped = Decode(encoded, 2, ("Filter", new PdfName("LZWDecode")), ("DecodeParms", Predictor(2, 3)));
        var nothing = Decode(encoded, 0, ("Filter", new PdfName("LZWDecode")), ("DecodeParms", Predictor(2, 3)));

        Assert.Equal([10, 20, 30], Kept(kept));
        Assert.Equal(3, dropped.Length);
        Assert.Null(dropped.Data);
        Assert.Equal(3, nothing.Length);
        Assert.Null(nothing.Data);
    }

    /// <summary>The chain a stream declares is described once, or not at all when it declares none.</summary>
    [Fact]
    public void Describes_the_filter_chain_a_stream_declares()
    {
        Assert.Null(PdfImageFilters.DescribeFilter(new PdfDictionary()));

        Assert.Equal(
            "FlateDecode",
            PdfImageFilters.DescribeFilter(new PdfDictionary().Set("Filter", new PdfName("FlateDecode"))));

        Assert.Equal(
            "LZWDecode ASCII85Decode",
            PdfImageFilters.DescribeFilter(
                new PdfDictionary().Set("Filter", new PdfArray([new PdfName("LZWDecode"), new PdfName("ASCII85Decode")]))));
    }

    /// <summary>The copy route writes an unfiltered payload through its bounded buffer.</summary>
    [Fact]
    public async Task Copies_the_payload_of_a_stream_that_declares_no_filter()
    {
        // Larger than one chunk, so the loop that walks the payload runs more than once and an offset that
        // is off by its own length lands on the second iteration rather than on the last one.
        var samples = Pattern(150_000);

        Assert.Equal(samples, await Copy(Stream(samples)));
    }

    /// <summary>The copy route inflates a Flate payload as it is written, a chunk at a time.</summary>
    [Fact]
    public async Task Copies_a_flate_payload_it_inflates()
    {
        var samples = Pattern(150_000);

        Assert.Equal(samples, await Copy(Stream(Deflate(samples), ("Filter", new PdfName("FlateDecode")))));
    }

    /// <summary>A Flate payload written without the zlib wrapper is copied the same way.</summary>
    [Fact]
    public async Task Copies_a_flate_payload_written_without_the_zlib_wrapper()
    {
        var samples = Pattern(4000);

        Assert.Equal(samples, await Copy(Stream(RawDeflate(samples), ("Filter", new PdfName("FlateDecode")))));
    }

    /// <summary>The copy route undoes a predictor row by row instead of holding the payload.</summary>
    [Fact]
    public async Task Copies_a_predicted_payload_row_by_row()
    {
        byte[] predicted = [10, 10, 10, 1, 1, 1];

        var copied = await Copy(
            Stream(Deflate(predicted), ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(2, 3))));

        Assert.Equal([10, 20, 30, 1, 2, 3], copied);
    }

    /// <summary>A predicted payload too short for a row is copied as it was inflated.</summary>
    /// <remarks>
    /// The first copy writes whole rows and leaves those bytes in the pooled buffer this route rents; the
    /// second then states a payload that completes no row at all. Writing the remainder from a buffer that
    /// was left alone would hand back the first payload's bytes, so the two calls are what makes the
    /// second answer say the short payload was written from itself.
    /// </remarks>
    [Fact]
    public async Task Copies_the_bytes_of_a_predicted_payload_too_short_for_a_row()
    {
        byte[] whole = await Copy(
            Stream(Deflate([10, 10, 10]), ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(2, 3))));

        byte[] partial = await Copy(
            Stream(Deflate([0x42, 0x43]), ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(12, 3))));

        Assert.Equal([10, 20, 30], whole);
        Assert.Equal([0x42, 0x43], partial);
    }

    /// <summary>A predicted payload written without the zlib wrapper is copied the same way.</summary>
    [Fact]
    public async Task Copies_a_predicted_payload_written_without_the_zlib_wrapper()
    {
        byte[] predicted = [10, 10, 10, 1, 1, 1];

        var copied = await Copy(
            Stream(RawDeflate(predicted), ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(2, 3))));

        Assert.Equal([10, 20, 30, 1, 2, 3], copied);
    }

    /// <summary>A copy whose predicted row is wider than a chunk still writes whole rows.</summary>
    /// <remarks>
    /// One read inflates to at most a chunk plus the row that was left unfinished, so the buffer this route
    /// rents has to be a chunk larger than a row it must hold. A row wider than a chunk is the case that
    /// says the addition is an addition: the rented buffer is smaller than the row as soon as the row is
    /// subtracted, and the row that arrives no longer fits.
    /// </remarks>
    [Fact]
    public async Task Copies_a_predicted_row_that_is_wider_than_a_chunk()
    {
        const int columns = 40_000;
        var predicted = new byte[columns];

        var copied = await Copy(
            Stream(Deflate(predicted), ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(2, columns))));

        Assert.Equal(predicted, copied);
    }

    /// <summary>A trailing row no prediction completes is not part of a predicted copy.</summary>
    /// <remarks>
    /// Two whole rows and two bytes of a third: the third row cannot be undone, so it is left out rather
    /// than written as it stands. That is the same answer the read route gives for the same bytes, where a
    /// remainder is only reported when no row at all was decoded.
    /// </remarks>
    [Fact]
    public async Task Copies_only_the_rows_a_predicted_payload_completes()
    {
        byte[] predicted = [10, 10, 10, 1, 1, 1, 0x42, 0x43];

        var copied = await Copy(
            Stream(Deflate(predicted), ("Filter", new PdfName("FlateDecode")), ("DecodeParms", Predictor(2, 3))));

        Assert.Equal([10, 20, 30, 1, 2, 3], copied);
    }

    /// <summary>A filter the copy route does not stream is decoded and then written.</summary>
    [Fact]
    public async Task Copies_a_payload_behind_a_filter_it_materialises()
    {
        var copied = await Copy(Stream(Encoding.Latin1.GetBytes("48656C6C6F>"), ("Filter", new PdfName("ASCIIHexDecode"))));

        Assert.Equal("Hello", Text(copied));
    }

    /// <summary>A chain whose first filter crossed the budget is reported, not guessed at.</summary>
    /// <remarks>
    /// The second filter has nothing to work on once the first stopped capturing, so the chain is reported
    /// as failed with the length the first filter did produce. Continuing anyway would decode the empty
    /// remainder and hand back a decodable payload of no bytes, which is the one answer that cannot be
    /// right: the bytes exist, and this class chose not to keep them.
    /// </remarks>
    [Fact]
    public void Reports_a_chain_whose_first_filter_crossed_the_budget()
    {
        byte[] samples = [1, 2, 3, 4, 5];

        var payload = Decode(
            Deflate(samples),
            4,
            ("Filter", new PdfArray([new PdfName("FlateDecode"), new PdfName("ASCIIHexDecode")])));

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
        Assert.Equal("FlateDecode ASCIIHexDecode", payload.Filter);
        Assert.Equal(samples.Length, payload.Length);
        Assert.Null(payload.Data);
    }

    /// <summary>A chain whose LZW filter crossed the budget is reported, as one whose Flate filter did is.</summary>
    [Fact]
    public void Reports_a_chain_whose_lzw_filter_crossed_the_budget()
    {
        byte[] samples = [1, 2, 3, 4, 5];

        var payload = Decode(
            TestLzw.Encode(samples),
            4,
            ("Filter", new PdfArray([new PdfName("LZWDecode"), new PdfName("ASCIIHexDecode")])));

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
        Assert.Equal(samples.Length, payload.Length);
        Assert.Null(payload.Data);
    }

    /// <summary>A payload whose decode parameters state a predictor of one is read as it stands.</summary>
    /// <remarks>
    /// One is the value the specification gives prediction when a producer states it at all, and it means
    /// that no prediction was applied. Routing it through the row decoder would undo nothing and then
    /// refuse the payload, which is why the parameter is compared rather than merely read.
    /// </remarks>
    [Fact]
    public void Reads_a_payload_whose_decode_parameters_state_no_prediction()
    {
        byte[] samples = [1, 2, 3];

        var payload = Decode(
            Deflate(samples),
            ("Filter", new PdfName("FlateDecode")),
            ("DecodeParms", Predictor(1, columns: 3)));

        Assert.True(payload.IsDecodable);
        Assert.Equal(samples, Kept(payload));
    }

    /// <summary>Prediction in a Flate payload written without the zlib wrapper is undone as well.</summary>
    [Fact]
    public void Undoes_a_prediction_in_a_flate_payload_written_without_the_zlib_wrapper()
    {
        byte[] predicted = [10, 10, 10, 1, 1, 1];

        var payload = Decode(
            RawDeflate(predicted),
            ("Filter", new PdfName("FlateDecode")),
            ("DecodeParms", Predictor(2, columns: 3)));

        Assert.True(payload.IsDecodable);
        Assert.Equal([10, 20, 30, 1, 2, 3], Kept(payload));
    }

    /// <summary>
    /// A raw Deflate block whose first two bytes pass the zlib checksum is still read as raw Deflate.
    /// </summary>
    /// <remarks>
    /// The two halves of the zlib test are joined by <c>and</c>, not by <c>or</c>, because a header is a zlib
    /// header only when both hold. A stored Deflate block is the case that tells them apart: it states its
    /// length in the second byte, so a block of twenty-three bytes makes the pair a multiple of thirty-one
    /// while the method bits still say "not zlib".
    /// </remarks>
    [Fact]
    public async Task Copies_a_raw_deflate_block_whose_length_passes_the_zlib_checksum()
    {
        byte[] body = [.. Enumerable.Range(1, 23).Select(value => (byte)value)];

        // A final stored block: the header, the length and its complement, then the bytes themselves. The
        // length is stated little-endian, so its low byte is what the zlib checksum sees.
        byte[] stored = [0x01, 0x17, 0x00, 0xE8, 0xFF, .. body];

        Assert.Equal(body, await Copy(Stream(stored, ("Filter", new PdfName("FlateDecode")))));
    }

    /// <summary>A code the table never held is reported rather than decoded into nothing.</summary>
    [Fact]
    public void Reports_an_lzw_payload_that_names_a_code_the_table_never_held()
    {
        var encoded = TestLzw.Pack([(256, 9), (511, 9)]);

        var payload = Decode(encoded, ("Filter", new PdfName("LZWDecode")));

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
    }

    /// <summary>A damaged LZW payload with a predictor is reported the same way.</summary>
    [Fact]
    public void Reports_a_damaged_lzw_payload_with_a_predictor()
    {
        var encoded = TestLzw.Pack([(256, 9), (511, 9)]);

        var payload = Decode(
            encoded,
            ("Filter", new PdfName("LZWDecode")),
            ("DecodeParms", Predictor(2, columns: 3)));

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
    }

    /// <summary>A row of an LZW payload that names an undefined prediction filter is reported too.</summary>
    [Fact]
    public void Reports_an_lzw_row_that_names_an_undefined_prediction_filter()
    {
        byte[] predicted = [9, 1, 2, 3];

        var payload = Decode(
            TestLzw.Encode(predicted),
            ("Filter", new PdfName("LZWDecode")),
            ("DecodeParms", Predictor(12, columns: 3)));

        Assert.False(payload.IsDecodable);
        Assert.True(payload.Failed);
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

    /// <summary>Builds the decode parameters that state a predictor, a row width and one component.</summary>
    /// <param name="predictor">The predictor the filter declares.</param>
    /// <param name="columns">The number of samples in a row.</param>
    /// <returns>The value of the <c>/DecodeParms</c> entry of the stream dictionary.</returns>
    private static PdfDictionary Predictor(int predictor, int columns) =>
        new PdfDictionary()
            .Set("Predictor", new PdfInteger(predictor))
            .Set("Columns", new PdfInteger(columns));

    /// <summary>Decodes a stream against the budget the inline route normally gets.</summary>
    /// <param name="raw">The raw bytes of the stream.</param>
    /// <param name="entries">The entries of the stream dictionary.</param>
    /// <returns>What the decoder produced.</returns>
    private static PdfPayload Decode(byte[] raw, params (string Key, PdfObject Value)[] entries) =>
        Decode(raw, 1 << 20, entries);

    /// <summary>Decodes a stream against a budget of the given size.</summary>
    /// <param name="raw">The raw bytes of the stream.</param>
    /// <param name="cap">The largest payload that is buffered.</param>
    /// <param name="entries">The entries of the stream dictionary.</param>
    /// <returns>What the decoder produced.</returns>
    private static PdfPayload Decode(byte[] raw, int cap, params (string Key, PdfObject Value)[] entries) =>
        PdfImageFilters.Decode(Stream(raw, entries), cap, "sample.pdf");

    /// <summary>Reads the bytes a payload carries, insisting that it carries any.</summary>
    /// <param name="payload">What the decoder produced.</param>
    /// <returns>The payload's bytes.</returns>
    private static byte[] Kept(PdfPayload payload) => payload.Data!.Value.ToArray();

    /// <summary>Copies a stream the way the export route does, into a destination of its own.</summary>
    /// <param name="stream">The stream to copy.</param>
    /// <returns>The bytes that were written.</returns>
    private static async Task<byte[]> Copy(PdfStream stream)
    {
        using var destination = new MemoryStream();

        await PdfImageFilters.CopyAsync(stream, destination, "sample.pdf", TestContext.Current.CancellationToken);

        return destination.ToArray();
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

