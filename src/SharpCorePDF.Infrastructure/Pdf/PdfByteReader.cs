using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Reads the bytes of a document into memory.
/// </summary>
/// <remarks>
/// The engine trades memory for simplicity on purpose: a document is read once, its objects are
/// resolved on demand from those bytes, and nothing is written until every source has been
/// understood. <see cref="PdfEngineOptions.MaxSourceBytes"/> is what keeps that trade safe.
/// </remarks>
internal static class PdfByteReader
{
    private const int ReadChunkSize = 81_920;

    /// <summary>Reads a stream into a byte array.</summary>
    /// <param name="source">The stream to read; it is read from its current position to its end and is not disposed.</param>
    /// <param name="maxBytes">The largest number of bytes that is acceptable.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The bytes that were read.</returns>
    /// <exception cref="PdfSourceFormatException">The stream does not contain a PDF document, or it exceeds the configured limit.</exception>
    /// <remarks>
    /// When the stream is a <see cref="MemoryStream"/> that already owns the whole document as one
    /// array — the shape <c>PdfByteArraySource</c> hands in — those bytes are used as they are. A
    /// second copy of a document that is already in memory is the cost a cold extract used to pay
    /// before every page walk, and it is not paid here.
    /// </remarks>
    public static async Task<byte[]> ReadAllAsync(
        Stream source,
        long maxBytes,
        string sourceName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (TryGetOwnedDocumentBytes(source, maxBytes, sourceName, out var owned))
        {
            return owned;
        }

        var length = TryGetLength(source);

        if (length > maxBytes)
        {
            throw new PdfSourceFormatException(
                sourceName,
                $"the document is {length} bytes long, which exceeds the configured limit of {maxBytes} bytes.");
        }

        var initialCapacity = length is > 0 and <= int.MaxValue ? (int)length : 0;
        using var buffer = new PooledByteBuffer(initialCapacity);
        var total = 0L;

        while (true)
        {
            // The read fills the buffer's own space, so a source is never copied through a scratch
            // block and a document of known size is read into one rented array instead of two.
            var read = await source.ReadAsync(buffer.GetMemory(ReadChunkSize), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > maxBytes)
            {
                throw new PdfSourceFormatException(
                    sourceName,
                    $"the document is longer than the configured limit of {maxBytes} bytes.");
            }

            buffer.Advance(read);
        }

        if (total == 0)
        {
            throw new PdfSourceFormatException(sourceName, "the stream is empty.");
        }

        return buffer.ToExactArray();
    }

    /// <summary>
    /// Returns the document bytes a memory stream already owns when they are the whole array and the
    /// stream is still at the start of that document.
    /// </summary>
    /// <param name="source">The stream that may already hold the document.</param>
    /// <param name="maxBytes">The largest number of bytes that is acceptable.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="bytes">The owned array when the stream can be used without a copy.</param>
    /// <returns><see langword="true"/> when <paramref name="bytes"/> is the document.</returns>
    /// <exception cref="PdfSourceFormatException">The stream is empty or longer than the limit.</exception>
    internal static bool TryGetOwnedDocumentBytes(
        Stream source,
        long maxBytes,
        string sourceName,
        out byte[] bytes)
    {
        bytes = [];

        if (source is not MemoryStream memory
            || memory.Position != 0L
            || !memory.TryGetBuffer(out var segment)
            || segment.Array is null
            || segment.Offset != 0
            || segment.Count != segment.Array.Length
            || segment.Count != memory.Length)
        {
            return false;
        }

        if (segment.Count == 0)
        {
            throw new PdfSourceFormatException(sourceName, "the stream is empty.");
        }

        if (segment.Count > maxBytes)
        {
            throw new PdfSourceFormatException(
                sourceName,
                $"the document is {segment.Count} bytes long, which exceeds the configured limit of {maxBytes} bytes.");
        }

        bytes = segment.Array;
        return true;
    }

    private static long TryGetLength(Stream source)
    {
        try
        {
            return source.CanSeek ? source.Length - source.Position : 0L;
        }
        catch (NotSupportedException)
        {
            return 0L;
        }
        catch (ObjectDisposedException)
        {
            return 0L;
        }
    }
}
