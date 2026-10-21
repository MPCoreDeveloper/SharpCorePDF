using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>The payload of a stream, as the read models carry it.</summary>
/// <param name="IsDecodable">Whether the bytes are in the form the caller asked for.</param>
/// <param name="Failed">Whether a filter the library does implement refused the data.</param>
/// <param name="Filter">The filter chain as the document declares it, or <see langword="null"/>.</param>
/// <param name="Length">The length of the payload, whether or not it is buffered.</param>
/// <param name="Data">The payload, or <see langword="null"/> when it is above the inline budget.</param>
internal readonly record struct PdfPayload(
    bool IsDecodable,
    bool Failed,
    string? Filter,
    long Length,
    ReadOnlyMemory<byte>? Data);

/// <summary>
/// Decodes image and attachment payloads without throwing for what the library does not implement.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PdfFilters.Decode"/> answers a different question and answers it correctly: an object
/// stream that cannot be inflated means a broken document, so it raises. An image is different — a
/// <c>JPXDecode</c> scan is a perfectly valid document this library simply does not decode — so this
/// class reports what it could not do and hands back the raw bytes (NFR-7.6, NFR-7.8).
/// </para>
/// <para>
/// The payload is buffered only while it fits the inline budget. A payload above it is still measured,
/// never held: the Flate path counts what it decompresses and stops capturing the moment the budget is
/// crossed, so a 300 MB image costs one chunk of working set rather than 300 MB.
/// </para>
/// </remarks>
internal static class PdfImageFilters
{
    /// <summary>The size of the buffer the copy route works through.</summary>
    public const int CopyChunkSize = 64 * 1024;

    /// <summary>Describes the filter chain a stream declares, or <see langword="null"/> for none.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <returns>The filter names in the order they are applied, joined by a space.</returns>
    public static string? DescribeFilter(PdfDictionary dictionary)
    {
        var filters = PdfFilters.ReadFilterNames(dictionary);
        return filters.Count == 0 ? null : string.Join(" ", filters);
    }

    /// <summary>Decodes a stream, buffering the payload only within a budget.</summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="maxInlineBytes">The largest payload that is buffered.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>What was produced, and how large the payload is whether or not it was produced.</returns>
    public static PdfPayload Decode(PdfStream stream, int maxInlineBytes, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var filters = PdfFilters.ReadFilterNames(stream.Dictionary);
        var description = filters.Count == 0 ? null : string.Join(" ", filters);

        if (filters.Count == 0)
        {
            return new PdfPayload(true, false, null, stream.Raw.Length, Buffer(stream.Raw, maxInlineBytes));
        }

        var parameters = PdfFilters.ReadDecodeParameters(stream.Dictionary);
        var current = stream.Raw;
        var length = (long)stream.Raw.Length;
        var buffered = true;

        for (var index = 0; index < filters.Count; index++)
        {
            var name = Normalize(filters[index]);
            var forFilter = index < parameters.Count ? parameters[index] : null;

            if (IsPassThrough(name))
            {
                // A DCT stream is a JPEG: nothing to do, and re-encoding it would only lose quality. Any
                // other pass-through filter is one the library does not decode, and its raw bytes are
                // still better than nothing.
                return new PdfPayload(
                    IsDecodablePassThrough(name) && index == filters.Count - 1,
                    false,
                    description,
                    length,
                    buffered ? Buffer(current, maxInlineBytes) : null);
            }

            if (!TryDecodeOne(name, current, forFilter, sourceName, maxInlineBytes, out var bytes, out var decodedLength))
            {
                return new PdfPayload(false, true, description, length, buffered ? Buffer(current, maxInlineBytes) : null);
            }

            length = decodedLength;
            buffered = bytes is not null;
            current = bytes ?? ReadOnlyMemory<byte>.Empty;

            // A decode that crossed the budget stops capturing, so a later filter in the chain has
            // nothing to work on. Such a chain is reported rather than guessed at.
            if (!buffered && index < filters.Count - 1)
            {
                return new PdfPayload(false, true, description, length, null);
            }
        }

        return new PdfPayload(true, false, description, length, buffered ? Buffer(current, maxInlineBytes) : null);
    }

    /// <summary>Copies a stream's payload to a destination through a bounded buffer.</summary>
    /// <param name="stream">The stream whose payload is wanted.</param>
    /// <param name="destination">A writable stream that receives the payload.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="cancellationToken">A token to cancel the copy.</param>
    /// <returns>A task that completes when the payload has been written.</returns>
    public static async Task CopyAsync(
        PdfStream stream,
        Stream destination,
        string sourceName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(destination);

        var filters = PdfFilters.ReadFilterNames(stream.Dictionary);

        if (filters.Count == 0)
        {
            await WriteAsync(stream.Raw, destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        var parameters = PdfFilters.ReadDecodeParameters(stream.Dictionary);
        var name = Normalize(filters[0]);

        // The two shapes worth streaming are the ones a large payload actually uses: bytes that are
        // already the answer, and Flate without a predictor. Everything else is rare enough that
        // materialising it is the honest trade, and it is still correct.
        if (filters.Count == 1 && IsPassThrough(name))
        {
            await WriteAsync(stream.Raw, destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (filters.Count == 1 && name == "FlateDecode")
        {
            var decodeParameters = parameters.Count > 0 ? parameters[0] : null;

            // A predictor turns the payload into rows, so it is undone a row at a time and written as it
            // is produced, rather than inflated whole and then unpredicted whole.
            if (HasPredictor(decodeParameters))
            {
                await InflatePredictorToAsync(stream.Raw, decodeParameters, sourceName, destination, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await InflateToAsync(stream.Raw, destination, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var payload = Decode(stream, int.MaxValue, sourceName);

        if (payload.Data is not { } data)
        {
            throw new PdfSourceFormatException(sourceName, "the payload could not be decoded for copying.");
        }

        await WriteAsync(data, destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAsync(
        ReadOnlyMemory<byte> data,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < data.Length)
        {
            var count = Math.Min(CopyChunkSize, data.Length - offset);

            await destination
                .WriteAsync(data.Slice(offset, count), cancellationToken)
                .ConfigureAwait(false);

            offset += count;
        }
    }

    private static async Task InflateToAsync(
        ReadOnlyMemory<byte> data,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var input = OpenInput(data);
        using Stream inflater = LooksLikeZlib(data)
            ? new ZLibStream(input, CompressionMode.Decompress)
            : new DeflateStream(input, CompressionMode.Decompress);

        var chunk = ArrayPool<byte>.Shared.Rent(CopyChunkSize);

        try
        {
            while (true)
            {
                var read = await inflater
                    .ReadAsync(chunk.AsMemory(0, CopyChunkSize), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                await destination
                    .WriteAsync(chunk.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    /// <summary>
    /// Decompresses Flate data with a predictor and writes the undone rows as they are produced, so a
    /// payload larger than the inline budget is never held whole on the copy route.
    /// </summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="parameters">The decode parameters of the filter.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="destination">A writable stream that receives the payload.</param>
    /// <param name="cancellationToken">A token to cancel the copy.</param>
    /// <returns>A task that completes when the payload has been written.</returns>
    private static async Task InflatePredictorToAsync(
        ReadOnlyMemory<byte> data,
        PdfDictionary? parameters,
        string sourceName,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var predictor = PdfFilters.CreatePredictorDecoder(parameters, sourceName);

        if (predictor is null)
        {
            await InflateToAsync(data, destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var input = OpenInput(data);
        using Stream inflater = LooksLikeZlib(data)
            ? new ZLibStream(input, CompressionMode.Decompress)
            : new DeflateStream(input, CompressionMode.Decompress);

        var compressed = ArrayPool<byte>.Shared.Rent(CopyChunkSize);

        // One read inflates to at most a chunk of rows plus the row that was left unfinished, so one
        // buffer of that size holds everything a single read produces.
        var rows = ArrayPool<byte>.Shared.Rent(CopyChunkSize + predictor.RowLength);

        try
        {
            while (true)
            {
                var read = await inflater
                    .ReadAsync(compressed.AsMemory(0, CopyChunkSize), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                var written = 0;

                predictor.Push(compressed.AsSpan(0, read), row =>
                {
                    row.CopyTo(rows.AsSpan(written));
                    written += row.Length;
                });

                if (written > 0)
                {
                    await destination
                        .WriteAsync(rows.AsMemory(0, written), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // A payload too short to form a row is written as it was read, which is what the read route
            // reports for the same bytes.
            if (predictor.RowsDecoded == 0 && predictor.PendingRow.Length > 0)
            {
                var remainder = predictor.PendingRow;

                remainder.CopyTo(rows);
                await destination
                    .WriteAsync(rows.AsMemory(0, remainder.Length), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
            ArrayPool<byte>.Shared.Return(rows);
        }
    }

    private static bool LooksLikeZlib(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 2)
        {
            return false;
        }

        var header = (data.Span[0] << 8) | data.Span[1];

        return (data.Span[0] & 0x0F) == 8 && header % 31 == 0;
    }

    private static bool TryDecodeOne(
        string name,
        ReadOnlyMemory<byte> data,
        PdfDictionary? parameters,
        string sourceName,
        int cap,
        out byte[]? bytes,
        out long length)
    {
        bytes = null;
        length = 0;

        try
        {
            switch (name)
            {
                case "FlateDecode" when HasPredictor(parameters):
                    return TryInflatePredictorCapped(data, parameters, cap, sourceName, out bytes, out length);

                case "FlateDecode":
                    return TryInflateCapped(data, cap, out bytes, out length);

                case "ASCIIHexDecode":
                {
                    var decoded = PdfFilters.DecodeAsciiHex(data);

                    length = decoded.Length;
                    bytes = Keep(decoded, cap);
                    return true;
                }

                case "ASCII85Decode":
                {
                    var decoded = PdfFilters.DecodeAscii85(data);

                    length = decoded.Length;
                    bytes = Keep(decoded, cap);
                    return true;
                }

                default:
                    return false;
            }
        }
        catch (PdfSourceFormatException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryInflateCapped(
        ReadOnlyMemory<byte> data,
        int cap,
        out byte[]? bytes,
        out long length)
    {
        if (TryInflateCappedCore(data, zlibWrapper: true, cap, out bytes, out length))
        {
            return true;
        }

        bytes = null;
        length = 0;

        return TryInflateCappedCore(data, zlibWrapper: false, cap, out bytes, out length);
    }

    private static bool TryInflateCappedCore(
        ReadOnlyMemory<byte> data,
        bool zlibWrapper,
        int cap,
        out byte[]? bytes,
        out long length)
    {
        bytes = null;
        length = 0;

        if (data.IsEmpty)
        {
            return false;
        }

        var scratch = ArrayPool<byte>.Shared.Rent(CopyChunkSize);
        PooledByteBuffer? output = cap > 0 ? new PooledByteBuffer(Math.Min(cap, CopyChunkSize)) : null;

        try
        {
            using var input = OpenInput(data);
            using Stream inflater = zlibWrapper
                ? new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);

            var total = 0L;

            while (true)
            {
                if (output is not null)
                {
                    var span = output.GetSpan(CopyChunkSize);
                    var read = inflater.Read(span);

                    if (read == 0)
                    {
                        break;
                    }

                    total += read;

                    // The budget was crossed by this chunk: the bytes that were read are dropped and
                    // the rest is only drained, so the length stays truthful without the memory.
                    if (total > cap)
                    {
                        output.Dispose();
                        output = null;
                        continue;
                    }

                    output.Advance(read);
                    continue;
                }

                var drained = inflater.Read(scratch, 0, CopyChunkSize);

                if (drained == 0)
                {
                    break;
                }

                total += drained;
            }

            length = total;

            // A stream that decompresses to nothing is not a payload: a truncated or mislabelled Flate
            // stream often ends without an error, and reporting an empty image as decodable would hide
            // exactly the damage the caller needs to know about.
            if (total == 0)
            {
                return false;
            }

            bytes = output?.ToExactArray();
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
        finally
        {
            output?.Dispose();
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>
    /// Decompresses Flate data and undoes its predictor row by row, buffering the payload only while it
    /// fits a budget.
    /// </summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="parameters">The decode parameters of the filter.</param>
    /// <param name="cap">The largest payload that is buffered; zero or less buffers nothing.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="bytes">The decoded bytes while they fit the budget, otherwise <see langword="null"/>.</param>
    /// <param name="length">The decoded length, whether or not the bytes are returned.</param>
    /// <returns><see langword="true"/> when the data could be decompressed.</returns>
    private static bool TryInflatePredictorCapped(
        ReadOnlyMemory<byte> data,
        PdfDictionary? parameters,
        int cap,
        string sourceName,
        out byte[]? bytes,
        out long length)
    {
        if (TryInflatePredictorCappedCore(data, zlibWrapper: true, parameters, cap, sourceName, out bytes, out length))
        {
            return true;
        }

        bytes = null;
        length = 0;

        return TryInflatePredictorCappedCore(
            data,
            zlibWrapper: false,
            parameters,
            cap,
            sourceName,
            out bytes,
            out length);
    }

    private static bool TryInflatePredictorCappedCore(
        ReadOnlyMemory<byte> data,
        bool zlibWrapper,
        PdfDictionary? parameters,
        int cap,
        string sourceName,
        out byte[]? bytes,
        out long length)
    {
        bytes = null;
        length = 0;

        if (data.IsEmpty)
        {
            return false;
        }

        using var predictor = PdfFilters.CreatePredictorDecoder(parameters, sourceName);

        if (predictor is null)
        {
            return false;
        }

        var scratch = ArrayPool<byte>.Shared.Rent(CopyChunkSize);
        PooledByteBuffer? output = cap > 0 ? new PooledByteBuffer(Math.Min(cap, CopyChunkSize)) : null;

        try
        {
            using var input = OpenInput(data);
            using Stream inflater = zlibWrapper
                ? new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);

            var total = 0L;

            while (true)
            {
                var read = inflater.Read(scratch, 0, CopyChunkSize);

                if (read == 0)
                {
                    break;
                }

                predictor.Push(scratch.AsSpan(0, read), row =>
                {
                    total += row.Length;

                    if (output is null)
                    {
                        return;
                    }

                    // The budget was crossed by this row: the rows that were captured are dropped and
                    // the rest is only counted, so the length stays truthful without the memory.
                    if (total > cap)
                    {
                        output.Dispose();
                        output = null;
                        return;
                    }

                    row.CopyTo(output.GetSpan(row.Length));
                    output.Advance(row.Length);
                });
            }

            if (predictor.RowsDecoded == 0)
            {
                // Nothing formed a whole row, so the bytes that were read are the whole payload — the
                // same answer undoing prediction over a buffer this short gives.
                var remainder = predictor.PendingRow.ToArray();

                length = remainder.Length;
                bytes = Keep(remainder, cap);
                return true;
            }

            length = total;
            bytes = output?.ToExactArray();
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
        finally
        {
            output?.Dispose();
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static ReadOnlyMemory<byte>? Buffer(ReadOnlyMemory<byte> data, int cap)
    {
        if (cap <= 0)
        {
            return null;
        }

        if (data.Length > cap)
        {
            return null;
        }

        return data;
    }

    private static byte[]? Keep(byte[] data, int cap) => data.Length <= cap ? data : null;

    private static bool HasPredictor(PdfDictionary? parameters) =>
        parameters is not null && (parameters.GetInteger("Predictor") ?? 1) > 1;

    private static bool IsPassThrough(string name) =>
        IsDecodablePassThrough(name)
        || name is "JPXDecode" or "JBIG2Decode" or "CCITTFaxDecode" or "LZWDecode" or "RunLengthDecode" or "Crypt";

    private static bool IsDecodablePassThrough(string name) => name == "DCTDecode";

    private static string Normalize(string name) => name switch
    {
        "Fl" => "FlateDecode",
        "AHx" => "ASCIIHexDecode",
        "A85" => "ASCII85Decode",
        "DCT" => "DCTDecode",
        "CCF" => "CCITTFaxDecode",
        "RL" => "RunLengthDecode",
        _ => name,
    };

    private static MemoryStream OpenInput(ReadOnlyMemory<byte> data) =>
        MemoryMarshal.TryGetArray(data, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);
}
