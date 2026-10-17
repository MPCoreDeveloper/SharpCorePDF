using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// The outcome of a merge: what was written, how long it took, and anything the caller should know
/// about content that could not be carried over.
/// </summary>
public sealed record MergeResult
{
    /// <summary>Gets the number of pages in the merged document.</summary>
    public required int PageCount { get; init; }

    /// <summary>Gets the number of sources that were merged.</summary>
    public required int SourceCount { get; init; }

    /// <summary>Gets the size in bytes of the merged document.</summary>
    public required long OutputBytes { get; init; }

    /// <summary>Gets the PDF version written to the merged document.</summary>
    public required PdfVersion OutputVersion { get; init; }

    /// <summary>Gets how long the merge took.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the identity of the source that led the merge.</summary>
    public required PdfSourceId PrimarySourceId { get; init; }

    /// <summary>Gets what each source contributed, in merge order.</summary>
    public IReadOnlyList<MergedSourceSummary> Sources { get; init; } = [];

    /// <summary>
    /// Gets non-fatal remarks about the merge, for example that a damaged cross-reference table had
    /// to be reconstructed, or that content of a secondary source was intentionally not carried over.
    /// </summary>
    /// <remarks>
    /// A remark is identified by its <see cref="PdfWarning.Kind"/>; its text is for humans and may be
    /// reworded. The remarks of every source document are folded in here, so a warning a caller would
    /// have seen on <see cref="PdfDocumentInfo.Warnings"/> appears in the result as well.
    /// </remarks>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];

    /// <summary>Formats the result for diagnostics.</summary>
    /// <returns>The formatted result.</returns>
    public override string ToString() =>
        $"merged {SourceCount} source(s) into {PageCount} page(s), {OutputBytes} bytes, PDF {OutputVersion} in {Duration.TotalMilliseconds:0} ms";
}
