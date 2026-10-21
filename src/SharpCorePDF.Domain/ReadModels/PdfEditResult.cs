using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// The outcome of an edit: the document that was written, how long it took, and anything the caller
/// should know about content that could not be carried over.
/// </summary>
public sealed record PdfEditResult
{
    /// <summary>Gets the number of pages in the edited document.</summary>
    public required int PageCount { get; init; }

    /// <summary>Gets the size in bytes of the edited document.</summary>
    public required long BytesWritten { get; init; }

    /// <summary>Gets the PDF version written to the edited document.</summary>
    public required PdfVersion OutputVersion { get; init; }

    /// <summary>Gets how long the edit took.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Gets the operations that were applied, in plan order, formatted for diagnostics.</summary>
    public IReadOnlyList<string> Operations { get; init; } = [];

    /// <summary>
    /// Gets non-fatal remarks about the edit, for example that a damaged cross-reference table had to
    /// be reconstructed, or that page labels could not survive a reorder.
    /// </summary>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];

    /// <summary>Formats the result for diagnostics.</summary>
    /// <returns>The formatted result.</returns>
    public override string ToString() =>
        $"edited into {PageCount} page(s), {BytesWritten} bytes, PDF {OutputVersion} in {Elapsed.TotalMilliseconds:0} ms";
}
