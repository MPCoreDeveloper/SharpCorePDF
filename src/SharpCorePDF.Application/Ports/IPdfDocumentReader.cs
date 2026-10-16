using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Reads the structure of a PDF document from a stream. Implemented by the PDF engine.
/// </summary>
/// <remarks>
/// Kept deliberately narrow: the merging use case needs the version, the page count, the page
/// descriptions and the document level features, and nothing else.
/// </remarks>
public interface IPdfDocumentReader
{
    /// <summary>Reads the structure of a document.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What the document contains.</returns>
    Task<PdfDocumentInfo> ReadAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);
}
