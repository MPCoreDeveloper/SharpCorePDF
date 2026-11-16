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

    /// <summary>
    /// The largest array a decode buffer takes from the pool. Above it the array is allocated and left to the
    /// collector, because the shared pool keeps what is returned to it: a document that inflates a stream to
    /// the decode limit in every read pass would otherwise leave hundreds of megabytes parked in the pool,
    /// and the next pass would allocate beside them.
    /// </summary>
    private const int MaximumPooledDecodeLength = 16 * 1024 * 1024;

    private byte[] array;
    private int length;
    private readonly bool poolLargeArrays;
    private bool returned;

    /// <summary>Initializes a new instance of the <see cref="PooledByteBuffer"/> class.</summary>
    /// <param name="initialCapacity">The number of bytes to rent up front.</param>
    /// <param name="poolLargeArrays">
    /// Whether arrays above 16 MiB go through the pool too. The read of a source keeps them pooled, because
    /// reusing the one buffer a document is read into is what keeps a large read at one allocation; a decode
    /// buffer does not, because its size is whatever a stream inflates to and the pool would keep it.
    /// </param>
    public PooledByteBuffer(int initialCapacity = 256, bool poolLargeArrays = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        this.poolLargeArrays = poolLargeArrays;
        array = Rent(Math.Max(initialCapacity, MinimumCapacity));
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
        Return(array);
        array = [];
        length = 0;
    }

    private void EnsureFree(int sizeHint)
    {
        if (array.Length - length >= sizeHint)
        {
            return;
        }

        Grow((long)length + sizeHint);
    }

    private void Grow(long minimumCapacity)
    {
        // Doubling is computed in 64 bits and clamped to the largest array the runtime allows, so a buffer
        // past a gigabyte grows to the limit instead of to a negative size; a request beyond the limit is
        // refused. The decode limits keep every caller below it, so this is a guard and not a path.
        if (minimumCapacity > Array.MaxLength)
        {
            throw new InvalidOperationException($"A buffer of {minimumCapacity} bytes is larger than the runtime allows.");
        }

        var capacity = (int)Math.Min(Math.Max(minimumCapacity, 2L * array.Length), Array.MaxLength);
        var grown = Rent(capacity);
        array.AsSpan(0, length).CopyTo(grown);
        Return(array);
        array = grown;
    }

    private byte[] Rent(int length) =>
        poolLargeArrays || length <= MaximumPooledDecodeLength
            ? ArrayPool<byte>.Shared.Rent(length)
            : GC.AllocateUninitializedArray<byte>(length);

    private void Return(byte[] rented)
    {
        if (poolLargeArrays || rented.Length <= MaximumPooledDecodeLength)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
