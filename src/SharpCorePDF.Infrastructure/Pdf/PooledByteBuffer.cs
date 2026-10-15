using System.Buffers;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// A growable byte buffer borrowed from <see cref="ArrayPool{T}"/>, so the buffers the engine reads,
/// inflates and decodes with are recycled instead of collected.
/// </summary>
/// <remarks>
/// <para>
/// This is the pooled counterpart of <see cref="System.Buffers.ArrayBufferWriter{T}"/> and the modern
/// replacement for a <see cref="MemoryStream"/> that exists only to accumulate bytes: nothing is
/// allocated on the heap per instance, growth copies into another rented buffer instead of into
/// garbage, and an exactly sized array is produced only where a caller needs to keep the bytes.
/// </para>
/// <para>
/// Arrays are returned to the pool without being cleared, which is what the pool is designed for: the
/// engine only ever exposes the bytes it has written, through <see cref="WrittenSpan"/> or
/// <see cref="ToExactArray"/>, and never the slack of the rented array. A disposed instance must not
/// be used again.
/// </para>
/// </remarks>
internal sealed class PooledByteBuffer : IDisposable
{
    private const int MinimumCapacity = 16;

    private byte[] array;
    private int length;
    private bool returned;

    /// <summary>Initializes a new instance of the <see cref="PooledByteBuffer"/> class.</summary>
    /// <param name="initialCapacity">The number of bytes to rent up front.</param>
    public PooledByteBuffer(int initialCapacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        array = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, MinimumCapacity));
    }

    /// <summary>Gets the number of bytes that have been written.</summary>
    public int Length => length;

    /// <summary>Gets the bytes that have been written.</summary>
    public ReadOnlySpan<byte> WrittenSpan => array.AsSpan(0, length);

    /// <summary>Appends bytes.</summary>
    /// <param name="bytes">The bytes to append.</param>
    public void Write(scoped ReadOnlySpan<byte> bytes)
    {
        EnsureFree(bytes.Length);
        bytes.CopyTo(array.AsSpan(length));

        length += bytes.Length;
    }

    /// <summary>Appends a single byte.</summary>
    /// <param name="value">The byte to append.</param>
    public void WriteByte(byte value)
    {
        EnsureFree(1);
        array[length++] = value;
    }

    /// <summary>
    /// Gets writable memory of at least <paramref name="sizeHint"/> bytes, for an asynchronous read
    /// that is published with <see cref="Advance"/>.
    /// </summary>
    /// <param name="sizeHint">The number of bytes the caller intends to read.</param>
    /// <returns>Memory that starts where the next byte would be written.</returns>
    public Memory<byte> GetMemory(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);

        EnsureFree(sizeHint);
        return array.AsMemory(length);
    }

    /// <summary>
    /// Gets writable space of at least <paramref name="sizeHint"/> bytes, which the caller fills and
    /// then publishes with <see cref="Advance"/>.
    /// </summary>
    /// <param name="sizeHint">The number of bytes the caller intends to write.</param>
    /// <returns>A span that starts where the next byte would be written.</returns>
    public Span<byte> GetSpan(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);

        EnsureFree(sizeHint);
        return array.AsSpan(length);
    }

    /// <summary>Publishes bytes that were written through <see cref="GetSpan"/>.</summary>
    /// <param name="count">The number of bytes that were written.</param>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, array.Length - length);

        length += count;
    }

    /// <summary>Copies the bytes that have been written into a right-sized array.</summary>
    /// <returns>The bytes of the buffer, owned by the caller.</returns>
    public byte[] ToExactArray() => array.AsSpan(0, length).ToArray();

    /// <summary>Returns the rented array to the pool.</summary>
    public void Dispose()
    {
        if (returned)
        {
            return;
        }

        returned = true;
        ArrayPool<byte>.Shared.Return(array);
        array = [];
        length = 0;
    }

    private void EnsureFree(int sizeHint)
    {
        if (array.Length - length >= sizeHint)
        {
            return;
        }

        Grow(length + sizeHint);
    }

    private void Grow(int minimumCapacity)
    {
        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(minimumCapacity, array.Length * 2));
        array.AsSpan(0, length).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(array);
        array = grown;
    }
}
