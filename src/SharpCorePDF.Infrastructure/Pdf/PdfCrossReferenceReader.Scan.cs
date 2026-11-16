using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Rebuilds a cross-reference table by scanning the file for object headers.
/// </summary>
/// <remarks>
/// This is the safety net for the files that tools should never have produced: a truncated file
/// without a trailer, a wrong <c>startxref</c> offset, or a table that lost entries during a failed
/// append. Scanning finds every <c>n g obj</c> header in the file, and later occurrences win because
/// an incremental update appends a newer version of an object.
/// </remarks>
internal sealed partial class PdfCrossReferenceReader
{
    private void Reconstruct()
    {
        if (table.Count == 0 && !options.ReconstructCrossReferenceOnFailure)
        {
            throw new PdfSourceFormatException(
                sourceName,
                "the cross-reference table could not be read and reconstruction is disabled by PdfEngineOptions.ReconstructCrossReferenceOnFailure.");
        }

        var found = ScanForObjects();
        IndexObjectStreams();
        var trailer = FindLastTrailer();

        if (trailer is not null)
        {
            table.MergeTrailer(trailer, onlyMissingKeys: false);
        }

        if (found == 0 && table.Count == 0)
        {
            throw new PdfSourceFormatException(
                sourceName,
                "no object headers were found; the file does not look like a PDF document.");
        }

        table.WasReconstructed = true;
        warnings.Add(new(
            PdfWarningKind.CrossReferenceRebuilt,
            $"'{sourceName}': the cross-reference table was rebuilt by scanning the file ({found} object header(s) found). The document may be damaged."));
    }

    private int ScanForObjects()
    {
        var found = 0;
        var searchFrom = 0;

        while (searchFrom < data.Length)
        {
            var index = data.AsSpan(searchFrom).IndexOf("obj"u8);

            if (index < 0)
            {
                break;
            }

            var position = searchFrom + index;
            searchFrom = position + 3;

            if (position + 3 < data.Length && !PdfLexer.IsWhitespaceOrDelimiter(data[position + 3]))
            {
                continue;
            }

            if (position < 1 || !PdfLexer.IsWhitespace(data[position - 1]))
            {
                continue;
            }

            var cursor = position;

            if (!TryReadBackwardsInteger(ref cursor, out var generation)
                || !TryReadBackwardsInteger(ref cursor, out var objectNumber))
            {
                continue;
            }

            if (objectNumber < 0 || objectNumber > int.MaxValue)
            {
                continue;
            }

            // The cursor rests on the first digit of the object number, which is where the object header
            // begins: the parser behind these offsets starts reading at the byte it is given, so an offset
            // that pointed behind that digit would find a header without an object number and resolve
            // nothing at all.
            table.AddOrReplace(new XrefEntry(objectNumber, cursor, (int)generation, XrefEntryType.Uncompressed));
            found++;
        }

        return found;
    }

    private bool TryReadBackwardsInteger(ref int cursor, out long value)
    {
        value = 0;

        while (cursor > 0 && PdfLexer.IsWhitespace(data[cursor - 1]))
        {
            cursor--;
        }

        var end = cursor;

        while (cursor > 0 && data[cursor - 1] >= (byte)'0' && data[cursor - 1] <= (byte)'9')
        {
            cursor--;
        }

        return end != cursor
            && long.TryParse(
                Encoding.Latin1.GetString(data, cursor, end - cursor),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
    }

    private PdfDictionary? FindLastTrailer()
    {
        var index = data.AsSpan().LastIndexOf("trailer"u8);

        while (index >= 0)
        {
            var parser = new PdfObjectParser(data, sourceName, index + 7, null, options.MaxNestingDepth);

            if (parser.ParseObject() is PdfDictionary trailer && trailer.ContainsKey("Root"))
            {
                return trailer;
            }

            index = index == 0 ? -1 : data.AsSpan(0, index).LastIndexOf("trailer"u8);
        }

        return FindCrossReferenceStreamTrailer() ?? FindCatalogTrailer();
    }

    /// <summary>
    /// Registers the objects the object streams found by the scan hold. A PDF 1.5 file keeps most of its
    /// objects — often its catalog and its pages — inside object streams, which a scan for <c>n g obj</c>
    /// headers cannot see into, so without this a file whose cross-reference stream is lost has nothing left.
    /// </summary>
    /// <remarks>
    /// An object the scan found on its own stays where the scan found it: entries are only added, never
    /// replaced, because an object written uncompressed after an object stream is the newer of the two.
    /// </remarks>
    private void IndexObjectStreams()
    {
        foreach (var container in table.Entries.Values.Where(entry => entry.Type == XrefEntryType.Uncompressed).ToList())
        {
            if (!MentionsObjectStream(container.Offset)
                || ParseAt(container.Offset) is not PdfStream { Dictionary: var dictionary } stream
                || dictionary.GetName("Type") != "ObjStm")
            {
                continue;
            }

            byte[] decoded;

            try
            {
                decoded = PdfFilters.Decode(stream, sourceName, options.MaxDecodedStreamBytes);
            }
            catch (PdfException)
            {
                continue;
            }

            var count = Math.Min(dictionary.GetInteger("N") ?? 0, (decoded.Length / 4L) + 1);
            var header = new PdfObjectParser(decoded, sourceName, 0, null, options.MaxNestingDepth);

            for (var index = 0; index < count; index++)
            {
                if (!header.TryReadInteger(out var objectNumber) || !header.TryReadInteger(out _))
                {
                    break;
                }

                if (objectNumber > 0)
                {
                    table.Add(new XrefEntry(objectNumber, container.ObjectNumber, index, XrefEntryType.Compressed));
                }
            }
        }
    }

    /// <summary>Determines cheaply whether the object at an offset names itself an object stream.</summary>
    /// <param name="offset">The offset of the object header.</param>
    /// <returns><see langword="true"/> when <c>/ObjStm</c> appears in the first bytes of the object.</returns>
    private bool MentionsObjectStream(long offset)
    {
        if (offset < 0 || offset >= data.Length)
        {
            return false;
        }

        var window = data.AsSpan((int)offset, (int)Math.Min(512, data.Length - offset));

        return window.IndexOf("/ObjStm"u8) >= 0;
    }

    /// <summary>Determines whether an offset names a byte of the document.</summary>
    /// <param name="offset">The offset.</param>
    /// <returns><see langword="true"/> when the offset is inside the data.</returns>
    /// <remarks>
    /// Reconstruction keeps the entries of the table it is repairing beside the ones the scan found, and those
    /// offsets are whatever the damaged table stated: past the end of the file, negative, or beyond the range
    /// of an <see cref="int"/>, which wraps to a negative index when it is cast. A slice of the data needs
    /// this check; a parser does not, because <see cref="PdfLexer"/> clamps the position it starts at.
    /// </remarks>
    private bool IsInside(long offset) => offset >= 0 && offset < data.Length;

    /// <summary>Parses the object whose header is at an offset.</summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The object, or <see langword="null"/>.</returns>
    private PdfObject? ParseAt(long offset)
    {
        var parser = new PdfObjectParser(data, sourceName, (int)offset, null, options.MaxNestingDepth);

        return parser.TryReadIndirectHeader(out _, out _) ? parser.ParseObject() : null;
    }

    /// <summary>
    /// Takes the trailer entries from the last cross-reference stream the scan found: a PDF 1.5 file has no
    /// <c>trailer</c> keyword, and its <c>/Root</c> lives in the stream's dictionary, which survives when the
    /// stream's data does not.
    /// </summary>
    /// <returns>A trailer carrying <c>/Root</c>, or <see langword="null"/>.</returns>
    private PdfDictionary? FindCrossReferenceStreamTrailer()
    {
        foreach (var offset in table.Entries.Values
            .Where(entry => entry.Type == XrefEntryType.Uncompressed && IsInside(entry.Offset))
            .Select(entry => entry.Offset)
            .OrderByDescending(offset => offset))
        {
            var window = data.AsSpan((int)offset, (int)Math.Min(512, data.Length - offset));

            if (window.IndexOf("/XRef"u8) < 0)
            {
                continue;
            }

            var dictionary = ParseAt(offset) switch
            {
                PdfStream stream => stream.Dictionary,
                PdfDictionary plain => plain,
                _ => null,
            };

            if (dictionary is null || dictionary.GetName("Type") != "XRef" || dictionary.GetReference("Root") is null)
            {
                continue;
            }

            var trailer = new PdfDictionary();

            foreach (var key in new[] { "Root", "Info", "ID", "Encrypt" })
            {
                if (dictionary.Get(key) is { } value)
                {
                    trailer.Set(key, value);
                }
            }

            return trailer;
        }

        return null;
    }

    /// <summary>Looks for a document catalog and uses it as the root of a synthetic trailer.</summary>
    /// <returns>A synthesised trailer, or <see langword="null"/>.</returns>
    private PdfDictionary? FindCatalogTrailer()
    {
        foreach (var xref in table.Entries
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value)
            .Where(xref => xref.Type == XrefEntryType.Uncompressed))
        {
            var parser = new PdfObjectParser(data, sourceName, (int)xref.Offset, null, options.MaxNestingDepth);

            if (!parser.TryReadIndirectHeader(out var objectNumber, out var generation)
                || objectNumber != xref.ObjectNumber
                || parser.ParseObject() is not PdfDictionary candidate
                || candidate.GetName("Type") != "Catalog")
            {
                continue;
            }

            var trailer = new PdfDictionary();
            trailer.Set("Root", new PdfReference(objectNumber, generation));
            trailer.SetInteger("Size", table.HighestObjectNumber + 1);

            return trailer;
        }

        return null;
    }
}
