using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Signs a document: appends a signature revision and reports what was written.
/// </summary>
/// <remarks>
/// <para>
/// The signature is a detached CMS over the two byte ranges of the PDF, written into an incremental
/// revision. The bytes of the source are copied verbatim and never rewritten, which is what keeps an
/// earlier signature valid after a later one is added: the revision it signed is still there, byte for
/// byte.
/// </para>
/// <para>
/// The source is only ever read. The destination receives the whole document — the source followed by the
/// new revision — and is flushed but not closed, exactly as the editor's destination is.
/// </para>
/// </remarks>
public interface IPdfSigner
{
    /// <summary>Signs a document and writes the signed document to a stream.</summary>
    /// <param name="source">The document to sign.</param>
    /// <param name="destination">A writable stream that receives the whole signed document.</param>
    /// <param name="options">The certificate, the digest, the field and the entries of the signature.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What the written signature states, read back from the certificate and the options.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The destination is not writable, the certificate has no private key, or the options name a field
    /// or an appearance that cannot be written.
    /// </exception>
    /// <exception cref="Exceptions.PdfUnsupportedFeatureException">
    /// The document is encrypted, or the signature names an algorithm this release does not implement.
    /// </exception>
    Task<PdfSignatureInfo> SignAsync(
        IPdfContentSource source,
        Stream destination,
        PdfSignatureOptions options,
        string? password = null,
        CancellationToken cancellationToken = default);
}
