using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Reads the signatures of a document from a stream and checks each of them. Implemented by the PDF
/// engine.
/// </summary>
/// <remarks>
/// The engine-side counterpart of <see cref="Domain.Ports.IPdfSignatureValidator"/>. The content stream is
/// read from its current position to its end and is not disposed; nothing is written.
/// </remarks>
public interface IPdfDocumentSignatureValidator
{
    /// <summary>Reads and checks every signature of the document in a stream.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="trust">The anchors to evaluate the chains against, or <see langword="null"/> to report no trust.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One validation per signature, in revision order.</returns>
    Task<IReadOnlyList<PdfSignatureValidation>> ValidateAsync(
        string sourceName,
        Stream content,
        PdfTrustOptions? trust = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
