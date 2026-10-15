namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The container half of <see cref="PdfSerializer"/>: dictionaries and streams.
/// </summary>
internal static partial class PdfSerializer
{
    /// <summary>Writes a dictionary.</summary>
    /// <param name="dictionary">The dictionary to write.</param>
    /// <param name="output">The buffer to write to.</param>
    public static void WriteDictionary(PdfDictionary dictionary, PdfOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(output);

        output.Write("<<"u8);

        foreach (var entry in dictionary.Entries)
        {
            output.WriteByte((byte)' ');

            // The key is written from its characters; wrapping every key in a PdfName would allocate an
            // object (and a string) per entry of every dictionary of every source document.
            WriteNameCharacters(entry.Key, output);
            output.WriteByte((byte)' ');
            Write(entry.Value, output);
        }

        output.Write(" >>"u8);
    }

    /// <summary>Writes a stream.</summary>
    /// <param name="stream">The stream to write.</param>
    /// <param name="output">The buffer to write to.</param>
    /// <remarks>
    /// The stored /Length may be wrong or indirect; the written form always states the exact size, which
    /// is what makes the output unambiguous for the next reader. The dictionary is written entry by entry
    /// instead of being cloned, because a clone costs a dictionary, a list of keys and a hash table for
    /// every stream of every source.
    /// </remarks>
    public static void WriteStream(PdfStream stream, PdfOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(stream);

        output.Write("<<"u8);

        var hasLength = false;

        foreach (var entry in stream.Dictionary.Entries)
        {
            output.WriteByte((byte)' ');

            if (entry.Key is "Length")
            {
                hasLength = true;
                WriteNameCharacters(entry.Key, output);
                output.WriteByte((byte)' ');
                output.WriteInteger(stream.Raw.Length);
                continue;
            }

            WriteNameCharacters(entry.Key, output);
            output.WriteByte((byte)' ');
            Write(entry.Value, output);
        }

        if (!hasLength)
        {
            output.Write(" /Length "u8);
            output.WriteInteger(stream.Raw.Length);
        }

        output.Write(" >>"u8);
        output.Write("\nstream\n"u8);
        output.Write(stream.Raw.Span);
        output.Write("\nendstream"u8);
    }
}
