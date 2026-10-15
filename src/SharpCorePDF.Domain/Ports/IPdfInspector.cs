using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Reads the structure of a PDF document without changing it.
/// </summary>
/// <remarks>
/// Inspection never throws for an encrypted document: it reports
/// <see cref="PdfDocumentInfo.IsEncrypted"/> instead, so a caller can explain the problem before
/// attempting a merge. Malformed input still raises
/// <see cref="Exceptions.PdfSourceFormatException"/>.
/// </remarks>
public interface IPdfInspector
{
    /// <summary>Reads the structure of a document.</summary>
    /// <param name="source">The content to inspect.</param>
    /// <param name="password">The user password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What the document contains: version, page count, page sizes, metadata and document level features.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfDocumentInfo> InspectAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);
}
