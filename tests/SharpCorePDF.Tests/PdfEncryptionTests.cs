using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of reading encrypted documents: the engine opens them with the password it is given and
/// exposes every string and stream in the clear.
/// </summary>
/// <remarks>
/// One fixture per revision of the standard security handler is checked in, because the revisions
/// derive their keys in completely different ways — MD5 with the file identifier for revisions 2 to
/// 4, SHA-256 for 5 and 6, and the hardened hash of revision 6 on top of that. A document's title
/// proves that strings were decrypted, and its page content that streams were.
/// </remarks>
public sealed class PdfEncryptionTests
{
    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default;
    private static readonly PdfDocumentReader Reader = new(Options);

    /// <summary>The encrypted fixtures, with the password, the title and the revision each is read with.</summary>
    public static TheoryData<string, string, string, int> EncryptedFixtures
    {
        get
        {
            var data = new TheoryData<string, string, string, int>();

            foreach (var (path, password, title, revision) in TestAssets.EncryptedFixtures)
            {
                data.Add(path, password, title, revision);
            }

            return data;
        }
    }

    /// <summary>
    /// Every revision of the standard security handler opens, and what comes back is the document that
    /// was encrypted: one page that carries content, the title that only the password reveals, and the
    /// revision the document itself declares.
    /// </summary>
    [Theory]
    [MemberData(nameof(EncryptedFixtures))]
    public async Task Opens_every_revision_of_the_standard_security_handler(
        string path,
        string password,
        string title,
        int revision)
    {
        var info = await ReadAsync(path, password);

        Assert.True(info.IsEncrypted);
        Assert.True(info.IsReadable);
        Assert.False(info.RequiresPassword);
        Assert.Equal(revision, info.SecurityHandlerRevision);
        Assert.Equal(1, info.PageCount);
        Assert.True(info.Pages[0].HasContent);
        Assert.Equal(title, info.Metadata.Title);
        Assert.Contains(info.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionOpened);
    }

    /// <summary>
    /// The owner password opens a document exactly as the user password does, for every revision: the
    /// revisions that derive the file key with MD5 have to recover the user password from <c>O</c> first,
    /// which is a code path no user password ever reaches.
    /// </summary>
    [Theory]
    [MemberData(nameof(EncryptedFixtures))]
    public async Task Opens_a_document_with_the_owner_password(string path, string _, string title, int revision)
    {
        var info = await ReadAsync(path, TestAssets.EncryptedOwnerPassword);

        Assert.True(info.IsReadable);
        Assert.False(info.RequiresPassword);
        Assert.Equal(revision, info.SecurityHandlerRevision);
        Assert.Equal(1, info.PageCount);
        Assert.Equal(title, info.Metadata.Title);
    }

    /// <summary>
    /// A password of revision 2 to 4 is a byte string, and those revisions predate any agreement on how
    /// the characters of one become bytes. Adobe, qpdf and pypdf hash the Latin-1 bytes of the text, and
    /// the fixture — written by qpdf — is opened with the same text the engine would have written.
    /// </summary>
    [Fact]
    public async Task Opens_a_legacy_document_whose_password_is_not_ascii()
    {
        var info = await ReadAsync(TestAssets.EncryptedNonAscii, TestAssets.EncryptedNonAsciiPassword);

        Assert.True(info.IsEncrypted);
        Assert.True(info.IsReadable);
        Assert.Equal(1, info.PageCount);
        Assert.Equal("Encrypted sample non-ASCII", info.Metadata.Title);
    }

    /// <summary>
    /// A password that is missing or wrong is asked for rather than thrown about, and nothing about the
    /// document is invented in the meantime.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("not-the-password")]
    public async Task Asks_for_a_password_that_is_missing_or_wrong(string? password)
    {
        var info = await ReadAsync(TestAssets.Encrypted, password);

        Assert.True(info.IsEncrypted);
        Assert.True(info.RequiresPassword);
        Assert.False(info.IsReadable);
        Assert.Equal(0, info.PageCount);
        Assert.Empty(info.Pages);
        Assert.Equal(PdfMetadata.Empty, info.Metadata);
        Assert.Contains(info.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>
    /// An encryption the engine does not implement leaves the document closed without asking for a
    /// password, because no password would help.
    /// </summary>
    [Theory]
    [InlineData("<</Filter/Adobe.PubSec/V 4/R 4/O <00>/U <00>>>", 4)]
    [InlineData("<</Filter/Standard/V 9/R 9/O <00>/U <00>>>", 9)]
    [InlineData("<</Filter/Standard/V 2/R 2>>", 2)]
    public async Task Leaves_an_encryption_it_does_not_implement_closed(string encryption, int revision)
    {
        var info = await ReadAsync(MinimalDocument(encryption), password: null);

        Assert.True(info.IsEncrypted);
        Assert.False(info.RequiresPassword);
        Assert.False(info.IsReadable);
        Assert.Equal(revision, info.SecurityHandlerRevision);
        Assert.Equal(0, info.PageCount);
        Assert.Contains(info.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>A trailer that points at something other than an encryption dictionary is reported.</summary>
    [Fact]
    public async Task Reports_an_encryption_dictionary_that_cannot_be_read()
    {
        var info = await ReadAsync(MinimalDocument("42"), password: null);

        Assert.True(info.IsEncrypted);
        Assert.False(info.IsReadable);
        Assert.Null(info.SecurityHandlerRevision);
        Assert.Equal(0, info.PageCount);
        Assert.Contains(info.Warnings, warning => warning.Kind == PdfWarningKind.EncryptionDictionaryUnreadable);
    }

    /// <summary>
    /// RC4 is checked against the published test vectors — the key stream of RFC 6229, which turns a run
    /// of zero bytes into the key stream itself — because the cipher is the one algorithm of the
    /// security handler that can be verified without a document.
    /// </summary>
    [Theory]
    [InlineData("0102030405", "b2396305f03dc027ccc3524a0a1118a8")]
    [InlineData("01020304050607", "293f02d47f37c9b633f2af5285feb46b")]
    [InlineData("0102030405060708090a0b0c0d0e0f10", "9ac7cc9a609d1ef7b2932899cde41b97")]
    public void Transforms_rc4_as_the_published_vectors_do(string key, string keyStream)
    {
        var expected = Convert.FromHexString(keyStream);

        var transformed = PdfRc4.Transform(Convert.FromHexString(key), new byte[expected.Length]);

        Assert.Equal(expected, transformed);
    }

    /// <summary>
    /// A document that clears <c>EncryptMetadata</c> keeps its metadata stream in the clear, and the
    /// reader has to act on that: decrypting that one stream turns the XMP into rubbish, which is the
    /// opposite of what the producer asked for and of what every other tool sees in the file.
    /// </summary>
    /// <remarks>
    /// The flag exists so that metadata stays readable to a tool that cannot otherwise open the
    /// document, which is why the assertions below also cover the case of no password at all.
    /// </remarks>
    [Fact]
    public void Reads_the_metadata_stream_of_a_document_that_keeps_it_in_the_clear()
    {
        var bytes = File.ReadAllBytes(TestAssets.EncryptedClearMetadata);
        var text = Encoding.Latin1.GetString(bytes);

        // The fixture is what makes the assertions proof rather than coincidence: the XMP is in the
        // file exactly as it stands, while the title of the document information dictionary — a string
        // of the very same document — is not, because that one was encrypted.
        Assert.Contains(TestAssets.ClearMetadataXmpTitle, text, StringComparison.Ordinal);
        Assert.DoesNotContain(TestAssets.ClearMetadataDocumentTitle, text, StringComparison.Ordinal);

        var document = new PdfFileReader(
            bytes,
            "EncryptedClearMetadata.pdf",
            Options,
            TestAssets.EncryptedUserPassword);

        Assert.False(document.Security!.EncryptsMetadata);

        // The XMP comes back as the producer stored it, not as the bytes decrypting it would produce.
        Assert.Contains(
            TestAssets.ClearMetadataXmpTitle,
            TestAssets.ReadMetadata(bytes, TestAssets.EncryptedUserPassword),
            StringComparison.Ordinal);

        // The exemption is that one object and nothing else: the same file is still read decrypted,
        // which is what the title of the document information dictionary proves.
        Assert.Equal(TestAssets.ClearMetadataDocumentTitle, document.Information?.GetText("Title"));

        // And the flag does what it exists for: the metadata is readable without the password, while
        // the rest of the document needs it.
        Assert.Contains(
            TestAssets.ClearMetadataXmpTitle,
            TestAssets.ReadMetadata(bytes),
            StringComparison.Ordinal);
    }

    private static async Task<PdfDocumentInfo> ReadAsync(string path, string? password)
    {
        await using var content = File.OpenRead(path);
        return await ReadAsync(content, password);
    }

    private static async Task<PdfDocumentInfo> ReadAsync(byte[] bytes, string? password) =>
        await ReadAsync(new MemoryStream(bytes), password);

    private static async Task<PdfDocumentInfo> ReadAsync(Stream content, string? password) =>
        await Reader.ReadAsync(
            "encrypted.pdf",
            content,
            password,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Builds the smallest readable document that declares the given encryption dictionary, so that an
    /// encryption the engine cannot open can be tested without needing a fixture no tool would write.
    /// </summary>
    /// <param name="encryption">The body of the object the trailer names as <c>Encrypt</c>.</param>
    /// <returns>The bytes of the document.</returns>
    private static byte[] MinimalDocument(string encryption)
    {
        string[] objects =
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
            encryption,
        ];

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>(objects.Length);

        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(builder.Length);
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder
            .Append("trailer\n<</Size ").Append(objects.Length + 1)
            .Append("/Root 1 0 R/Encrypt 4 0 R/ID[<0102030405060708090A0B0C0D0E0F10><0102030405060708090A0B0C0D0E0F10>]>>\n")
            .Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(builder.ToString());
    }
}
