using System.Security.Cryptography;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>
/// The object identifiers and the platform primitives behind <see cref="PdfHashAlgorithm"/>, kept in one
/// place so the writer and the reader cannot disagree about which digest an identifier names.
/// </summary>
internal static class SignatureAlgorithms
{
    /// <summary>The identifier of the <c>data</c> content type, which is what a detached CMS carries.</summary>
    public const string DataOid = "1.2.840.113549.1.7.1";

    /// <summary>The identifier of the <c>signedData</c> content type.</summary>
    public const string SignedDataOid = "1.2.840.113549.1.7.2";

    /// <summary>The identifier of the <c>contentType</c> signed attribute.</summary>
    public const string ContentTypeOid = "1.2.840.113549.1.9.3";

    /// <summary>The identifier of the <c>messageDigest</c> signed attribute.</summary>
    public const string MessageDigestOid = "1.2.840.113549.1.9.4";

    /// <summary>The identifier of the <c>signingTime</c> signed attribute.</summary>
    public const string SigningTimeOid = "1.2.840.113549.1.9.5";

    /// <summary>The identifier of the <c>signingCertificate</c> signed attribute of PKCS#7.</summary>
    public const string SigningCertificateOid = "1.2.840.113549.1.9.16.2.12";

    /// <summary>The identifier of the <c>signingCertificateV2</c> signed attribute of CAdES.</summary>
    public const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    /// <summary>The identifier of the <c>signatureTimeStampToken</c> unsigned attribute.</summary>
    public const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";

    /// <summary>The identifier of the rsaEncryption key algorithm.</summary>
    public const string RsaOid = "1.2.840.113549.1.1.1";

    /// <summary>The identifier of the id-ecPublicKey key algorithm.</summary>
    public const string EcPublicKeyOid = "1.2.840.10045.2.1";

    /// <summary>Gets the object identifier of a digest.</summary>
    /// <param name="algorithm">The digest.</param>
    /// <returns>The identifier the CMS writes for it.</returns>
    public static string DigestOid(PdfHashAlgorithm algorithm) => algorithm switch
    {
        PdfHashAlgorithm.Sha256 => "2.16.840.1.101.3.4.2.1",
        PdfHashAlgorithm.Sha384 => "2.16.840.1.101.3.4.2.2",
        PdfHashAlgorithm.Sha512 => "2.16.840.1.101.3.4.2.3",
        _ => throw new NotSupportedException($"The digest '{algorithm}' has no object identifier here."),
    };

    /// <summary>Finds the digest an object identifier names.</summary>
    /// <param name="identifier">The identifier.</param>
    /// <returns>The digest, or <see langword="null"/> when this release does not implement it.</returns>
    public static PdfHashAlgorithm? FromDigestOid(string? identifier) => identifier switch
    {
        "2.16.840.1.101.3.4.2.1" => PdfHashAlgorithm.Sha256,
        "2.16.840.1.101.3.4.2.2" => PdfHashAlgorithm.Sha384,
        "2.16.840.1.101.3.4.2.3" => PdfHashAlgorithm.Sha512,
        _ => null,
    };

    /// <summary>Gets the platform name of a digest.</summary>
    /// <param name="algorithm">The digest.</param>
    /// <returns>The name the cryptography classes take.</returns>
    public static HashAlgorithmName Name(PdfHashAlgorithm algorithm) => algorithm switch
    {
        PdfHashAlgorithm.Sha256 => HashAlgorithmName.SHA256,
        PdfHashAlgorithm.Sha384 => HashAlgorithmName.SHA384,
        PdfHashAlgorithm.Sha512 => HashAlgorithmName.SHA512,
        _ => throw new NotSupportedException($"The digest '{algorithm}' is not implemented."),
    };

    /// <summary>Hashes one buffer with a digest.</summary>
    /// <param name="algorithm">The digest.</param>
    /// <param name="data">The bytes to hash.</param>
    /// <returns>The digest of the bytes.</returns>
    public static byte[] Hash(PdfHashAlgorithm algorithm, ReadOnlySpan<byte> data) => algorithm switch
    {
        PdfHashAlgorithm.Sha256 => SHA256.HashData(data),
        PdfHashAlgorithm.Sha384 => SHA384.HashData(data),
        PdfHashAlgorithm.Sha512 => SHA512.HashData(data),
        _ => throw new NotSupportedException($"The digest '{algorithm}' is not implemented."),
    };

    /// <summary>Gets the object identifier of the signature algorithm a key produces over a digest.</summary>
    /// <param name="keyAlgorithm">The identifier of the key algorithm, or <see langword="null"/> when unknown.</param>
    /// <param name="algorithm">The digest.</param>
    /// <returns>The identifier of the signature algorithm.</returns>
    public static string SignatureOid(string? keyAlgorithm, PdfHashAlgorithm algorithm)
    {
        if (keyAlgorithm == EcPublicKeyOid)
        {
            return algorithm switch
            {
                PdfHashAlgorithm.Sha256 => "1.2.840.10045.4.3.2",
                PdfHashAlgorithm.Sha384 => "1.2.840.10045.4.3.3",
                _ => "1.2.840.10045.4.3.4",
            };
        }

        return algorithm switch
        {
            PdfHashAlgorithm.Sha256 => "1.2.840.113549.1.1.11",
            PdfHashAlgorithm.Sha384 => "1.2.840.113549.1.1.12",
            _ => "1.2.840.113549.1.1.13",
        };
    }

    /// <summary>Finds the digest a signature algorithm identifier names.</summary>
    /// <param name="identifier">The identifier of the signature algorithm.</param>
    /// <returns>The digest, or <see langword="null"/> when this release does not implement it.</returns>
    public static PdfHashAlgorithm? FromSignatureOid(string? identifier) => identifier switch
    {
        "1.2.840.113549.1.1.11" or "1.2.840.10045.4.3.2" => PdfHashAlgorithm.Sha256,
        "1.2.840.113549.1.1.12" or "1.2.840.10045.4.3.3" => PdfHashAlgorithm.Sha384,
        "1.2.840.113549.1.1.13" or "1.2.840.10045.4.3.4" => PdfHashAlgorithm.Sha512,
        _ => null,
    };

    /// <summary>Determines whether a signature algorithm identifier names an elliptic curve signature.</summary>
    /// <param name="identifier">The identifier of the signature algorithm.</param>
    /// <returns><see langword="true"/> when the signature is an ECDSA value.</returns>
    public static bool IsEllipticCurve(string? identifier) =>
        identifier is not null && identifier.StartsWith("1.2.840.10045.4.", StringComparison.Ordinal);
}