namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Whether the certificate behind a signature leads to a root the caller trusts.
/// </summary>
/// <remarks>
/// The library ships no root store and never consults the machine store on its own: a validation that is
/// not given trust anchors reports <see cref="NotEvaluated"/> rather than guessing, and a chain that is
/// evaluated is evaluated against exactly the anchors the caller named.
/// </remarks>
public enum PdfTrustResult
{
    /// <summary>No trust evaluation was asked for, so none is claimed.</summary>
    NotEvaluated,

    /// <summary>The chain of the signer leads to one of the caller's anchors and is otherwise sound.</summary>
    Trusted,

    /// <summary>
    /// The chain does not lead to one of the caller's anchors, is incomplete, or has a certificate that
    /// is expired or not yet valid.
    /// </summary>
    Untrusted,
}
