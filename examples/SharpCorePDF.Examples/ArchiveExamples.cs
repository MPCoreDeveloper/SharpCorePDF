using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Writes a document for archival delivery: a PDF/A profile, the structures it requires, and the honest
/// answer when the document cannot be made conformant.
/// </summary>
internal static class ArchiveExamples
{
    /// <summary>Converts an authored page to PDF/A-2b and reports what the writer did.</summary>
    /// <param name="context">The ports to write with and the folder to write to.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task ConvertToPdfAAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // The font travels with the application for the same reason it does in the authoring example: a
        // PDF/A document must carry the program of every font it uses, and a standard font has none.
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans-subset.ttf");
        var body = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync(fontPath, cancellationToken));

        var document = PdfDocumentBuilder
            .Create(PdfAuthoringOptions.Default with
            {
                CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DocumentId = "00112233445566778899AABBCCDDEEFF",
            })
            .WithMetadata(new PdfMetadata
            {
                Title = "Décompte 12345",
                Author = "Décomptes SPRL",
                Creator = "SharpCorePDF examples",
            })
            .AddPage(PdfPageSize.A4, page => page.DrawText(
                "Archival master, 2026",
                60,
                760,
                body,
                14))
            .Build("archive-source.pdf");

        using var destination = new MemoryStream();

        var options = new PdfAConversionOptions
        {
            ModificationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DocumentId = "00112233445566778899AABBCCDDEEFF",
        };

        // The same page is written for each profile. The writer decides the version of the file, adds
        // the XMP packet and the output intent, and refuses rather than mislabels.
        foreach (var profile in (PdfAProfile[])[PdfAProfile.PdfA1B, PdfAProfile.PdfA2B, PdfAProfile.PdfA3B])
        {
            destination.SetLength(0);

            var result = await context.ArchiveWriter.ConvertAsync(
                document,
                destination,
                profile,
                options,
                cancellationToken: cancellationToken);

            Report.Heading(PdfAProfileName(profile));

            if (!result.IsWritten)
            {
                Report.Item("written", false);

                foreach (var requirement in result.UnsatisfiedRequirements)
                {
                    Report.Line(requirement);
                }

                continue;
            }

            var output = context.OutputPath(FileName(profile));
            await File.WriteAllBytesAsync(output, destination.ToArray(), cancellationToken);

            Report.Item("pages", result.PageCount);
            Report.Item("bytes", result.BytesWritten);
            Report.Item("wrote", output);
            Report.Item("unsatisfied", result.UnsatisfiedRequirements.Count);
            Report.Item("remarks", result.Warnings.Count);
        }

        Report.Line("next: python docs/tools/validate-pdfa.py <file> --profile 2b");

        // A document that sets text in a standard font cannot become PDF/A: the profile requires the
        // program of every font, and a standard font has none. The writer refuses it and leaves the
        // destination empty rather than labelling it archival.
        var unembeddable = PdfDocumentBuilder
            .Create(PdfAuthoringOptions.Default with { CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) })
            .AddPage(PdfPageSize.A4, page => page.DrawText(
                "Set in Helvetica, which has no program to embed",
                60,
                760,
                PdfTypeface.Standard(StandardFont.Helvetica),
                12))
            .Build("unembeddable.pdf");

        using var refusedDestination = new MemoryStream();

        var refused = await context.ArchiveWriter.ConvertAsync(
            unembeddable,
            refusedDestination,
            PdfAProfile.PdfA2B,
            cancellationToken: cancellationToken);

        Report.Item("refused", !refused.IsWritten);
        Report.Item("bytes written when refused", refusedDestination.Length);

        foreach (var requirement in refused.UnsatisfiedRequirements)
        {
            Report.Line(requirement);
        }
    }

    /// <summary>Archives a page for PDF/A-3b and signs the archive, so the signature covers the archival bytes.</summary>
    /// <param name="context">The ports to write, archive, sign and validate with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <remarks>
    /// <para>
    /// The order matters: the page is archived first and signed second. A signature is an incremental
    /// revision that leaves every byte before it untouched, so signing last keeps both promises at once —
    /// the file is still the archival file the converter wrote, and the signature covers exactly those
    /// bytes. Converting a signed file would rewrite the bytes the signature names and invalidate it.
    /// </para>
    /// <para>
    /// The certificate is generated when the example runs and is never written to disk, so the repository
    /// ships no key: a checked-in key is either a liability or an expired one. The timestamp token is the
    /// caller's, taken from the <c>SCPDF_TIMESTAMP_TOKEN</c> environment variable when it is set — a DER
    /// <c>TimeStampToken</c> from whatever authority the caller uses — because this library never opens a
    /// socket of its own.
    /// </para>
    /// </remarks>
    public static async Task SignAndArchiveAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans-subset.ttf");
        var body = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync(fontPath, cancellationToken));
        var archived = context.OutputPath("signed-archive-source.pdf");
        var signed = context.OutputPath("signed-archive.pdf");
        var moment = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var document = PdfDocumentBuilder
            .Create(PdfAuthoringOptions.Default with { CreationDate = moment, DocumentId = "00112233445566778899AABBCCDDEEFF" })
            .WithMetadata(new PdfMetadata { Title = "Archival master, signed", Creator = "SharpCorePDF examples" })
            .AddPage(PdfPageSize.A4, page => page.DrawText("Archival master, 2026", 60, 760, body, 14))
            .Build("signed-archive-source.pdf");

        Report.Heading("archive for PDF/A-3b");

        PdfAConversionResult conversion;

        await using (var destination = File.Create(archived))
        {
            conversion = await context.ArchiveWriter.ConvertAsync(
                document,
                destination,
                PdfAProfile.PdfA3B,
                new PdfAConversionOptions
                {
                    ModificationDate = moment,
                    DocumentId = "00112233445566778899AABBCCDDEEFF",
                },
                cancellationToken: cancellationToken);
        }

        Report.Item("written", conversion.IsWritten);
        Report.Item("file", archived);
        Report.Item("bytes", new FileInfo(archived).Length);

        if (!conversion.IsWritten)
        {
            foreach (var requirement in conversion.UnsatisfiedRequirements)
            {
                Report.Line(requirement);
            }

            return;
        }

        // A certificate that lives for the length of this run: CN only, the digital-signature usage, and
        // no key material anywhere but this process.
        using var certificate = CreateCertificate();
        var token = TimestampFromEnvironment();

        Report.Heading("sign the archive");

        PdfSignatureInfo signature;

        await using (var content = File.OpenRead(archived))
        await using (var destination = File.Create(signed))
        {
            signature = await new PdfDocumentSigner().SignAsync(
                Path.GetFileName(archived),
                content,
                destination,
                new PdfSignatureOptions
                {
                    Certificate = certificate,
                    FieldName = "ArchivalSignature",
                    Reason = "Archival delivery",
                    Location = "Vienna",
                    SigningTime = moment,
                    TimestampProvider = token is { } der ? new SuppliedTimestamp(der) : null,
                },
                cancellationToken: cancellationToken);
        }

        Report.Item("field", signature.FieldName);
        Report.Item("signer", signature.SignerSubject);
        Report.Item("algorithm", signature.HashAlgorithm);
        Report.Item("timestamp", signature.HasTimestamp);
        Report.Item("file", signed);
        Report.Item("bytes", new FileInfo(signed).Length);

        Report.Heading("validate what was written");

        // Integrity and trust are two answers, and the certificate is both the signer and the anchor
        // here, which is what a self-signed delivery looks like: the document is intact, and the chain
        // only means something to a reader who was given the certificate out of band.
        IReadOnlyList<PdfSignatureValidation> validations;

        await using (var content = File.OpenRead(signed))
        {
            validations = await new PdfDocumentSignatureValidator().ValidateAsync(
                Path.GetFileName(signed),
                content,
                new PdfTrustOptions { ExtraRoots = [certificate] },
                cancellationToken: cancellationToken);
        }

        foreach (var validation in validations)
        {
            Report.Item("signature", validation.Signer.FieldName);
            Report.Item("integrity", validation.Integrity);
            Report.Item("trust", validation.Trust);
            Report.Item("is valid", validation.IsValid);

            foreach (var problem in validation.Problems)
            {
                Report.Line($"  {problem.Kind}: {problem.Message}");
            }
        }
    }

    /// <summary>Gets the name of a profile, as the manual spells it.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The name, for example <c>PDF/A-2b</c>.</returns>
    private static string PdfAProfileName(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => "PDF/A-1b",
        PdfAProfile.PdfA2B => "PDF/A-2b",
        PdfAProfile.PdfA3B => "PDF/A-3b",
        _ => profile.ToString(),
    };

    /// <summary>Gets the file the example writes a profile to.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The file name.</returns>
    private static string FileName(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => "archive-1b.pdf",
        PdfAProfile.PdfA2B => "archive-2b.pdf",
        PdfAProfile.PdfA3B => "archive-3b.pdf",
        _ => "archive.pdf",
    };

    /// <summary>Creates the self-signed certificate the example signs with.</summary>
    /// <returns>The certificate, with its private key attached.</returns>
    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=SharpCorePDF Example Signer, O=SharpCorePDF, C=AT",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, false));

        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1d),
            DateTimeOffset.UtcNow.AddDays(365d));

        // The round trip through PKCS#12 keeps the private key attached on every platform, which
        // CreateSelfSigned alone does not promise.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>Reads the timestamp token the caller supplied, if any.</summary>
    /// <returns>The DER token, or <see langword="null"/> when the caller has no authority to ask.</returns>
    private static ReadOnlyMemory<byte>? TimestampFromEnvironment()
    {
        var encoded = Environment.GetEnvironmentVariable("SCPDF_TIMESTAMP_TOKEN");

        return string.IsNullOrWhiteSpace(encoded) ? null : Convert.FromBase64String(encoded);
    }

    /// <summary>Answers the signer with a token the caller already holds.</summary>
    /// <param name="token">The DER-encoded <c>TimeStampToken</c>.</param>
    private sealed class SuppliedTimestamp(ReadOnlyMemory<byte> token) : IPdfTimestampProvider
    {
        /// <inheritdoc />
        public Task<ReadOnlyMemory<byte>> GetTimestampTokenAsync(
            ReadOnlyMemory<byte> digest,
            PdfHashAlgorithm hashAlgorithm,
            CancellationToken cancellationToken = default) => Task.FromResult(token);
    }
}
