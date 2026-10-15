namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Determines what happens to the page labels of the sources.
/// </summary>
public enum PageLabelPolicy
{
    /// <summary>
    /// Write a result without page labels, so viewers number the pages of the merged document
    /// sequentially. This is the default because page labels are tied to absolute page indices and
    /// therefore misalign as soon as sources are reordered or partially selected.
    /// </summary>
    Drop = 0,

    /// <summary>
    /// Copy the page labels of the primary source and apply them from the first page of the result.
    /// Only meaningful when the primary source contributes its pages starting at the first output page.
    /// </summary>
    FromPrimarySource = 1,
}
