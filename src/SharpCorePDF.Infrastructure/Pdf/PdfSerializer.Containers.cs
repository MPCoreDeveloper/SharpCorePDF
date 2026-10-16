namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The container half of <see cref="PdfSerializer"/>: dictionaries and streams.
/// </summary>
internal static partial class PdfSerializer
{
    /// <summary>Writes a dictionary.</summary>
    /// <param name="dictionary">The dictionary to write.</param>
    /// <param name="output">The buffer to write to.</param>
    /// <param name="encryption">The encryption of the object being written; nothing is encrypted by default.</param>
    public static void WriteDictionary(
        PdfDictionary dictionary,
        PdfOutputBuffer output,
        PdfObjectEncryption encryption = default)
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
            Write(entry.Value, output, encryption);
        }

        output.Write(" >>"u8);
    }

    /// <summary>Writes a stream.</summary>
    /// <param name="stream">The stream to write.</param>
    /// <param name="output">The buffer to write to.</param>
    /// <param name="encryption">The encryption of the object being written; nothing is encrypted by default.</param>
    /// <remarks>
    /// <para>
    /// The stored /Length may be wrong or indirect; the written form always states the exact size, which
    /// is what makes the output unambiguous for the next reader. The dictionary is written entry by entry
    /// instead of being cloned, because a clone costs a dictionary, a list of keys and a hash table for
    /// every stream of every source.
    /// </para>
    /// <para>
    /// An encrypted stream body is longer than the bytes it was made of, so the encryption runs before
    /// the dictionary that states the length is written, and the length that is stated is the one of
    /// what is actually stored.
    /// </para>
    /// </remarks>
    public static void WriteStream(
        PdfStream stream,
        PdfOutputBuffer output,
        PdfObjectEncryption encryption = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var body = stream.Raw;

        if (encryption.IsActive)
        {
            body = encryption.EncryptStream(stream.Raw.Span);
        }

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
                output.WriteInteger(body.Length);
                continue;
            }

            WriteNameCharacters(entry.Key, output);
            output.WriteByte((byte)' ');
            Write(entry.Value, output, encryption);
        }

        if (!hasLength)
        {
            output.Write(" /Length "u8);
            output.WriteInteger(body.Length);
        }

        output.Write(" >>"u8);
        output.Write("\nstream\n"u8);
        output.Write(body.Span);
        output.Write("\nendstream"u8);
    }
}
