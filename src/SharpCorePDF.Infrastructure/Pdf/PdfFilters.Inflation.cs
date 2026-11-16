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

    /// <summary>
    /// The decoded size a stream is held to when the caller has no <see cref="PdfEngineOptions"/> to read it
    /// from; the same value as <see cref="PdfEngineOptions.MaxDecodedStreamBytes"/>'s default.
    /// </summary>
    internal const long DefaultMaxDecodedBytes = 128L * 1024 * 1024;

    /// <summary>What one attempt at inflating a stream came to.</summary>
    private enum InflateOutcome
    {
        /// <summary>The stream inflated within the limit.</summary>
        Inflated,

        /// <summary>The bytes are not a stream this wrapper can inflate.</summary>
        Invalid,

        /// <summary>The stream inflates past the limit; it was stopped there.</summary>
        TooLarge,
    }

    /// <summary>Attempts to decompress Flate data, tolerating the raw deflate streams some producers write.</summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="result">The decompressed bytes when decompression succeeded.</param>
    /// <returns><see langword="true"/> when the data could be decompressed within the default limit.</returns>
    public static bool TryInflate(ReadOnlyMemory<byte> data, out byte[] result) =>
        TryInflate(data, DefaultMaxDecodedBytes, out result) == InflateOutcome.Inflated;

    /// <summary>Decompresses Flate data under the default limit on its decoded size.</summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="PdfSourceFormatException">The data could not be decompressed, or decodes past the limit.</exception>
    public static byte[] Inflate(ReadOnlyMemory<byte> data, string sourceName) =>
        Inflate(data, sourceName, DefaultMaxDecodedBytes);

    /// <summary>Decompresses Flate data, refusing a stream that decodes to more than a limit.</summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="maxDecodedBytes">The largest number of bytes the stream may decode to.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="PdfSourceFormatException">The data could not be decompressed, or decodes past the limit.</exception>
    public static byte[] Inflate(ReadOnlyMemory<byte> data, string sourceName, long maxDecodedBytes) =>
        TryInflate(data, maxDecodedBytes, out var result) switch
        {
            InflateOutcome.Inflated => result,
            InflateOutcome.TooLarge => throw DecodedTooLarge(sourceName, "a Flate", maxDecodedBytes),
            _ => throw new PdfSourceFormatException(sourceName, "a Flate compressed stream could not be decompressed."),
        };

    /// <summary>Builds the refusal of a stream that decodes past the limit.</summary>
    /// <param name="sourceName">The name of the source.</param>
    /// <param name="what">The kind of stream, for the message.</param>
    /// <param name="maxDecodedBytes">The limit that was reached.</param>
    /// <returns>The exception to throw.</returns>
    internal static PdfSourceFormatException DecodedTooLarge(string sourceName, string what, long maxDecodedBytes) =>
        new(sourceName, $"{what} stream decodes to more than {maxDecodedBytes} bytes, the limit PdfEngineOptions.MaxDecodedStreamBytes sets, so it was not decoded.");

    private static InflateOutcome TryInflate(ReadOnlyMemory<byte> data, long maxDecodedBytes, out byte[] result)
    {
        result = [];

        if (data.IsEmpty)
        {
            return InflateOutcome.Invalid;
        }

        var outcome = TryInflateCore(data, zlibWrapper: true, maxDecodedBytes, out result);

        return outcome == InflateOutcome.Invalid
            ? TryInflateCore(data, zlibWrapper: false, maxDecodedBytes, out result)
            : outcome;
    }

    private static InflateOutcome TryInflateCore(ReadOnlyMemory<byte> data, bool zlibWrapper, long maxDecodedBytes, out byte[] result)
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
                (int)Math.Clamp((long)data.Length * 4, InflateChunkSize, MaximumInitialInflateCapacity),
                poolLargeArrays: false);

            while (true)
            {
                var chunk = output.GetSpan(InflateChunkSize);
                var read = decompressor.Read(chunk);

                if (read == 0)
                {
                    break;
                }

                // Flate reaches about a thousand to one, so the limit on the input does not bound this
                // buffer: a megabyte of zeros is a gigabyte here. The stream is stopped at the limit
                // instead of being allowed to take the process with it.
                if (output.Length + (long)read > maxDecodedBytes)
                {
                    return InflateOutcome.TooLarge;
                }

                output.Advance(read);
            }

            result = output.ToExactArray();
            return InflateOutcome.Inflated;
        }
        catch (InvalidDataException)
        {
            return InflateOutcome.Invalid;
        }
        catch (IOException)
        {
            return InflateOutcome.Invalid;
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

    private static byte[] ApplyFlate(ReadOnlyMemory<byte> data, PdfDictionary? parameters, string sourceName, long maxDecodedBytes)
    {
        var inflated = Inflate(data, sourceName, maxDecodedBytes);

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

    /// <summary>
    /// Decompresses Flate data and undoes the predictor the filter declares, if any.
    /// </summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="parameters">The decode parameters of the filter, or <see langword="null"/>.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The decoded bytes.</returns>
    /// <remarks>
    /// Exposed to the rest of the engine so image and attachment decoding share one implementation of
    /// both the inflation and the predictors, rather than growing a second copy that can drift.
    /// </remarks>
    /// <exception cref="PdfSourceFormatException">The data could not be decompressed.</exception>
    public static byte[] InflateAndUnpredict(
        ReadOnlyMemory<byte> data,
        PdfDictionary? parameters,
        string sourceName) => ApplyFlate(data, parameters, sourceName, DefaultMaxDecodedBytes);
}
