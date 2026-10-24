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
    private readonly PdfSecurityWriter? security;
    private readonly Dictionary<int, long> offsets = [];
    private readonly long origin;
    private int lastObjectNumber;

    /// <summary>Initializes a new instance of the <see cref="PdfWriter"/> class.</summary>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="version">The version to put in the file header.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="security">The encryption of the document, or <see langword="null"/> for one that is written in the clear.</param>
    public PdfWriter(
        Stream destination,
        PdfVersion version,
        PdfEngineOptions options,
        PdfSecurityWriter? security = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        this.security = security;

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

    /// <summary>
    /// Creates a writer that appends a revision to a document whose bytes are already in the stream.
    /// </summary>
    /// <param name="destination">The stream that already holds the earlier revisions; it is written to at its current position.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="highestObjectNumber">The highest object number an earlier revision used, so new objects are numbered above it.</param>
    /// <param name="origin">The absolute offset of the first byte this writer writes.</param>
    /// <returns>The writer.</returns>
    /// <remarks>
    /// An appended revision has no header of its own: the header of the file belongs to the first
    /// revision, and writing a second one would make the file unreadable. Every offset recorded here is
    /// absolute in the output, because that is what the cross-reference section of the revision holds.
    /// </remarks>
    public static PdfWriter Append(
        Stream destination,
        PdfEngineOptions options,
        int highestObjectNumber,
        long origin)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        return new PdfWriter(destination, options, highestObjectNumber, origin);
    }

    private PdfWriter(Stream destination, PdfEngineOptions options, int highestObjectNumber, long origin)
    {
        this.origin = origin;
        lastObjectNumber = highestObjectNumber;
        output = new PdfOutputBuffer(destination);
    }

    /// <summary>Gets the number of bytes written so far.</summary>
    /// <remarks>
    /// For a writer that appends a revision this is the size of the whole file, because the bytes of the
    /// earlier revisions precede everything this writer wrote.
    /// </remarks>
    public long Length => origin + output.Length;

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
        offsets[objectNumber] = Length;
        output.WriteInteger(objectNumber);
        output.Write(" 0 obj\n"u8);

        // The number of the object is what the key of every string and stream of it is derived from, so
        // it is what the serializer is handed here rather than anything the object carries itself.
        PdfSerializer.Write(body, output, PdfObjectEncryption.For(security, objectNumber));
        output.Write("\nendobj\n"u8);
    }

    /// <summary>Writes the encryption dictionary and returns the object number it was written as.</summary>
    /// <param name="security">The encryption of the document.</param>
    /// <returns>The object number to reference from the trailer.</returns>
    /// <remarks>
    /// This deliberately does not go through <see cref="WriteObject"/>: the encryption dictionary is the
    /// one object a PDF keeps in the clear, because a reader must be able to read the salts and the file
    /// key out of it before it has a key at all.
    /// </remarks>
    public int WriteEncryptionDictionary(PdfSecurityWriter security)
    {
        ArgumentNullException.ThrowIfNull(security);

        var objectNumber = AllocateObject();
        offsets[objectNumber] = Length;
        output.WriteInteger(objectNumber);
        output.Write(" 0 obj\n"u8);
        PdfSerializer.WriteDictionary(security.Dictionary, output);
        output.Write("\nendobj\n"u8);

        return objectNumber;
    }

    /// <summary>Writes the cross-reference table, trailer and the closing <c>startxref</c>.</summary>
    /// <param name="trailer">The trailer dictionary; its <c>Size</c> entry is corrected here.</param>
    public void Complete(PdfDictionary trailer)
    {
        ArgumentNullException.ThrowIfNull(trailer);

        var startXref = Length;
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
