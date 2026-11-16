namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Writes the cross-reference section of an appended revision as a cross-reference stream, for a document
/// whose newest section is one.
/// </summary>
/// <remarks>
/// A revision appended to a file that uses cross-reference streams used to end in a classic table whose
/// <c>/Prev</c> pointed at a stream. Most readers follow such a chain, but ISO 32000-1 only describes a
/// classic table chained to a classic table, a stream to a stream, and the hybrid form of 7.5.8.4, so the
/// revision is written in the form the document already uses. The stream is written uncompressed and in the
/// clear: a cross-reference stream is never encrypted, and a revision holds a few entries at most.
/// </remarks>
internal static class PdfCrossReferenceStreamWriter
{
    /// <summary>The width of the generation field, which holds up to 65535.</summary>
    private const int GenerationWidth = 2;

    /// <summary>Writes the stream, the <c>startxref</c> that points at it and the closing <c>%%EOF</c>.</summary>
    /// <param name="output">The buffer the revision is written to.</param>
    /// <param name="startXref">The absolute offset the stream object is written at.</param>
    /// <param name="entries">The objects of the revision: number, generation and absolute offset.</param>
    /// <param name="trailer">The trailer entries, which become entries of the stream's dictionary; its <c>Size</c> is corrected here.</param>
    /// <param name="freeHead">Whether the section lists the head of the free list, object zero, as a classic table of a complete file does.</param>
    public static void Write(
        PdfOutputBuffer output,
        long startXref,
        IReadOnlyCollection<(int Number, int Generation, long Offset)> entries,
        PdfDictionary trailer,
        bool freeHead)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(trailer);

        var size = (int)Math.Max(trailer.GetInteger("Size") ?? 0, 1);

        foreach (var entry in entries)
        {
            size = Math.Max(size, entry.Number + 1);
        }

        // The stream is an object of the revision too, numbered above every other, and lists itself.
        var streamNumber = size;
        var rows = entries
            .Select(entry => (entry.Number, Type: (byte)1, Field2: entry.Offset, Field3: entry.Generation))
            .Append((Number: streamNumber, Type: (byte)1, Field2: startXref, Field3: 0));

        if (freeHead)
        {
            rows = rows.Append((Number: 0, Type: (byte)0, Field2: 0L, Field3: 65535));
        }

        var sorted = rows.OrderBy(row => row.Number).ToArray();
        var offsetWidth = BytesFor(startXref);
        var data = new byte[sorted.Length * (1 + offsetWidth + GenerationWidth)];
        var index = new List<PdfObject>();
        var position = 0;

        for (var row = 0; row < sorted.Length; row++)
        {
            if (row == 0 || sorted[row].Number != sorted[row - 1].Number + 1)
            {
                index.Add(new PdfInteger(sorted[row].Number));
                index.Add(new PdfInteger(0));
            }

            var count = (PdfInteger)index[^1];
            index[^1] = new PdfInteger(count.Value + 1);

            data[position++] = sorted[row].Type;
            WriteBigEndian(data.AsSpan(position, offsetWidth), sorted[row].Field2);
            position += offsetWidth;
            WriteBigEndian(data.AsSpan(position, GenerationWidth), sorted[row].Field3);
            position += GenerationWidth;
        }

        var dictionary = new PdfDictionary().SetName("Type", "XRef");

        foreach (var entry in trailer.Entries.Where(entry => entry.Key is not ("Size" or "XRefStm")))
        {
            dictionary.Set(entry.Key, entry.Value);
        }

        dictionary
            .SetInteger("Size", streamNumber + 1)
            .Set("W", new PdfArray([new PdfInteger(1), new PdfInteger(offsetWidth), new PdfInteger(GenerationWidth)]))
            .Set("Index", new PdfArray(index))
            .SetInteger("Length", data.Length);

        output.WriteInteger(streamNumber);
        output.Write(" 0 obj\n"u8);
        PdfSerializer.WriteDictionary(dictionary, output);
        output.Write("\nstream\n"u8);
        output.Write(data);
        output.Write("\nendstream\nendobj\nstartxref\n"u8);
        output.WriteInteger(startXref);
        output.Write("\n%%EOF\n"u8);
        output.Flush();
    }

    /// <summary>Gets the number of bytes a big-endian field needs to hold a value.</summary>
    /// <param name="value">The largest value of the field.</param>
    /// <returns>The width, at least one byte.</returns>
    private static int BytesFor(long value)
    {
        var width = 1;

        while (width < 8 && value >> (8 * width) != 0)
        {
            width++;
        }

        return width;
    }

    /// <summary>Writes a value into a field, most significant byte first.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value, which fits the field.</param>
    private static void WriteBigEndian(Span<byte> field, long value)
    {
        for (var index = field.Length - 1; index >= 0; index--)
        {
            field[index] = (byte)value;
            value >>= 8;
        }
    }
}
