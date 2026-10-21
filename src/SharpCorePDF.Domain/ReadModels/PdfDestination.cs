using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// Where a link or a bookmark points: a place in this document, a URL, or a file beside it.
/// </summary>
/// <remarks>
/// <para>
/// A destination that the document states directly and one that it states through a named destination
/// are the same value once resolved, so a caller never has to resolve a name itself. When the name
/// does not resolve, <see cref="NamedDestination"/> keeps the name that was written and
/// <see cref="PageNumber"/> stays <see langword="null"/>, so the entry is still returned rather than
/// dropped (FR-8).
/// </para>
/// <para>
/// Page numbers are one-based, as everywhere in this API; the zero-based index a PDF uses internally
/// never surfaces.
/// </para>
/// </remarks>
public sealed record PdfDestination
{
    /// <summary>
    /// Gets the one-based number of the target page, or <see langword="null"/> when the destination is
    /// external or did not resolve.
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>
    /// Gets the area or point the destination names, in page space, or <see langword="null"/> when it
    /// names none — a <c>/Fit</c> destination fits the whole page. A destination that names a corner
    /// rather than an area reports a rectangle of no size at that corner.
    /// </summary>
    public ValueObjects.PdfRectangle? Area { get; init; }

    /// <summary>
    /// Gets the name the document used, when it used one, or <see langword="null"/> for a destination
    /// the document states directly. It is kept whether or not the name resolved.
    /// </summary>
    public string? NamedDestination { get; init; }

    /// <summary>Gets the URL of an external link, exactly as the document wrote it.</summary>
    public string? Uri { get; init; }

    /// <summary>Gets the name of the other file an external destination targets.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets a value indicating whether the destination points inside this document.</summary>
    public bool IsInternal => PageNumber is not null;

    /// <summary>Gets a value indicating whether the destination leaves this document.</summary>
    public bool IsExternal => Uri is not null || FileName is not null;

    /// <summary>
    /// Gets a value indicating whether the document named a destination that could not be resolved.
    /// </summary>
    public bool IsUnresolved => NamedDestination is not null && PageNumber is null && !IsExternal;

    /// <summary>Formats the destination for diagnostics.</summary>
    /// <returns>The page, the URL or the unresolved name.</returns>
    public override string ToString() => this switch
    {
        { Uri: { } uri } => uri,
        { FileName: { } file } => file,
        { PageNumber: { } page } => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"page {page}"),
        { NamedDestination: { } name } => $"'{name}' (unresolved)",
        _ => "none",
    };
}
