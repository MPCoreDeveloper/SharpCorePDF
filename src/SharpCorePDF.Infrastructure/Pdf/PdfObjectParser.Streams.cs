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

    private PdfStream ReadStreamByScanning(PdfDictionary dictionary, int dataStart)
    {
        var data = lexer.Data;
        var isCompressed = PdfFilters.HasFlateCompression(dictionary);
        var searchFrom = dataStart;
        ReadOnlyMemory<byte>? firstCandidate = null;
        var firstCandidateEnd = dataStart;

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

            // The candidate has to be decrypted before it can be decompressed, because the stream of
            // an encrypted document is deflated first and encrypted second.
            if (!isCompressed || PdfFilters.TryInflate(candidate, out _))
            {
                Position = index + 9;
                TryReadKeyword("endstream");
                return new PdfStream(dictionary, candidate);
            }

            searchFrom = index + 9;
        }

        Position = firstCandidateEnd;
        return new PdfStream(dictionary, firstCandidate ?? ReadOnlyMemory<byte>.Empty);
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
        for (var index = Math.Max(from, 1); index + 9 <= data.Length; index++)
        {
            if (data[index] != (byte)'e' || !data.AsSpan(index, 9).SequenceEqual("endstream"u8))
            {
                continue;
            }

            if (!PdfLexer.IsWhitespace(data[index - 1]))
            {
                continue;
            }

            var after = index + 9;

            if (after < data.Length && !PdfLexer.IsWhitespaceOrDelimiter(data[after]))
            {
                continue;
            }

            return index;
        }

        return -1;
    }
}
