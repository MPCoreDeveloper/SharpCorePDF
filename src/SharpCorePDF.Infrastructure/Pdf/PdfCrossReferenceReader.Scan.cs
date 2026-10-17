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

        return FindCatalogTrailer();
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
