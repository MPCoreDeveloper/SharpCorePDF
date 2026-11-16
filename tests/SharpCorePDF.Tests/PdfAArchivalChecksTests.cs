using System.Text;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of what the archival conversion does about the rules of ISO 19005 that live in annotations, actions,
/// forms and content streams: what it sets right, what it removes, and what it refuses.
/// </summary>
/// <remarks>
/// Each test builds the smallest document that carries one feature and converts it for PDF/A-2b, so what is
/// asserted is the conversion's answer and the bytes it wrote, not what a validator makes of them.
/// <c>docs/validation.md</c> records the measurement against the veraPDF corpus, which is the other half of the
/// evidence: these tests pin each behaviour, and the corpus says how far the behaviours reach.
/// </remarks>
public sealed class PdfAArchivalChecksTests
{
    private static readonly DateTimeOffset Moment = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>An annotation with no flags prints as well as it shows once the profile's Print bit is set.</summary>
    [Fact]
    public async Task Sets_the_print_flag_of_an_annotation_that_states_none()
    {
        var (result, output) = await ConvertAsync(Annotated("/Subtype /Square /Rect [10 10 50 50]"), PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
        Assert.Equal(4, FlagsOf(output));
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PdfAFeatureRepaired);
    }

    /// <summary>Flags the source already states are kept and the Print bit is added to them.</summary>
    [Fact]
    public async Task Adds_the_print_bit_to_flags_that_state_other_bits()
    {
        var (result, output) = await ConvertAsync(Annotated("/Subtype /Square /Rect [10 10 50 50] /F 8"), PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten);
        Assert.Equal(12, FlagsOf(output));
    }

    /// <summary>An annotation that already prints is left exactly as it is, and nothing is reported about it.</summary>
    [Fact]
    public async Task Leaves_the_flags_of_an_annotation_that_already_prints()
    {
        var (result, output) = await ConvertAsync(Annotated("/Subtype /Square /Rect [10 10 50 50] /F 4"), PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten);
        Assert.Equal(4, FlagsOf(output));
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.PdfAFeatureRepaired);
    }

    /// <summary>A flag that hides the annotation is refused: clearing it would show what nobody saw.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(32)]
    public async Task Refuses_an_annotation_that_its_flags_hide(int flags)
    {
        var (result, _) = await ConvertAsync(Annotated($"/Subtype /Square /Rect [10 10 50 50] /F {flags + 4}"), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("hidden by its flags", StringComparison.Ordinal));
    }

    /// <summary>A popup is exempt from the flags in parts 2 to 4, and a Link carries none of the appearance a square does.</summary>
    [Fact]
    public async Task Does_not_ask_a_popup_for_flags_in_part_two()
    {
        var (result, output) = await ConvertAsync(Annotated("/Subtype /Popup /Rect [10 10 50 50]"), PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten);
        Assert.Null(AnnotationOf(output).Get("F"));
    }

    /// <summary>The types the profile does not list — here a movie — are refused.</summary>
    [Fact]
    public async Task Refuses_an_annotation_of_a_type_the_profile_does_not_permit()
    {
        var (result, _) = await ConvertAsync(Annotated("/Subtype /Movie /Rect [10 10 50 50] /F 4"), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("does not permit", StringComparison.Ordinal));
    }

    /// <summary>The first part lists fewer types than the second: a polygon is permitted by one and not the other.</summary>
    [Fact]
    public void Lists_the_annotation_types_of_each_part_as_the_reference_validator_does()
    {
        Assert.True(PdfAAnnotationRules.PermitsType(PdfAProfile.PdfA2B, "Polygon"));
        Assert.False(PdfAAnnotationRules.PermitsType(PdfAProfile.PdfA1B, "Polygon"));
        Assert.True(PdfAAnnotationRules.PermitsType(PdfAProfile.PdfA2B, "FileAttachment"));
        Assert.False(PdfAAnnotationRules.PermitsType(PdfAProfile.PdfA4, "FileAttachment"));
        Assert.False(PdfAAnnotationRules.PermitsType(PdfAProfile.PdfA2B, "3D"));
        Assert.False(PdfAAnnotationRules.PermitsType(PdfAProfile.PdfA2B, "NotAType"));
    }

    /// <summary>Part 1 asks a popup for flags too; the later parts do not.</summary>
    [Fact]
    public void Asks_a_popup_for_flags_only_in_part_one()
    {
        Assert.True(PdfAAnnotationRules.RequiresFlags(PdfAProfile.PdfA1B, "Popup"));
        Assert.False(PdfAAnnotationRules.RequiresFlags(PdfAProfile.PdfA2B, "Popup"));
        Assert.True(PdfAAnnotationRules.RequiresFlags(PdfAProfile.PdfA2B, "Square"));
    }

    /// <summary>A widget carries no action in an archival document, and the copy does not.</summary>
    [Fact]
    public async Task Removes_the_action_of_a_widget()
    {
        const string Widget = "/Subtype /Widget /FT /Btn /T (ok) /Rect [10 10 50 50] /F 4 /A << /S /URI /URI (https://example.org) >>";

        var (result, output) = await ConvertAsync(Annotated(Widget, catalogExtra: "/AcroForm << /Fields [5 0 R] >>"), PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
        Assert.Null(AnnotationOf(output).Get("A"));
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PdfAFeatureRemoved
            && warning.Message.Contains("widget", StringComparison.Ordinal));
    }

    /// <summary>The XFA form of a document is not carried into the archival copy.</summary>
    [Fact]
    public async Task Drops_the_xfa_form()
    {
        var (result, output) = await ConvertAsync(
            Annotated("/Subtype /Square /Rect [10 10 50 50] /F 4", catalogExtra: "/AcroForm << /Fields [] /XFA [(template) 6 0 R] >>"),
            PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
        Assert.False(Open(output).ResolveDictionary(Open(output).Catalog!.Get("AcroForm"))!.ContainsKey("XFA"));
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PdfAFeatureRemoved
            && warning.Message.Contains("XFA", StringComparison.Ordinal));
    }

    /// <summary>A forbidden action is as often the second link of a chain as the first; the chain is followed.</summary>
    [Fact]
    public async Task Refuses_a_forbidden_action_that_an_allowed_one_names_in_next()
    {
        const string Link = "/Subtype /Link /Rect [10 10 50 50] /F 4 /A << /S /URI /URI (https://example.org) /Next << /S /Launch /F (x.exe) >> >>";

        var (result, _) = await ConvertAsync(Annotated(Link), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("'Launch'", StringComparison.Ordinal));
    }

    /// <summary>The action of a bookmark is looked at too.</summary>
    [Fact]
    public async Task Refuses_a_forbidden_action_on_a_bookmark()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>");
        builder.AddStream(string.Empty, "0 0 m 1 1 l S"u8.ToArray());
        builder.Add("<< /Type /Outlines /First 6 0 R /Last 6 0 R /Count 1 >>");
        builder.Add("<< /Title (go) /Parent 5 0 R /A << /S /Launch /F (x.exe) >> >>");
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        var (result, _) = await ConvertAsync(builder.Build(catalog), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("a bookmark", StringComparison.Ordinal));
    }

    /// <summary>A named action outside the four the profiles allow is refused, and one of the four is not.</summary>
    [Theory]
    [InlineData("Print", false)]
    [InlineData("NextPage", true)]
    public async Task Refuses_a_named_action_other_than_the_four_standard_ones(string name, bool written)
    {
        var (result, _) = await ConvertAsync(
            Annotated($"/Subtype /Link /Rect [10 10 50 50] /F 4 /A << /S /Named /N /{name} >>"),
            PdfAProfile.PdfA2B);

        Assert.Equal(written, result.IsWritten);
    }

    /// <summary>The submission of a form is permitted by every part, which the analysis used to refuse.</summary>
    [Fact]
    public async Task Writes_a_document_whose_link_submits_a_form()
    {
        var (result, _) = await ConvertAsync(
            Annotated("/Subtype /Link /Rect [10 10 50 50] /F 4 /A << /S /SubmitForm /F (https://example.org/submit) >>"),
            PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
    }

    /// <summary>Content in DeviceCMYK under the writer's RGB output intent is refused: the two contradict.</summary>
    [Fact]
    public async Task Refuses_content_that_paints_in_devicecmyk_under_an_rgb_intent()
    {
        var (result, _) = await ConvertAsync(Plain("0 0 0 1 k 10 10 50 50 re f"), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("DeviceCMYK", StringComparison.Ordinal));
    }

    /// <summary>The same content is written when the caller supplies the CMYK intent it was prepared for.</summary>
    [Fact]
    public async Task Writes_content_in_devicecmyk_under_a_cmyk_intent_the_caller_supplies()
    {
        var options = Options() with { OutputIntent = CmykIntent() };

        var (result, output) = await ConvertAsync(Plain("0 0 0 1 k 10 10 50 50 re f"), PdfAProfile.PdfA2B, options);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
        Assert.Equal(4, IntentComponents(output));
    }

    /// <summary>
    /// The colour space a transparency group states counts as a use of it: a group in DeviceRGB breaks the rule under a
    /// CMYK output intent as a fill in DeviceRGB does, which the reference validator reports against the group.
    /// </summary>
    [Fact]
    public async Task Refuses_a_transparency_group_in_devicergb_under_a_cmyk_intent()
    {
        var options = Options() with { OutputIntent = CmykIntent() };
        var source = Plain("0 0 m 1 1 l S", pageExtra: "/Group << /S /Transparency /CS /DeviceRGB >>");

        var (result, _) = await ConvertAsync(source, PdfAProfile.PdfA2B, options);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("transparency group", StringComparison.Ordinal)
            && requirement.Contains("DeviceRGB", StringComparison.Ordinal));
    }

    /// <summary>A page's content may be an array of streams, read as one: the operator in the second one is still seen.</summary>
    [Fact]
    public async Task Reads_a_content_array_as_one_stream_and_finds_what_the_second_part_paints()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents [4 0 R 5 0 R] >>");
        builder.AddStream(string.Empty, "q"u8.ToArray());
        builder.AddStream(string.Empty, "0 0 0 1 k 10 10 50 50 re f Q"u8.ToArray());
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        var (result, _) = await ConvertAsync(builder.Build(catalog), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("DeviceCMYK", StringComparison.Ordinal));
    }

    /// <summary>A DefaultCMYK space the page defines says what DeviceCMYK means, so no matching intent is needed.</summary>
    [Fact]
    public async Task Writes_content_in_devicecmyk_when_the_page_defines_a_default_space()
    {
        var source = Plain(
            "0 0 0 1 k 10 10 50 50 re f",
            pageExtra: "/Resources << /ColorSpace << /DefaultCMYK [/ICCBased 5 0 R] >> >>",
            extra: ("/N 4", Encoding.Latin1.GetBytes("not read")));

        var (result, _) = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
    }

    /// <summary>A Separation space counts through its alternate: it is painted in DeviceCMYK when its colorant is missing.</summary>
    [Fact]
    public async Task Refuses_a_separation_space_built_on_devicecmyk_under_an_rgb_intent()
    {
        var source = Plain(
            "/CS0 cs 1 scn 10 10 50 50 re f",
            pageExtra: "/Resources << /ColorSpace << /CS0 [/Separation /Spot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0 0 1] /N 1 >>] >> >>");

        var (result, _) = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("DeviceCMYK", StringComparison.Ordinal));
    }

    /// <summary>
    /// A source that already states a usable output intent keeps it. Writing sRGB over a CMYK intent would leave
    /// content painted in CMYK under an intent that contradicts it.
    /// </summary>
    [Fact]
    public async Task Keeps_the_output_intent_a_source_already_states()
    {
        var profile = CmykIntent();
        var source = Plain(
            "0 0 0 1 k 10 10 50 50 re f",
            catalogExtra: "/OutputIntents [<< /Type /OutputIntent /S /GTS_PDFA1 /OutputConditionIdentifier (CMYK source) /DestOutputProfile 5 0 R >>]",
            extra: ("/N 4", profile.IccProfile.ToArray()));

        var (result, output) = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
        Assert.Equal(4, IntentComponents(output));
    }

    /// <summary>An operator ISO 32000 does not define is refused, wherever it stands.</summary>
    [Theory]
    [InlineData("1 2 foo")]
    [InlineData("BX 1 2 foo EX")]
    public async Task Refuses_content_that_uses_an_operator_nothing_defines(string content)
    {
        var (result, _) = await ConvertAsync(Plain(content), PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains("'foo'", StringComparison.Ordinal));
    }

    /// <summary>The operators of the specification, and the three literals a property list holds, are not refused.</summary>
    [Fact]
    public async Task Writes_content_that_uses_only_defined_operators()
    {
        var (result, _) = await ConvertAsync(
            Plain("q 1 0 0 rg 10 10 50 50 re f Q /Span <</MCID 0>> BDC EMC [1 2] 0 d 1 w 0 0 m 5 5 l S"),
            PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
    }

    /// <summary>The graphics-state keys the profiles forbid are refused, each by name.</summary>
    [Theory]
    [InlineData("/HTP 1 0 R", "halftone phase")]
    [InlineData("/TR 1 0 R", "transfer function")]
    [InlineData("/TR2 /Custom", "transfer function")]
    [InlineData("/BM /Weird", "blend mode")]
    public async Task Refuses_an_extended_graphics_state_the_profile_forbids(string entry, string named)
    {
        var source = Plain(
            "/GS0 gs 10 10 50 50 re f",
            pageExtra: $"/Resources << /ExtGState << /GS0 << /Type /ExtGState {entry} >> >> >>");

        var (result, _) = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.False(result.IsWritten);
        Assert.Contains(result.UnsatisfiedRequirements, requirement => requirement.Contains(named, StringComparison.Ordinal));
    }

    /// <summary>A blend mode the specification names, in either spelling, is not refused.</summary>
    [Theory]
    [InlineData("/BM /Multiply")]
    [InlineData("/BM [/Multiply /Screen]")]
    public async Task Writes_an_extended_graphics_state_with_a_defined_blend_mode(string entry)
    {
        var source = Plain(
            "/GS0 gs 10 10 50 50 re f",
            pageExtra: $"/Resources << /ExtGState << /GS0 << /Type /ExtGState {entry} >> >> >>");

        var (result, _) = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.True(result.IsWritten, string.Join(" ", result.UnsatisfiedRequirements));
    }

    private static PdfAConversionOptions Options() => new()
    {
        ModificationDate = Moment,
        DocumentId = "00112233445566778899AABBCCDDEEFF",
    };

    private static PdfOutputIntent CmykIntent()
    {
        var bytes = new byte[132];

        bytes[3] = 132;
        Encoding.ASCII.GetBytes("CMYK").CopyTo(bytes, 16);
        Encoding.ASCII.GetBytes("acsp").CopyTo(bytes, 36);

        return new PdfOutputIntent
        {
            IccProfile = bytes,
            NumberOfComponents = 4,
            OutputConditionIdentifier = "test CMYK",
        };
    }

    private static async Task<(PdfAConversionResult Result, byte[] Output)> ConvertAsync(
        byte[] source,
        PdfAProfile profile,
        PdfAConversionOptions? options = null)
    {
        using var destination = new MemoryStream();

        var result = await new PdfAWriter().ConvertAsync(
            "source.pdf",
            new MemoryStream(source),
            destination,
            profile,
            options ?? Options(),
            password: null,
            Token);

        return (result, destination.ToArray());
    }

    private static PdfFileReader Open(byte[] bytes) => new(bytes, "converted.pdf", PdfEngineOptions.Default);

    private static PdfDictionary AnnotationOf(byte[] output)
    {
        var reader = Open(output);

        return reader.ResolveDictionary(reader.ResolveArray(reader.Pages[0].Dictionary.Get("Annots"))!.Items[0])!;
    }

    private static long FlagsOf(byte[] output) => AnnotationOf(output).GetInteger("F") ?? -1;

    private static long IntentComponents(byte[] output)
    {
        var reader = Open(output);
        var intent = reader.ResolveDictionary(reader.ResolveArray(reader.Catalog!.Get("OutputIntents"))!.Items[0])!;

        return ((PdfStream)reader.Resolve(intent.Get("DestOutputProfile"))!).Dictionary.GetInteger("N") ?? -1;
    }

    /// <summary>
    /// Builds a one page document whose first annotation is object 5, with its form appearance as object 6. The
    /// annotation carries an appearance stream, which the profile requires of every annotation but a link or a popup.
    /// </summary>
    private static byte[] Annotated(string annotation, string catalogExtra = "")
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Annots [5 0 R] >>");
        builder.AddStream(string.Empty, "0 0 m 1 1 l S"u8.ToArray());
        builder.Add($"<< /Type /Annot {annotation} /AP << /N 6 0 R >> >>");

        builder.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 40 40] /Resources << >>", "0 0 m 1 1 l S"u8.ToArray());

        builder.Set(catalog, $"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// Builds a one page document that draws <paramref name="content"/>. The optional stream is object 5, which a
    /// resource or an output intent can name.
    /// </summary>
    private static byte[] Plain(
        string content,
        string pageExtra = "",
        string catalogExtra = "",
        (string Entries, byte[] Data)? extra = null)
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R {pageExtra} >>");
        builder.AddStream(string.Empty, Encoding.Latin1.GetBytes(content));

        if (extra is { } stream)
        {
            builder.AddStream(stream.Entries, stream.Data);
        }

        builder.Set(catalog, $"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        return builder.Build(catalog);
    }
}
