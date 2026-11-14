using System.Runtime.InteropServices;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// What one read produced: the description of a document together with the bytes it was described from.
/// </summary>
/// <remarks>
/// <para>
/// The two travel together because a use case that inspects a document before writing it out — a merge, an
/// edit — would otherwise have to read the same document twice, and reading it once is the whole reason this
/// record exists ([ADR-004](../../docs/adr/0004-buffer-model.md)).
/// </para>
/// <para>
/// The memory is the array the read filled, not a copy of it. It stays valid while the array is alive, and
/// <see cref="OpenRead"/> hands out a stream over it that a reader recognises as its own, so handing the
/// bytes on costs nothing.
/// </para>
/// </remarks>
public sealed record PdfDocumentBytes
{
    /// <summary>Gets the description of the document.</summary>
    public required PdfDocumentInfo Info { get; init; }

    /// <summary>Gets the bytes the description was read from.</summary>
    public required ReadOnlyMemory<byte> Bytes { get; init; }

    /// <summary>Opens a read-only stream over the bytes without copying them.</summary>
    /// <returns>A stream positioned at the start of the document.</returns>
    /// <remarks>
    /// The stream is a view of the same array, so a caller that disposes it does not release
    /// <see cref="Bytes"/>. When the memory is not array-backed — which this library never produces, but a
    /// hand-built value may — the stream is over a copy instead.
    /// </remarks>
    public Stream OpenRead() =>
        MemoryMarshal.TryGetArray(Bytes, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false, publiclyVisible: true)
            : new MemoryStream(Bytes.ToArray(), writable: false);

    /// <summary>Formats the read for diagnostics.</summary>
    /// <returns>The formatted read.</returns>
    public override string ToString() => $"{Info} ({Bytes.Length:N0} bytes)";
}
