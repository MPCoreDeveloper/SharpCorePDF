using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>
/// What one CMS signature states, as far as checking it needs: the digest of the content it claims, the
/// signature over its signed attributes, and the certificate that made it.
/// </summary>
/// <remarks>
/// Values are kept as the caller will use them — the signed attributes with the tag the signature was
/// computed over, the encoded bytes of every certificate — so that verification never has to re-encode
/// anything and therefore never verifies a different byte string than the one that was signed.
/// </remarks>
internal sealed class CmsSignature
{
    /// <summary>Gets the digest the signature was made with, when this release implements it.</summary>
    public PdfHashAlgorithm? DigestAlgorithm { get; init; }

    /// <summary>Gets the object identifier of the digest, for diagnostics.</summary>
    public string? DigestAlgorithmOid { get; init; }

    /// <summary>Gets the digest of the content stated by the <c>messageDigest</c> attribute.</summary>
    public required byte[] MessageDigest { get; init; }

    /// <summary>Gets the signed attributes as a <c>SET OF</c>, which is what the signature covers.</summary>
    public required byte[] SignedAttributes { get; init; }

    /// <summary>Gets the signature value.</summary>
    public required byte[] Signature { get; init; }

    /// <summary>Gets the object identifier of the signature algorithm.</summary>
    public required string SignatureAlgorithm { get; init; }

    /// <summary>Gets the certificate the signer named, when it could be found among those embedded.</summary>
    public X509Certificate2? Certificate { get; init; }

    /// <summary>Gets every certificate the CMS embeds, in the order it lists them.</summary>
    public required IReadOnlyList<X509Certificate2> Certificates { get; init; }

    /// <summary>Gets the moment of the <c>signingTime</c> attribute.</summary>
    public DateTimeOffset? SigningTime { get; init; }

    /// <summary>Gets a value indicating whether a timestamp token attribute is embedded.</summary>
    public bool HasTimestamp { get; init; }

    /// <summary>Gets a value indicating whether the embedded timestamp token could be read as structured data.</summary>
    public bool TimestampReadable { get; init; } = true;
}
