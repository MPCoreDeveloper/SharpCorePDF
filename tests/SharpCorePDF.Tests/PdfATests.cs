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

    /// <summary>The offset of the profile identifier in an ICC header.</summary>
    private const int ProfileIdOffset = 84;

    /// <summary>The number of bytes the profile identifier occupies.</summary>
    private const int ProfileIdLength = 16;

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
    /// <param name="options">The options, or <see langword="null"/> for the test's own.</param>
    /// <returns>The result and the bytes of the destination.</returns>
    private static async Task<(PdfAConversionResult Result, byte[] Output)> ConvertAsync(
        byte[] source,
        PdfAProfile profile,
        string? password = null,
        PdfAConversionOptions? options = null)
    {
        using var destination = new MemoryStream();

        var result = await new PdfAWriter()
            .ConvertAsync(
                "source.pdf",
                new MemoryStream(source),
                destination,
                profile,
                options ?? Options(),
                password,
                Token);

        return (result, destination.ToArray());
    }

    private static PdfFileReader Open(byte[] bytes, string? password = null) =>
        new(bytes, "converted.pdf", PdfEngineOptions.Default, password);

    /// <summary>Writes the version, the XMP packet and the output intent of every profile.</summary>
    /// <param name="profile">The profile to write for.</param>
    /// <param name="version">The version the file header states.</param>
    /// <param name="part">The part of ISO 19005 the packet names.</param>
    /// <param name="conformance">The conformance level the packet names.</param>
    [Theory]
    [InlineData(PdfAProfile.PdfA1B, "1.4", "1", "B")]
    [InlineData(PdfAProfile.PdfA2B, "1.7", "2", "B")]
    [InlineData(PdfAProfile.PdfA2U, "1.7", "2", "U")]
    [InlineData(PdfAProfile.PdfA3B, "1.7", "3", "B")]
    [InlineData(PdfAProfile.PdfA3U, "1.7", "3", "U")]
    public async Task Writes_the_structures_the_profile_requires(
        PdfAProfile profile,
        string version,
        string part,
        string conformance)
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
        Assert.Contains($"<pdfaid:conformance>{conformance}</pdfaid:conformance>", xmp);
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
        Assert.Equal("OutputIntent", intent.GetName("Type"));
        Assert.Equal("sRGB IEC61966-2.1", intent.GetText("OutputConditionIdentifier"));
        Assert.Equal("https://www.color.org", intent.GetText("RegistryName"));

        var icc = Assert.IsType<PdfStream>(document.Resolve(intent.Get("DestOutputProfile")));
        Assert.Equal(3, icc.Dictionary.GetInteger("N"));
        Assert.Equal("acsp", Encoding.ASCII.GetString(icc.Raw.Span.Slice(36, 4)));

        var metadata = Assert.IsType<PdfStream>(document.Resolve(document.Catalog.Get("Metadata")));

        Assert.Equal("Metadata", metadata.Dictionary.GetName("Type"));
        Assert.Equal("XML", metadata.Dictionary.GetName("Subtype"));

        AssertEveryFontIsEmbedded(document);
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PdfAProfileWritten);
    }

    /// <summary>Writes the output intent the caller supplied, in place of the built-in sRGB one.</summary>
    /// <remarks>
    /// The bytes are a real ICC profile — the sRGB program the library builds with its profile identifier
    /// overwritten — so they are valid, and they are not the bytes the default intent writes. That
    /// distinction is the point: a caller's profile that happened to be byte-identical to the built-in one
    /// would make the test pass against a writer that ignored the caller entirely.
    /// </remarks>
    [Fact]
    public async Task Writes_the_callers_output_intent_instead_of_the_built_in_one()
    {
        var callerProfile = SRgbColorProfile.Bytes.ToArray();

        callerProfile.AsSpan(ProfileIdOffset, ProfileIdLength).Fill(0xAB);

        var intent = new PdfOutputIntent
        {
            IccProfile = callerProfile,
            NumberOfComponents = 3,
            OutputConditionIdentifier = "SharpCorePDF test condition",
            OutputCondition = "SharpCorePDF test condition, described",
            Info = "SharpCorePDF test intent",
        };

        var (result, output) = await ConvertAsync(
            Authored(),
            PdfAProfile.PdfA2B,
            options: Options() with { OutputIntent = intent });

        Assert.True(result.IsWritten, string.Join(" | ", result.UnsatisfiedRequirements));

        var document = Open(output);
        var intents = document.ResolveArray(document.Catalog!.Get("OutputIntents"));
        var written = document.ResolveDictionary(Assert.Single(intents!.Items));

        Assert.NotNull(written);
        Assert.Equal("GTS_PDFA1", written!.GetName("S"));
        Assert.Equal("SharpCorePDF test condition", written.GetText("OutputConditionIdentifier"));
        Assert.Equal("SharpCorePDF test condition, described", written.GetText("OutputCondition"));
        Assert.Equal("SharpCorePDF test intent", written.GetText("Info"));
        Assert.Null(written.Get("RegistryName"));

        var icc = Assert.IsType<PdfStream>(document.Resolve(written.Get("DestOutputProfile")));

        Assert.Equal(3, icc.Dictionary.GetInteger("N"));
        Assert.Equal(callerProfile, icc.Raw.ToArray());
    }

    /// <summary>Writes the registry the caller names, and the component count its profile states.</summary>
    /// <remarks>
    /// The header here is synthetic — a Gray profile with no colour conversion data — because what the test
    /// pins is that the writer takes the count and the registry from the intent instead of substituting its
    /// own sRGB answers. Whether such a profile is a good idea for a page painted in device RGB is the
    /// validator's question, and Chapter 24 of the manual says so.
    /// </remarks>
    [Fact]
    public async Task Writes_the_registry_and_the_component_count_the_caller_states()
    {
        var intent = new PdfOutputIntent
        {
            IccProfile = IccHeader("GRAY"),
            NumberOfComponents = 1,
            OutputConditionIdentifier = "gray condition",
            RegistryName = "https://example.invalid/conditions",
        };

        var (result, output) = await ConvertAsync(
            Authored(),
            PdfAProfile.PdfA2B,
            options: Options() with { OutputIntent = intent });

        Assert.True(result.IsWritten, string.Join(" | ", result.UnsatisfiedRequirements));

        var document = Open(output);
        var intents = document.ResolveArray(document.Catalog!.Get("OutputIntents"));
        var written = document.ResolveDictionary(Assert.Single(intents!.Items));

        Assert.NotNull(written);
        Assert.Equal("gray condition", written!.GetText("OutputConditionIdentifier"));
        Assert.Equal("https://example.invalid/conditions", written.GetText("RegistryName"));

        var icc = Assert.IsType<PdfStream>(document.Resolve(written.Get("DestOutputProfile")));

        Assert.Equal(1, icc.Dictionary.GetInteger("N"));
    }

    /// <summary>Refuses data that is not an ICC profile before a byte of the result is written.</summary>
    [Fact]
    public async Task Refuses_an_output_intent_that_is_not_an_icc_profile()
    {
        var intent = new PdfOutputIntent
        {
            IccProfile = new byte[200],
            NumberOfComponents = 3,
            OutputConditionIdentifier = "not a profile",
        };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => ConvertAsync(
            Authored(),
            PdfAProfile.PdfA2B,
            options: Options() with { OutputIntent = intent }));

        Assert.Contains("acsp", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Refuses a component count the profile itself contradicts.</summary>
    [Fact]
    public async Task Refuses_a_component_count_the_profile_does_not_state()
    {
        var intent = new PdfOutputIntent
        {
            IccProfile = SRgbColorProfile.Bytes,
            NumberOfComponents = 4,
            OutputConditionIdentifier = "mismatched",
        };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => ConvertAsync(
            Authored(),
            PdfAProfile.PdfA2B,
            options: Options() with { OutputIntent = intent }));

        Assert.Contains("declares 4", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An output intent belongs to an archival profile, so an ordinary edit refuses one.</summary>
    [Fact]
    public void Refuses_an_output_intent_on_an_edit_that_is_not_archival()
    {
        var errors = (PdfEditOutputOptions.Default with
        {
            OutputIntent = new PdfOutputIntent
            {
                IccProfile = SRgbColorProfile.Bytes,
                NumberOfComponents = 3,
                OutputConditionIdentifier = "condition",
            },
        }).Validate();

        Assert.Contains(
            errors,
            error => error.Contains("only for a PDF/A profile", StringComparison.Ordinal));
    }

    /// <summary>Builds the 132 bytes of an ICC header and its empty tag table.</summary>
    /// <param name="colourSpace">The four-character data colour space, for example <c>RGB </c>.</param>
    /// <param name="declaredSize">The length the header states, or <see langword="null"/> for its own.</param>
    /// <param name="signature">The four-character profile signature; <c>acsp</c> makes it a profile.</param>
    /// <returns>The bytes.</returns>
    private static byte[] IccHeader(
        string colourSpace = "RGB ",
        int? declaredSize = null,
        string signature = "acsp")
    {
        var bytes = new byte[132];
        var size = declaredSize ?? bytes.Length;

        bytes[0] = (byte)(size >> 24);
        bytes[1] = (byte)(size >> 16);
        bytes[2] = (byte)(size >> 8);
        bytes[3] = (byte)size;

        Encoding.ASCII.GetBytes(colourSpace).CopyTo(bytes, 16);
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 36);

        return bytes;
    }

    /// <summary>Reads a Gray profile as one component, and refuses it when the intent says three.</summary>
    [Fact]
    public void Reads_a_gray_profile_as_one_component()
    {
        var gray = new PdfOutputIntent
        {
            IccProfile = IccHeader("GRAY"),
            NumberOfComponents = 1,
            OutputConditionIdentifier = "gray condition",
        };

        Assert.Empty(gray.Validate());

        Assert.Contains(
            (gray with { NumberOfComponents = 3 }).Validate(),
            error => error.Contains("'GRAY'", StringComparison.Ordinal));
    }

    /// <summary>Reads a CMYK profile as four components, and refuses it when the intent says three.</summary>
    [Fact]
    public void Reads_a_cmyk_profile_as_four_components()
    {
        var cmyk = new PdfOutputIntent
        {
            IccProfile = IccHeader("CMYK"),
            NumberOfComponents = 4,
            OutputConditionIdentifier = "cmyk condition",
        };

        Assert.Empty(cmyk.Validate());

        Assert.Contains(
            (cmyk with { NumberOfComponents = 3 }).Validate(),
            error => error.Contains("'CMYK'", StringComparison.Ordinal));
    }

    /// <summary>Reports the length the profile states, in the number the header carries.</summary>
    [Fact]
    public void Reports_the_length_a_profile_states()
    {
        var intent = new PdfOutputIntent
        {
            IccProfile = IccHeader(declaredSize: 0x01020304),
            NumberOfComponents = 3,
            OutputConditionIdentifier = "overstated",
        };

        Assert.Contains(
            intent.Validate(),
            error => error.Contains("16909060", StringComparison.Ordinal)
                && error.Contains("carries 132", StringComparison.Ordinal));
    }

    /// <summary>A colour space that is none of the three is not a mismatch by itself.</summary>
    [Fact]
    public void Accepts_a_profile_whose_colour_space_is_not_one_of_the_three()
    {
        var intent = new PdfOutputIntent
        {
            IccProfile = IccHeader("Lab "),
            NumberOfComponents = 3,
            OutputConditionIdentifier = "lab",
        };

        Assert.Empty(intent.Validate());
    }

    /// <summary>Names an empty identifier, an impossible component count and a truncated profile.</summary>
    [Fact]
    public void Refuses_an_empty_identifier_and_an_impossible_component_count()
    {
        var intent = new PdfOutputIntent
        {
            IccProfile = IccHeader(),
            NumberOfComponents = 2,
            OutputConditionIdentifier = " ",
        };

        var errors = intent.Validate();

        Assert.Contains(
            errors,
            error => error.Contains("output condition identifier", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("but is 2", StringComparison.Ordinal));

        var truncated = intent with { IccProfile = new byte[100] };

        Assert.Contains(
            truncated.Validate(),
            error => error.Contains("shorter than the 132", StringComparison.Ordinal));
    }

    /// <summary>Refuses a document identifier that has the right length but is not hexadecimal.</summary>
    [Fact]
    public async Task Refuses_a_document_identifier_that_is_not_hexadecimal()
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => ConvertAsync(
            Authored(),
            PdfAProfile.PdfA2B,
            options: Options() with { DocumentId = "ZZ112233445566778899AABBCCDDEEFF" }));

        Assert.Contains("32 hexadecimal digits", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Refuses a profile number the engine does not define, and accepts PDF 2.0 as a version.</summary>
    [Fact]
    public void Refuses_an_edit_plan_with_an_undefined_profile_and_accepts_pdf_2_0()
    {
        var undefined = (PdfEditOutputOptions.Default with { PdfAProfile = (PdfAProfile)99 }).Validate();

        Assert.Contains(undefined, error => error.Contains("is not defined", StringComparison.Ordinal));
        Assert.Empty((PdfEditOutputOptions.Default with { TargetVersion = PdfVersion.V2_0 }).Validate());
    }

    /// <summary>Refuses a producer that is set but blank, in both option types.</summary>
    [Fact]
    public void Refuses_a_blank_producer()
    {
        Assert.Contains(
            (PdfEditOutputOptions.Default with { Producer = " " }).Validate(),
            error => error.Contains("producer", StringComparison.Ordinal));

        Assert.Contains(
            (Options() with { Producer = " " }).Validate(),
            error => error.Contains("producer", StringComparison.Ordinal));
    }

    /// <summary>Refuses an incremental update that would also encrypt the revision it appends.</summary>
    [Fact]
    public void Refuses_an_incremental_update_with_encryption()
    {
        var errors = (PdfEditOutputOptions.Default with
        {
            UseIncrementalUpdate = true,
            Encryption = PdfEncryptionOptions.Default,
        }).Validate();

        Assert.Contains(errors, error => error.Contains("incremental update", StringComparison.Ordinal));
    }

    /// <summary>Names the remaining refusals of an edit plan: blank identifier, archival update and cipher.</summary>
    [Fact]
    public void Refuses_a_blank_identifier_an_archival_incremental_update_and_a_cipher()
    {
        Assert.Contains(
            (PdfEditOutputOptions.Default with { DocumentId = string.Empty }).Validate(),
            error => error.Contains("document identifier", StringComparison.Ordinal));

        Assert.Contains(
            (PdfEditOutputOptions.Default with
            {
                PdfAProfile = PdfAProfile.PdfA2B,
                UseIncrementalUpdate = true,
            }).Validate(),
            error => error.Contains("cannot be written for a PDF/A profile", StringComparison.Ordinal));

        // AES-256 needs a version the crypt filter was defined for, so a PDF 1.4 result cannot carry it:
        // the cipher is validated against the version the caller asked for.
        Assert.NotEmpty(
            (PdfEditOutputOptions.Default with
            {
                TargetVersion = PdfVersion.V1_4,
                Encryption = PdfEncryptionOptions.Default with
                {
                    Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,
                },
            }).Validate());
    }

    /// <summary>
    /// Associates every embedded file for PDF/A-3b, keeps a relationship the source stated, and names the same
    /// objects in the tree and in the association.
    /// </summary>
    [Fact]
    public async Task Associates_embedded_files_for_pdf_a_3b()
    {
        var (result, output) = await ConvertAsync(
            PdfContentFixtures.WithAssociatedFiles(),
            PdfAProfile.PdfA3B,
            options: Options() with { AssociatedFileRelationship = PdfAssociatedFileRelationship.Alternative });

        Assert.True(result.IsWritten, string.Join(" | ", result.UnsatisfiedRequirements));

        var document = Open(output);
        var associated = document.ResolveArray(document.Catalog!.Get("AF"));

        Assert.NotNull(associated);
        Assert.Equal(2, associated!.Items.Count);

        var relationships = new List<string?>();
        var associatedNumbers = new List<long>();

        foreach (var item in associated.Items)
        {
            var reference = Assert.IsType<PdfReference>(item);
            var specification = document.ResolveDictionary(reference);

            Assert.NotNull(specification);
            relationships.Add(specification!.GetName("AFRelationship"));
            associatedNumbers.Add(reference.ObjectNumber);
        }

        // The file that stated /Source keeps it; the file that stated nothing takes the caller's answer.
        Assert.Equal("Source", relationships[0]);
        Assert.Equal("Alternative", relationships[1]);

        // The association and the name tree have to name the same objects: a reader that follows one must
        // not see a different file specification than a reader that follows the other.
        var names = document.ResolveDictionary(document.Catalog.Get("Names"));
        var embedded = document.ResolveDictionary(names?.Get("EmbeddedFiles"));
        var pairs = document.ResolveArray(embedded?.Get("Names"));

        Assert.NotNull(pairs);
        Assert.Equal(4, pairs!.Items.Count);

        var treeNumbers = new List<long>();

        for (var index = 1; index < pairs.Items.Count; index += 2)
        {
            treeNumbers.Add(Assert.IsType<PdfReference>(pairs.Items[index]).ObjectNumber);
        }

        Assert.Equal(associatedNumbers, treeNumbers);
    }

    /// <summary>An associated-file relationship belongs to PDF/A-3b, so another profile refuses it.</summary>
    [Fact]
    public async Task Refuses_an_associated_file_relationship_outside_pdf_a_3()
    {
        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => ConvertAsync(
            Authored(),
            PdfAProfile.PdfA2B,
            options: Options() with { AssociatedFileRelationship = PdfAssociatedFileRelationship.Data }));
    }

    /// <summary>Associates an embedded file that hangs under a child node and is written as a direct dictionary.</summary>
    [Fact]
    public async Task Associates_a_nested_embedded_file_that_is_a_direct_dictionary()
    {
        var (result, output) = await ConvertAsync(
            PdfContentFixtures.WithNestedAssociatedFiles(),
            PdfAProfile.PdfA3B,
            options: Options() with { AssociatedFileRelationship = PdfAssociatedFileRelationship.Data });

        Assert.True(result.IsWritten, string.Join(" | ", result.UnsatisfiedRequirements));

        var document = Open(output);
        var associated = document.ResolveArray(document.Catalog!.Get("AF"));

        Assert.NotNull(associated);

        var reference = Assert.IsType<PdfReference>(Assert.Single(associated!.Items));
        var specification = document.ResolveDictionary(reference);

        Assert.NotNull(specification);
        Assert.Equal("Data", specification!.GetName("AFRelationship"));
        Assert.Equal("direct.txt", specification.GetText("F"));

        // The tree was flattened, and it names the same object the association does.
        var names = document.ResolveDictionary(document.Catalog.Get("Names"));
        var embedded = document.ResolveDictionary(names?.Get("EmbeddedFiles"));
        var pairs = document.ResolveArray(embedded?.Get("Names"));

        Assert.NotNull(pairs);
        Assert.Equal(2, pairs!.Items.Count);
        Assert.Equal(reference.ObjectNumber, Assert.IsType<PdfReference>(pairs.Items[1]).ObjectNumber);
    }

    /// <summary>Pins the name, part, level and relationship each profile and relationship maps to.</summary>
    [Fact]
    public void Names_every_profile_and_every_relationship()
    {
        Assert.Equal("PDF/A-1b", PdfAProfileRules.Name(PdfAProfile.PdfA1B));
        Assert.Equal("PDF/A-2b", PdfAProfileRules.Name(PdfAProfile.PdfA2B));
        Assert.Equal("PDF/A-3b", PdfAProfileRules.Name(PdfAProfile.PdfA3B));

        Assert.Equal(1, PdfAProfileRules.Part(PdfAProfile.PdfA1B));
        Assert.Equal(2, PdfAProfileRules.Part(PdfAProfile.PdfA2B));
        Assert.Equal(3, PdfAProfileRules.Part(PdfAProfile.PdfA3B));

        Assert.Equal("B", PdfAProfileRules.Conformance(PdfAProfile.PdfA1B));
        Assert.Equal("B", PdfAProfileRules.Conformance(PdfAProfile.PdfA2B));
        Assert.Equal("B", PdfAProfileRules.Conformance(PdfAProfile.PdfA3B));

        Assert.Equal(PdfVersion.V1_4, PdfAProfileRules.Version(PdfAProfile.PdfA1B));
        Assert.Equal(PdfVersion.V1_7, PdfAProfileRules.Version(PdfAProfile.PdfA2B));
        Assert.Equal(PdfVersion.V1_7, PdfAProfileRules.Version(PdfAProfile.PdfA3B));

        Assert.Equal("Data", PdfAProfileRules.RelationshipName(PdfAssociatedFileRelationship.Data));
        Assert.Equal("Source", PdfAProfileRules.RelationshipName(PdfAssociatedFileRelationship.Source));
        Assert.Equal(
            "Alternative",
            PdfAProfileRules.RelationshipName(PdfAssociatedFileRelationship.Alternative));
        Assert.Equal(
            "Supplement",
            PdfAProfileRules.RelationshipName(PdfAssociatedFileRelationship.Supplement));
        Assert.Equal(
            "Unspecified",
            PdfAProfileRules.RelationshipName(PdfAssociatedFileRelationship.Unspecified));

        Assert.True(PdfAProfileRules.ForbidsTransparency(PdfAProfile.PdfA1B));
        Assert.False(PdfAProfileRules.ForbidsTransparency(PdfAProfile.PdfA2B));
        Assert.True(PdfAProfileRules.AllowsEmbeddedFiles(PdfAProfile.PdfA3B));
        Assert.False(PdfAProfileRules.AllowsEmbeddedFiles(PdfAProfile.PdfA2B));

        Assert.Equal("PDF/A-2u", PdfAProfileRules.Name(PdfAProfile.PdfA2U));
        Assert.Equal("PDF/A-3u", PdfAProfileRules.Name(PdfAProfile.PdfA3U));
        Assert.Equal(2, PdfAProfileRules.Part(PdfAProfile.PdfA2U));
        Assert.Equal(3, PdfAProfileRules.Part(PdfAProfile.PdfA3U));
        Assert.Equal("U", PdfAProfileRules.Conformance(PdfAProfile.PdfA2U));
        Assert.Equal("U", PdfAProfileRules.Conformance(PdfAProfile.PdfA3U));
        Assert.Equal(PdfVersion.V1_7, PdfAProfileRules.Version(PdfAProfile.PdfA2U));
        Assert.Equal(PdfVersion.V1_7, PdfAProfileRules.Version(PdfAProfile.PdfA3U));
        Assert.False(PdfAProfileRules.ForbidsTransparency(PdfAProfile.PdfA2U));
        Assert.True(PdfAProfileRules.AllowsEmbeddedFiles(PdfAProfile.PdfA3U));
        Assert.False(PdfAProfileRules.AllowsEmbeddedFiles(PdfAProfile.PdfA2U));
    }

    /// <summary>PDF/A-3u allows embedded files, so it associates them exactly as PDF/A-3b does.</summary>
    [Fact]
    public async Task Associates_embedded_files_for_pdf_a_3u()
    {
        var (result, output) = await ConvertAsync(
            PdfContentFixtures.WithAssociatedFiles(),
            PdfAProfile.PdfA3U,
            options: Options() with { AssociatedFileRelationship = PdfAssociatedFileRelationship.Data });

        Assert.True(result.IsWritten, string.Join(" | ", result.UnsatisfiedRequirements));

        var document = Open(output);
        var associated = document.ResolveArray(document.Catalog!.Get("AF"));

        Assert.NotNull(associated);
        Assert.Equal(2, associated!.Items.Count);
    }

    /// <summary>Refuses a relationship number the enum does not define, in both option types.</summary>
    [Fact]
    public void Refuses_an_undefined_associated_file_relationship()
    {
        var editErrors = (PdfEditOutputOptions.Default with
        {
            PdfAProfile = PdfAProfile.PdfA3B,
            AssociatedFileRelationship = (PdfAssociatedFileRelationship)99,
        }).Validate();

        Assert.Contains(editErrors, error => error.Contains("is not defined", StringComparison.Ordinal));

        var conversionErrors = (Options() with
        {
            AssociatedFileRelationship = (PdfAssociatedFileRelationship)99,
        }).Validate();

        Assert.Contains(
            conversionErrors,
            error => error.Contains("is not defined", StringComparison.Ordinal));
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
