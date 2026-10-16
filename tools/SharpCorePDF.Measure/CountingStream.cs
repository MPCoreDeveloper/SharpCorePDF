namespace SharpCorePDF.Measure;

/// <summary>A destination that counts bytes and keeps none of them, so that the harness measures the engine.</summary>
internal sealed class CountingStream : Stream
{
    /// <summary>Gets how many bytes the engine wrote.</summary>
    public long Written { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => Written;

    /// <inheritdoc />
    public override long Position { get => Written; set => throw new NotSupportedException(); }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => Written += count;

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => Written += buffer.Length;

    /// <inheritdoc />
    public override void WriteByte(byte value) => Written++;

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
}
