using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of writing a signature: what the revision contains, what the report says, and what the signer
/// refuses to write.
/// </summary>
/// <remarks>
/// Every test goes through the public engine rather than a helper, and most of them then read the result back
/// with the validator: a signature that only its own writer believes is not a signature.
/// </remarks>
public sealed class PdfDocumentSignerTests
{
    private static readonly X509Certificate2 Certificate = SigningFixtures.Certificate;

    /// <summary>
    /// The signature the engine writes verifies against the bytes it was written into, and the document it
    /// signed is the document the validator reads.
    /// </summary>
    [Fact]
    public async Task Writes_a_signature_that_verifies_against_its_own_bytes()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(signed));

        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.True(validation.IsValid);
        Assert.Equal(PdfTrustResult.NotEvaluated, validation.Trust);
        Assert.Empty(validation.Problems);
    }

    /// <summary>
    /// What the call returns is read back from the file it wrote: the field, the entries, the signer, the byte
    /// range and the revision all describe the document the caller now holds.
    /// </summary>
    [Fact]
    public async Task Reports_the_signature_the_options_asked_for()
    {
        using var content = new MemoryStream(SigningFixtures.BuildDocument(), writable: false);
        using var destination = new MemoryStream();

        var info = await new PdfDocumentSigner().SignAsync(
            "source.pdf",
            content,
            destination,
            SigningFixtures.Options(Certificate),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Signature1", info.FieldName);
        Assert.Equal("Adobe.PPKLite", info.Filter);
        Assert.Equal("adbe.pkcs7.detached", info.SubFilter);
        Assert.False(info.IsCades);
        Assert.Equal(PdfHashAlgorithm.Sha256, info.HashAlgorithm);
        Assert.Equal("Testing", info.Reason);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), info.SignedAt);
        Assert.Equal("SharpCorePDF Test Signer", info.SignerName);
        Assert.Contains("CN=SharpCorePDF Test Signer", info.SignerSubject, StringComparison.Ordinal);
        Assert.True(info.CoversWholeDocument);
        Assert.Equal(0L, info.ByteRange.Offset1);
        Assert.Equal(2, info.CoveredRevision);
        Assert.True(info.SignatureLength > 0);
        Assert.False(info.HasTimestamp);
    }

    /// <summary>
    /// The source bytes are copied through untouched and the revision follows them, which is the only way a
    /// signature already in the document survives a second one.
    /// </summary>
    [Fact]
    public async Task Appends_a_revision_and_leaves_the_source_bytes_untouched()
    {
        var document = SigningFixtures.BuildDocument();
        var signed = await SigningFixtures.SignAsync(document, SigningFixtures.Options(Certificate));
        var text = SigningFixtures.Text(signed);

        Assert.True(signed.Length > document.Length);
        Assert.Equal(document, signed[..document.Length]);
        Assert.Equal(2, text.Split("%%EOF").Length - 1);
        Assert.Contains(" /Prev ", text, StringComparison.Ordinal);
        Assert.Contains(" /AcroForm ", text, StringComparison.Ordinal);
        Assert.Contains(" /SigFlags 3", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second signature appends a revision of its own, and the first one still covers exactly the bytes it
    /// signed: intact, and no longer the whole file.
    /// </summary>
    [Fact]
    public async Task Signing_a_second_time_keeps_the_first_signature_valid()
    {
        var once = await SigningFixtures.SignAsync(
            SigningFixtures.BuildDocument(),
            SigningFixtures.Options(Certificate, "Signature1"));

        var twice = await SigningFixtures.SignAsync(
            once,
            SigningFixtures.Options(Certificate, "Signature2"));

        var validations = await SigningFixtures.ValidateAsync(twice);

        Assert.Equal(2, validations.Count);

        var first = validations[0];
        Assert.Equal("Signature1", first.Signer.FieldName);
        Assert.Equal(PdfSignatureIntegrity.Valid, first.Integrity);
        Assert.False(first.Signer.CoversWholeDocument);
        Assert.Equal(2, first.Signer.CoveredRevision);
        Assert.Contains(first.Problems, problem => problem.Kind == PdfSignatureProblemKind.RevisionNotCovered);
        Assert.DoesNotContain(first.Problems, problem => problem.Kind == PdfSignatureProblemKind.DigestMismatch);

        var second = validations[1];
        Assert.Equal("Signature2", second.Signer.FieldName);
        Assert.Equal(PdfSignatureIntegrity.Valid, second.Integrity);
        Assert.True(second.Signer.CoversWholeDocument);
        Assert.Equal(3, second.Signer.CoveredRevision);
        Assert.Empty(second.Problems);
    }

    /// <summary>
    /// A visible appearance travels in the signed revision, inside the widget's rectangle, so what a reader
    /// draws is what was signed rather than something added afterwards.
    /// </summary>
    [Fact]
    public async Task Draws_the_appearance_inside_the_signed_revision()
    {
        var options = SigningFixtures.Options(Certificate) with
        {
            Appearance = new PdfSignatureAppearance
            {
                PageNumber = 1,
                Rectangle = new PdfRectangle(100d, 100d, 260d, 150d),
                Text = "Signed by (the) test",
                BorderWidth = 2d,
            },
        };

        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), options);
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(signed));
        var text = SigningFixtures.Text(signed);

        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Contains("/Subtype /Form", text, StringComparison.Ordinal);
        Assert.Contains("/Rect [100 100 260 150]", text, StringComparison.Ordinal);
        Assert.Contains("(Signed by \\(the\\) test) Tj", text, StringComparison.Ordinal);
        Assert.Contains("/AP << /N ", text, StringComparison.Ordinal);
        Assert.Contains("/Subtype /Widget", text, StringComparison.Ordinal);
        Assert.Contains("/T (Signature1)", text, StringComparison.Ordinal);
    }

    /// <summary>An Invisible signature writes a widget of zero size and no appearance stream.</summary>
    [Fact]
    public async Task Writes_an_invisible_widget_when_no_appearance_is_asked_for()
    {
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), SigningFixtures.Options(Certificate));
        var text = SigningFixtures.Text(signed);

        Assert.Contains("/Rect [0 0 0 0]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Form", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/AP ", text, StringComparison.Ordinal);
    }

    /// <summary>The CAdES sub-filter is written when the caller asks for it, and the signature still verifies.</summary>
    [Fact]
    public async Task Uses_the_cades_sub_filter_when_asked()
    {
        var options = SigningFixtures.Options(Certificate) with { UseCades = true };
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), options);
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(signed));

        Assert.Equal("ETSI.CAdES.detached", validation.Signer.SubFilter);
        Assert.True(validation.Signer.IsCades);
        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
    }

    /// <summary>The digest is the one the options name, all the way through the CMS and back.</summary>
    [Theory]
    [InlineData(PdfHashAlgorithm.Sha256)]
    [InlineData(PdfHashAlgorithm.Sha384)]
    [InlineData(PdfHashAlgorithm.Sha512)]
    public async Task Signs_with_the_digest_the_options_name(PdfHashAlgorithm algorithm)
    {
        var options = SigningFixtures.Options(Certificate) with { HashAlgorithm = algorithm };
        var signed = await SigningFixtures.SignAsync(SigningFixtures.BuildDocument(), options);
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(signed));

        Assert.Equal(algorithm, validation.Signer.HashAlgorithm);
        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
    }

    /// <summary>A certificate whose key is elliptic-curve signs and verifies as well as an RSA one.</summary>
    [Fact]
    public async Task Signs_with_an_elliptic_curve_certificate()
    {
        var signed = await SigningFixtures.SignAsync(
            SigningFixtures.BuildDocument(),
            SigningFixtures.Options(SigningFixtures.EcCertificate));

        var validation = Assert.Single(await SigningFixtures.ValidateAsync(signed));

        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Equal("SharpCorePDF Curve Signer", validation.Signer.SignerName);
    }

    /// <summary>
    /// The token the caller's authority returns is embedded as the unsigned attribute RFC 3161 names, and the
    /// report read back from the file says the signature carries a timestamp.
    /// </summary>
    [Fact]
    public async Task Embeds_the_timestamp_token_the_caller_supplies()
    {
        var provider = new StubTimestampProvider();
        var options = SigningFixtures.Options(Certificate) with { TimestampProvider = provider };

        using var content = new MemoryStream(SigningFixtures.BuildDocument(), writable: false);
        using var destination = new MemoryStream();

        var info = await new PdfDocumentSigner().SignAsync(
            "source.pdf",
            content,
            destination,
            options,
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(provider.Requests);

        Assert.Equal(PdfHashAlgorithm.Sha256, request.HashAlgorithm);
        Assert.Equal(32, request.Digest.Length);
        Assert.Contains(request.Digest, value => value != 0);
        Assert.True(info.HasTimestamp);

        var text = SigningFixtures.Text(destination.ToArray());
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(destination.ToArray()));

        Assert.True(validation.Signer.HasTimestamp);
        Assert.Equal(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.Empty(validation.Problems);
        Assert.Contains(StubTimestampProvider.TokenHex, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A certificate without its private key is refused before the source is read, so the destination stays
    /// empty and nothing that looks signed is produced.
    /// </summary>
    [Fact]
    public async Task Refuses_a_certificate_without_a_private_key()
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(Certificate.Export(X509ContentType.Cert));
        using var content = new MemoryStream(SigningFixtures.BuildDocument(), writable: false);
        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new PdfDocumentSigner().SignAsync(
                "source.pdf",
                content,
                destination,
                SigningFixtures.Options(publicOnly),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("private key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, destination.Length);
    }

    /// <summary>
    /// An encrypted document is refused rather than signed: the appended objects would have to be encrypted
    /// with the document's key while <c>/Contents</c> has to stay in the clear.
    /// </summary>
    [Fact]
    public async Task Refuses_an_encrypted_document()
    {
        var document = await File.ReadAllBytesAsync(TestAssets.Encrypted, TestContext.Current.CancellationToken);
        using var content = new MemoryStream(document, writable: false);
        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<PdfUnsupportedFeatureException>(() =>
            new PdfDocumentSigner().SignAsync(
                "encrypted.pdf",
                content,
                destination,
                SigningFixtures.Options(Certificate),
                TestAssets.EncryptedUserPassword,
                TestContext.Current.CancellationToken));

        Assert.Contains("encrypted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, destination.Length);
    }

    /// <summary>A page the document does not have is refused rather than clamped to the last one.</summary>
    [Fact]
    public async Task Refuses_a_page_the_document_does_not_have()
    {
        var options = SigningFixtures.Options(Certificate) with
        {
            Appearance = new PdfSignatureAppearance
            {
                PageNumber = 2,
                Rectangle = new PdfRectangle(10d, 10d, 110d, 40d),
            },
        };

        using var content = new MemoryStream(SigningFixtures.BuildDocument(), writable: false);
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new PdfDocumentSigner().SignAsync(
                "source.pdf",
                content,
                destination,
                options,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>A field name a PDF name cannot hold is refused before the source is read.</summary>
    [Fact]
    public async Task Refuses_a_field_name_a_pdf_name_cannot_hold()
    {
        using var content = new MemoryStream(SigningFixtures.BuildDocument(), writable: false);
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new PdfDocumentSigner().SignAsync(
                "source.pdf",
                content,
                destination,
                SigningFixtures.Options(Certificate, "Signature (1)"),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The cross-reference table of the appended revision is a run of twenty-byte records, which is what a
    /// reader that seeks by entry instead of scanning the file depends on.
    /// </summary>
    [Fact]
    public async Task Writes_cross_reference_entries_as_twenty_byte_records()
    {
        var signed = await SigningFixtures.SignAsync(
            SigningFixtures.BuildDocument(),
            SigningFixtures.Options(Certificate));

        var text = SigningFixtures.Text(signed);
        var start = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + "\nxref\n".Length;
        var end = text.IndexOf("trailer", start, StringComparison.Ordinal);

        Assert.True(start > 0 && end > start, "the revision carries no cross-reference section");

        var entries = text[start..end]
            .Split('\n')
            .Where(line => line.EndsWith(" n ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            // Nineteen characters plus the newline that ends the record: ten of offset, five of generation.
            Assert.Equal(19, entry.Length);
            Assert.Matches(@"^\d{10} \d{5} n $", entry);
        }
    }

    /// <summary>A destination that cannot be written to is refused before the source is read.</summary>
    [Fact]
    public async Task Refuses_a_destination_that_cannot_be_written()
    {
        using var content = new MemoryStream(SigningFixtures.BuildDocument(), writable: false);
        using var destination = new MemoryStream([], writable: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new PdfDocumentSigner().SignAsync(
                "source.pdf",
                content,
                destination,
                SigningFixtures.Options(Certificate),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A timestamp authority that answers from memory: the token is a CMS <c>ContentInfo</c> whose content
    /// type is <c>signedData</c>, which is all a reader needs to call the signature time-stamped. The real
    /// authority, and the authority of its signature over the token, are the caller's problem — the library
    /// never opens a socket and never claims to have checked one.
    /// </summary>
    private sealed class StubTimestampProvider : IPdfTimestampProvider
    {
        /// <summary>The token this authority hands out, in the hex a PDF string carries it in.</summary>
        public const string TokenHex = "301106092A864886F70D010702A00430020500";

        /// <summary>Gets the digests this authority was asked to stamp.</summary>
        public List<(byte[] Digest, PdfHashAlgorithm HashAlgorithm)> Requests { get; } = [];

        /// <inheritdoc/>
        public Task<ReadOnlyMemory<byte>> GetTimestampTokenAsync(
            ReadOnlyMemory<byte> digest,
            PdfHashAlgorithm hashAlgorithm,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((digest.ToArray(), hashAlgorithm));

            return Task.FromResult<ReadOnlyMemory<byte>>(Convert.FromHexString(TokenHex));
        }
    }
}
