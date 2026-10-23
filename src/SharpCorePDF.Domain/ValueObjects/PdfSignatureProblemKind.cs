namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Identifies what is wrong with one signature: the condition, as opposed to the wording that describes
/// it.
/// </summary>
/// <remarks>
/// This is the contract a caller matches on, exactly as <see cref="PdfWarningKind"/> is for a remark, so
/// a test can assert the reason a signature is not valid without depending on a sentence. The set is
/// open: a later release may add a kind, so a <c>switch</c> over this enum must keep a <c>default</c> arm.
/// </remarks>
public enum PdfSignatureProblemKind
{
    /// <summary>
    /// The <c>/ByteRange</c> entry is missing, is not four numbers, leaves the covered bytes out of the
    /// document, or has ranges that overlap or run backwards.
    /// </summary>
    MalformedByteRange,

    /// <summary>
    /// The digest of the covered bytes is not the digest the signature states, which is what a byte
    /// changed after signing looks like.
    /// </summary>
    DigestMismatch,

    /// <summary>
    /// The signature dictionary names no certificate, or the CMS it holds carries none.
    /// </summary>
    CertificateMissing,

    /// <summary>The signer's certificate is past its <c>notAfter</c> date.</summary>
    CertificateExpired,

    /// <summary>The signer's certificate is before its <c>notBefore</c> date.</summary>
    CertificateNotYetValid,

    /// <summary>A trust evaluation was asked for, and the chain the document carries does not reach an anchor.</summary>
    ChainIncomplete,

    /// <summary>A trust evaluation was asked for, and the chain leads to a root the caller did not name.</summary>
    ChainUntrusted,

    /// <summary>
    /// The signature uses a digest or signature algorithm this release does not implement, so it cannot
    /// be verified here.
    /// </summary>
    UnsupportedAlgorithm,

    /// <summary>
    /// The signature covers less than the whole document: a later revision was appended after it was
    /// signed.
    /// </summary>
    RevisionNotCovered,

    /// <summary>A timestamp token is embedded in the signature but cannot be read.</summary>
    TimestampInvalid,

    /// <summary>
    /// A CMS structure, a certificate or a signature dictionary could not be parsed, so the integrity of
    /// that signature is <see cref="PdfSignatureIntegrity.Unknown"/>.
    /// </summary>
    StructureUnreadable,

    /// <summary>
    /// The caller asked for an online revocation check, which this release does not perform: no network
    /// call is made and the boundary is reported instead.
    /// </summary>
    OnlineRevocationUnsupported,

    /// <summary>The signature value of the CMS does not verify against the public key of the signer.</summary>
    SignatureMismatch,
}
