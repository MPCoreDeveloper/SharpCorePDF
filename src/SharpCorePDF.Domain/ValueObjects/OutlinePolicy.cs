namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Determines what happens to the bookmarks (the document outline) of the sources.
/// </summary>
public enum OutlinePolicy
{
    /// <summary>
    /// Merge the outline trees of every source into a single tree whose top level items follow the
    /// source order. This is the default.
    /// </summary>
    Preserve = 0,

    /// <summary>Write a result without bookmarks.</summary>
    Drop = 1,
}
