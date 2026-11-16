using System.Text;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of what a merge writes for values it does not change: the numbers a source states come back as the
/// source states them, and a page the source leaves without a size comes back with the size the library
/// describes it with.
/// </summary>
public sealed class PdfMergeFidelityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// A real a merge copies is written from the text the source used, not from a value formatted to five
    /// decimals: a Type 3 font matrix of <c>0.000012</c> used to come out as <c>0.00001</c>, seventeen percent
    /// off, and <c>0.000004</c> as <c>0</c>.
    /// </summary>
    [Fact]
    public async Task Writes_the_reals_it_copies_as_the_source_states_them()
    {
        var merged = await MergeAsync(Page("/Scale [0.000012 -0.00048828125 0.000004 12.5 +.5]"));
        var text = Encoding.Latin1.GetString(merged);

        Assert.Contains("[0.000012 -0.00048828125 0.000004 12.5 +.5]", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real outside the form PDF defines — here with an exponent, which PDF does not have — is written in the
    /// form PDF defines, whatever its magnitude, instead of failing the write of the whole document.
    /// </summary>
    [Fact]
    public async Task Writes_a_real_of_any_magnitude_in_the_form_pdf_defines()
    {
        var merged = await MergeAsync(Page("/Huge 1e300 /Small 2.5e-3"));
        var text = Encoding.Latin1.GetString(merged);

        Assert.Contains("/Huge 1" + new string('0', 300), text, StringComparison.Ordinal);
        Assert.Contains("/Small 0.0025", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page that states no media box and inherits none is written with the A4 box the inspection describes it
    /// with, because every page of a PDF needs one; the merge says so with <see cref="PdfWarningKind.PageSizeAssumed"/>.
    /// </summary>
    [Fact]
    public async Task Writes_a_media_box_for_a_page_that_has_none()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R >>");
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        using var destination = new MemoryStream();
        var result = await Merger().MergeAsync(
            MergePlan.Create().AddSource(PdfSource.Create(new PdfByteArraySource(builder.Build(catalog), "sizeless.pdf"))),
            destination,
            Token);

        var reader = new PdfFileReader(destination.ToArray(), "merged.pdf", PdfEngineOptions.Default);
        var box = Assert.IsType<PdfArray>(reader.Pages[0].Dictionary.Get("MediaBox"));

        Assert.Equal(4, box.Count);
        Assert.Equal(PdfPageSize.A4.WidthPoints, ((PdfReal)box.Items[2]).Value, 3);
        Assert.Equal(PdfPageSize.A4.HeightPoints, ((PdfReal)box.Items[3]).Value, 3);
        Assert.Contains(
            result.Warnings,
            warning => warning.Kind == PdfWarningKind.PageSizeAssumed && warning.Message.Contains("page 1 ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A media box whose digits overflow a double reads as infinite, which is no size a page can have; the page is
    /// described as A4 with a remark, as one with no media box is, instead of the read throwing — a crash the
    /// <c>document</c> fuzz target found.
    /// </summary>
    [Fact]
    public async Task Assumes_a_size_for_a_page_whose_media_box_overflows()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var huge = "1" + new string('0', 400);

        builder.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {huge} {huge}] >>");
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        using var destination = new MemoryStream();
        var result = await Merger().MergeAsync(
            MergePlan.Create().AddSource(PdfSource.Create(new PdfByteArraySource(builder.Build(catalog), "overflow.pdf"))),
            destination,
            Token);

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PageSizeAssumed);
    }

    /// <summary>A page that states its own media box keeps it, and no size is assumed for it.</summary>
    [Fact]
    public async Task Keeps_the_media_box_a_page_states()
    {
        using var destination = new MemoryStream();
        var result = await Merger().MergeAsync(
            MergePlan.Create().AddSource(PdfSource.Create(new PdfByteArraySource(Page(string.Empty), "sized.pdf"))),
            destination,
            Token);

        Assert.Contains("/MediaBox [0 0 200 300]", Encoding.Latin1.GetString(destination.ToArray()), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.PageSizeAssumed);
    }

    /// <summary>
    /// A page whose <c>/Rotate</c> is not a multiple of 90 — which ISO 32000-1 requires it to be — is edited
    /// with the nearest quarter turn, the rotation the inspection already reports for it; the edit engine used
    /// to throw <see cref="ArgumentOutOfRangeException"/> for it (found by the <c>document</c> fuzz target).
    /// </summary>
    /// <param name="declared">The rotation the page declares.</param>
    /// <param name="expected">The rotation the edited page carries.</param>
    [Theory]
    [InlineData("91", 90)]
    [InlineData("-91", 270)]
    [InlineData("44", 0)]
    [InlineData("180.4", 180)]
    [InlineData("99999999999", 270)]
    public async Task Edits_a_page_whose_rotation_is_not_a_quarter_turn(string declared, int expected)
    {
        var editor = new PdfDocumentEditorService(new PdfDocumentReader(), new PdfEditEngine());
        var edited = await editor.ApplyToArrayAsync(
            new PdfByteArraySource(Page($"/Rotate {declared}"), "rotated.pdf"),
            PdfEditPlan.Create().SetMetadata(new PdfMetadata { Title = "rotated" }),
            cancellationToken: Token);

        var rotate = new PdfFileReader(edited, "edited.pdf", PdfEngineOptions.Default).Pages[0].Dictionary.GetInteger("Rotate") ?? 0;

        Assert.Equal(expected, rotate);
    }

    private static PdfMergerService Merger() => new(new PdfDocumentReader(), new PdfMergeEngine());

    private static byte[] Page(string entries)
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] {entries} >>");
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        return builder.Build(catalog);
    }

    private static Task<byte[]> MergeAsync(byte[] document) =>
        Merger().MergeToBytesAsync(
            MergePlan.Create().AddSource(PdfSource.Create(new PdfByteArraySource(document, "source.pdf"))),
            Token);
}
