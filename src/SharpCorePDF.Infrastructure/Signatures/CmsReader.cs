using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>
/// Reads the detached CMS a signature dictionary carries, so a validator can check it.
/// </summary>
/// <remarks>
/// <para>
/// The reader is strict about structure and lenient about content: anything it cannot follow is reported by
/// throwing, and the caller turns that into an integrity of <c>Unknown</c> rather than a verdict. It never
/// re-encodes a value it has to verify — the signed attributes travel as the bytes they were read from —
/// because a signature computed over one encoding and checked against another fails for no reason a caller
/// could act on.
/// </para>
/// <para>
/// The encoder is the one in <c>Adobe.PPKLite</c> terms: <c>adbe.pkcs7.detached</c> and
/// <c>ETSI.CAdES.detached</c> both hold a <c>SignedData</c> with no encapsulated content, which is what this
/// reader expects.
/// </para>
/// </remarks>
internal static class CmsReader
{
    /// <summary>Reads a CMS signature value.</summary>
    /// <param name="content">The DER bytes of a <c>ContentInfo</c>.</param>
    /// <returns>What the signature states.</returns>
    /// <exception cref="InvalidDataException">The value is not a CMS signature this reader can follow.</exception>
    public static CmsSignature Read(ReadOnlyMemory<byte> content)
    {
        var contentInfo = Parse(content, "the signature value is not a DER value.");

        if (!contentInfo.Is(DerEncoding.SequenceTag))
        {
            throw Unreadable("the signature value is not a CMS ContentInfo.");
        }

        var contentType = contentInfo.Child(0)?.Oid();

        if (contentType != SignatureAlgorithms.SignedDataOid)
        {
            throw Unreadable(
                $"the signature value holds the CMS content type '{contentType}' instead of signedData.");
        }

        var wrapper = contentInfo.Child(1)
            ?? throw Unreadable("the signature value has no signedData content.");

        var signedData = wrapper.Children.Count == 1 ? wrapper.Children[0] : wrapper;

        if (!signedData.Is(DerEncoding.SequenceTag))
        {
            throw Unreadable("the signedData content is not a sequence.");
        }

        var certificates = ReadCertificates(signedData);
        var signerInfos = LastSet(signedData)
            ?? throw Unreadable("the signedData has no signer info.");

        var signerInfo = signerInfos.Children.Count > 0
            ? signerInfos.Children[0]
            : throw Unreadable("the signedData has an empty signer info set.");

        if (!signerInfo.Is(DerEncoding.SequenceTag) || signerInfo.Children.Count < 6)
        {
            throw Unreadable("the signer info is not a sequence of the expected shape.");
        }

        var subjectKeyIdentifier = signerInfo.Child(1)?.Is(0x80) == true;
        var digestAlgorithm = signerInfo.Child(2) ?? throw Unreadable("the signer info names no digest algorithm.");
        var digestOid = digestAlgorithm.Child(0)?.Oid();
        var signedAttributes = signerInfo.Child(3);

        if (signedAttributes is null || !signedAttributes.Is(DerEncoding.SignedAttributesTag))
        {
            throw Unreadable(
                "the signer info carries no signed attributes, so the content digest it covers cannot be read.");
        }

        var signatureAlgorithm = signerInfo.Child(4)?.Child(0)?.Oid()
            ?? throw Unreadable("the signer info names no signature algorithm.");

        var signature = signerInfo.Child(5)?.Octets()
            ?? throw Unreadable("the signer info carries no signature value.");

        var attributes = ReadAttributes(signedAttributes);
        var messageDigest = attributes.TryGetValue(SignatureAlgorithms.MessageDigestOid, out var digestValue)
            ? digestValue.Octets()
                ?? throw Unreadable("the messageDigest attribute is not an octet string.")
            : throw Unreadable("the signed attributes carry no messageDigest, so nothing is covered.");

        var certificate = FindCertificate(
            signerInfo.Child(1),
            certificates,
            subjectKeyIdentifier);

        var (hasTimestamp, timestampReadable) = ReadTimestamp(signerInfo.Child(6));

        return new CmsSignature
        {
            DigestAlgorithm = SignatureAlgorithms.FromDigestOid(digestOid),
            DigestAlgorithmOid = digestOid,
            MessageDigest = messageDigest,
            // The signature was computed over the attributes with the SET OF tag, not the [0] IMPLICIT one
            // they carry inside the signer info.
            SignedAttributes = Retag(signedAttributes.Encoded.ToArray(), DerEncoding.SetTag),
            Signature = signature,
            SignatureAlgorithm = signatureAlgorithm,
            Certificate = certificate,
            Certificates = certificates,
            SigningTime = attributes.TryGetValue(SignatureAlgorithms.SigningTimeOid, out var time)
                ? time.Time()
                : null,
            HasTimestamp = hasTimestamp,
            TimestampReadable = timestampReadable,
        };
    }

    /// <summary>Reads a DER value, or throws the message the caller wants for an unreadable one.</summary>
    /// <param name="content">The bytes to read.</param>
    /// <param name="message">The message to throw when they are not DER.</param>
    /// <returns>The value.</returns>
    private static DerValue Parse(ReadOnlyMemory<byte> content, string message) =>
        DerValue.ReadExact(content.Span) ?? throw Unreadable(message);

    /// <summary>Creates the exception of an unreadable CMS.</summary>
    /// <param name="message">What could not be read.</param>
    /// <returns>The exception.</returns>
    private static InvalidDataException Unreadable(string message) =>
        new($"The signature value cannot be read: {message}");

    /// <summary>Reads the certificates a signed data structure embeds.</summary>
    /// <param name="signedData">The signed data.</param>
    /// <returns>The certificates, in the order they are listed.</returns>
    private static List<X509Certificate2> ReadCertificates(DerValue signedData)
    {
        var certificates = new List<X509Certificate2>();

        foreach (var child in signedData.Children.Where(child => child.Is(DerEncoding.CertificatesTag)))
        {
            foreach (var entry in child.Children)
            {
                // The certificate set may hold other certificate choices — attribute certificates, for
                // instance — which are not X.509 certificates and are skipped rather than ending the read.
                if (!entry.Is(DerEncoding.SequenceTag))
                {
                    continue;
                }

                try
                {
                    certificates.Add(X509CertificateLoader.LoadCertificate(entry.Encoded.ToArray()));
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    // A malformed embedded certificate is not fatal: the signature can still be checked,
                    // and the missing certificate is reported by the trust evaluation instead.
                }
            }
        }

        return certificates;
    }

    /// <summary>Reads the attributes of a signed or unsigned attribute set.</summary>
    /// <param name="attributes">The attribute set.</param>
    /// <returns>The encoded value of each attribute, keyed by its identifier.</returns>
    private static Dictionary<string, DerValue> ReadAttributes(DerValue attributes)
    {
        var values = new Dictionary<string, DerValue>(StringComparer.Ordinal);

        foreach (var attribute in attributes.Children)
        {
            if (!attribute.Is(DerEncoding.SequenceTag) || attribute.Children.Count < 2)
            {
                continue;
            }

            var identifier = attribute.Child(0)?.Oid();
            var set = attribute.Child(1);

            // An attribute holds a SET OF values; the ones this reader understands hold exactly one.
            if (identifier is null || set is null || set.Children.Count == 0)
            {
                continue;
            }

            values[identifier] = set.Children[0];
        }

        return values;
    }

    /// <summary>Finds the signer info set of a signed data structure.</summary>
    /// <param name="signedData">The signed data.</param>
    /// <returns>The last set the structure holds, which is the signer info set.</returns>
    private static DerValue? LastSet(DerValue signedData)
    {
        for (var index = signedData.Children.Count - 1; index >= 0; index--)
        {
            if (signedData.Children[index].Is(DerEncoding.SetTag))
            {
                return signedData.Children[index];
            }
        }

        return null;
    }

    /// <summary>Finds the certificate the signer info names.</summary>
    /// <param name="sid">The signer identifier.</param>
    /// <param name="certificates">The certificates the CMS embeds.</param>
    /// <param name="subjectKeyIdentifier">Whether the identifier is a subject key identifier.</param>
    /// <returns>The certificate, or <see langword="null"/> when it was not embedded.</returns>
    private static X509Certificate2? FindCertificate(
        DerValue? sid,
        List<X509Certificate2> certificates,
        bool subjectKeyIdentifier)
    {
        if (certificates.Count == 0)
        {
            return null;
        }

        // A subject key identifier is not compared: a signer that names its key this way is rare in PDF,
        // and the first certificate of the set is the only reading the CMS order supports.
        if (subjectKeyIdentifier || sid is null || sid.Children.Count < 2)
        {
            return certificates[0];
        }

        var issuer = sid.Child(0)?.Encoded;
        var serial = sid.Child(1)?.Content;

        if (issuer is null || serial is null)
        {
            return certificates[0];
        }

        foreach (var certificate in certificates)
        {
            if (certificate.IssuerName.RawData.AsSpan().SequenceEqual(issuer.Value.Span)
                && SerialNumber(certificate).AsSpan().SequenceEqual(serial.Value.Span))
            {
                return certificate;
            }
        }

        return null;
    }

    /// <summary>Reads the serial number of a certificate the way a signer identifier states it.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The big-endian two's complement bytes of the serial number.</returns>
    private static byte[] SerialNumber(X509Certificate2 certificate) =>
        new BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true)
            .ToByteArray(isUnsigned: false, isBigEndian: true);

    /// <summary>Reads the timestamp token attribute.</summary>
    /// <param name="unsignedAttributes">The unsigned attributes of the signer, when it has any.</param>
    /// <returns>Whether a token is embedded, and whether it could be read as a CMS structure.</returns>
    private static (bool HasTimestamp, bool Readable) ReadTimestamp(DerValue? unsignedAttributes)
    {
        if (unsignedAttributes is null
            || !unsignedAttributes.Is(DerEncoding.UnsignedAttributesTag)
            || unsignedAttributes.Children.Count == 0)
        {
            return (false, true);
        }

        var attributes = ReadAttributes(unsignedAttributes);

        if (!attributes.TryGetValue(SignatureAlgorithms.TimestampTokenOid, out var token))
        {
            return (false, true);
        }

        var readable = token.Is(DerEncoding.SequenceTag)
            && token.Child(0)?.Oid() == SignatureAlgorithms.SignedDataOid;

        return (true, readable);
    }

    /// <summary>Replaces the identifier of an encoded value, keeping its contents.</summary>
    /// <param name="encoded">The encoded value.</param>
    /// <param name="tag">The identifier to write instead.</param>
    /// <returns>A copy carrying the other identifier.</returns>
    private static byte[] Retag(byte[] encoded, byte tag)
    {
        encoded[0] = tag;

        return encoded;
    }
}
