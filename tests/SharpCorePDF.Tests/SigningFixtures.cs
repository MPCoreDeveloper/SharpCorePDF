using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.Tests;

/// <summary>
/// The document, the certificates and the two engine calls the signature tests share.
/// </summary>
/// <remarks>
/// The certificates are generated once per test run rather than checked in: a fixture certificate that ships
/// in a repository either expires, or lives long enough to be a liability, and generating one costs a few
/// milliseconds. Each is self-signed and marked as a certificate authority, so the same certificate can sign
/// and act as the anchor the chain is measured against.
/// </remarks>
internal static class SigningFixtures
{
    private static readonly Lazy<X509Certificate2> LazyCertificate = new(() => CreateRsa("SharpCorePDF Test Signer"));
    private static readonly Lazy<X509Certificate2> LazyOtherCertificate = new(() => CreateRsa("Somebody Else"));
    private static readonly Lazy<X509Certificate2> LazyEcCertificate = new(CreateEc);
    private static readonly Lazy<X509Certificate2> LazyExpiredCertificate = new(
        () => CreateRsa("Expired Signer", DateTimeOffset.UtcNow.AddDays(-30d), DateTimeOffset.UtcNow.AddDays(-20d)));

    private static readonly PdfDocumentSigner Signer = new();
    private static readonly PdfDocumentSignatureValidator Validator = new();

    /// <summary>Gets the certificate the signature tests sign with.</summary>
    public static X509Certificate2 Certificate => LazyCertificate.Value;

    /// <summary>Gets a certificate the tests never sign with, so a chain can lead somewhere unasked for.</summary>
    public static X509Certificate2 OtherCertificate => LazyOtherCertificate.Value;

    /// <summary>Gets a certificate whose key is elliptic-curve rather than RSA.</summary>
    public static X509Certificate2 EcCertificate => LazyEcCertificate.Value;

    /// <summary>Gets a certificate that is past its validity period, with the key that made it.</summary>
    public static X509Certificate2 ExpiredCertificate => LazyExpiredCertificate.Value;

    /// <summary>Gets a one page document with an empty content stream.</summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] BuildDocument()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> /Contents 4 0 R >>");
        builder.AddStream(string.Empty, []);
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        return builder.Build(catalog);
    }

    /// <summary>Builds signature options that name a fixed moment, so a report can be asserted exactly.</summary>
    /// <param name="certificate">The certificate to sign with.</param>
    /// <param name="fieldName">The name of the field.</param>
    /// <returns>The options.</returns>
    public static PdfSignatureOptions Options(X509Certificate2 certificate, string fieldName = "Signature1") =>
        new()
        {
            Certificate = certificate,
            FieldName = fieldName,
            Reason = "Testing",
            SigningTime = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        };

    /// <summary>Signs a document and returns what was written.</summary>
    /// <param name="document">The document to sign.</param>
    /// <param name="options">The signature to produce.</param>
    /// <param name="password">The password of an encrypted document.</param>
    /// <returns>The whole signed document.</returns>
    public static async Task<byte[]> SignAsync(byte[] document, PdfSignatureOptions options, string? password = null)
    {
        using var content = new MemoryStream(document, writable: false);
        using var destination = new MemoryStream();

        await Signer.SignAsync("source.pdf", content, destination, options, password, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return destination.ToArray();
    }

    /// <summary>Reads and checks every signature of a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="trust">What counts as trusted, or <see langword="null"/> for no trust evaluation.</param>
    /// <returns>One validation per signature.</returns>
    public static async Task<IReadOnlyList<PdfSignatureValidation>> ValidateAsync(
        byte[] document,
        PdfTrustOptions? trust = null)
    {
        using var content = new MemoryStream(document, writable: false);

        return await Validator.ValidateAsync("source.pdf", content, trust, password: null, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads a document as text, so a test can state what the bytes say.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The bytes as Latin-1 text.</returns>
    public static string Text(byte[] document) => Encoding.Latin1.GetString(document);

    /// <summary>Finds a fixture that ships with the tests.</summary>
    /// <param name="parts">The path below the assets folder.</param>
    /// <returns>The full path of the fixture.</returns>
    public static string Asset(params string[] parts) => Path.Combine([TestAssets.Folder, .. parts]);

    private static X509Certificate2 CreateRsa(
        string commonName,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}, O=SharpCorePDF, C=AT",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, false));

        return Export(request, notBefore, notAfter);
    }

    private static X509Certificate2 CreateEc()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=SharpCorePDF Curve Signer, O=SharpCorePDF, C=AT",
            key,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, false));

        return Export(request);
    }

    private static X509Certificate2 Export(
        CertificateRequest request,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var created = request.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddDays(-1d),
            notAfter ?? DateTimeOffset.UtcNow.AddDays(365d));

        // The round trip through PKCS#12 keeps the private key attached on every platform, which
        // CreateSelfSigned alone does not promise.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }
}
