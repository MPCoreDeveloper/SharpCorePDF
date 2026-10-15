namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Forwards everything written to it to another stream and keeps a copy, so the engine can read its
/// own output back without needing a seekable destination.
/// </summary>
/// <remarks>
/// Only used when <see cref="PdfEngineOptions.VerifyOutput"/> is enabled. This is the one place where the
/// engine does hold the whole Output Document, which is why verification is a development-time option
/// under the memory rule of the PRD; the copy lives in a pooled buffer, so the price is one right-sized
/// array rather than the garbage of a stream that doubles its own buffer as it grows.
/// </remarks>
internal sealed class PdfCaptureStream(Stream destination) : Stream
{
    private readonly PooledByteBuffer captured = new();

    /// <summary>Gets the bytes that have been written through this stream so far.</summary>
    public byte[] CapturedBytes => captured.ToExactArray();

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => destination.CanWrite;

    /// <inheritdoc />
    public override long Length => captured.Length;


    /// <inheritdoc />
    public override long Position
    {
        get => captured.Length;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        captured.Write(buffer.AsSpan(offset, count));
        destination.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        captured.Write(buffer);
        destination.Write(buffer);
    }

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        captured.WriteByte(value);
        destination.WriteByte(value);
    }

    /// <inheritdoc />
    public override void Flush() => destination.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    /// <remarks>
    /// The destination stream belongs to the caller and is never disposed here; only the copy this stream
    /// owns is released, and its buffer goes back to the pool.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            captured.Dispose();
        }

        base.Dispose(disposing);
    }
}
