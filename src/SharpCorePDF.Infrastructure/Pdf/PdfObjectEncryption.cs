namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The encryption that applies to the strings and the stream bodies of one indirect object.
/// </summary>
/// <remarks>
/// <para>
/// Only two kinds of object are encrypted in a PDF file, and the key of each of them is derived from
/// the number of the indirect object that holds it. This value therefore carries both the writer and
/// that number, and it travels through the serializer as an argument rather than as state on the
/// writer, so nothing depends on the order in which objects are written.
/// </para>
/// <para>
/// It is a struct so that passing it costs nothing, and its default value means "encrypt nothing",
/// which is what a document that is written in the clear passes: the serializer then takes exactly the
/// path it took before encryption existed.
/// </para>
/// </remarks>
internal readonly struct PdfObjectEncryption
{
    private readonly PdfSecurityWriter? writer;
    private readonly int objectNumber;

    private PdfObjectEncryption(PdfSecurityWriter writer, int objectNumber)
    {
        this.writer = writer;
        this.objectNumber = objectNumber;
    }

    /// <summary>Gets a value indicating whether the object being written is encrypted.</summary>
    public bool IsActive => writer is not null;

    /// <summary>Creates the encryption of one object.</summary>
    /// <param name="writer">The encryption of the document, or <see langword="null"/> when it is written in the clear.</param>
    /// <param name="objectNumber">The object number the values belong to.</param>
    /// <returns>The encryption to pass to the serializer.</returns>
    public static PdfObjectEncryption For(PdfSecurityWriter? writer, int objectNumber) =>
        writer is null ? default : new PdfObjectEncryption(writer, objectNumber);

    /// <summary>Encrypts the bytes of a string.</summary>
    /// <param name="data">The bytes of the string.</param>
    /// <returns>The bytes to write instead.</returns>
    public byte[] EncryptString(ReadOnlySpan<byte> data) => writer!.EncryptString(data, objectNumber);

    /// <summary>Encrypts the bytes of a stream body.</summary>
    /// <param name="data">The encoded bytes of the stream.</param>
    /// <returns>The bytes to write instead.</returns>
    public byte[] EncryptStream(ReadOnlySpan<byte> data) => writer!.EncryptStream(data, objectNumber);
}
