using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// What a single page of a source document contains.
/// </summary>
/// <param name="Number">The one-based page number.</param>
/// <param name="Size">The page size as stored in the document, before <see cref="Rotation"/> is applied.</param>
/// <param name="Rotation">The rotation a viewer applies before displaying the page.</param>
/// <param name="HasContent">Whether the page has any content stream.</param>
/// <param name="AnnotationCount">The number of annotations on the page.</param>
public sealed record PdfPageInfo(
    int Number,
    PdfPageSize Size,
    PageRotation Rotation,
    bool HasContent,
    int AnnotationCount)
{
    /// <summary>Gets the size as a viewer displays it, that is with the rotation applied.</summary>
    public PdfPageSize DisplaySize => Size.Rotate(Rotation);

    /// <summary>Formats the page for diagnostics.</summary>
    /// <returns>The formatted page.</returns>
    public override string ToString()
    {
        var rotation = Rotation == PageRotation.None
            ? string.Empty
            : $" rotated {Rotation.Degrees} degrees";

        return $"page {Number}: {Size}{rotation}";
    }
}
