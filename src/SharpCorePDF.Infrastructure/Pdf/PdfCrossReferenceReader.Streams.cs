namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The cross-reference stream reader introduced with PDF 1.5.
/// </summary>
/// <remarks>
/// A cross-reference stream stores its entries as binary fields whose widths are declared in
/// <c>/W</c>, compressed with the predictor that <c>/DecodeParms</c> describes. The stream object
/// itself is registered in the table it describes, which is why the object number that was just
/// parsed is added before the entries are.
/// </remarks>
internal sealed partial class PdfCrossReferenceReader
{
    private (long? Previous, long? Hybrid) ReadCrossReferenceStream(int offset, bool isNewest)
    {
        var parser = new PdfObjectParser(data, sourceName, offset, null, options.MaxNestingDepth);

        if (!parser.TryReadIndirectHeader(out var objectNumber, out var generation)
            || parser.ParseObject() is not PdfStream stream)
        {
            return (null, null);
        }

        var dictionary = stream.Dictionary;

        if (dictionary.GetName("Type") != "XRef")
        {
            return (null, null);
        }

        if (table.Get(objectNumber) is null)
        {
            table.Add(new XrefEntry(objectNumber, offset, generation, XrefEntryType.Uncompressed));
        }

        var decoded = PdfFilters.Decode(stream, sourceName);
        var widthType = 1;
        var widthField1 = 4;
        var widthField2 = 2;

        if (dictionary.GetArray("W") is { Items.Count: >= 3 } widths)
        {
            widthType = (int)ToLong(widths.Items[0], 1);
            widthField1 = (int)ToLong(widths.Items[1], 4);
            widthField2 = (int)ToLong(widths.Items[2], 2);
        }

        var size = dictionary.GetInteger("Size") ?? 0;
        var ranges = ReadIndexRanges(dictionary, size);

        ReadEntries(decoded, widthType, widthField1, widthField2, ranges);

        var previous = dictionary.GetInteger("Prev");
        table.MergeTrailer(dictionary, onlyMissingKeys: !isNewest);

        return (previous, null);
    }

    private static List<(long First, long Count)> ReadIndexRanges(PdfDictionary dictionary, long size)
    {
        var ranges = new List<(long First, long Count)>();

        if (dictionary.GetArray("Index") is { Items.Count: > 1 } index)
        {
            for (var position = 0; position + 1 < index.Items.Count; position += 2)
            {
                ranges.Add((ToLong(index.Items[position], 0), ToLong(index.Items[position + 1], 0)));
            }
        }

        if (ranges.Count == 0)
        {
            ranges.Add((0, size));
        }

        return ranges;
    }

    private void ReadEntries(
        byte[] decoded,
        int widthType,
        int widthField1,
        int widthField2,
        List<(long First, long Count)> ranges)
    {
        var entrySize = widthType + widthField1 + widthField2;

        if (entrySize <= 0)
        {
            return;
        }

        var position = 0;

        foreach (var (first, count) in ranges)
        {
            for (var index = 0L; index < count; index++)
            {
                if (position + entrySize > decoded.Length)
                {
                    return;
                }

                var type = widthType == 0 ? 1 : (int)ReadField(decoded, ref position, widthType);
                var field1 = ReadField(decoded, ref position, widthField1);
                var field2 = ReadField(decoded, ref position, widthField2);
                var kind = type switch
                {
                    0 => XrefEntryType.Free,
                    1 => XrefEntryType.Uncompressed,
                    2 => XrefEntryType.Compressed,
                    _ => XrefEntryType.Free,
                };

                table.Add(new XrefEntry(first + index, field1, (int)field2, kind));
            }
        }
    }

    private static long ReadField(byte[] data, ref int position, int width)
    {
        var value = 0L;

        for (var index = 0; index < width; index++)
        {
            value = (value << 8) | data[position++];
        }

        return value;
    }

    private static long ToLong(PdfObject value, long fallback) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => (long)real.Value,
        _ => fallback,
    };
}
