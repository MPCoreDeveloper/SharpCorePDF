using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the cross-reference stream an appended revision ends in: the bytes of every row, the subsections
/// <c>/Index</c> names, the field widths of <c>/W</c>, and the trailer entries the stream's dictionary carries.
/// </summary>
/// <remarks>
/// The rows are compared as hexadecimal so that a wrong width, a wrong byte order or a missing row shows in
/// the expectation itself.
/// </remarks>
public sealed class PdfCrossReferenceStreamWriterTests
{
    /// <summary>
    /// Every row is a type byte, an offset as wide as the largest offset needs and a two-byte generation, in
    /// object number order; the stream lists itself, numbered above every other object, and the head of the free
    /// list when it is asked to.
    /// </summary>
    [Fact]
    public void Writes_every_row_of_the_section_in_object_number_order()
    {
        var (dictionary, rows, text) = Write(
            [(7, 2, 70000), (3, 0, 100), (4, 0, 200)],
            300000,
            new PdfDictionary().SetInteger("Size", 5).SetInteger("XRefStm", 9).Set("Root", new PdfReference(1, 0)),
            freeHead: true);

        Assert.Equal("XRef", dictionary.GetName("Type"));
        Assert.Equal(9, dictionary.GetInteger("Size"));
        Assert.Null(dictionary.Get("XRefStm"));
        Assert.Equal(1, Assert.IsType<PdfReference>(dictionary.Get("Root")).ObjectNumber);
        Assert.Equal([1L, 3L, 2L], Integers(dictionary.Get("W")));
        Assert.Equal([0L, 1L, 3L, 2L, 7L, 2L], Integers(dictionary.Get("Index")));
        Assert.Equal(
            "00000000FFFF" + "010000640000" + "010000C80000" + "010111700002" + "010493E00000",
            Convert.ToHexString(rows));
        Assert.StartsWith("8 0 obj\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\nendstream\nendobj\nstartxref\n300000\n%%EOF\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stream is numbered at the <c>/Size</c> the trailer states when that is above every object of the
    /// revision, and a revision appended by a signature lists no free head.
    /// </summary>
    [Fact]
    public void Numbers_the_stream_at_the_size_the_trailer_states()
    {
        var (dictionary, rows, text) = Write([(3, 0, 16)], 40, new PdfDictionary().SetInteger("Size", 20), freeHead: false);

        Assert.Equal(21, dictionary.GetInteger("Size"));
        Assert.Equal([3L, 1L, 20L, 1L], Integers(dictionary.Get("Index")));
        Assert.Equal("0110000001280000", Convert.ToHexString(rows));
        Assert.StartsWith("20 0 obj\n", text, StringComparison.Ordinal);
    }

    /// <summary>The offset field is as narrow as the largest offset allows, and no narrower.</summary>
    /// <param name="startXref">The offset of the stream, the largest of the section.</param>
    /// <param name="width">The width the offset field must have.</param>
    [Theory]
    [InlineData(255L, 1)]
    [InlineData(256L, 2)]
    [InlineData(65535L, 2)]
    [InlineData(65536L, 3)]
    [InlineData(16777216L, 4)]
    [InlineData(4294967296L, 5)]
    public void Sizes_the_offset_field_to_the_largest_offset(long startXref, int width)
    {
        var (dictionary, _, _) = Write([], startXref, new PdfDictionary(), freeHead: false);

        Assert.Equal(width, Integers(dictionary.Get("W"))[1]);
    }

    private static (PdfDictionary Dictionary, byte[] Rows, string Text) Write(
        (int Number, int Generation, long Offset)[] entries,
        long startXref,
        PdfDictionary trailer,
        bool freeHead)
    {
        using var buffer = new MemoryStream();
        var output = new PdfOutputBuffer(buffer);

        PdfCrossReferenceStreamWriter.Write(output, startXref, entries, trailer, freeHead);

        var bytes = buffer.ToArray();
        var parser = new PdfObjectParser(bytes, "section.pdf", 0, null, 64);

        Assert.True(parser.TryReadIndirectHeader(out _, out _));

        var stream = Assert.IsType<PdfStream>(parser.ParseObject());

        return (stream.Dictionary, stream.Raw.ToArray(), Encoding.Latin1.GetString(bytes));
    }

    private static long[] Integers(PdfObject? array) =>
        Assert.IsType<PdfArray>(array).Items.Select(item => Assert.IsType<PdfInteger>(item).Value).ToArray();
}
