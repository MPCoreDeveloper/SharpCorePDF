using System.Text;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of merging encrypted sources: a document the engine can open contributes its pages in the
/// clear, and one it cannot open is refused with the exception that says whether a password is what
/// stands in the way.
/// </summary>
public sealed partial class PdfMergeEngineTests
{
    /// <summary>The content of one page of the generated fixtures, which an encrypted fixture holds encrypted.</summary>
    private const string PageContent = "BT /F1 12 Tf 72 720 Td (page) Tj ET\n";

    /// <summary>
    /// An encrypted source is merged with its password, and what comes out is the document that was
    /// hidden: the page content and the title are in the clear, and the result is not encrypted.
    /// </summary>
    [Fact]
    public async Task Merges_an_encrypted_source_with_its_password()
    {
        var source = await File.ReadAllBytesAsync(
            TestAssets.Encrypted,
            TestContext.Current.CancellationToken);

        // The fixture makes the assertion below proof rather than coincidence: the text the merged
        // document carries is not in the source file at all, so it can only come from decryption.
        Assert.DoesNotContain(PageContent, Encoding.Latin1.GetString(source), StringComparison.Ordinal);

        var merged = await MergeAsync(
            MergePlan.Create().AddFile(TestAssets.Encrypted, password: TestAssets.EncryptedUserPassword));

        Assert.Equal(1, merged.Result.PageCount);
        Assert.False(merged.Document.IsEncrypted);
        Assert.Equal("Encrypted sample", merged.Document.Metadata.Title);

        // The source keeps this page content encrypted and compressed; the merged document holds it in
        // the clear, and the stream still decompresses, because decryption happens before anything else.
        Assert.Equal(PageContent, DecodePageContent(merged.Raw, pageIndex: 0));
        Assert.Contains(
            merged.Result.Warnings,
            warning => warning.Kind == PdfWarningKind.EncryptionOpened);
    }

    /// <summary>Reads and decompresses the content of one page of a document.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <param name="pageIndex">The zero-based index of the page.</param>
    /// <returns>The decoded content as text.</returns>
    private static string DecodePageContent(byte[] bytes, int pageIndex)
    {
        var reader = new PdfFileReader(bytes, "merged.pdf", Options);
        var contents = reader.Pages[pageIndex].Dictionary.Get("Contents");

        if (contents is PdfReference reference)
        {
            contents = reader.Resolve(reference);
        }

        return contents is PdfStream stream
            ? Encoding.Latin1.GetString(PdfFilters.Decode(stream, reader.SourceName))
            : string.Empty;
    }

    /// <summary>
    /// A document that is only protected against editing opens and merges without being told a
    /// password, which is the shape most owner-protected documents have.
    /// </summary>
    [Fact]
    public async Task Merges_an_owner_protected_source_without_a_password()
    {
        var merged = await MergeAsync(MergePlan.Create().AddFile(TestAssets.EncryptedRc4));

        Assert.Equal(1, merged.Result.PageCount);
        Assert.False(merged.Document.IsEncrypted);
        Assert.Equal("Encrypted sample R2", merged.Document.Metadata.Title);
    }

    /// <summary>Encrypted and plain sources merge together, in the order the plan lists them.</summary>
    [Fact]
    public async Task Merges_an_encrypted_source_alongside_a_plain_one()
    {
        var merged = await MergeAsync(
            MergePlan.Create()
                .AddFile(TestAssets.Modern, pages: "1")
                .AddFile(TestAssets.EncryptedAes128, password: TestAssets.EncryptedUserPassword, pages: "1"));

        Assert.Equal(2, merged.Result.PageCount);
        Assert.Equal([2, 1], merged.Result.Sources.Select(source => source.SourcePageCount).ToArray());
        Assert.All(merged.Document.Pages, page => Assert.True(page.HasContent));
    }

    /// <summary>
    /// A source that keeps its metadata stream in the clear contributes the XMP it carries, rather than
    /// the rubbish that decrypting a stream the producer deliberately left alone would produce. It is
    /// the one value of an encrypted source that the merge must not treat like the rest of it.
    /// </summary>
    [Fact]
    public async Task Keeps_the_metadata_a_source_keeps_in_the_clear()
    {
        var merged = await MergeAsync(
            MergePlan.Create().AddFile(
                TestAssets.EncryptedClearMetadata,
                password: TestAssets.EncryptedUserPassword));

        Assert.Equal(1, merged.Result.PageCount);
        Assert.False(merged.Document.IsEncrypted);

        // The title of the document information dictionary was encrypted and is decrypted on the way
        // in, and the XMP was not encrypted and travels as it is.
        Assert.Equal(TestAssets.ClearMetadataDocumentTitle, merged.Document.Metadata.Title);
        Assert.Contains(
            TestAssets.ClearMetadataXmpTitle,
            TestAssets.ReadMetadata(merged.Raw),
            StringComparison.Ordinal);
    }

    /// <summary>An encrypted source without its password is refused, and the refusal asks for one.</summary>
    [Fact]
    public async Task Refuses_an_encrypted_source_without_its_password()
    {
        using var buffer = new MemoryStream();

        var exception = await Assert.ThrowsAsync<PdfEncryptionException>(
            async () => await Merger.MergeAsync(
                MergePlan.Create().AddFile(TestAssets.Encrypted),
                buffer,
                TestContext.Current.CancellationToken));

        Assert.True(exception.RequiresPassword);
        Assert.Equal("Encrypted.pdf", exception.SourceName);
    }

    /// <summary>An encrypted source whose password is wrong is refused the same way.</summary>
    [Fact]
    public async Task Refuses_an_encrypted_source_whose_password_is_wrong()
    {
        using var buffer = new MemoryStream();

        var exception = await Assert.ThrowsAsync<PdfEncryptionException>(
            async () => await Merger.MergeAsync(
                MergePlan.Create().AddFile(TestAssets.Encrypted, password: "not-the-password"),
                buffer,
                TestContext.Current.CancellationToken));

        Assert.True(exception.RequiresPassword);
    }
}
