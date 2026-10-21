namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// The XMP packet a document carries: the bytes as the document stores them, and the values this
/// library could read out of them.
/// </summary>
/// <remarks>
/// <para>
/// The packet is metadata a producer wrote about itself — a standards profile such as PDF/A, a Dublin
/// Core title, a creator tool — and it is kept byte-exact because a caller validating a profile needs
/// the bytes the producer signed, not a re-serialised approximation. The parsed values are a
/// convenience over the same packet; nothing is written back.
/// </para>
/// <para>
/// Parsed values are keyed by the prefixed name a caller recognises — <c>dc:title</c>,
/// <c>pdf:Producer</c>, <c>xmp:CreatorTool</c> — for the three namespaces the read model covers. A
/// packet this library cannot parse leaves the values empty and reports a warning; it does not make
/// the read fail.
/// </para>
/// </remarks>
public sealed record PdfXmpMetadata
{
    /// <summary>Gets the packet as the document stores it, after any compression is removed.</summary>
    public required ReadOnlyMemory<byte> RawPacket { get; init; }

    /// <summary>Gets the packet decoded as text, which is UTF-8 by definition.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the parsed values, keyed by prefixed name such as <c>dc:title</c>. A value that the packet
    /// states more than once, or as a list, is joined with <c>", "</c>; a language alternative prefers
    /// <c>x-default</c>. The dictionary is empty when the packet could not be parsed.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the title the packet states as <c>dc:title</c>, or <see langword="null"/>.</summary>
    public string? Title => Properties.GetValueOrDefault("dc:title");

    /// <summary>Gets the producer the packet states as <c>pdf:Producer</c>, or <see langword="null"/>.</summary>
    public string? Producer => Properties.GetValueOrDefault("pdf:Producer");

    /// <summary>Gets the tool that created the document as <c>xmp:CreatorTool</c>, or <see langword="null"/>.</summary>
    public string? CreatorTool => Properties.GetValueOrDefault("xmp:CreatorTool");

    /// <summary>Formats the packet for diagnostics.</summary>
    /// <returns>The number of bytes and the number of parsed values.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"XMP packet, {RawPacket.Length} byte(s), {Properties.Count} parsed value(s)");
}
