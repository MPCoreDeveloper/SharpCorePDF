using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of reading signatures back: what a signature states, whether the bytes it covers are intact, and
/// what trust evaluation does when the caller supplies anchors.
/// </summary>
/// <remarks>
/// Integrity and trust are asserted separately in every test that touches both, because the contract keeps
/// them independent: a document can be tampered with and trusted, or intact and signed by nobody the caller
/// knows.
/// </remarks>
public sealed class PdfDocumentSignatureValidatorTests
{
    /// <summary>Gets the certificate the tests sign with, which is also the anchor they trust.</summary>
    private static X509Certificate2 Certificate => SigningFixtures.Certificate;

    /// <summary>A document with no signature field at all yields no validation, not a failure.</summary>
    [Fact]
    public async Task Reports_nothing_for_a_document_that_is_not_signed()
    {
        Assert.Empty(await SigningFixtures.ValidateAsync(SigningFixtures.BuildDocument()));
    }

    /// <summary>The checked-in unsigned fixture carries no signature either.</summary>
    [Fact]
    public async Task Reports_nothing_for_an_unsigned_fixture()
    {
        var document = await File.ReadAllBytesAsync(
            SigningFixtures.Asset("parity", "signatures", "Unsigned.pdf"),
            TestContext.Current.CancellationToken);

        Assert.Empty(await SigningFixtures.ValidateAsync(document));
    }

    /// <summary>
    /// A placeholder signature — a byte range of zeroes over a gap of zeroes — is reported as unreadable rather
    /// than as valid, which is the one answer that would be wrong.
    /// </summary>
    [Fact]
    public async Task Reports_a_placeholder_signature_as_unreadable()
    {
        var document = await File.ReadAllBytesAsync(
            SigningFixtures.Asset("parity", "signatures", "SignedPlaceholder.pdf"),
            TestContext.Current.CancellationToken);
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(document));

        Assert.Equal("Signature1", validation.Signer.FieldName);
        Assert.Equal(PdfSignatureIntegrity.Unknown, validation.Integrity);
        Assert.False(validation.IsValid);
        Assert.False(validation.Signer.CoversWholeDocument);
        Assert.Contains(validation.Problems, problem => problem.Kind == PdfSignatureProblemKind.MalformedByteRange);
        Assert.Equal("Placeholder for the Phase 4 signing tests", validation.Signer.Reason);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), validation.Signer.SignedAt);
    }

    /// <summary>
    /// A byte changed after signing yields an invalid digest, and the problem names the byte range that no
    /// longer matches.
    /// </summary>
    [Fact]
    public async Task Reports_a_changed_byte_as_a_digest_mismatch()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var tampered = Tamper(signed);
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(tampered));

        Assert.Equal(PdfSignatureIntegrity.Invalid, validation.Integrity);
        Assert.False(validation.IsValid);

        var problem = Assert.Single(validation.Problems);
        Assert.Equal(PdfSignatureProblemKind.DigestMismatch, problem.Kind);
        Assert.Contains(validation.Signer.ByteRange.ToString(), problem.Message, StringComparison.Ordinal);
    }

    /// <summary>A chain that ends in the anchor the caller named is trusted, and integrity is unaffected.</summary>
    [Fact]
    public async Task Trusts_a_chain_that_ends_in_a_named_anchor()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(
            signed,
            new PdfTrustOptions { ExtraRoots = [Certificate] }));

        Assert.Equal(PdfTrustResult.Trusted, validation.Trust);
        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Empty(validation.Problems);
    }

    /// <summary>A chain that ends in a root the caller did not name is reported as untrusted.</summary>
    [Fact]
    public async Task Reports_an_unnamed_root_as_untrusted()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(
            signed,
            new PdfTrustOptions { ExtraRoots = [SigningFixtures.OtherCertificate] }));

        Assert.Equal(PdfTrustResult.Untrusted, validation.Trust);
        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Contains(validation.Problems, problem => problem.Kind == PdfSignatureProblemKind.ChainUntrusted);
    }

    /// <summary>Naming no anchor at all still evaluates the chain, against nothing.</summary>
    [Fact]
    public async Task Reports_no_anchor_as_untrusted()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(signed, new PdfTrustOptions()));

        Assert.Equal(PdfTrustResult.Untrusted, validation.Trust);
        Assert.Contains(
            validation.Problems,
            problem => problem.Kind is PdfSignatureProblemKind.ChainIncomplete or PdfSignatureProblemKind.ChainUntrusted);
    }

    /// <summary>Asking for an online revocation check reports the boundary and makes no request.</summary>
    [Fact]
    public async Task Reports_a_request_for_an_online_check_without_making_one()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(
            signed,
            new PdfTrustOptions { ExtraRoots = [Certificate], AllowOnlineRevocationCheck = true }));

        Assert.Equal(PdfTrustResult.Trusted, validation.Trust);
        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Contains(validation.Problems, problem => problem.Kind == PdfSignatureProblemKind.OnlineRevocationUnsupported);
    }

    /// <summary>
    /// Trust and integrity answer different questions: a document changed after signing is still reported as
    /// signed by a certificate the caller trusts.
    /// </summary>
    [Fact]
    public async Task Keeps_integrity_independent_of_trust()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(
            Tamper(signed),
            new PdfTrustOptions { ExtraRoots = [Certificate] }));

        Assert.Equal(PdfSignatureIntegrity.Invalid, validation.Integrity);
        Assert.Equal(PdfTrustResult.Trusted, validation.Trust);
    }

    /// <summary>
    /// A certificate that has expired is reported as such while the bytes it signed are still intact: the
    /// period is a separate fact from the digest.
    /// </summary>
    [Fact]
    public async Task Reports_an_expired_certificate_but_keeps_the_digest_check()
    {
        var signed = await SigningFixtures.SignAsync(
            SigningFixtures.BuildDocument(),
            SigningFixtures.Options(SigningFixtures.ExpiredCertificate));

        var validation = Assert.Single(await SigningFixtures.ValidateAsync(signed));

        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Contains(validation.Problems, problem => problem.Kind == PdfSignatureProblemKind.CertificateExpired);
    }

    /// <summary>Changes one byte of the page box, which is inside the range the signature covers.</summary>
    /// <param name="document">The signed document.</param>
    /// <returns>The changed document.</returns>
    private static byte[] Tamper(byte[] document)
    {
        var index = SigningFixtures.Text(document).IndexOf("/MediaBox [0 0 200 200]", StringComparison.Ordinal);

        if (index < 0)
        {
            throw new InvalidOperationException("The fixture no longer carries the page box this test changes.");
        }

        var tampered = (byte[])document.Clone();
        tampered[index + "/MediaBox [0 0 ".Length] = (byte)'1';

        return tampered;
    }
}
