namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// How the content of a page follows a change of its page size.
/// </summary>
public enum PdfPageResizeMode
{
    /// <summary>
    /// The content is scaled, independently on both axes, so that it fills the new page size.
    /// </summary>
    Scale = 0,

    /// <summary>
    /// The content keeps its original size and is centred on the new page.
    /// </summary>
    Preserve = 1,
}
