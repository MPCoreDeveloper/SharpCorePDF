namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// A PDF stream: a dictionary plus raw bytes.
/// </summary>
/// <remarks>
/// <para>
/// The bytes are kept exactly as they were read, so a merged document never re-compresses image,
/// font or content data. Only the streams the engine must actually understand — object streams and
/// cross-reference streams — are decompressed, and only to inspect their contents.
/// </para>
/// <para>
/// The bytes are a view of the source document rather than a copy of it: a document is held in memory
/// for the whole merge, so duplicating every stream body on the way in would only double the largest
/// part of the memory the engine uses.
/// </para>
/// </remarks>
internal sealed class PdfStream : PdfObject
{
    /// <summary>Initializes a new instance of the <see cref="PdfStream"/> class.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="raw">The raw, still encoded bytes; a view of the document when it comes from a parsed file.</param>
    public PdfStream(PdfDictionary dictionary, ReadOnlyMemory<byte> raw)
    {
        Dictionary = dictionary;
        Raw = raw;
    }

    /// <summary>Gets the stream dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the raw, still encoded bytes of the stream.</summary>
    public ReadOnlyMemory<byte> Raw { get; }


    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Stream;

    /// <inheritdoc />
    public override string ToString() => $"stream({Raw.Length} bytes, {Dictionary.Count} entries)";
}

/// <summary>An indirect reference to another object, written as <c>12 0 R</c>.</summary>
internal sealed class PdfReference : PdfObject, IEquatable<PdfReference>
{
    /// <summary>Initializes a new instance of the <see cref="PdfReference"/> class.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="generation">The generation number.</param>
    public PdfReference(long objectNumber, int generation)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    /// <summary>Gets the object number.</summary>
    public long ObjectNumber { get; }

    /// <summary>Gets the generation number.</summary>
    public int Generation { get; }

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Reference;

    /// <summary>Determines whether two references point at the same object.</summary>
    /// <param name="other">The reference to compare with.</param>
    /// <returns><see langword="true"/> when both the object number and the generation match.</returns>
    public bool Equals(PdfReference? other) => other is not null && ObjectNumber == other.ObjectNumber && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PdfReference other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ObjectNumber, Generation);

    /// <inheritdoc />
    public override string ToString() => $"{ObjectNumber} {Generation} R";
}
