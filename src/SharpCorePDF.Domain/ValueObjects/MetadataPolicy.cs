namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Determines which document information a merge writes to the result.
/// </summary>
public enum MetadataPolicy
{
    /// <summary>
    /// Copy the document information of the primary source. This is the default: a merge is judged
    /// mostly by its pages, so the metadata of the document that leads the merge is preserved
    /// verbatim, including custom entries.
    /// </summary>
    FromPrimarySource = 0,

    /// <summary>
    /// Start from the primary source and fill fields it leaves empty from the other sources, in the
    /// order in which they appear in the plan.
    /// </summary>
    Merge = 1,

    /// <summary>Write no document information at all, except the producer.</summary>
    None = 2,
}
