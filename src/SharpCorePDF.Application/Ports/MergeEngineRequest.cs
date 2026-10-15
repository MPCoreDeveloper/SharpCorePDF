using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Everything the engine needs to produce a merged document: the sources with their page lists
/// already resolved against the real documents, and the options that control the result.
/// </summary>
public sealed record MergeEngineRequest
{
    /// <summary>Gets the sources in merge order, with the content streams that are open for reading.</summary>
    public required IReadOnlyList<ResolvedPdfSource> Sources { get; init; }

    /// <summary>Gets the options that control the merged document.</summary>
    public required MergeOutputOptions Output { get; init; }

    /// <summary>Gets the identity of the source whose document level features lead the merge.</summary>
    public required PdfSourceId PrimarySourceId { get; init; }

    /// <summary>Gets the total number of pages the merge produces.</summary>
    public int TotalPageCount => Sources.Sum(source => source.Pages.Count);
}
