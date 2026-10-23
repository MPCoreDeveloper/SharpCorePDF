namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// What the covered bytes of a document are worth: whether they still hash and verify to what the
/// signer signed.
/// </summary>
/// <remarks>
/// This is deliberately separate from <see cref="PdfTrustResult"/>. A document can be intact and signed
/// by a certificate nobody trusts, and it can be signed by a trusted certificate over bytes that were
/// changed afterwards (NFR: integrity and trust are reported independently).
/// </remarks>
public enum PdfSignatureIntegrity
{
    /// <summary>
    /// The signature could not be checked at all: its structure was unreadable, its algorithm is not
    /// supported, or the bytes it covers are not there. Never reported as a success.
    /// </summary>
    Unknown,

    /// <summary>The covered bytes hash and verify to what the signature states.</summary>
    Valid,

    /// <summary>
    /// The covered bytes do not match what the signature states, or the signature itself does not
    /// verify against the signer's certificate.
    /// </summary>
    Invalid,
}
