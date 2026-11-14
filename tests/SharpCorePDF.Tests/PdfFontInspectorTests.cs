using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Reading;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the font inspector: the fonts a document states, where each program lives, which pages select
/// it, and what is said about the ones that cannot be read.
/// </summary>
/// <remarks>
/// The inspector is wired the way the container wires it — the use case in front of the engine — so every
/// assertion also covers the argument checks the surface promises. The fixture is built in memory by
/// <see cref="PdfContentFixtures.Fonts"/>, which lets a test name the font dictionary it expects instead of
/// describing a binary asset, and one test opens a document this repository did not write so the answer is
/// also checked against what `pikepdf` reports for the same file.
/// </remarks>
public sealed class PdfFontInspectorTests
{
    private const string Password = "opens-it";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfFontInspectorService Inspector(PdfEngineOptions? options = null) =>
        new(new PdfFontInspector(options));

    private static PdfByteArraySource Source(byte[] bytes, string name = "fixture.pdf") => new(bytes, name);

    private static async Task<PdfReadResult<PdfFontInfo>> FontsAsync(
        byte[] bytes,
        string? password = null,
        PdfEngineOptions? options = null) =>
        await Inspector(options).ReadFontsAsync(Source(bytes), password, Token);

    private static PdfFontInfo Font(PdfReadResult<PdfFontInfo> result, string baseFont) =>
        Assert.Single(result.Items, font => string.Equals(font.BaseFont, baseFont, StringComparison.Ordinal));

    private static void AssertWarning(IReadOnlyList<PdfWarning> warnings, PdfWarningKind kind) =>
        Assert.Contains(warnings, warning => warning.Kind == kind);

    /// <summary>Writes a document with the library's own encryption, so the fonts are sealed in it.</summary>
    /// <param name="source">The bytes of the document to protect.</param>
    /// <returns>The bytes of the encrypted document.</returns>
    private static async Task<byte[]> EncryptedAsync(byte[] source)
    {
        var plan = MergePlan
            .Create()
            .AddSource(PdfSource.Create(Source(source)))
            .WithOutput(MergeOutputOptions.Default with
            {
                Encryption = new PdfEncryptionOptions
                {
                    Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,
                    UserPassword = Password,
                },
            });

        return await new PdfMergerService(new PdfDocumentReader(), new PdfMergeEngine())
            .MergeToBytesAsync(plan, Token);
    }

    /// <summary>
    /// Every font the pages select is reported once, in the order the pages select them: the fonts of a
    /// page before the fonts of the forms it paints with, and a font two pages select only on its first.
    /// </summary>
    [Fact]
    public async Task Reports_every_font_the_document_states()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());

        string[] expected =
        [
            "ABCDEF+Arial",
            "Helvetica",
            "Courier",
            "ZapfDingbats",
            "XYZABC+SomeFont",
            "ZapfType3",
            "MinionMM",
            "Loose",
            "ABCDEFHelvetica",
            "abcDEF+Weird",
            "EmptyType3",
            "ABCDEF+X",
            "FontFileOne",
            "CffOne",
            "BaseEncoded",
        ];

        Assert.Equal(expected, result.Items.Select(font => font.BaseFont));
    }

    /// <summary>A subset that carries its program is reported with the program, the encoding and its pages.</summary>
    [Fact]
    public async Task Reports_an_embedded_subset_with_its_program_and_pages()
    {
        var font = Font(await FontsAsync(PdfContentFixtures.Fonts()), "ABCDEF+Arial");

        Assert.Equal("F1", font.ResourceName, StringComparer.Ordinal);
        Assert.Equal(PdfFontKind.TrueType, font.Kind);
        Assert.Equal(PdfEmbeddedFontProgramKind.FontFile2, font.Program);
        Assert.True(font.IsEmbedded);
        Assert.True(font.IsSubset);
        Assert.True(font.HasToUnicode);
        Assert.Equal("WinAnsiEncoding", font.Encoding);
        Assert.Equal([1, 2], font.PageNumbers);
        Assert.Contains("embedded", font.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A font with no descriptor is reported as one that carries no program, and the remark says which font
    /// it is about instead of leaving the caller to guess.
    /// </summary>
    [Fact]
    public async Task Reports_a_font_that_carries_no_program()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());
        var font = Font(result, "Helvetica");

        Assert.Equal(PdfFontKind.Type1, font.Kind);
        Assert.Equal(PdfEmbeddedFontProgramKind.None, font.Program);
        Assert.False(font.IsEmbedded);
        Assert.False(font.IsSubset);
        Assert.False(font.HasToUnicode);
        Assert.Equal("WinAnsiEncoding", font.Encoding);
        Assert.Equal([1], font.PageNumbers);
        Assert.Contains(
            result.Warnings,
            warning => warning.Kind == PdfWarningKind.FontDescriptorMissing
                && warning.Message.Contains("Helvetica", StringComparison.Ordinal));
    }

    /// <summary>A composite font is reported as the descendant it points at, with the encoding it states.</summary>
    [Fact]
    public async Task Resolves_a_composite_font_to_its_descendant()
    {
        var font = Font(await FontsAsync(PdfContentFixtures.Fonts()), "XYZABC+SomeFont");

        Assert.Equal(PdfFontKind.CIDFontType2, font.Kind);
        Assert.Equal(PdfEmbeddedFontProgramKind.FontFile2, font.Program);
        Assert.True(font.IsEmbedded);
        Assert.Equal("Identity-H", font.Encoding);
        Assert.Equal([2], font.PageNumbers);
    }

    /// <summary>A composite font that names no descendant is reported as the <c>Type0</c> it states.</summary>
    [Fact]
    public async Task Reports_a_composite_font_that_names_no_descendant()
    {
        var font = Font(await FontsAsync(PdfContentFixtures.Fonts()), "Loose");

        Assert.Equal(PdfFontKind.Type0, font.Kind);
        Assert.Equal(PdfEmbeddedFontProgramKind.None, font.Program);
        Assert.False(font.IsEmbedded);
        Assert.Equal([3], font.PageNumbers);
    }

    /// <summary>A Multiple Master font is reported as the kind it states rather than as an unknown.</summary>
    [Fact]
    public async Task Reports_a_multiple_master_font()
    {
        var font = Font(await FontsAsync(PdfContentFixtures.Fonts()), "MinionMM");

        Assert.Equal(PdfFontKind.MMType1, font.Kind);
        Assert.Equal([3], font.PageNumbers);
    }

    /// <summary>
    /// A Type 3 font draws with content streams of its own, so it counts as embedded and it states no
    /// descriptor — which is not a remark, because a Type 3 font that states its <c>/CharProcs</c> needs
    /// nothing outside the document.
    /// </summary>
    [Fact]
    public async Task Reports_a_type3_font_as_carrying_its_own_glyphs()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());
        var font = Font(result, "ZapfType3");

        Assert.Equal(PdfFontKind.Type3, font.Kind);
        Assert.Equal(PdfEmbeddedFontProgramKind.Type3Procedural, font.Program);
        Assert.True(font.IsEmbedded);
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Message.Contains("ZapfType3", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Type 3 font that states no <c>/CharProcs</c> draws nothing and carries nothing, so it is reported
    /// as not embedded rather than as a Type 3 font that needs no program.
    /// </summary>
    [Fact]
    public async Task Reports_a_type3_font_that_states_no_glyphs()
    {
        var font = Font(await FontsAsync(PdfContentFixtures.Fonts()), "EmptyType3");

        Assert.Equal(PdfFontKind.Type3, font.Kind);
        Assert.Equal(PdfEmbeddedFontProgramKind.None, font.Program);
        Assert.False(font.IsEmbedded);
    }

    /// <summary>
    /// A CIDFontType0 descendant is reported as the kind it states, an encoding dictionary that names no
    /// base encoding as <c>custom</c>, and a tag of exactly the eight characters that make a subset name.
    /// </summary>
    [Fact]
    public async Task Reports_a_cid_type0_font_with_a_custom_encoding()
    {
        var font = Font(await FontsAsync(PdfContentFixtures.Fonts()), "ABCDEF+X");

        Assert.Equal(PdfFontKind.CIDFontType0, font.Kind);
        Assert.Equal("custom", font.Encoding);
        Assert.Equal(PdfEmbeddedFontProgramKind.None, font.Program);
        Assert.False(font.IsEmbedded);
        Assert.True(font.IsSubset);
        Assert.Equal([3], font.PageNumbers);
    }

    /// <summary>
    /// The three places a program can be stored are each reported as themselves, and an encoding
    /// dictionary that names a base encoding is reported by that name.
    /// </summary>
    [Fact]
    public async Task Reports_where_each_program_is_stored()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());

        Assert.Equal(PdfEmbeddedFontProgramKind.FontFile, Font(result, "FontFileOne").Program);
        Assert.Equal(PdfEmbeddedFontProgramKind.FontFile3, Font(result, "CffOne").Program);
        Assert.True(Font(result, "CffOne").IsEmbedded);
        Assert.Equal("MacRomanEncoding", Font(result, "BaseEncoded").Encoding);
        Assert.Equal(PdfFontKind.TrueType, Font(result, "BaseEncoded").Kind);
    }

    /// <summary>
    /// A name is a subset only when six capitals are followed by a plus sign: six capitals alone, a plus
    /// after lowercase letters and a name too short for a tag are each reported as not a subset.
    /// </summary>
    [Fact]
    public async Task Recognises_a_subset_tag_and_rejects_a_name_that_only_looks_like_one()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());

        Assert.True(Font(result, "ABCDEF+Arial").IsSubset);
        Assert.True(Font(result, "XYZABC+SomeFont").IsSubset);
        Assert.False(Font(result, "ABCDEFHelvetica").IsSubset);
        Assert.False(Font(result, "abcDEF+Weird").IsSubset);
        Assert.False(Font(result, "Helvetica").IsSubset);
    }

    /// <summary>
    /// A form XObject paints with resources of its own, so a font used only there is used; the form's
    /// fonts are reported after the fonts of the page that paints it.
    /// </summary>
    [Fact]
    public async Task Reads_a_font_a_form_paints_with()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());
        var font = Font(result, "Courier");

        Assert.Equal("F5", font.ResourceName, StringComparer.Ordinal);
        Assert.Equal(PdfFontKind.Type1, font.Kind);
        Assert.False(font.IsEmbedded);
        Assert.Equal([1], font.PageNumbers);
    }

    /// <summary>
    /// Resources inherited from the page tree are what a page draws with: the fonts a branch states are
    /// reported for every page below it.
    /// </summary>
    [Fact]
    public async Task Reads_the_resources_a_page_inherits_from_the_tree()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());

        Assert.Equal([3], Font(result, "ZapfType3").PageNumbers);
        Assert.Equal([3], Font(result, "ABCDEFHelvetica").PageNumbers);
    }

    /// <summary>
    /// The walk stops at the depth it is given: with a limit of four, the fonts of the first four forms are
    /// reported and the font of the fifth is not, and the same document read with the default limit reports
    /// all five.
    /// </summary>
    [Fact]
    public async Task Stops_the_walk_at_the_nesting_depth_it_is_given()
    {
        var fixture = PdfContentFixtures.FontsInForms();

        var shallow = await FontsAsync(fixture, options: PdfEngineOptions.Default with { MaxNestingDepth = 4 });

        string[] reached = ["Level1", "Level2", "Level3", "Level4"];

        Assert.Equal(reached, shallow.Items.Select(font => font.BaseFont));

        // The default limit is deep enough for the same document, which is what makes the limit — and not
        // the fixture — the difference between the two answers.
        var deep = await FontsAsync(fixture);

        Assert.Contains(deep.Items, font => string.Equals(font.BaseFont, "Level5", StringComparison.Ordinal));
    }

    /// <summary>
    /// A resource that names an object the file does not contain is reported, not fatal, and the fonts
    /// around it are still read.
    /// </summary>
    [Fact]
    public async Task Reports_a_resource_that_names_no_object()
    {
        var result = await FontsAsync(PdfContentFixtures.Fonts());

        Assert.Equal(15, result.Count);
        AssertWarning(result.Warnings, PdfWarningKind.FontUnreadable);
        Assert.Contains(
            result.Warnings,
            warning => warning.Kind == PdfWarningKind.FontUnreadable
                && warning.Message.Contains("'F4'", StringComparison.Ordinal));
    }

    /// <summary>
    /// A real-world document this repository did not write answers the same way: the three standard fonts
    /// its pages select are reported by name, kind and encoding, each as carrying no program.
    /// </summary>
    /// <remarks>
    /// The expectations are the ones `pikepdf` reports for the same file — <c>F1</c> Helvetica, <c>F2</c>
    /// Courier and <c>F3</c> Times-Roman, all <c>/Type1</c> with <c>/WinAnsiEncoding</c> and no
    /// <c>/FontDescriptor</c> — so the test is a cross-check against an independent reader rather than a
    /// restatement of what this library wrote.
    /// </remarks>
    [Fact]
    public async Task Reports_the_fonts_of_a_real_world_document()
    {
        var path = Path.Combine(TestAssets.Folder, "parity", "labelled", "table.pdf");
        var result = await FontsAsync(File.ReadAllBytes(path));

        string[] expected = ["Courier", "Helvetica", "Times-Roman"];

        Assert.Equal(expected, result.Items.Select(font => font.BaseFont).Order(StringComparer.Ordinal));
        Assert.All(result.Items, font =>
        {
            Assert.NotEmpty(font.PageNumbers);
            Assert.Equal(PdfFontKind.Type1, font.Kind);
            Assert.Equal("WinAnsiEncoding", font.Encoding);
            Assert.False(font.IsEmbedded);
        });

        AssertWarning(result.Warnings, PdfWarningKind.FontDescriptorMissing);
    }

    /// <summary>
    /// A document this library encrypted is still reported with its fonts once the password opens it, and
    /// with none when it stays closed — the two answers are different because the caller has to be able to
    /// tell "no fonts" from "not readable".
    /// </summary>
    [Fact]
    public async Task Reads_the_fonts_of_a_document_it_encrypted_itself()
    {
        var encrypted = await EncryptedAsync(PdfContentFixtures.Fonts());

        var opened = await FontsAsync(encrypted, Password);

        Assert.Equal(15, opened.Count);
        Assert.DoesNotContain(opened.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionNotOpened);

        var closed = await FontsAsync(encrypted);

        Assert.Empty(closed.Items);
        AssertWarning(closed.Warnings, PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>
    /// An encrypted document this repository did not write opens with its password instead of being
    /// reported as closed. The fixture states no font, so what this pins is the outcome of the open.
    /// </summary>
    [Fact]
    public async Task Opens_an_encrypted_document_with_its_password()
    {
        var result = await FontsAsync(
            File.ReadAllBytes(TestAssets.Encrypted),
            TestAssets.EncryptedUserPassword);

        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionNotOpened);
        Assert.All(result.Items, font => Assert.NotEmpty(font.PageNumbers));
    }

    /// <summary>
    /// An encrypted document that stays closed yields no font and the remark that explains it, because
    /// "there are no fonts" and "the fonts could not be read" mean different things.
    /// </summary>
    [Fact]
    public async Task Returns_no_font_for_a_document_that_stays_closed()
    {
        var result = await FontsAsync(PdfContentFixtures.UnopenedEncryption());

        Assert.Empty(result.Items);
        AssertWarning(result.Warnings, PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>A missing source is refused before anything is opened.</summary>
    [Fact]
    public async Task Refuses_a_null_source() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Inspector().ReadFontsAsync(null!, cancellationToken: Token));
}



