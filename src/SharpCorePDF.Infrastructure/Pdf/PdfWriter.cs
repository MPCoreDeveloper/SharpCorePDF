using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Writes a PDF document: allocates object numbers, records the offset of every object and finishes
/// with a classic cross-reference table.
/// </summary>
/// <remarks>
/// Classic tables are written in preference to cross-reference streams because every reader in
/// existence supports them, and because a merge copies object streams rather than preserving them.
/// </remarks>
internal sealed class PdfWriter
{
    private readonly PdfOutputBuffer output;
    private readonly Dictionary<int, long> offsets = [];
    private int lastObjectNumber;

    /// <summary>Initializes a new instance of the <see cref="PdfWriter"/> class.</summary>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="version">The version to put in the file header.</param>
    /// <param name="options">The engine options.</param>
    public PdfWriter(Stream destination, PdfVersion version, PdfEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        // The version and the options are written into the header and the binary marker here and are not
        // needed afterwards: the header is the only thing a writer puts in the file from either of them.
        output = new PdfOutputBuffer(destination);

        // The header and the object headers are written byte by byte: a document has one header and
        // thousands of object headers, and an interpolated string per object is a string per object.
        output.Write("%PDF-"u8);
        output.WriteInteger(version.Major);
        output.WriteByte((byte)'.');
        output.WriteInteger(version.Minor);
        output.WriteByte((byte)'\n');

        if (options.WriteBinaryMarker)
        {
            output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
        }
    }

    /// <summary>Gets the number of bytes written so far.</summary>
    public long Length => output.Length;

    /// <summary>Reserves the next object number.</summary>
    /// <returns>The reserved object number.</returns>
    public int AllocateObject() => ++lastObjectNumber;

    /// <summary>Gets the highest object number that has been allocated.</summary>
    public int HighestObjectNumber => lastObjectNumber;

    /// <summary>Writes an indirect object.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="body">The body of the object.</param>
    public void WriteObject(int objectNumber, PdfObject body)
    {
        offsets[objectNumber] = output.Length;
        output.WriteInteger(objectNumber);
        output.Write(" 0 obj\n"u8);
        PdfSerializer.Write(body, output);
        output.Write("\nendobj\n"u8);
    }

    /// <summary>Writes the cross-reference table, trailer and the closing <c>startxref</c>.</summary>
    /// <param name="trailer">The trailer dictionary; its <c>Size</c> entry is corrected here.</param>
    public void Complete(PdfDictionary trailer)
    {
        ArgumentNullException.ThrowIfNull(trailer);

        var startXref = output.Length;
        output.Write("xref\n"u8);
        WriteSubsections();

        trailer.SetInteger("Size", Math.Max(Math.Max(trailer.GetInteger("Size") ?? 0, lastObjectNumber + 1), 1));

        output.Write("trailer\n"u8);
        PdfSerializer.WriteDictionary(trailer, output);
        output.Write("\nstartxref\n"u8);
        output.WriteInteger(startXref);
        output.Write("\n%%EOF\n"u8);
        output.Flush();
    }

    /// <summary>
    /// Writes the free entry for object zero and one subsection for every run of consecutive objects.
    /// </summary>
    private void WriteSubsections()
    {
        output.Write("0 1\n0000000000 65535 f \n"u8);

        // The object numbers are sorted into an array of exactly the right size: a LINQ ordering would
        // allocate a list, an enumerator and a second list for the very same numbers.
        var numbers = new int[offsets.Count];
        var count = 0;

        foreach (var number in offsets.Keys)
        {
            numbers[count++] = number;
        }

        Array.Sort(numbers);
        var index = 0;

        while (index < count)
        {
            var first = numbers[index];
            var last = first;

            while (index + 1 < count && numbers[index + 1] == last + 1)
            {
                last = numbers[++index];
            }

            output.WriteInteger(first);
            output.WriteByte((byte)' ');
            output.WriteInteger(last - first + 1);
            output.WriteByte((byte)'\n');

            for (var number = first; number <= last; number++)
            {
                output.WriteOffsetField(offsets[number]);
                output.Write(" 00000 n \n"u8);
            }

            index++;
        }
    }
}
