using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// Everything this library knows about one page, yielded one page at a time.
/// </summary>
/// <remarks>
/// <para>
/// The detail is a view of the page rather than a second parse: the same read models the page-scoped
/// readers return are gathered here, so a caller indexing a long document does not loop over five
/// APIs. A page with nothing on it is still yielded, with empty collections, so page numbers never
/// shift because a page happened to be blank.
/// </para>
/// <para>
/// A page whose content is damaged is still yielded. Whatever could be read is there, the part that
/// could not is missing, and <see cref="Warnings"/> says why — the remarks are the page's own, not the
/// document's, so a caller can attribute them without guessing.
/// </para>
/// </remarks>
public sealed record PdfPageDetail
{
    /// <summary>Gets the one-based number of the page.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Gets the page size as stored in the document, before <see cref="Rotation"/>.</summary>
    public required PdfPageSize Size { get; init; }

    /// <summary>Gets the rotation a viewer applies before displaying the page.</summary>
    public PageRotation Rotation { get; init; }

    /// <summary>Gets the images the page draws, in the order it draws them.</summary>
    public IReadOnlyList<PdfImageInfo> Images { get; init; } = [];

    /// <summary>Gets the annotations of the page, links included.</summary>
    public IReadOnlyList<PdfAnnotationInfo> Annotations { get; init; } = [];

    /// <summary>Gets the form fields that have a widget on the page.</summary>
    public IReadOnlyList<PdfFormFieldInfo> FormFields { get; init; } = [];

    /// <summary>Gets the problems that did not stop the page from being read.</summary>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];

    /// <summary>Gets the annotations of the page that are hyperlinks.</summary>
    public IReadOnlyList<PdfAnnotationInfo> Links => [.. Annotations.Where(annotation => annotation.IsLink)];

    /// <summary>Gets the size as a viewer displays it, that is with the rotation applied.</summary>
    public PdfPageSize DisplaySize => Size.Rotate(Rotation);

    /// <summary>Formats the page for diagnostics.</summary>
    /// <returns>The page number and what it carries.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"page {PageNumber}: {Images.Count} image(s), {Annotations.Count} annotation(s), {FormFields.Count} form field(s)");
}
