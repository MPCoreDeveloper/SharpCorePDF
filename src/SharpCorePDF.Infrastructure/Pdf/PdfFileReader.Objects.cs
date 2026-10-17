using System.Text;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The header, indirect object and length resolution half of <see cref="PdfFileReader"/>.
/// </summary>
internal sealed partial class PdfFileReader
{
    private PdfVersion ReadVersion()
    {
        var limit = Math.Min(data.Length, 1024);
        var index = data.AsSpan(0, limit).IndexOf("%PDF-"u8);

        if (index < 0)
        {
            return PdfVersion.Unknown;
        }

        var start = index + 5;
        var end = start;

        while (end < limit && (data[end] == (byte)'.' || (data[end] >= (byte)'0' && data[end] <= (byte)'9')))
        {
            end++;
        }

        return PdfVersion.TryParse(Encoding.Latin1.GetString(data, start, end - start), out var version)
            ? version
            : PdfVersion.Unknown;
    }

    private PdfObject? ParseObjectAt(int offset, long expectedObjectNumber)
    {
        if (offset < 0 || offset >= data.Length)
        {
            return null;
        }

        var parser = new PdfObjectParser(data, SourceName, offset, ResolveIndirectLength, options.MaxNestingDepth);

        if (!parser.TryReadIndirectHeader(out var objectNumber, out var generation))
        {
            return null;
        }

        // The header is what makes decryption possible: the key of every string and stream of an object
        // is derived from the number this object carries in the file, so the parser is told about it
        // before a byte of the body is read. The number from the header is the one used even when the
        // cross-reference table disagrees, because that is the number the producer derived the key
        // from.
        //
        // Exactly one object is exempt: the metadata stream of a document that clears EncryptMetadata,
        // which the specification keeps in the clear. Decrypting it would turn the XMP into rubbish,
        // and the catalog that names it is resolved before anything else can reach it, so the object
        // is already known when it is parsed.
        if (Security is not null && !IsClearTextMetadata(objectNumber))
        {
            parser.DecryptWith(Security, objectNumber, generation);
        }

        if (objectNumber != expectedObjectNumber)
        {
            warnings.Add(new(
                PdfWarningKind.CrossReferenceEntryMismatch,
                $"'{SourceName}': the cross-reference table points object {expectedObjectNumber} at the header of object {objectNumber}; the parsed object is used anyway."));
        }

        return parser.ParseObject();
    }

    /// <summary>
    /// Resolves a stream <c>/Length</c> that is itself an indirect reference. The guard set breaks
    /// the cycle that such a reference creates when the length object is the stream itself.
    /// </summary>
    /// <param name="objectNumber">The object number of the length object.</param>
    /// <returns>The length, or <see langword="null"/> when it cannot be resolved safely.</returns>
    private long? ResolveIndirectLength(long objectNumber)
    {
        if (!resolvingLengths.Add(objectNumber))
        {
            return null;
        }

        try
        {
            return Resolve(objectNumber) is PdfInteger integer ? integer.Value : null;
        }
        finally
        {
            resolvingLengths.Remove(objectNumber);
        }
    }
}
