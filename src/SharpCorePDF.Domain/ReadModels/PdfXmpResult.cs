using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// What reading the XMP metadata of a document produced.
/// </summary>
/// <remarks>
/// A document without a <c>/Metadata</c> stream has no packet at all, which is why
/// <see cref="Metadata"/> is <see langword="null"/> rather than an empty packet: "absent" and
/// "present and empty" are different answers. A packet that exists but cannot be parsed is returned
/// with its bytes intact and its values empty, together with a warning.
/// </remarks>
public sealed record PdfXmpResult
{
    /// <summary>Gets the packet, or <see langword="null"/> when the document carries none.</summary>
    public PdfXmpMetadata? Metadata { get; init; }

    /// <summary>Gets the problems that did not stop the read.</summary>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];
}
