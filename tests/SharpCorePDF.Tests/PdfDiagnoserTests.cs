using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Services;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Reading;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the one-call report: the document, its fonts, its signatures, the archival pre-flight and the
/// merged warnings, assembled from the use cases the rest of the library already exposes.
/// </summary>
public sealed class PdfDiagnoserTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfDiagnoserService Diagnoser() => new(
        new PdfInspectorService(new PdfDocumentReader()),
        new PdfFontInspectorService(new PdfFontInspector()),
        new PdfSignatureValidatorService(new PdfDocumentSignatureValidator()),
        new PdfAWriterService(new PdfAWriter()));

    private static async Task<PdfDiagnosticsReport> DiagnoseAsync(
        byte[] bytes,
        string? password = null,
        PdfAProfile? archivalProfile = null) =>
        await Diagnoser().DiagnoseAsync(
            new PdfByteArraySource(bytes, "diagnosed.pdf"),
            password,
            archivalProfile,
            Token);

    /// <summary>A document the repository authored and never had to repair yields no finding.</summary>
    [Fact]
    public async Task Reports_a_clean_document_without_findings()
    {
        var face = PdfTypeface.FromTrueType(File.ReadAllBytes(TestAssets.EmbeddedFont));

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("diagnostics", 72, 700, face, 12))
            .ToArray();

        var report = await DiagnoseAsync(bytes);

        Assert.True(report.Document.IsReadable);
        Assert.Equal(1, report.Document.PageCount);
        Assert.NotEmpty(report.Fonts);
        Assert.Empty(report.Signatures);
        Assert.Null(report.Archival);
        Assert.Empty(report.Warnings);
        Assert.False(report.HasFindings);
        Assert.Contains("1 page(s)", report.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A table the reader had to rebuild is a finding, not a failure.</summary>
    [Fact]
    public async Task Surfaces_a_rebuilt_cross_reference_as_a_finding()
    {
        var report = await DiagnoseAsync(DamageTheTableKeyword(File.ReadAllBytes(TestAssets.Outlined)));

        Assert.Equal(3, report.Document.PageCount);
        Assert.Contains(report.Warnings, warning => warning.Kind == PdfWarningKind.CrossReferenceRebuilt);
        Assert.True(report.HasFindings);

        // Two sub-reads can repair the same table; the report folds their remarks together.
        Assert.Equal(report.Warnings.Count, report.Warnings.Distinct().Count());
    }

    /// <summary>A document that stays closed is reported, with what stands in the way, and does not throw.</summary>
    [Fact]
    public async Task Reports_an_encrypted_document_without_throwing()
    {
        var report = await DiagnoseAsync(await File.ReadAllBytesAsync(TestAssets.Encrypted, Token));

        Assert.False(report.Document.IsReadable);
        Assert.True(report.Document.RequiresPassword);
        Assert.Empty(report.Fonts);
        Assert.Empty(report.Signatures);
        Assert.Null(report.Archival);
        Assert.NotEmpty(report.Warnings);
    }

    /// <summary>A profile turns the report into the writer's pre-flight, without writing a file.</summary>
    [Fact]
    public async Task Surfaces_the_archival_requirements_when_a_profile_is_named()
    {
        var face = PdfTypeface.FromTrueType(File.ReadAllBytes(TestAssets.EmbeddedFont));

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("archive me", 72, 700, face, 12))
            .ToArray();

        var report = await DiagnoseAsync(bytes, archivalProfile: PdfAProfile.PdfA2B);

        Assert.NotNull(report.Archival);
        Assert.Equal(PdfAProfile.PdfA2B, report.Archival.Profile);
        Assert.True(report.Archival.IsWritten);
    }

    /// <summary>A null source is refused before anything is opened.</summary>
    [Fact]
    public async Task Refuses_a_null_source()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Diagnoser().DiagnoseAsync(null!));
    }

    /// <summary>The container resolves the diagnoser, so an application does not have to wire it by hand.</summary>
    [Fact]
    public void Is_registered_in_the_container()
    {
        using var provider = new ServiceCollection()
            .AddSharpCorePDF()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<IPdfDiagnoser>());
    }

    /// <summary>An undefined profile is refused before the source is read.</summary>
    [Fact]
    public async Task Refuses_an_undefined_profile()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Diagnoser().DiagnoseAsync(
                new PdfByteArraySource([], "empty.pdf"),
                archivalProfile: (PdfAProfile)99));
    }

    /// <summary>A warning only the font read makes is part of the report, not lost in the merge.</summary>
    [Fact]
    public async Task Reports_a_font_warning_the_inspection_did_not_make()
    {
        var report = await DiagnoseAsync(PdfContentFixtures.Fonts());

        Assert.Contains(report.Warnings, warning => warning.Kind == PdfWarningKind.FontDescriptorMissing);
    }

    /// <summary>A document that stays closed is read once: the other use cases are not asked.</summary>
    [Fact]
    public async Task Reads_a_closed_document_once()
    {
        var source = new CountingSource(await File.ReadAllBytesAsync(TestAssets.Encrypted, Token), "encrypted.pdf");

        var report = await Diagnoser().DiagnoseAsync(source);

        Assert.False(report.Document.IsReadable);
        Assert.Equal(1, source.Opens);
    }

    /// <summary>Reads the offset a document announces behind its <c>startxref</c> keyword.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The offset.</returns>
    private static int ReadStartxrefOffset(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;

        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var end = start;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return int.Parse(text[start..end], CultureInfo.InvariantCulture);
    }

    /// <summary>Rewrites the <c>xref</c> keyword of a document that carries a classic table.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The damaged bytes.</returns>
    private static byte[] DamageTheTableKeyword(byte[] bytes)
    {
        var offset = ReadStartxrefOffset(bytes);

        Assert.Equal("xref", Encoding.Latin1.GetString(bytes, offset, 4));

        "Xref"u8.CopyTo(bytes.AsSpan(offset));
        return bytes;
    }

    /// <summary>A content source that counts how often it is opened.</summary>
    /// <param name="bytes">The bytes the source serves.</param>
    /// <param name="name">The display name of the source.</param>
    private sealed class CountingSource(byte[] bytes, string name) : IPdfContentSource
    {
        /// <summary>Gets the number of times the source has been opened.</summary>
        public int Opens { get; private set; }

        /// <inheritdoc />
        public string DisplayName => name;

        /// <inheritdoc />
        public long? Length => bytes.Length;

        /// <inheritdoc />
        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
    }
}
