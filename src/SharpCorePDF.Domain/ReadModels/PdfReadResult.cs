using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// What a read produced: the items it found and the remarks it made on the way, kept together so that
/// a caller who asked for one page's worth of data also receives the reasons some of it is missing.
/// </summary>
/// <typeparam name="T">The type of the items, a read model of the same namespace.</typeparam>
/// <remarks>
/// Every read of the content of a document returns this shape rather than a bare collection, because
/// "nothing was found" and "nothing could be read" look identical in a bare collection and mean very
/// different things to the caller (NFR-7.8). The warnings are empty, never <see langword="null"/>,
/// when the read went cleanly.
/// </remarks>
public sealed record PdfReadResult<T>
{
    /// <summary>Gets the items that were read, in document order.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Gets the problems that did not stop the read, for example an image whose filter the
    /// library does not decode or a named destination that does not resolve.</summary>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];

    /// <summary>Gets the number of items that were read.</summary>
    public int Count => Items.Count;
}
