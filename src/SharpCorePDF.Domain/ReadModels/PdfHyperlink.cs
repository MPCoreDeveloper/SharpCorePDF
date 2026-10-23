using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// A link a page carries: the area a reader clicks, and where that click goes — a URI outside the document
/// or a destination inside it.
/// </summary>
/// <remarks>
/// <para>
/// The read keeps the two kinds of target apart instead of flattening them into one string, because they
/// answer different questions: <see cref="Uri"/> is something a caller can open, and
/// <see cref="Destination"/> is a place in this document — or in the file the destination names — that only
/// this document can resolve. Exactly one of the two is set for a link that states a target at all.
/// </para>
/// <para>
/// <see cref="Rectangle"/> is the clickable area in page space and may be <see langword="null"/>: a producer
/// is free to write a link with no area, and an empty rectangle would claim the link covers a point. A
/// destination that does not resolve is kept with its name and reported through a
/// <see cref="PdfWarningKind.NamedDestinationUnresolved"/> warning, so a link is never silently dropped
/// (NFR-7.8).
/// </para>
/// </remarks>
public sealed record PdfHyperlink
{
    /// <summary>Gets the one-based number of the page the link is drawn on.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Gets the clickable area of the link in page space, or <see langword="null"/> when it has none.</summary>
    public PdfRectangle? Rectangle { get; init; }

    /// <summary>Gets the URI the link opens, or <see langword="null"/> when the link is internal.</summary>
    public string? Uri { get; init; }

    /// <summary>Gets the destination the link jumps to, or <see langword="null"/> when the link is external.</summary>
    public PdfDestination? Destination { get; init; }

    /// <summary>
    /// Gets a value indicating whether the link leaves the document: a URI always does, and so does a
    /// destination that names a file.
    /// </summary>
    public bool IsExternal => Uri is not null || Destination?.FileName is not null;

    /// <summary>
    /// Gets a value indicating whether the link goes somewhere this document resolves, which is what
    /// distinguishes a jump from a link that names something the reader cannot follow.
    /// </summary>
    public bool IsInternal => Uri is null;

    /// <summary>Formats the link for diagnostics.</summary>
    /// <returns>The page, the area and the target.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"link on page {PageNumber} to {Uri ?? Destination?.NamedDestination ?? (Destination?.PageNumber is { } page ? $"page {page}" : "an unresolved destination")}");
}
