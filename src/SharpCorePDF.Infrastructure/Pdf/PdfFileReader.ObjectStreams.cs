using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Extracts objects that live inside an object stream.
/// </summary>
/// <remarks>
/// An object stream is a compressed blob holding a header of object number and offset pairs followed
/// by the objects themselves. Because the objects inside are stored uncompressed, the engine
/// flattens them: the merged document writes them as ordinary objects, which is always legal and
/// keeps the writer simple.
/// </remarks>
internal sealed partial class PdfFileReader
{
    private PdfObject? ResolveFromObjectStream(XrefEntry entry)
    {
        EnsureObjectStreamLoaded(entry.Offset);

        return fromObjectStreams.GetValueOrDefault(entry.ObjectNumber);
    }

    private void EnsureObjectStreamLoaded(long containerObjectNumber)
    {
        if (!resolvingObjectStreams.Add(containerObjectNumber))
        {
            return;
        }

        try
        {
            if (!loadedObjectStreams.Add(containerObjectNumber))
            {
                return;
            }

            if (Resolve(containerObjectNumber) is not PdfStream stream)
            {
                warnings.Add(new(
                    PdfWarningKind.ObjectStreamUnreadable,
                    $"'{SourceName}': object {containerObjectNumber} should be an object stream but is not; its objects are treated as missing."));
                return;
            }

            var dictionary = stream.Dictionary;
            var declared = dictionary.GetInteger("N") ?? 0;
            var first = dictionary.GetInteger("First") ?? 0;

            if (declared <= 0 || first < 0)
            {
                return;
            }

            var decoded = PdfFilters.Decode(stream, SourceName, options.MaxDecodedStreamBytes);

            // /N sizes two arrays, and it is a number the file states: 2^31 - 1 of them would ask for
            // gigabytes from a stream of a few hundred bytes. Every pair of the header takes at least four
            // bytes ("1 0 "), so the stream itself bounds how many objects it can describe, and the arrays
            // are sized by that bound; a header that ends before /N says so below.
            var count = (int)Math.Min(declared, (decoded.Length / 4L) + 1);
            var header = new PdfObjectParser(decoded, SourceName, 0, null, options.MaxNestingDepth);
            var numbers = new long[count];
            var offsets = new long[count];

            for (var index = 0; index < count; index++)
            {
                if (!header.TryReadInteger(out var number) || !header.TryReadInteger(out var offset))
                {
                    warnings.Add(new(
                        PdfWarningKind.ObjectStreamTruncated,
                        $"'{SourceName}': object stream {containerObjectNumber} declares {declared} objects but its header ends early."));
                    break;
                }

                numbers[index] = number;
                offsets[index] = offset;
            }

            for (var index = 0; index < count; index++)
            {
                var position = first + offsets[index];

                if (numbers[index] <= 0 || position < 0 || position >= decoded.Length)
                {
                    continue;
                }

                var parser = new PdfObjectParser(decoded, SourceName, (int)position, null, options.MaxNestingDepth);
                fromObjectStreams[numbers[index]] = parser.ParseObject();
            }
        }
        finally
        {
            resolvingObjectStreams.Remove(containerObjectNumber);
        }
    }
}
