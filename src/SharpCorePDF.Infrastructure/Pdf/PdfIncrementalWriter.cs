namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Appends objects to an existing document as an incremental update.
/// </summary>
/// <remarks>
/// An incremental update is the only way to add a signature to a PDF without invalidating one that is
/// already there: the original bytes are copied through untouched, the new objects follow them, and the
/// new cross-reference section points back at the old one through <c>/Prev</c>. Every offset this writer
/// records is absolute in the output, so a document that starts after a byte-order mark, or after the bytes
/// of an earlier revision, still resolves.
/// </remarks>
internal sealed class PdfIncrementalWriter
{
    private readonly PdfOutputBuffer output;
    private readonly long origin;
    private readonly List<Entry> entries = [];

    /// <summary>Initializes a new instance of the <see cref="PdfIncrementalWriter"/> class.</summary>
    /// <param name="destination">The stream the document is written to.</param>
    /// <param name="origin">The number of bytes that precede the first object written here.</param>
    public PdfIncrementalWriter(Stream destination, long origin)
    {
        output = new PdfOutputBuffer(destination);
        this.origin = origin;
    }

    /// <summary>Gets the absolute length of everything written so far.</summary>
    public long Length => origin + output.Length;

    /// <summary>Gets the number of bytes written by this writer.</summary>
    public long Written => output.Length;

    /// <summary>Writes a single byte into the object that is open.</summary>
    /// <param name="value">The byte.</param>
    public void WriteByte(byte value) => output.WriteByte(value);

    /// <summary>Writes bytes into the object that is open.</summary>
    /// <param name="bytes">The bytes; typically the raw hexadecimal digits of a signature.</param>
    public void Write(ReadOnlySpan<byte> bytes) => output.Write(bytes);

    /// <summary>Opens an indirect object.</summary>
    /// <param name="objectNumber">The number of the object.</param>
    /// <param name="generation">The generation of the object.</param>
    /// <returns>The absolute offset of the object, which is the value the cross-reference entry holds.</returns>
    public long BeginObject(int objectNumber, int generation)
    {
        var offset = Length;

        entries.Add(new Entry(objectNumber, generation, offset));
        output.WriteInteger(objectNumber);
        output.WriteByte((byte)' ');
        output.WriteInteger(generation);
        output.Write(" obj\n"u8);

        return offset;
    }

    /// <summary>Closes the object that is open.</summary>
    public void EndObject() => output.Write("\nendobj\n"u8);


    /// <summary>Writes one value of the object model into the object that is open.</summary>
    /// <param name="value">The value to write.</param>
    /// <remarks>
    /// A signature dictionary has to keep its <c>/ByteRange</c> and <c>/Contents</c> entries at offsets the
    /// caller knows, so its parts are written one after another instead of as a whole object; this method
    /// writes the parts that are ordinary values — strings, names, numbers — with the same formatting every
    /// other object in the document gets.
    /// </remarks>
    public void WriteValue(PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);

        PdfSerializer.Write(value, output);
    }

    /// <summary>Writes a whole indirect object from an object model value.</summary>
    /// <param name="objectNumber">The number of the object.</param>
    /// <param name="generation">The generation of the object.</param>
    /// <param name="body">The body of the object.</param>
    /// <returns>The absolute offset of the object.</returns>
    public long WriteObject(int objectNumber, int generation, PdfObject body)
    {
        var offset = BeginObject(objectNumber, generation);

        PdfSerializer.Write(body, output);
        EndObject();

        return offset;
    }

    /// <summary>Writes the cross-reference section, the trailer and the closing <c>%%EOF</c>.</summary>
    /// <param name="trailer">The trailer dictionary, which must already carry <c>/Root</c> and <c>/Prev</c>.</param>
    public void Complete(PdfDictionary trailer)
    {
        ArgumentNullException.ThrowIfNull(trailer);

        var startXref = Length;
        output.Write("xref\n"u8);
        WriteSubsections();

        var size = 1;

        foreach (var entry in entries)
        {
            size = Math.Max(size, entry.Number + 1);
        }

        trailer.SetInteger("Size", Math.Max(trailer.GetInteger("Size") ?? 0, size));

        output.Write("trailer\n"u8);
        PdfSerializer.WriteDictionary(trailer, output);
        output.Write("\nstartxref\n"u8);
        output.WriteInteger(startXref);
        output.Write("\n%%EOF\n"u8);
        output.Flush();
    }

    /// <summary>Flushes the buffer to the stream, so the bytes written so far can be read back.</summary>
    public void Flush() => output.Flush();

    /// <summary>Writes one subsection for every run of consecutive object numbers.</summary>
    private void WriteSubsections()
    {
        if (entries.Count == 0)
        {
            return;
        }

        var sorted = entries.ToArray();
        Array.Sort(sorted, static (left, right) => left.Number.CompareTo(right.Number));

        var start = 0;

        while (start < sorted.Length)
        {
            var end = start;

            while (end + 1 < sorted.Length && sorted[end + 1].Number == sorted[end].Number + 1)
            {
                end++;
            }

            output.WriteInteger(sorted[start].Number);
            output.WriteByte((byte)' ');
            output.WriteInteger(sorted[end].Number - sorted[start].Number + 1);
            output.WriteByte((byte)'\n');

            for (var position = start; position <= end; position++)
            {
                WriteEntry(sorted[position]);
            }

            start = end + 1;
        }
    }

    /// <summary>
    /// Writes one cross-reference entry. Both numbers are fields of a fixed width — ten digits of offset
    /// and five of generation — because a reader that seeks by twenty-byte records, which is what the
    /// format promises, lands in the middle of an entry otherwise.
    /// </summary>
    /// <param name="entry">The entry.</param>
    private void WriteEntry(Entry entry)
    {
        output.WriteOffsetField(entry.Offset);
        output.WriteByte((byte)' ');

        Span<byte> generation = stackalloc byte[5];
        var value = entry.Generation;

        for (var index = generation.Length - 1; index >= 0; index--)
        {
            generation[index] = (byte)('0' + (value % 10));
            value /= 10;
        }

        output.Write(generation);
        output.Write(" n \n"u8);
    }

    /// <summary>One object written in this revision.</summary>
    /// <param name="Number">The object number.</param>
    /// <param name="Generation">The generation number.</param>
    /// <param name="Offset">The absolute offset of the object.</param>
    private readonly record struct Entry(int Number, int Generation, long Offset);
}
