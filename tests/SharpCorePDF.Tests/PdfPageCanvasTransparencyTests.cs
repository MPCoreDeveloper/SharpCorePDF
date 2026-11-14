using System.Text;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the opacity and blend mode a page canvas selects: the <c>/ExtGState</c> the writer registers,
/// the <c>gs</c> operator that selects it, and the refusals at the call site.
/// </summary>
/// <remarks>
/// The writer leaves the content stream and the resource dictionary uncompressed, so the assertions read
/// the authored bytes as Latin-1: the operators are ASCII and the bytes are the contract, the way the other
/// drawing tests read them. The archival pairing is exercised through the real PDF/A writer, because the
/// rule that refuses transparency on PDF/A-1b belongs to the pre-flight and not to the canvas.
/// </remarks>
public sealed class PdfPageCanvasTransparencyTests
{
    private const string DocumentId = "00112233445566778899AABBCCDDEEFF";

    private static readonly DateTimeOffset Moment = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfRectangle Shape => new(40, 40, 240, 140);

    private static PdfPaint Fill => new(PdfColor.FromRgb(0x1F, 0x3A, 0x5F));

    private static string Content(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>An opacity reaches the page as one graphics state and the operator that selects it.</summary>
    [Fact]
    public void Writes_an_alpha_as_one_extgstate_and_a_gs_selection()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetAlpha(0.5)
                .DrawRectangle(Shape, Fill, stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("/GS1 gs\n", content, StringComparison.Ordinal);
        Assert.Contains("/ExtGState", content, StringComparison.Ordinal);
        Assert.Contains("/ca 0.5", content, StringComparison.Ordinal);
        Assert.Contains("/CA 0.5", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same selection twice writes one dictionary and one operator, not one per call: a page of
    /// translucent shapes selects one state rather than growing a resource entry for each.
    /// </summary>
    [Fact]
    public void Registers_an_identical_alpha_once()
    {
        var translucent = new PdfPaint(PdfColor.FromRgb(0x1F, 0x3A, 0x5F), alpha: 0.5);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetAlpha(0.5)
                .DrawRectangle(Shape, translucent, stroke: null)
                .SetAlpha(0.5)
                .DrawRectangle(Shape, translucent, stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.Equal(1, content.Split("/GS1 gs\n", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("GS2", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A paint states an opacity of its own, and it wins over the selection — the rule the colour setters
    /// already follow. The selection is what carries a blend mode and what reaches text and images, neither
    /// of which states an opacity.
    /// </summary>
    [Fact]
    public void An_explicit_paint_states_its_own_opacity()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetAlpha(0.5)
                .DrawRectangle(Shape, Fill, stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("/GS1 gs\n", content, StringComparison.Ordinal);
        Assert.Contains("/GS2 gs\n", content, StringComparison.Ordinal);
        Assert.Contains("/ca 1", content, StringComparison.Ordinal);
    }

    /// <summary>A blend mode travels in the same dictionary as the alpha.</summary>
    [Fact]
    public void Writes_the_blend_mode_it_is_given()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetAlpha(0.5, PdfBlendMode.Multiply)
                .DrawRectangle(Shape, Fill, stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("/BM /Multiply", content, StringComparison.Ordinal);

        // The dictionary is built once per distinct state, so the state is what the page selects rather
        // than a per-shape copy of it.
        Assert.Contains("/GS1 gs\n", content, StringComparison.Ordinal);
        Assert.Equal(1, content.Split("/ExtGState", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// The opacity is part of the graphics state, so a restore puts the outer one back: what follows the
    /// <c>Q</c> selects no state at all.
    /// </summary>
    [Fact]
    public void Restores_the_state_of_the_enclosing_scope()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SaveState()
                .SetAlpha(0.2)
                .DrawRectangle(Shape, Fill, stroke: null)
                .RestoreState()
                .DrawRectangle(Shape, Fill, stroke: null))
            .ToArray();

        var content = Content(bytes);
        var saved = content.IndexOf("q\n", StringComparison.Ordinal);
        var selected = content.IndexOf("/GS1 gs\n", StringComparison.Ordinal);
        var restored = content.IndexOf("Q\n", StringComparison.Ordinal);

        Assert.True(saved >= 0 && selected > saved && restored > selected, content);

        var afterRestore = content[(restored + 2)..];

        Assert.DoesNotContain(" gs\n", afterRestore, StringComparison.Ordinal);
    }

    /// <summary>An opacity outside the unit interval is refused where it is stated.</summary>
    /// <param name="alpha">The value to refuse.</param>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Refuses_an_alpha_outside_the_unit_interval(double alpha)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfDocumentBuilder.Create().AddPage(PdfPageSize.A4, page => page.SetAlpha(alpha)));

        Assert.Equal("alpha", exception.ParamName);
        Assert.Contains("between 0 and 1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A zero opacity is legal and writes the state it states: an invisible shape is something the format
    /// can describe, and a guard that refused it would refuse a value a caller can need.
    /// </summary>
    [Fact]
    public void Accepts_a_zero_alpha()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetAlpha(0.0)
                .DrawRectangle(Shape, Fill, stroke: null))
            .ToArray();

        Assert.Contains("/GS1 gs\n", Content(bytes), StringComparison.Ordinal);
        Assert.Contains("/ca 0", Content(bytes), StringComparison.Ordinal);
    }

    /// <summary>
    /// An opaque selection is the state a page already starts in, so it is accepted and writes nothing: a
    /// document that selects normal, opaque painting must not grow a graphics state that states the default.
    /// </summary>
    [Fact]
    public void Accepts_an_opaque_selection_and_writes_no_state()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetAlpha(1.0)
                .DrawRectangle(Shape, Fill, stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.DoesNotContain("/ExtGState", content, StringComparison.Ordinal);
        Assert.Contains("\nf\n", content, StringComparison.Ordinal);
    }

    /// <summary>A blend mode the engine does not write is refused rather than approximated.</summary>
    [Fact]
    public void Refuses_a_blend_mode_the_engine_does_not_write()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfDocumentBuilder.Create().AddPage(PdfPageSize.A4, page => page.SetAlpha(0.5, (PdfBlendMode)99)));

        Assert.Equal("blendMode", exception.ParamName);
        Assert.Contains("blend mode", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// PDF/A-1b has no transparency, so a page that fades is refused for that profile and accepted for
    /// PDF/A-2b, which keeps it.
    /// </summary>
    [Fact]
    public async Task Refuses_a_faded_page_for_pdfa1b_and_writes_it_for_pdfa2b()
    {
        var source = Faded();

        var refused = await ConvertAsync(source, PdfAProfile.PdfA1B);

        Assert.False(refused.Result.IsWritten);
        Assert.Contains(
            refused.Result.UnsatisfiedRequirements,
            requirement => requirement.Contains("blends or fades", StringComparison.Ordinal));

        var written = await ConvertAsync(source, PdfAProfile.PdfA2B);

        Assert.True(written.Result.IsWritten);
        Assert.Contains("/ca 0.5", Content(written.Output), StringComparison.Ordinal);
    }

    /// <summary>Builds a page whose text is opaque and whose shape is not.</summary>
    /// <returns>The bytes of the authored document.</returns>
    /// <remarks>
    /// The font is embedded so the only requirement the profile can fail is the transparency: a document
    /// refused for two reasons would not say which one the test is about.
    /// </remarks>
    private static byte[] Faded()
    {
        var face = PdfTypeface.FromTrueType(File.ReadAllBytes(TestAssets.EmbeddedFont));

        return PdfDocumentBuilder
            .Create(PdfAuthoringOptions.Default with { CreationDate = Moment, DocumentId = DocumentId })
            .AddPage(PdfPageSize.A4, page => page
                .DrawText("Watermark", 60, 760, face, 14)
                .SetAlpha(0.5)
                .DrawRectangle(Shape, Fill, stroke: null))
            .ToArray();
    }

    /// <summary>Converts a document and returns both the result and the bytes that were written.</summary>
    /// <param name="source">The document to convert.</param>
    /// <param name="profile">The profile to write for.</param>
    /// <returns>The result and the bytes of the destination.</returns>
    private static async Task<(PdfAConversionResult Result, byte[] Output)> ConvertAsync(
        byte[] source,
        PdfAProfile profile)
    {
        using var destination = new MemoryStream();

        var options = new PdfAConversionOptions
        {
            ModificationDate = Moment,
            DocumentId = DocumentId,
        };

        var result = await new PdfAWriter()
            .ConvertAsync("faded.pdf", new MemoryStream(source), destination, profile, options, null, Token);

        return (result, destination.ToArray());
    }
}
