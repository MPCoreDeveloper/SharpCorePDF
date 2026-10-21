namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One bookmark of a document's outline, with its children and where it points.
/// </summary>
/// <remarks>
/// The tree is returned as the document states it: children keep the order of their siblings, and
/// <see cref="IsOpen"/> is the open or closed state a viewer shows. A bookmark whose destination does
/// not resolve is still returned, with <see cref="PdfDestination.NamedDestination"/> naming the
/// destination that failed, so the outline stays navigable even where the document is not (FR-8).
/// </remarks>
public sealed record PdfOutlineItem
{
    /// <summary>Gets the title of the bookmark as the document states it.</summary>
    public required string Title { get; init; }

    /// <summary>Gets where the bookmark points, or <see langword="null"/> when it points nowhere.</summary>
    public PdfDestination? Destination { get; init; }

    /// <summary>Gets a value indicating whether a viewer shows the children of this bookmark.</summary>
    public bool IsOpen { get; init; } = true;

    /// <summary>Gets the children of the bookmark, in document order.</summary>
    public IReadOnlyList<PdfOutlineItem> Children { get; init; } = [];

    /// <summary>Formats the bookmark for diagnostics.</summary>
    /// <returns>The title and where it points.</returns>
    public override string ToString() => $"{Title} -> {Destination?.ToString() ?? "nowhere"}";
}
