using System.Text;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of archival output: the structures each PDF/A profile requires, the refusals that keep a
/// document from being labelled archival when it is not, and the conflict between a profile and
/// encryption.
/// </summary>
/// <remarks>
/// The assertions read the written bytes back through the same reader the merge and edit engines use, so
/// a result that is only archival in the options it was asked for does not pass. The transparency refusal
/// is exercised on a document the authoring surface wrote, which is what makes it a real page rather
/// than a hand-built dictionary.
/// </remarks>
public sealed class PdfATests
{
    private const string DocumentId = "00112233445566778899AABBCCDDEEFF";

    private static readonly DateTimeOffset Moment = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfTypeface Body => PdfTypeface.FromTrueType(File.ReadAllBytes(TestAssets.EmbeddedFont));

    private static PdfTypeface Heading => PdfTypeface.Standard(StandardFont.Helvetica);

    /// <summary>Builds the document the conversion tests start from.</summary>
    /// <param name="embeddedFont">Whether the page uses a font that carries its program.</param>
    /// <param name="transparency">Whether the page paints an image with an alpha channel.</param>
    /// <returns>The bytes of the authored document.</returns>
    private static byte[] Authored(bool embeddedFont = true, bool transparency = false)
    {
        var face = embeddedFont ? Body : Heading;

        return PdfDocumentBuilder
            .Create(PdfAuthoringOptions.Default with { CreationDate = Moment, DocumentId = DocumentId })
            .WithMetadata(new PdfMetadata
            {
                Title = "Décompte 12345",
                Author = "Décomptes SPRL",
                Creator = "SharpCorePDF tests",
            })
            .AddPage(PdfPageSize.A4, page =>
            {
                page.DrawText("Archival master", 60, 760, face, 14);

                if (transparency)
                {
                    page.DrawImage(TransparentLogo(), 60, 660, 60, 30);
                }
            })
            .ToArray();
    }

    /// <summary>Builds a small RGBA image whose left edge is fully transparent.</summary>
    /// <returns>The bytes of the PNG.</returns>
    private static byte[] TransparentLogo() => TestPng.Rgba(
        12,
        6,
        (x, _) => ((byte)200, (byte)30, (byte)30, x < 4 ? (byte)0 : (byte)255));

    private static PdfAConversionOptions Options() => new()
    {
        ModificationDate = Moment,
        DocumentId = DocumentId,
    };

    /// <summary>Converts a document and returns both the result and the bytes that were written.</summary>
    /// <param name="source">The document to convert.</param>
    /// <param name="profile">The profile to write for.</param>
    /// <param name="password">The password, when the source is encrypted.</param>
    /// <returns>The result and the bytes of the destination.</returns>
    private static async Task<(PdfAConversionResult Result, byte[] Output)> ConvertAsync(
        byte[] source,
        PdfAProfile profile,
        string? password = null)
    {
        using var destination = new MemoryStream();

        var result = await new PdfAWriter()
            .ConvertAsync("source.pdf", new MemoryStream(source), destination, profile, Options(), password, Token);

        return (result, destination.ToArray());
    }

    private static PdfFileReader Open(byte[] bytes, string? password = null) =>
        new(bytes, "converted.pdf", PdfEngineOptions.Default, password);

    /// <summary>Writes the version, the XMP packet and the output intent of every profile.</summary>
    /// <param name="profile">The profile to write for.</param>
    /// <param name="version">The version the file header states.</param>
    /// <param name="part">The part of ISO 19005 the packet names.</param>
    [Theory]
    [InlineData(PdfAProfile.PdfA1B, "1.4", "1")]
    [InlineData(PdfAProfile.PdfA2B, "1.7", "2")]
    [InlineData(PdfAProfile.PdfA3B, "1.7", "3")]
    public async Task Writes_the_structures_the_profile_requires(PdfAProfile profile, string version, string part)
    {
        var (result, output) = await ConvertAsync(Authored(), profile);

        Assert.True(result.IsWritten, string.Join(" | ", result.UnsatisfiedRequirements));
        Assert.Empty(result.UnsatisfiedRequirements);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(output.Length, result.BytesWritten);
        Assert.Equal($"%PDF-{version}", Encoding.Latin1.GetString(output, 0, 8));

        var document = Open(output);

        Assert.Null(document.CrossReferenceTable.Trailer.Get("Encrypt"));

        var xmp = TestAssets.ReadMetadata(output);

        Assert.Contains($"<pdfaid:part>{part}</pdfaid:part>", xmp);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", xmp);
        Assert.Contains("Décompte 12345", xmp);
        Assert.Contains("<pdf:Producer>", xmp);

        Assert.NotNull(document.Information);
        Assert.Equal("Décompte 12345", document.Information!.GetText("Title"));

        var intents = document.ResolveArray(document.Catalog!.Get("OutputIntents"));
        Assert.NotNull(intents);
        Assert.Single(intents!.Items);

        var intent = document.ResolveDictionary(intents.Items[0]);
        Assert.NotNull(intent);
        Assert.Equal("GTS_PDFA1", intent!.GetName("S"));
        Assert.Equal("sRGB IEC61966-2.1", intent.GetText("OutputConditionIdentifier"));

        var icc = Assert.IsType<PdfStream>(document.Resolve(intent.Get("DestOutputProfile")));
        Assert.Equal(3, icc.Dictionary.GetInteger("N"));
        Assert.Equal("acsp", Encoding.ASCII.GetString(icc.Raw.Span.Slice(36, 4)));

        AssertEveryFontIsEmbedded(document);
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PdfAProfileWritten);
    }

    /// <summary>Refuses a document whose font carries no program.</summary>
    [Fact]
    public async Task Refuses_a_font_it_cannot_embed()
    {
        var (result, output) = await ConvertAsync(Authored(embeddedFont: false), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Empty(output);
        Assert.Equal(1, result.PageCount);
        Assert.Contains(
            result.UnsatisfiedRequirements,
            requirement => requirement.Contains("Helvetica", StringComparison.Ordinal)
                && requirement.Contains("not embedded", StringComparison.Ordinal));
        Assert.StartsWith($"{PdfAProfile.PdfA2B}: nothing written", result.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A transparency is refused by PDF/A-1b and accepted by PDF/A-2b.</summary>
    [Fact]
    public async Task Refuses_transparency_for_pdf_a_1b_but_writes_it_for_2b()
    {
        var source = Authored(transparency: true);

        var (refused, nothing) = await ConvertAsync(source, PdfAProfile.PdfA1B);

        Assert.False(refused.IsWritten);
        Assert.Empty(nothing);
        Assert.Contains(
            refused.UnsatisfiedRequirements,
            requirement => requirement.Contains("soft mask", StringComparison.Ordinal));

        var (written, output) = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.True(written.IsWritten, string.Join(" | ", written.UnsatisfiedRequirements));
        Assert.NotEmpty(output);
    }

    /// <summary>Two conversions of the same source with the same identity produce the same bytes.</summary>
    [Fact]
    public async Task Two_conversions_with_the_same_identity_produce_the_same_bytes()
    {
        var source = Authored();

        var (_, first) = await ConvertAsync(source, PdfAProfile.PdfA2B);
        var (_, second) = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.Equal(first, second);
    }

    /// <summary>The use case behind the port converts a source and leaves the destination to the caller.</summary>
    [Fact]
    public async Task Converts_a_content_source_through_the_service()
    {
        using var destination = new MemoryStream();
        var service = new PdfAWriterService(new PdfAWriter());

        var result = await service.ConvertAsync(
            new PdfByteArraySource(Authored(), "source.pdf"),
            destination,
            PdfAProfile.PdfA1B,
            Options(),
            cancellationToken: Token);

        Assert.True(result.IsWritten, string.Join(" | ", result.UnsatisfiedRequirements));
        Assert.True(destination.CanWrite);
        Assert.NotEmpty(destination.ToArray());
    }

    /// <summary>A profile and encryption cannot both be written, and the plan is refused before the read.</summary>
    [Fact]
    public async Task Refuses_a_profile_combined_with_encryption_before_the_source_is_read()
    {
        var options = PdfEditOutputOptions.Default with
        {
            PdfAProfile = PdfAProfile.PdfA2B,
            Encryption = PdfEncryptionOptions.Default with { UserPassword = "secret" },
        };

        Assert.Contains(
            options.Validate(),
            problem => problem.Contains("cannot be combined with encryption", StringComparison.Ordinal));

        var plan = PdfEditPlan.Create(options);

        using var source = new MemoryStream();
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(
            () => new PdfEditEngine().ApplyAsync("source.pdf", source, plan, null, destination, Token));
    }

    /// <summary>An edit can be written encrypted, and the result reads back with its password.</summary>
    [Fact]
    public async Task Writes_an_encrypted_edit_that_reopens_with_its_password()
    {
        var options = PdfEditOutputOptions.Default with
        {
            Encryption = PdfEncryptionOptions.Default with { UserPassword = "secret", OwnerPassword = "owner" },
            ModificationDate = Moment,
            DocumentId = DocumentId,
        };

        var plan = PdfEditPlan.Create(options).RotatePage(1, PageRotation.Clockwise90);

        using var destination = new MemoryStream();

        var result = await new PdfEditEngine(new PdfEngineOptions { VerifyOutput = true })
            .ApplyAsync("authored.pdf", new MemoryStream(Authored()), plan, null, destination, Token);

        Assert.Equal(PdfVersion.V1_7, result.OutputVersion);
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.OutputVerified);

        var written = destination.ToArray();

        Assert.NotNull(Open(written).CrossReferenceTable.Trailer.Get("Encrypt"));
        Assert.Single(Open(written, "secret").Pages);
    }

    /// <summary>Options that cannot describe a document are reported, not applied.</summary>
    [Fact]
    public void Refuses_options_that_cannot_describe_a_result()
    {
        var errors = new PdfAConversionOptions { DocumentId = "1234", Producer = " " }.Validate();

        Assert.Contains(errors, error => error.Contains("32 hexadecimal digits", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("producer", StringComparison.Ordinal));
        Assert.Empty(PdfAConversionOptions.Default.Validate());
    }

    /// <summary>An undefined profile is refused before the source is opened.</summary>
    [Fact]
    public async Task Refuses_an_undefined_profile_before_opening_the_source()
    {
        using var destination = new MemoryStream();
        var service = new PdfAWriterService(new PdfAWriter());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ConvertAsync(
                new PdfByteArraySource([], "source.pdf"),
                destination,
                (PdfAProfile)99,
                cancellationToken: Token));
    }

    /// <summary>A real-world document converts for every profile; one whose annotation has no appearance is refused.</summary>
    [Fact]
    public async Task Converts_a_real_world_document_and_names_what_stops_another()
    {
        var realWorld = await File.ReadAllBytesAsync(TestAssets.SafeWebCore, Token);

        foreach (var profile in (PdfAProfile[])[PdfAProfile.PdfA1B, PdfAProfile.PdfA2B, PdfAProfile.PdfA3B])
        {
            var (result, output) = await ConvertAsync(realWorld, profile);

            Assert.True(result.IsWritten, $"{profile}: {string.Join(" | ", result.UnsatisfiedRequirements)}");
            Assert.Equal(6, result.PageCount);
            Assert.NotEmpty(output);
            Assert.StartsWith($"%PDF-1.{(profile == PdfAProfile.PdfA1B ? 4 : 7)}", Encoding.Latin1.GetString(output, 0, 8), StringComparison.Ordinal);
        }

        var (refused, nothing) = await ConvertAsync(
            await File.ReadAllBytesAsync(TestAssets.Outlined, Token),
            PdfAProfile.PdfA2B);

        Assert.False(refused.IsWritten);
        Assert.Empty(nothing);
        Assert.Contains(
            refused.UnsatisfiedRequirements,
            requirement => requirement.Contains("appearance stream", StringComparison.Ordinal));
    }

    /// <summary>Confirms that every font of the document carries its program.</summary>
    /// <param name="document">The document to inspect.</param>
    private static void AssertEveryFontIsEmbedded(PdfFileReader document)
    {
        var examined = 0;

        foreach (var page in document.Pages)
        {
            var resources = document.ResolveDictionary(page.Dictionary.Get("Resources"))
                ?? document.ResolveDictionary(page.Inherited.Get("Resources"));
            var fonts = document.ResolveDictionary(resources?.Get("Font"));

            if (fonts is null)
            {
                continue;
            }

            foreach (var entry in fonts.Entries)
            {
                var font = document.ResolveDictionary(entry.Value);
                Assert.NotNull(font);

                var descriptor = document.ResolveDictionary(font!.Get("FontDescriptor"));

                if (descriptor is null
                    && document.ResolveArray(font.Get("DescendantFonts")) is { Items.Count: > 0 } descendants)
                {
                    var descendant = document.ResolveDictionary(descendants.Items[0]);
                    Assert.NotNull(descendant);
                    descriptor = document.ResolveDictionary(descendant!.Get("FontDescriptor"));
                }

                Assert.NotNull(descriptor);
                Assert.True(
                    descriptor!.Get("FontFile") is not null
                        || descriptor.Get("FontFile2") is not null
                        || descriptor.Get("FontFile3") is not null,
                    $"the font '{entry.Key}' of the conversion is not embedded");

                examined++;
            }
        }

        Assert.True(examined > 0, "the document carries no font to examine");
    }
}
