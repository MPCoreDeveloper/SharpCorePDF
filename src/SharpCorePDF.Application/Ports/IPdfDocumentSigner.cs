using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Signs a document from a stream and writes the signed document to a stream. Implemented by the PDF
/// engine.
/// </summary>
/// <remarks>
/// The engine-side counterpart of <see cref="Domain.Ports.IPdfSigner"/>, exactly as
/// <see cref="IPdfDocumentObjectInspector"/> mirrors <see cref="Domain.Ports.IPdfObjectInspector"/>: the
/// use case opens the content source and hands the stream to this port, which keeps the engine free of
/// the domain's source abstraction and of I/O policy. The content stream is read from its current
/// position to its end and is not disposed; the destination is written and flushed, not closed.
/// </remarks>
public interface IPdfDocumentSigner
{
    /// <summary>Signs the document in a stream and writes the whole signed document to another stream.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="destination">A writable stream that receives the whole signed document.</param>
    /// <param name="options">The certificate, the digest, the field and the entries of the signature.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What the written signature states.</returns>
    Task<PdfSignatureInfo> SignAsync(
        string sourceName,
        Stream content,
        Stream destination,
        PdfSignatureOptions options,
        string? password = null,
        CancellationToken cancellationToken = default);
}
