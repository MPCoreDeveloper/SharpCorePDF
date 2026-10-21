namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One file embedded in a document, as the document stores it.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is distinct within the document even when two entries of the filename tree share a
/// name, so neither is silently dropped and a caller can address each one; the collision itself is
/// reported as a warning. The payload follows the same rule as an image:
/// <see cref="Data"/> is non-null only within <c>PdfEngineOptions.MaxInlineContentBytes</c>, and above
/// that budget the caller fetches it with the copy route, with <see cref="ByteLength"/> still truthful.
/// </para>
/// </remarks>
public sealed record PdfEmbeddedFileInfo
{
    /// <summary>Gets the identifier that is distinct within the document — the name, or the name with a
    /// numeric suffix when the tree holds two entries of that name.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the name the document gives the file.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the MIME type the embedded stream declares in its <c>/Subtype</c>, or <see langword="null"/>
    /// when it declares none.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Gets the relationship the file has to the document — <c>Source</c>, <c>Data</c>,
    /// <c>Alternative</c> — or <see langword="null"/> when it states none.
    /// </summary>
    public string? Relationship { get; init; }

    /// <summary>
    /// Gets the moment the file was last modified, or <see langword="null"/> when the document states
    /// none.
    /// </summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>Gets the filter the embedded stream declares, or <see langword="null"/>.</summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Gets a value indicating whether the payload was produced. It is <see langword="false"/> for a
    /// filter the library does not decode, whose raw bytes are returned instead.
    /// </summary>
    public bool IsDecodable { get; init; } = true;

    /// <summary>
    /// Gets the length of the file in bytes, which is the true length whether or not
    /// <see cref="Data"/> carries it.
    /// </summary>
    public long ByteLength { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Data"/> carries the payload.</summary>
    public bool IsBuffered { get; init; }

    /// <summary>
    /// Gets the bytes of the file, or <see langword="null"/> when it was not buffered because it
    /// exceeds the inline budget. A <see langword="null"/> here means "fetch it with the copy route",
    /// not "the file is empty".
    /// </summary>
    public ReadOnlyMemory<byte>? Data { get; init; }

    /// <summary>Formats the file for diagnostics.</summary>
    /// <returns>The name, the MIME type and the size.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"embedded file '{Name}' ({MimeType ?? "unknown type"}), {ByteLength} byte(s)");
}
