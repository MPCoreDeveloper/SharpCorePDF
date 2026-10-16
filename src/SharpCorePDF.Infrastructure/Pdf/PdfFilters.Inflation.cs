using System.IO.Compression;
using System.Runtime.InteropServices;
using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The Flate inflation half of <see cref="PdfFilters"/>.
/// </summary>
internal static partial class PdfFilters
{
    private const int InflateChunkSize = 16 * 1024;
    private const int MaximumInitialInflateCapacity = 1024 * 1024;

    /// <summary>Attempts to decompress Flate data, tolerating the raw deflate streams some producers write.</summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="result">The decompressed bytes when decompression succeeded.</param>
    /// <returns><see langword="true"/> when the data could be decompressed.</returns>
    public static bool TryInflate(ReadOnlyMemory<byte> data, out byte[] result)
    {
        result = [];

        return !data.IsEmpty
            && (TryInflateCore(data, zlibWrapper: true, out result)
                || TryInflateCore(data, zlibWrapper: false, out result));
    }

    /// <summary>Decompresses Flate data.</summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="PdfSourceFormatException">The data could not be decompressed.</exception>
    public static byte[] Inflate(ReadOnlyMemory<byte> data, string sourceName) =>
        TryInflate(data, out var result)
            ? result
            : throw new PdfSourceFormatException(sourceName, "a Flate compressed stream could not be decompressed.");

    private static bool TryInflateCore(ReadOnlyMemory<byte> data, bool zlibWrapper, out byte[] result)
    {
        result = [];

        try
        {
            using var input = OpenInput(data);
            using Stream decompressor = zlibWrapper
                ? new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);

            // Decompressed data is normally a few times the compressed size, so that multiple is the
            // buffer's starting capacity — rented rather than allocated, and grown by the pool when a
            // stream surprises us. Growth is progressive instead of one guess, so a small stream never
            // reserves the memory of a large one.
            using var output = new PooledByteBuffer(
                (int)Math.Clamp((long)data.Length * 4, InflateChunkSize, MaximumInitialInflateCapacity));

            while (true)
            {
                var chunk = output.GetSpan(InflateChunkSize);
                var read = decompressor.Read(chunk);

                if (read == 0)
                {
                    break;
                }

                output.Advance(read);
            }

            result = output.ToExactArray();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a stream over the compressed bytes. A stream cannot be built over a
    /// <see cref="ReadOnlyMemory{T}"/> itself, so the array behind it is used as it is and only data that
    /// is not backed by an array is copied.
    /// </summary>
    /// <param name="data">The compressed bytes.</param>
    /// <returns>A read-only stream over the bytes.</returns>
    private static MemoryStream OpenInput(ReadOnlyMemory<byte> data) =>
        MemoryMarshal.TryGetArray(data, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);

    private static byte[] ApplyFlate(ReadOnlyMemory<byte> data, PdfDictionary? parameters, string sourceName)
    {
        var inflated = Inflate(data, sourceName);

        // Every parameter of the filter is optional and every one of them is absent when the stream has no
        // filter dictionary at all, so the dictionary is established once here instead of being tested
        // again for each entry.
        if (parameters is null)
        {
            return inflated;
        }

        var predictor = (int)(parameters.GetInteger("Predictor") ?? 1);

        if (predictor <= 1)
        {
            return inflated;
        }

        return ApplyPredictor(
            inflated,
            predictor,
            (int)(parameters.GetInteger("Colors") ?? parameters.GetInteger("ColorSpace") ?? 1),
            (int)(parameters.GetInteger("BitsPerComponent") ?? 8),
            (int)(parameters.GetInteger("Columns") ?? 1),
            sourceName);
    }
}
