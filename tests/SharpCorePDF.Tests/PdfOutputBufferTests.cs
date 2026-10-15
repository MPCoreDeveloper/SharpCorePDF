using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the output buffer: what reaches the stream, what the buffer reports as the length of the output,
/// and how a chunk that does not fit into the free room of the buffer is written.
/// </summary>
/// <remarks>
/// The length the buffer tracks is what the cross-reference table of a merged document is built from, so a
/// byte that is written without being counted, or counted twice, moves every offset behind it.
/// </remarks>
public sealed class PdfOutputBufferTests
{
    /// <summary>Every kind of write reaches the stream, and the length adds up.</summary>
    [Fact]
    public void Writes_everything_it_is_given()
    {
        using var destination = new MemoryStream();
        var output = new PdfOutputBuffer(destination);

        output.WriteByte((byte)'%');
        output.Write("PDF-1.7"u8);
        output.WriteInteger(-42);
        output.WriteReal(0.5d);
        output.WriteReference(12, 0);
        output.WriteOffsetField(7);
        output.Flush();

        Assert.Equal("%PDF-1.7-420.512 0 R0000000007", Encoding.Latin1.GetString(destination.ToArray()));
        Assert.Equal(30L, output.Length);
    }

    /// <summary>Nothing is written before the first byte, and flushing twice is harmless.</summary>
    [Fact]
    public void Flushes_only_what_was_written()
    {
        using var destination = new MemoryStream();
        var output = new PdfOutputBuffer(destination);

        output.Flush();
        output.Flush();

        Assert.Equal(0L, output.Length);
        Assert.Empty(destination.ToArray());

        output.Write("x"u8);
        output.Flush();
        output.Flush();

        Assert.Equal(1L, output.Length);
        Assert.Equal("x", Encoding.Latin1.GetString(destination.ToArray()));
    }

    /// <summary>A buffer that is too small for the output is flushed as it fills up.</summary>
    [Fact]
    public void Flushes_when_the_buffer_is_full()
    {
        using var destination = new MemoryStream();
        var output = new PdfOutputBuffer(destination, bufferSize: 1);

        for (var index = 0; index < 5000; index++)
        {
            output.WriteByte((byte)(index % 256));
        }

        output.Flush();

        var written = destination.ToArray();

        Assert.Equal(5000L, output.Length);
        Assert.Equal(5000, written.Length);
        Assert.Equal(0, written[0]);
        Assert.Equal(255, written[255]);
        Assert.Equal(4999 % 256, written[4999]);
    }

    /// <summary>A chunk that fills the buffer goes straight to the stream, without being copied through it.</summary>
    [Fact]
    public void Passes_a_large_chunk_straight_to_the_stream()
    {
        using var destination = new MemoryStream();
        var output = new PdfOutputBuffer(destination, bufferSize: 1024);
        var large = new byte[4096];

        output.Write("head"u8);
        output.Write(large);
        output.Write("tail"u8);
        output.Flush();

        var written = destination.ToArray();

        Assert.Equal(8L + 4096, output.Length);
        Assert.Equal("head", Encoding.Latin1.GetString(written[..4]));
        Assert.Equal("tail", Encoding.Latin1.GetString(written[^4..]));
    }

    /// <summary>A buffer needs a stream to write to.</summary>
    [Fact]
    public void Refuses_a_missing_destination() =>
        Assert.Throws<ArgumentNullException>(() => new PdfOutputBuffer(null!));
}
