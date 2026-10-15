using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// A source of a merge whose content stream is open and whose page selection has been resolved
/// against the real document.
/// </summary>
public sealed record ResolvedPdfSource
{
    /// <summary>Gets the source being merged.</summary>
    public required PdfSource Source { get; init; }

    /// <summary>
    /// Gets the open content stream of the document.
    /// </summary>
    /// <remarks>The application layer owns this stream; an engine must read from it but never dispose it.</remarks>
    public required Stream Content { get; init; }

    /// <summary>Gets the structure of the document as reported by the reader.</summary>
    public required PdfDocumentInfo Info { get; init; }

    /// <summary>Gets the one-based page numbers to take from <see cref="Content"/>, in merge order.</summary>
    public required IReadOnlyList<int> Pages { get; init; }

    /// <summary>Formats the resolved source for diagnostics.</summary>
    /// <returns>The formatted source.</returns>
    public override string ToString() =>
        $"{Source.DisplayName} [{string.Join(",", Pages)}] of {Info.PageCount} page(s)";
}
