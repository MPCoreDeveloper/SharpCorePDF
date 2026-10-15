namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// What one source contributed to a merge.
/// </summary>
/// <param name="SourceName">The display name of the source.</param>
/// <param name="PagesContributed">The number of pages taken from the source.</param>
/// <param name="SourcePageCount">The total number of pages the source document contained.</param>
/// <param name="Version">The PDF version of the source document.</param>
public sealed record MergedSourceSummary(
    string SourceName,
    int PagesContributed,
    int SourcePageCount,
    ValueObjects.PdfVersion Version)
{
    /// <summary>Gets a value indicating whether the whole source was contributed.</summary>
    public bool IsComplete => PagesContributed == SourcePageCount;

    /// <summary>Formats the contribution for diagnostics.</summary>
    /// <returns>The formatted contribution.</returns>
    public override string ToString() =>
        IsComplete
            ? $"{SourceName}: all {SourcePageCount} page(s) of PDF {Version}"
            : $"{SourceName}: {PagesContributed} of {SourcePageCount} page(s) of PDF {Version}";
}
