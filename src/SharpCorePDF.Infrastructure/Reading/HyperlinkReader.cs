using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// Projects the annotations of a page onto the links a consumer of a layout cares about.
/// </summary>
/// <remarks>
/// <para>
/// The resolution of a link's target already lives in <see cref="PdfPageContentReader"/>: an action with a
/// URI, a direct destination, a named destination looked up in either place the document can keep one, and
/// the warning a name that does not resolve produces. This class only decides which annotations are links
/// and how their two kinds of target are presented, which is what keeps the link surface and the annotation
/// surface from telling different stories about the same document.
/// </para>
/// <para>
/// An annotation of another subtype is not a link, however it is drawn, and is left to the annotation read.
/// </para>
/// </remarks>
internal static class HyperlinkReader
{
    /// <summary>Projects an annotation read into a link read, keeping the order and the warnings.</summary>
    /// <param name="annotations">The annotations and the remarks of the read that produced them.</param>
    /// <returns>The links of the same pages, in the same order, with the same remarks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="annotations"/> is <see langword="null"/>.</exception>
    public static PdfReadResult<PdfHyperlink> Extract(PdfReadResult<PdfAnnotationInfo> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        return new()
        {
            Items = [.. annotations.Items.Where(annotation => annotation.IsLink).Select(ToHyperlink)],
            Warnings = annotations.Warnings,
        };
    }

    /// <summary>Projects one link annotation into one hyperlink.</summary>
    /// <param name="annotation">The annotation of the link subtype.</param>
    /// <returns>The hyperlink.</returns>
    private static PdfHyperlink ToHyperlink(PdfAnnotationInfo annotation) => new()
    {
        PageNumber = annotation.PageNumber,
        Rectangle = annotation.Rectangle,
        Uri = annotation.Target?.Uri,
        Destination = annotation.Target is { Uri: null } target ? target : null,
    };
}
