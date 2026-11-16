namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The stream reading half of <see cref="PdfObjectParser"/>.
/// </summary>
/// <remarks>
/// A stream is normally delimited by its <c>/Length</c>, but that length is allowed to be an
/// indirect reference and plenty of files get it wrong. The reader therefore trusts a direct length
/// only after checking that the <c>endstream</c> keyword really follows it, and otherwise scans for
/// the keyword, validating each candidate by attempting to decompress it when the stream is
/// compressed. That combination reads everything from well formed files to files other tools reject.
/// </remarks>
internal sealed partial class PdfObjectParser
{
    private PdfStream ReadStream(PdfDictionary dictionary)
    {
        var dataStart = ReadStreamDataStart();
        var length = ResolveLength(dictionary);

        if (length is { } direct
            && direct >= 0
            && direct <= int.MaxValue
            && dataStart + direct <= lexer.Length
            && EndStreamFollows((int)(dataStart + direct)))
        {
            // The body is a view of the document rather than a copy: a source is held in memory for the
            // whole merge, so duplicating every image, font and content stream would double the largest
            // part of what the engine uses, without any byte of it being needed twice. A body that has
            // to be decrypted is the one exception, because ciphertext never reaches the model.
            Position = (int)(dataStart + direct);
            TryReadKeyword("endstream");

            return new PdfStream(dictionary, ReadStreamBody(lexer.Memory.Slice(dataStart, (int)direct)));
        }

        return ReadStreamByScanning(dictionary, dataStart);
    }

    /// <summary>Decrypts the body of a stream, when the parser was told how to.</summary>
    /// <param name="body">The bytes as they were stored.</param>
    /// <returns>The bytes to expose as the raw body of the stream.</returns>
    /// <remarks>
    /// What comes back is still encoded, because a stream is compressed first and encrypted second,
    /// so everything downstream keeps treating <see cref="PdfStream.Raw"/> as the stored form of the
    /// stream and applies the filters of its dictionary as usual.
    /// </remarks>
    private ReadOnlyMemory<byte> ReadStreamBody(ReadOnlyMemory<byte> body) =>
        security is null
            ? body
            : security.DecryptStream(body.Span, encryptedObjectNumber, encryptedGeneration);

    /// <summary>Skips the end of line that must follow the <c>stream</c> keyword.</summary>
    /// <returns>The offset of the first byte of stream data.</returns>
    private int ReadStreamDataStart()
    {
        var position = Position;

        if (position < lexer.Length && lexer.Data[position] == (byte)'\r')
        {
            position++;

            if (position < lexer.Length && lexer.Data[position] == (byte)'\n')
            {
                position++;
            }
        }
        else if (position < lexer.Length && lexer.Data[position] == (byte)'\n')
        {
            position++;
        }

        Position = position;
        return position;
    }

    private bool EndStreamFollows(int position)
    {
        if (position > lexer.Length)
        {
            return false;
        }

        var probe = new PdfLexer(lexer.Data, position);
        return probe.TryReadKeyword("endstream");
    }

    private long? ResolveLength(PdfDictionary dictionary) => dictionary.Get("Length") switch
    {
        PdfInteger integer => integer.Value,
        PdfReference reference when indirectLengthResolver is not null => indirectLengthResolver(reference.ObjectNumber),
        _ => null,
    };

    /// <summary>
    /// The most <c>endstream</c> candidates a compressed stream is inflated for. Each is inflated from the
    /// start of the data, so the work grows with the square of the candidates, and a hostile file can hold
    /// one every ten bytes.
    /// </summary>
    private const int MaximumInflatedCandidates = 16;

    private PdfStream ReadStreamByScanning(PdfDictionary dictionary, int dataStart)
    {
        var data = lexer.Data;
        var isCompressed = PdfFilters.HasFlateCompression(dictionary);
        var searchFrom = dataStart;
        var inflated = 0;
        ReadOnlyMemory<byte>? firstCandidate = null;
        var firstCandidateEnd = dataStart;
        ReadOnlyMemory<byte>? inflatingCandidate = null;
        var inflatingCandidateEnd = dataStart;

        while (searchFrom < data.Length)
        {
            var index = FindEndStream(data, searchFrom);

            if (index < 0)
            {
                break;
            }

            var end = TrimTrailingEol(data, dataStart, index);

            var candidate = ReadStreamBody(lexer.Memory.Slice(dataStart, end - dataStart));

            if (firstCandidate is null)
            {
                firstCandidate = candidate;
                firstCandidateEnd = index + 9;
            }

            if (!isCompressed)
            {
                return EndScannedStream(dictionary, candidate, index + 9);
            }

            // The candidate has to be decrypted before it can be decompressed, because the stream of
            // an encrypted document is deflated first and encrypted second. A deflate stream cut short
            // inflates without complaint to the bytes in front of the cut, so a candidate is taken at once
            // only when its data is complete; the first that merely inflates is kept for when none is.
            if (inflated++ < MaximumInflatedCandidates && PdfFilters.TryInflate(candidate, out var decoded))
            {
                if (PdfFilters.EndsWithAdler32Of(candidate.Span, decoded))
                {
                    return EndScannedStream(dictionary, candidate, index + 9);
                }

                if (inflatingCandidate is null)
                {
                    inflatingCandidate = candidate;
                    inflatingCandidateEnd = index + 9;
                }
            }

            searchFrom = index + 9;
        }

        return inflatingCandidate is { } inflating
            ? EndScannedStream(dictionary, inflating, inflatingCandidateEnd)
            : EndScannedStream(dictionary, firstCandidate ?? ReadOnlyMemory<byte>.Empty, firstCandidateEnd);
    }

    /// <summary>Ends a stream the scan delimited, behind the <c>endstream</c> keyword it chose.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="body">The body the scan chose.</param>
    /// <param name="keywordEnd">The offset just behind the keyword that ends the body.</param>
    /// <returns>The stream.</returns>
    private PdfStream EndScannedStream(PdfDictionary dictionary, ReadOnlyMemory<byte> body, int keywordEnd)
    {
        Position = keywordEnd;
        return new PdfStream(dictionary, body);
    }

    /// <summary>Removes the end of line that a writer puts between the data and the keyword.</summary>
    /// <param name="data">The bytes of the document.</param>
    /// <param name="dataStart">The offset the stream data starts at.</param>
    /// <param name="end">The offset the keyword was found at.</param>
    /// <returns>The offset the stream data ends at.</returns>
    private static int TrimTrailingEol(byte[] data, int dataStart, int end)
    {
        if (end > dataStart && data[end - 1] == (byte)'\n')
        {
            end--;

            if (end > dataStart && data[end - 1] == (byte)'\r')
            {
                end--;
            }

            return end;
        }

        if (end > dataStart && data[end - 1] == (byte)'\r')
        {
            end--;
        }

        return end;
    }

    private static int FindEndStream(byte[] data, int from)
    {
        var span = data.AsSpan();
        var index = Math.Max(from, 1);

        while (index + 9 <= data.Length)
        {
            // A stream body is the bulk run a scan like this exists for: the search for the 'e' of
            // "endstream" walks hundreds of kilobytes of image or font data at a vector a time, and only
            // the few positions it lands on pay for the nine-byte comparison.
            var found = SimdByteScan.IndexOf(span[index..], (byte)'e');

            if (found < 0)
            {
                return -1;
            }

            index += found;

            if (index + 9 > data.Length)
            {
                return -1;
            }

            if (span.Slice(index, 9).SequenceEqual("endstream"u8)
                && PdfLexer.IsWhitespace(data[index - 1])
                && (index + 9 >= data.Length || PdfLexer.IsWhitespaceOrDelimiter(data[index + 9])))
            {
                return index;
            }

            index++;
        }

        return -1;
    }
}
