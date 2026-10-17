using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of <see cref="PdfDocumentReader"/> against the documents that ship with the test assembly.
/// </summary>
public sealed class PdfDocumentReaderTests
{
    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default;
    private static readonly PdfDocumentReader Reader = new(Options);

    /// <summary>
    /// The engine must describe a real-world document exactly as an independent tool does: this fixture
    /// was written by "Microsoft: Print To PDF" and reports 6 US Letter pages.
    /// </summary>
    [Fact]
    public async Task Reads_a_real_world_document()
    {
        var info = await ReadAsync(TestAssets.SafeWebCore);

        Assert.Equal(6, info.PageCount);
        Assert.Equal(PdfVersion.V1_7, info.Version);
        Assert.False(info.IsEncrypted);
        Assert.Null(info.SecurityHandlerRevision);
        Assert.Equal("Document 2.docx", info.Metadata.Title);
        Assert.Equal("Michel Posseth", info.Metadata.Author);
        Assert.Equal("Microsoft: Print To PDF", info.Metadata.Producer);
        Assert.NotNull(info.Metadata.CreationDate);
        Assert.True(info.Metadata.CreationDate.Value.Offset == TimeSpan.FromHours(2));
        Assert.False(info.HasOutlines);
        Assert.Equal(0, info.OutlineItemCount);
        Assert.Equal(0, info.EmbeddedFileCount);
        Assert.False(info.HasAcroForm);
        Assert.True(info.HasUniformPageSize);
        Assert.Equal(PdfPageSize.Letter, info.FirstPageSize);
        Assert.Equal(6, info.Pages.Count);
        Assert.All(info.Pages, page => Assert.True(page.HasContent));
        Assert.Empty(info.Warnings);
        Assert.Equal(new FileInfo(TestAssets.SafeWebCore).Length, info.SourceLengthBytes);
    }

    /// <summary>A document whose objects live in object streams behind a cross-reference stream.</summary>
    [Fact]
    public async Task Reads_a_document_with_object_streams()
    {
        var info = await ReadAsync(TestAssets.Modern);

        Assert.Equal(2, info.PageCount);
        Assert.Equal(PdfVersion.V1_5, info.Version);
        Assert.Equal("Modern sample", info.Metadata.Title);
        Assert.Equal(PageRotation.None, info.Pages[0].Rotation);
        Assert.Equal(PageRotation.Clockwise90, info.Pages[1].Rotation);
        Assert.Equal(595.276d, info.Pages[0].Size.WidthPoints, 0.01d);
        Assert.Equal(841.89d, info.Pages[0].Size.HeightPoints, 0.01d);
        Assert.True(info.Pages[1].DisplaySize.IsLandscape);
        Assert.Empty(info.Warnings);
    }

    /// <summary>Bookmarks, annotations, page labels and an inherited-less rotated page.</summary>
    [Fact]
    public async Task Reads_bookmarks_annotations_and_rotation()
    {
        var info = await ReadAsync(TestAssets.Outlined);

        Assert.Equal(3, info.PageCount);
        Assert.Equal(new PdfVersion(1, 3), info.Version);
        Assert.True(info.HasOutlines);
        Assert.Equal(3, info.OutlineItemCount);
        Assert.Equal(1, info.Pages[0].AnnotationCount);
        Assert.Equal(0, info.Pages[1].AnnotationCount);
        Assert.Equal(PageRotation.Clockwise180, info.Pages[2].Rotation);
        Assert.Equal("Outlined sample", info.Metadata.Title);
        Assert.Empty(info.Warnings);
    }

    /// <summary>An encrypted document is described, not rejected, and still says what it declares.</summary>
    [Fact]
    public async Task Describes_an_encrypted_document_without_throwing()
    {
        var info = await ReadAsync(TestAssets.Encrypted);

        Assert.True(info.IsEncrypted);
        Assert.Equal(6, info.SecurityHandlerRevision);
        Assert.Equal(0, info.PageCount);
        Assert.Empty(info.Pages);
        Assert.Equal(PdfMetadata.Empty, info.Metadata);
        Assert.Contains(info.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>A stream that does not hold a PDF document is rejected with a domain exception.</summary>
    [Fact]
    public async Task Rejects_a_stream_that_is_not_a_document()
    {
        using var stream = new MemoryStream("this is not a PDF document at all"u8.ToArray());

        var exception = await Assert.ThrowsAsync<PdfSourceFormatException>(
            () => Reader.ReadAsync("not-a-pdf", stream, null, TestContext.Current.CancellationToken));

        Assert.Equal("not-a-pdf", exception.SourceName);
    }

    private static async Task<PdfDocumentInfo> ReadAsync(string path)
    {
        await using var content = File.OpenRead(path);

        return await Reader.ReadAsync(
            Path.GetFileName(path),
            content,
            password: null,
            TestContext.Current.CancellationToken);
    }
}
