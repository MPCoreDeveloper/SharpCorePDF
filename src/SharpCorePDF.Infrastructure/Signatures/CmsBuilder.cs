using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>
/// Builds the detached CMS signature value a PDF signature dictionary carries.
/// </summary>
/// <remarks>
/// <para>
/// The structure is a CMS <c>SignedData</c> with no encapsulated content, one signer and the signed
/// attributes a reader checks: the content type, the digest of the bytes the signature covers, the signing
/// time and — for CAdES — the identifier of the signer's certificate. Nothing else is decided here, because
/// the signature is written into the file it signs and must therefore be built after the byte ranges are
/// known and before the value is patched in.
/// </para>
/// <para>
/// The timestamp is the caller's, never this library's: the provider is handed the digest of the signature
/// value and whatever token it returns is embedded as the <c>signatureTimeStampToken</c> attribute. A
/// provider that throws stops the signature here, before anything is patched into the document.
/// </para>
/// </remarks>
internal static class CmsBuilder
{
    /// <summary>Builds the CMS of one signature.</summary>
    /// <param name="contentDigest">The digest of the bytes the signature covers.</param>
    /// <param name="options">The certificate, the digest, the sub-filter and the timestamp provider.</param>
    /// <param name="certificates">The certificates to embed: the signer's first, then its chain.</param>
    /// <param name="signingTime">The moment to write as the signing time.</param>
    /// <param name="cancellationToken">A token to cancel the timestamp request.</param>
    /// <returns>The DER-encoded <c>ContentInfo</c> of a detached signature.</returns>
    /// <exception cref="ArgumentException">The certificate has no private key this release can sign with.</exception>
    /// <exception cref="NotSupportedException">The digest is not implemented.</exception>
    public static async Task<byte[]> BuildAsync(
        byte[] contentDigest,
        PdfSignatureOptions options,
        IReadOnlyList<X509Certificate2> certificates,
        DateTimeOffset signingTime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contentDigest);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificates);

        var certificate = options.Certificate;
        var hash = options.HashAlgorithm;
        var digestAlgorithm = DerEncoding.Algorithm(SignatureAlgorithms.DigestOid(hash));

        var attributes = new List<byte[]>
        {
            Attribute(SignatureAlgorithms.ContentTypeOid, DerEncoding.ObjectIdentifier(SignatureAlgorithms.DataOid)),
            Attribute(SignatureAlgorithms.MessageDigestOid, DerEncoding.OctetString(contentDigest)),
            Attribute(SignatureAlgorithms.SigningTimeOid, DerEncoding.Time(signingTime)),
        };

        if (options.UseCades)
        {
            attributes.Add(SigningCertificateV2(certificate, hash));
        }

        // The signature covers the signed attributes with the SET OF tag they are written with in the
        // certificate structure, not the [0] IMPLICIT tag they carry inside the signer info.
        var signedAttributes = DerEncoding.SetOf([.. attributes]);
        var signature = Sign(signedAttributes, certificate, hash);
        var signatureAlgorithm = SignatureAlgorithms.SignatureOid(KeyAlgorithmOid(certificate), hash);

        var signerInfo = new List<byte[]>
        {
            DerEncoding.Integer(1),
            IssuerAndSerialNumber(certificate),
            digestAlgorithm,
            Retag(signedAttributes, DerEncoding.SignedAttributesTag),
            DerEncoding.Algorithm(signatureAlgorithm, DerEncoding.NullValue()),
            DerEncoding.OctetString(signature),
        };

        if (options.TimestampProvider is { } provider)
        {
            var valueDigest = SignatureAlgorithms.Hash(hash, signature);
            var token = await provider
                .GetTimestampTokenAsync(valueDigest, hash, cancellationToken)
                .ConfigureAwait(false);

            signerInfo.Add(DerEncoding.Tagged(DerEncoding.UnsignedAttributesTag, TimestampAttribute(token.ToArray())));
        }

        var signedData = DerEncoding.Sequence(
            DerEncoding.Integer(1),
            DerEncoding.SetOf(digestAlgorithm),
            DerEncoding.Sequence(DerEncoding.ObjectIdentifier(SignatureAlgorithms.DataOid)),
            DerEncoding.Tagged(DerEncoding.CertificatesTag, [.. certificates.Select(item => item.RawData)]),
            DerEncoding.SetOf(DerEncoding.Sequence([.. signerInfo])));

        return DerEncoding.Sequence(
            DerEncoding.ObjectIdentifier(SignatureAlgorithms.SignedDataOid),
            DerEncoding.Tagged(DerEncoding.SignedAttributesTag, signedData));
    }

    /// <summary>Encodes one signed or unsigned attribute: its identifier paired with one value.</summary>
    /// <param name="identifier">The identifier of the attribute.</param>
    /// <param name="value">The encoded value.</param>
    /// <returns>The encoded attribute.</returns>
    private static byte[] Attribute(string identifier, byte[] value) =>
        DerEncoding.Sequence(
            DerEncoding.ObjectIdentifier(identifier),
            DerEncoding.SetOf(value));

    /// <summary>Encodes the CAdES <c>signingCertificateV2</c> attribute of the signer's certificate.</summary>
    /// <param name="certificate">The certificate that signs.</param>
    /// <param name="hash">The digest of the signature.</param>
    /// <returns>The encoded attribute.</returns>
    /// <remarks>
    /// The hash algorithm is left out for SHA-256, which the structure defaults to; a stated one and the
    /// default are the same value, and DER wants the shorter encoding.
    /// </remarks>
    private static byte[] SigningCertificateV2(X509Certificate2 certificate, PdfHashAlgorithm hash)
    {
        var certificateHash = SignatureAlgorithms.Hash(hash, certificate.RawData);
        var identifier = hash == PdfHashAlgorithm.Sha256
            ? DerEncoding.Sequence(
                DerEncoding.OctetString(certificateHash),
                IssuerSerial(certificate))
            : DerEncoding.Sequence(
                DerEncoding.Algorithm(SignatureAlgorithms.DigestOid(hash)),
                DerEncoding.OctetString(certificateHash),
                IssuerSerial(certificate));

        return Attribute(
            SignatureAlgorithms.SigningCertificateV2Oid,
            DerEncoding.Sequence(identifier));
    }

    /// <summary>Encodes the issuer and serial number of a certificate.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The encoded <c>IssuerSerial</c>.</returns>
    private static byte[] IssuerSerial(X509Certificate2 certificate) => DerEncoding.Sequence(
        DerEncoding.Sequence(DerEncoding.Tagged(0xA4, certificate.IssuerName.RawData)),
        SerialNumber(certificate));

    /// <summary>Encodes the <c>IssuerAndSerialNumber</c> that names a signer's certificate.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The encoded <c>IssuerAndSerialNumber</c>.</returns>
    private static byte[] IssuerAndSerialNumber(X509Certificate2 certificate) => DerEncoding.Sequence(
        certificate.IssuerName.RawData,
        SerialNumber(certificate));

    /// <summary>Encodes the serial number of a certificate as a positive integer.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The encoded serial number.</returns>
    private static byte[] SerialNumber(X509Certificate2 certificate) =>
        DerEncoding.Integer(new BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true));

    /// <summary>Replaces the identifier of an encoded value, keeping its contents.</summary>
    /// <param name="encoded">The encoded value.</param>
    /// <param name="tag">The identifier to write instead.</param>
    /// <returns>A copy carrying the other identifier.</returns>
    /// <remarks>
    /// The <c>[0] IMPLICIT</c> wrapper of the signed attributes holds exactly the contents of the
    /// <c>SET OF</c> the signature covers, so only the identifier byte differs.
    /// </remarks>
    private static byte[] Retag(byte[] encoded, byte tag)
    {
        var retagged = (byte[])encoded.Clone();
        retagged[0] = tag;

        return retagged;
    }

    /// <summary>
    /// Encodes the <c>signatureTimeStampToken</c> unsigned attribute, without the <c>[1] IMPLICIT</c> wrapper
    /// its field carries — the caller adds that, because the wrapper holds the field and not the attribute.
    /// </summary>
    /// <param name="token">The token the provider returned.</param>
    /// <returns>The encoded attribute.</returns>
    private static byte[] TimestampAttribute(byte[] token) =>
        Attribute(SignatureAlgorithms.TimestampTokenOid, token);

    /// <summary>Signs the encoded signed attributes with the key of a certificate.</summary>
    /// <param name="data">The bytes to sign.</param>
    /// <param name="certificate">The certificate whose private key signs.</param>
    /// <param name="hash">The digest of the signature.</param>
    /// <returns>The signature value.</returns>
    /// <exception cref="ArgumentException">The certificate has no private key this release can sign with.</exception>
    private static byte[] Sign(byte[] data, X509Certificate2 certificate, PdfHashAlgorithm hash)
    {
        var name = SignatureAlgorithms.Name(hash);

        using (var rsa = certificate.GetRSAPrivateKey())
        {
            if (rsa is not null)
            {
                // RSASSA-PKCS1-v1_5 is what the rsaEncryption signature algorithm names; the CMS
                // signature format has no room for the PSS parameters that a PSS signature would need.
#pragma warning disable S5542 // The padding scheme is fixed by the signature algorithm of the CMS.
                return rsa.SignData(data, name, RSASignaturePadding.Pkcs1);
#pragma warning restore S5542
            }
        }

        using (var ecdsa = certificate.GetECDsaPrivateKey())
        {
            if (ecdsa is not null)
            {
                return ecdsa.SignData(data, name);
            }
        }

        throw new ArgumentException(
            $"the certificate '{certificate.Subject}' has no RSA or ECDSA private key, so no signature can be made with it.",
            nameof(certificate));
    }

    /// <summary>Finds the key algorithm of a certificate.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The object identifier of its public key algorithm.</returns>
    private static string? KeyAlgorithmOid(X509Certificate2 certificate)
    {
        using var key = certificate.GetECDsaPublicKey();

        return key is not null ? SignatureAlgorithms.EcPublicKeyOid : SignatureAlgorithms.RsaOid;
    }
}
