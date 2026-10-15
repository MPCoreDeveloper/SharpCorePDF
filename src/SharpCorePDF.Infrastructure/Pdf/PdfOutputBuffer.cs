namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Writes bytes to the output stream through a buffer, and tracks how many bytes have been written so
/// object offsets can be recorded for the cross-reference table.
/// </summary>
/// <remarks>
/// The writer is synchronous on purpose: it runs inside a merge step that has already read every
/// source into memory, so an asynchronous write would only add overhead.
/// </remarks>
internal sealed class PdfOutputBuffer
{
    private readonly Stream destination;
    private readonly byte[] buffer;
    private int used;

    /// <summary>Initializes a new instance of the <see cref="PdfOutputBuffer"/> class.</summary>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="bufferSize">The size of the buffer.</param>
    public PdfOutputBuffer(Stream destination, int bufferSize = 64 * 1024)
    {
        this.destination = destination ?? throw new ArgumentNullException(nameof(destination));
        buffer = new byte[Math.Max(bufferSize, 1024)];
    }

    /// <summary>Gets the number of bytes written so far.</summary>
    public long Length { get; private set; }

    /// <summary>Writes a single byte.</summary>
    /// <param name="value">The byte to write.</param>
    public void WriteByte(byte value)
    {
        if (used == buffer.Length)
        {
            FlushBuffer();
        }

        buffer[used++] = value;
        Length++;
    }

    /// <summary>Writes bytes.</summary>
    /// <param name="bytes">The bytes to write.</param>
    /// <remarks>
    /// A chunk that fits the buffer is copied into it, a bigger one goes straight to the stream: a stream
    /// body of a megabyte is therefore never copied through the buffer a byte at a time.
    /// </remarks>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        var available = buffer.Length - used;

        if (bytes.Length <= available)
        {
            bytes.CopyTo(buffer.AsSpan(used));
            used += bytes.Length;
            Length += bytes.Length;
            return;
        }

        WriteSlow(bytes);
    }

    /// <summary>Writes an integer in its shortest invariant form.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteInteger(long value)
    {
        Span<byte> scratch = stackalloc byte[24];

        if (!value.TryFormat(scratch, out var written, default, System.Globalization.CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("The integer could not be formatted.");
        }

        Write(scratch[..written]);
    }

    /// <summary>Writes a real number in the form PDF expects, without materialising a string.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteReal(double value)
    {
        Span<byte> scratch = stackalloc byte[32];

        if (!PdfNumberFormat.TryFormat(value, scratch, out var written))
        {
            throw new InvalidOperationException("The real number could not be formatted.");
        }

        Write(scratch[..written]);
    }

    /// <summary>Writes an indirect reference, for example <c>12 0 R</c>.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="generation">The generation number.</param>
    public void WriteReference(long objectNumber, int generation)
    {
        WriteInteger(objectNumber);
        WriteByte((byte)' ');
        WriteInteger(generation);
        Write(" R"u8);
    }

    /// <summary>Writes an eight digit hex value behind a leading zero, for a cross-reference entry.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteOffsetField(long value)
    {
        Span<byte> scratch = stackalloc byte[24];

        if (!value.TryFormat(scratch, out var written, "D10", System.Globalization.CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("The offset could not be formatted.");
        }

        Write(scratch[..written]);
    }

    /// <summary>Flushes the buffer to the destination.</summary>
    public void Flush()
    {
        FlushBuffer();
        destination.Flush();
    }

    /// <summary>Writes a chunk that does not fit into the free room of the buffer.</summary>
    /// <param name="bytes">The bytes to write.</param>
    private void WriteSlow(ReadOnlySpan<byte> bytes)
    {
        FlushBuffer();

        if (bytes.Length >= buffer.Length)
        {
            destination.Write(bytes);
            Length += bytes.Length;
            return;
        }

        bytes.CopyTo(buffer);
        used = bytes.Length;
        Length += bytes.Length;
    }

    private void FlushBuffer()
    {
        if (used == 0)
        {
            return;
        }

        destination.Write(buffer, 0, used);
        used = 0;
    }
}
