using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Reports the signatures a document carries and checks each of them, without changing the document.
/// </summary>
/// <remarks>
/// <para>
/// Every signature in a multi-signed document is returned, in the order the revisions were appended, which
/// is the order a reader lists them in. A document without a signature yields an empty list rather than an
/// error, but a document that is not a PDF at all raises <see cref="Exceptions.PdfSourceFormatException"/>,
/// exactly like the other reads.
/// </para>
/// <para>
/// Integrity is always checked. Trust is checked only when the caller passes
/// <see cref="PdfTrustOptions"/>, because this library ships no root store and does not consult the
/// machine's on its own.
/// </para>
/// </remarks>
public interface IPdfSignatureValidator
{
    /// <summary>Reads and checks every signature of a document.</summary>
    /// <param name="source">The document to read.</param>
    /// <param name="trust">The anchors to evaluate the chains against, or <see langword="null"/> to report no trust.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One validation per signature, in revision order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<IReadOnlyList<PdfSignatureValidation>> ValidateAsync(
        IPdfContentSource source,
        PdfTrustOptions? trust = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
