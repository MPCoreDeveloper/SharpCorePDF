using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// What one signature of a document states: the field it lives in, the certificate it was made with, the
/// bytes it covers and the entries a reader shows to a human.
/// </summary>
/// <remarks>
/// <para>
/// The record is a report rather than a verdict: it says what the signature claims, and
/// <see cref="PdfSignatureValidation"/> says whether the claim survives checking. A signature whose CMS
/// could not be parsed still yields a record, with <see cref="HashAlgorithm"/> and the certificate
/// properties left <see langword="null"/>, so the caller sees that something is there and unreadable
/// instead of nothing at all.
/// </para>
/// <para>
/// <see cref="CoversWholeDocument"/> is what separates a signature that still protects the file from one
/// that protects an earlier revision only: signing a second time appends a revision, and the first
/// signature then covers everything except that addition.
/// </para>
/// </remarks>
public sealed record PdfSignatureInfo
{
    /// <summary>Gets the fully qualified name of the signature field.</summary>
    public required string FieldName { get; init; }

    /// <summary>Gets the filter a reader dispatches on, for example <c>Adobe.PPKLite</c>.</summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Gets the sub-filter that says what the signature value is, for example
    /// <c>adbe.pkcs7.detached</c> or <c>ETSI.CAdES.detached</c>.
    /// </summary>
    public string? SubFilter { get; init; }

    /// <summary>Gets a value indicating whether the sub-filter is the CAdES one.</summary>
    public bool IsCades => string.Equals(SubFilter, "ETSI.CAdES.detached", StringComparison.Ordinal);

    /// <summary>Gets the moment the signature states it was made, or <see langword="null"/> when it states none.</summary>
    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>Gets the reason the signer gave, when the signature carries one.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the place the signer gave, when the signature carries one.</summary>
    public string? Location { get; init; }

    /// <summary>Gets the contact information the signer gave, when the signature carries one.</summary>
    public string? ContactInfo { get; init; }

    /// <summary>Gets the digest the signature uses, or <see langword="null"/> when the CMS could not be read.</summary>
    public PdfHashAlgorithm? HashAlgorithm { get; init; }

    /// <summary>Gets the distinguished name of the signer's certificate subject.</summary>
    public string? SignerSubject { get; init; }

    /// <summary>Gets the common name of the signer's certificate, which is what a reader shows.</summary>
    public string? SignerName { get; init; }

    /// <summary>Gets the distinguished name of the issuer of the signer's certificate.</summary>
    public string? Issuer { get; init; }

    /// <summary>Gets the serial number of the signer's certificate as hexadecimal text.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Gets the first moment the signer's certificate is valid.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>Gets the last moment the signer's certificate is valid.</summary>
    public DateTimeOffset? NotAfter { get; init; }

    /// <summary>Gets the byte ranges the signature covers.</summary>
    public required PdfByteRange ByteRange { get; init; }

    /// <summary>Gets a value indicating whether the ranges cover the file as it is now.</summary>
    public required bool CoversWholeDocument { get; init; }

    /// <summary>Gets the one-based number of the revision the signature covers.</summary>
    public required int CoveredRevision { get; init; }

    /// <summary>Gets the number of bytes the signature value occupies, which is the size of its CMS.</summary>
    public required int SignatureLength { get; init; }

    /// <summary>Gets a value indicating whether the signature embeds an RFC 3161 timestamp token.</summary>
    public bool HasTimestamp { get; init; }

    /// <summary>Formats the signature for diagnostics.</summary>
    /// <returns>The field name and the signer, when one could be read.</returns>
    public override string ToString() =>
        SignerName is { Length: > 0 } name ? $"{FieldName} ({name})" : FieldName;
}
