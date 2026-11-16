using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Validation;

/// <summary>
/// Writes the documents the independent validators read, and a manifest of what each one has to say.
/// </summary>
/// <remarks>
/// <para>
/// A validator that only reports "no problems" proves the file is well formed, not that it says what was
/// written into it. The manifest is the other half: the text every page carries (for the text round trip),
/// the algorithm, revision, passwords and permissions of every encrypted file (for the encryption matrix),
/// the revisions of every incrementally updated file and the identifier its source started with (for the
/// update invariants), and the signer and expected coverage of every signed file (for pyHanko).
/// </para>
/// <para>
/// Passwords and the signing key are made for the run and written only into the corpus directory, which is
/// an artefact of the run and never committed, so no credential lives in the repository.
/// </para>
/// </remarks>
internal static class CorpusWriter
{
    private const string SourceId = "00112233445566778899AABBCCDDEEFF";

    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The lines of the standard-font document, page by page, in the order they are drawn.</summary>
    private static readonly string[][] StandardText =
    [
        [
            "The quick brown fox jumps over the lazy dog",
            "Façade, café, naïve, résumé, Ångström, Øre",
            "Price: 12,50 € – “quoted” and ‘single’ … done",
        ],
        [
            "Times-Roman 0123456789 (parentheses) and a \\ backslash",
            "Courier: [brackets] {braces} <angles> & % # $",
        ],
    ];

    /// <summary>The lines of the embedded-font document, which the subset of the face covers.</summary>
    private static readonly string[][] EmbeddedText =
    [
        [
            "Veuillez trouver ci-joint le décompte pour l'immeuble 12345.",
            "Référence : é è à ç ô ï ' / |",
            "Total : 1 234,56 EUR",
        ],
    ];

    /// <summary>Writes the corpus.</summary>
    /// <param name="directory">The directory the documents and the manifest are written to.</param>
    /// <returns>0 when the corpus was written.</returns>
    public static async Task<int> WriteAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var services = Probe.Services;
        var manifest = new Dictionary<string, object>(StringComparer.Ordinal) { ["generator"] = "SharpCorePDF.Validation corpus" };

        var standard = StandardDocument();
        var embedded = await EmbeddedDocumentAsync().ConfigureAwait(false);

        await SaveAsync(directory, "text-standard.pdf", standard).ConfigureAwait(false);
        await SaveAsync(directory, "text-embedded.pdf", embedded).ConfigureAwait(false);

        // The merge copies pages it did not write, so the text has to survive the copier as well as the writer.
        var merged = await services.GetRequiredService<IPdfMerger>().MergeToBytesAsync(
            MergePlan.Create()
                .AddSource(PdfSource.Create(new PdfByteArraySource(standard, "text-standard.pdf")))
                .AddSource(PdfSource.Create(new PdfByteArraySource(embedded, "text-embedded.pdf")))
                .WithOutput(output => output with { ModificationDate = Moment }),
            CancellationToken.None).ConfigureAwait(false);

        await SaveAsync(directory, "text-merged.pdf", merged).ConfigureAwait(false);

        manifest["text"] = new object[]
        {
            new { file = "text-standard.pdf", pages = StandardText },
            new { file = "text-embedded.pdf", pages = EmbeddedText },
            new { file = "text-merged.pdf", pages = StandardText.Concat(EmbeddedText).ToArray() },
        };

        manifest["fonts"] = new[] { "text-standard.pdf", "text-embedded.pdf", "text-merged.pdf" };
        manifest["encryption"] = await WriteEncryptedAsync(directory, services, standard).ConfigureAwait(false);
        manifest["incremental"] = await WriteIncrementalAsync(directory, services).ConfigureAwait(false);
        manifest["signatures"] = await WriteSignedAsync(directory, services, standard).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, Json)).ConfigureAwait(false);

        Console.WriteLine($"corpus written to {Path.GetFullPath(directory)}");
        return 0;
    }

    private static byte[] StandardDocument(string? documentId = null)
    {
        var helvetica = PdfTypeface.Standard(StandardFont.Helvetica);
        var times = PdfTypeface.Standard(StandardFont.TimesRoman);
        var courier = PdfTypeface.Standard(StandardFont.Courier);

        return PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with { CreationDate = Moment, DocumentId = documentId ?? SourceId })
            .WithMetadata(new PdfMetadata { Title = "Validation corpus: standard fonts" })
            .AddPage(PdfPageSize.A4, page => page
                .DrawText(StandardText[0][0], 72, 100, helvetica, 14)
                .DrawText(StandardText[0][1], 72, 130, helvetica, 14)
                .DrawText(StandardText[0][2], 72, 160, helvetica, 14))
            .AddPage(PdfPageSize.Letter, page => page
                .DrawText(StandardText[1][0], 72, 100, times, 14)
                .DrawText(StandardText[1][1], 72, 130, courier, 12))
            .ToArray();
    }

    private static async Task<byte[]> EmbeddedDocumentAsync()
    {
        var face = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans-subset.ttf")).ConfigureAwait(false));

        return PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with { CreationDate = Moment, DocumentId = SourceId })
            .WithMetadata(new PdfMetadata { Title = "Validation corpus: embedded subset" })
            .AddPage(PdfPageSize.A4, page => page
                .DrawText(EmbeddedText[0][0], 72, 100, face, 12)
                .DrawText(EmbeddedText[0][1], 72, 130, face, 12)
                .DrawText(EmbeddedText[0][2], 72, 160, face, 12))
            .ToArray();
    }

    /// <summary>Writes one document per algorithm and permission set, plus one that only has an owner password.</summary>
    private static async Task<object[]> WriteEncryptedAsync(string directory, IServiceProvider services, byte[] standard)
    {
        (PdfEncryptionAlgorithm Algorithm, string Name, int Version, int Revision, int Bits)[] algorithms =
        [
            (PdfEncryptionAlgorithm.Rc4With40BitKey, "rc4-40", 1, 2, 40),
            (PdfEncryptionAlgorithm.Rc4With128BitKey, "rc4-128", 2, 3, 128),
            (PdfEncryptionAlgorithm.AesWith128BitKey, "aes-128", 4, 4, 128),
            (PdfEncryptionAlgorithm.AesWith256BitKey, "aes-256", 5, 6, 256),
        ];
        (PdfPermissions Permissions, string Name)[] permissionSets =
        [
            (PdfPermissions.All, "all"),
            (PdfPermissions.PrintOnly, "print-only"),
        ];

        var entries = new List<object>();

        foreach (var (algorithm, name, version, revision, bits) in algorithms)
        {
            foreach (var (permissions, permissionName) in permissionSets)
            {
                var userPassword = MakePassword();
                var ownerPassword = MakePassword();
                var file = $"encrypted-{name}-{permissionName}.pdf";

                await WriteEncryptedAsync(directory, services, standard, file, algorithm, userPassword, ownerPassword, permissions)
                    .ConfigureAwait(false);
                entries.Add(Entry(file, name, version, revision, bits, userPassword, ownerPassword, permissions));
            }
        }

        // The case most producers write: anyone may open the document, and only the owner may change it.
        var owner = MakePassword();

        await WriteEncryptedAsync(directory, services, standard, "encrypted-aes-256-owner-only.pdf", PdfEncryptionAlgorithm.AesWith256BitKey, string.Empty, owner, PdfPermissions.PrintOnly)
            .ConfigureAwait(false);
        entries.Add(Entry("encrypted-aes-256-owner-only.pdf", "aes-256", 5, 6, 256, string.Empty, owner, PdfPermissions.PrintOnly));

        return [.. entries];

        object Entry(string file, string algorithm, int version, int revision, int bits, string user, string ownerPassword, PdfPermissions permissions) => new
        {
            file,
            algorithm,
            version,
            revision,
            keyBits = bits,
            userPassword = user,
            ownerPassword,
            permissions = new
            {
                print = permissions.HasFlag(PdfPermissions.Print),
                modify = permissions.HasFlag(PdfPermissions.ModifyContents),
                copy = permissions.HasFlag(PdfPermissions.CopyContent),
                annotate = permissions.HasFlag(PdfPermissions.Annotate),
            },
            pages = StandardText,
        };
    }

    private static async Task WriteEncryptedAsync(
        string directory,
        IServiceProvider services,
        byte[] standard,
        string file,
        PdfEncryptionAlgorithm algorithm,
        string userPassword,
        string ownerPassword,
        PdfPermissions permissions)
    {
        var bytes = await services.GetRequiredService<IPdfMerger>().MergeToBytesAsync(
            MergePlan.Create()
                .AddSource(PdfSource.Create(new PdfByteArraySource(standard, "text-standard.pdf")))
                .WithOutput(output => output with
                {
                    ModificationDate = Moment,
                    Encryption = new PdfEncryptionOptions
                    {
                        Algorithm = algorithm,
                        UserPassword = userPassword,
                        OwnerPassword = ownerPassword,
                        Permissions = permissions,
                    },
                }),
            CancellationToken.None).ConfigureAwait(false);

        await SaveAsync(directory, file, bytes).ConfigureAwait(false);
    }

    /// <summary>Writes a source and the same document after two appended revisions.</summary>
    private static async Task<object[]> WriteIncrementalAsync(string directory, IServiceProvider services)
    {
        var source = StandardDocument();
        var first = await AppendAsync(services, source, PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)).ConfigureAwait(false);
        var second = await AppendAsync(services, first, PdfEditPlan.Create().RotatePage(2, PageRotation.Clockwise180)).ConfigureAwait(false);

        await SaveAsync(directory, "incremental-source.pdf", source).ConfigureAwait(false);
        await SaveAsync(directory, "incremental.pdf", second).ConfigureAwait(false);

        return
        [
            new
            {
                file = "incremental.pdf",
                source = "incremental-source.pdf",
                revisions = 3,
                sourceId = SourceId,
                rotations = new[] { new[] { 0, 0 }, new[] { 90, 0 }, new[] { 90, 180 } },
            },
        ];
    }

    private static async Task<byte[]> AppendAsync(IServiceProvider services, byte[] source, PdfEditPlan plan) =>
        await services.GetRequiredService<IPdfDocumentEditor>().ApplyToArrayAsync(
            new PdfByteArraySource(source, "source.pdf"),
            plan.WithOutput(options => options with { UseIncrementalUpdate = true, ModificationDate = Moment }),
            password: null,
            CancellationToken.None).ConfigureAwait(false);

    /// <summary>Signs a document once, twice, and once followed by an appended edit.</summary>
    private static async Task<object> WriteSignedAsync(string directory, IServiceProvider services, byte[] standard)
    {
        using var certificate = CreateCertificate();
        await File.WriteAllTextAsync(Path.Combine(directory, "signer.pem"), certificate.ExportCertificatePem()).ConfigureAwait(false);

        var signer = services.GetRequiredService<IPdfSigner>();
        var once = await SignAsync(signer, standard, certificate, "FirstSignature").ConfigureAwait(false);
        var twice = await SignAsync(signer, once, certificate, "SecondSignature").ConfigureAwait(false);
        var edited = await AppendAsync(services, once, PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)).ConfigureAwait(false);

        await SaveAsync(directory, "signed.pdf", once).ConfigureAwait(false);
        await SaveAsync(directory, "signed-twice.pdf", twice).ConfigureAwait(false);
        await SaveAsync(directory, "signed-then-edited.pdf", edited).ConfigureAwait(false);

        return new
        {
            anchor = "signer.pem",
            files = new object[]
            {
                new { file = "signed.pdf", signatures = new[] { new { field = "FirstSignature", coverage = "ENTIRE_FILE" } } },
                new
                {
                    file = "signed-twice.pdf",
                    signatures = new[]
                    {
                        new { field = "FirstSignature", coverage = "ENTIRE_REVISION" },
                        new { field = "SecondSignature", coverage = "ENTIRE_FILE" },
                    },
                },
                new { file = "signed-then-edited.pdf", signatures = new[] { new { field = "FirstSignature", coverage = "ENTIRE_REVISION" } } },
            },
        };
    }

    private static async Task<byte[]> SignAsync(IPdfSigner signer, byte[] document, X509Certificate2 certificate, string field)
    {
        using var destination = new MemoryStream();

        await signer.SignAsync(
            new PdfByteArraySource(document, "source.pdf"),
            destination,
            new PdfSignatureOptions
            {
                Certificate = certificate,
                FieldName = field,
                Reason = "Validation corpus",
                SigningTime = Moment,
            },
            password: null,
            CancellationToken.None).ConfigureAwait(false);

        return destination.ToArray();
    }

    /// <summary>A self-signed certificate that lives for the run, the shape the signing example uses.</summary>
    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=SharpCorePDF Validation Signer, O=SharpCorePDF",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyCertSign, false));

        using var created = request.CreateSelfSigned(Moment.AddDays(-1), Moment.AddYears(10));

        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private static string MakePassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private static Task SaveAsync(string directory, string file, byte[] bytes) =>
        File.WriteAllBytesAsync(Path.Combine(directory, file), bytes);
}
