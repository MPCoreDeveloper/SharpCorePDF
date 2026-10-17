using System.Text;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// The PDF fixtures that ship next to the test assembly, and the temporary files tests may write to.
/// </summary>
internal static class TestAssets
{
    /// <summary>Gets the folder that holds the fixtures.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>Gets a real-world 6 page document written by "Microsoft: Print To PDF".</summary>
    public static string SafeWebCore => Path.Combine(Folder, "SafeWebCore.pdf");

    /// <summary>Gets a 2 page document that uses object streams and a cross-reference stream.</summary>
    public static string Modern => Path.Combine(Folder, "Modern.pdf");

    /// <summary>Gets a 3 page document with bookmarks, page labels, an annotation and a rotated page.</summary>
    public static string Outlined => Path.Combine(Folder, "Outlined.pdf");

    /// <summary>Gets a 1 page document that is encrypted with a user password.</summary>
    public static string Encrypted => Path.Combine(Folder, "Encrypted.pdf");

    /// <summary>Gets a 1 page document encrypted with RC4 and a 40 bit key, revision 2, with no user password.</summary>
    public static string EncryptedRc4 => Path.Combine(Folder, "EncryptedRc4.pdf");

    /// <summary>Gets a 1 page document encrypted with RC4 and a 128 bit key, revision 3.</summary>
    public static string EncryptedRc4Large => Path.Combine(Folder, "EncryptedRc4Large.pdf");

    /// <summary>Gets a 1 page document encrypted with AES-128, revision 4.</summary>
    public static string EncryptedAes128 => Path.Combine(Folder, "EncryptedAes128.pdf");

    /// <summary>Gets a 1 page document encrypted with AES-256, revision 5, with no user password.</summary>
    public static string EncryptedAes256R5 => Path.Combine(Folder, "EncryptedAes256R5.pdf");

    /// <summary>Gets a 1 page document of revision 4 whose user password is not ASCII.</summary>
    public static string EncryptedNonAscii => Path.Combine(Folder, "EncryptedNonAscii.pdf");

    /// <summary>
    /// Gets a 1 page document of revision 4 whose metadata stream is stored in the clear, because the
    /// document clears <c>EncryptMetadata</c>.
    /// </summary>
    public static string EncryptedClearMetadata => Path.Combine(Folder, "EncryptedClearMetadata.pdf");

    /// <summary>Gets the user password of the encrypted fixtures.</summary>
    public static string EncryptedUserPassword => "user-password";

    /// <summary>Gets the owner password of the encrypted fixtures.</summary>
    public static string EncryptedOwnerPassword => "owner-password";

    /// <summary>
    /// Gets the password of the fixture that is not ASCII, whose legacy revision hashes the Latin-1 bytes
    /// of the text, as Adobe and the tools that follow it do.
    /// </summary>
    public static string EncryptedNonAsciiPassword => "ünïcode-pässword";

    /// <summary>
    /// Gets the title the fixture that keeps its metadata in the clear carries in its XMP, which the
    /// producer stored as it stands, unlike every other string of that document.
    /// </summary>
    public static string ClearMetadataXmpTitle => "Cleartext metadata sample";

    /// <summary>
    /// Gets the title the fixture that keeps its metadata in the clear carries in its document
    /// information dictionary, which the producer did encrypt.
    /// </summary>
    public static string ClearMetadataDocumentTitle => "Encrypted sample clear metadata";

    /// <summary>
    /// The encrypted fixtures, the password each of them is opened with, the title it carries, and
    /// the revision of the standard security handler its own generator recorded.
    /// </summary>
    /// <remarks>
    /// The revision is part of this list rather than a second list on purpose: a fixture that asserts
    /// a revision carries it, so a new fixture cannot be added without one. The values are the ones
    /// <c>docs/tools/make-fixtures.py</c> passes to pikepdf.
    /// </remarks>
    public static IReadOnlyList<(string Path, string Password, string Title, int Revision)> EncryptedFixtures =>
    [
        (EncryptedRc4, string.Empty, "Encrypted sample R2", 2),
        (EncryptedRc4Large, EncryptedUserPassword, "Encrypted sample R3", 3),
        (EncryptedAes128, EncryptedUserPassword, "Encrypted sample R4", 4),
        (EncryptedAes256R5, string.Empty, "Encrypted sample R5", 5),
        (EncryptedNonAscii, EncryptedNonAsciiPassword, "Encrypted sample non-ASCII", 4),
        (EncryptedClearMetadata, EncryptedUserPassword, "Encrypted sample clear metadata", 4),
        (Encrypted, EncryptedUserPassword, "Encrypted sample", 6),
    ];

    /// <summary>Gets every fixture.</summary>
    public static IReadOnlyList<string> All => [SafeWebCore, Modern, Outlined];

    /// <summary>
    /// Reads the metadata stream of a document — the XMP the catalog names — as text.
    /// </summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <param name="password">The password, or <see langword="null"/> when the document needs none.</param>
    /// <returns>The decoded XMP, or an empty string when the document carries no metadata stream.</returns>
    public static string ReadMetadata(byte[] bytes, string? password = null)
    {
        var document = new PdfFileReader(bytes, "metadata.pdf", PdfEngineOptions.Default, password);

        if (document.Catalog?.Get("Metadata") is not PdfReference reference
            || document.Resolve(reference) is not PdfStream stream)
        {
            return string.Empty;
        }

        // XMP is UTF-8 by definition, whether or not the stream is compressed.
        return Encoding.UTF8.GetString(PdfFilters.Decode(stream, document.SourceName));
    }

    /// <summary>Creates a path in a fresh folder that is removed by the returned scope.</summary>
    /// <param name="name">The file name to use inside the folder.</param>
    /// <returns>A scope that deletes the folder when it is disposed.</returns>
    public static TemporaryFolder NewTemporaryFolder(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "SharpCorePDF.Tests", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TemporaryFolder(path);
    }

    /// <summary>A folder that deletes itself.</summary>
    internal sealed class TemporaryFolder(string path) : IDisposable
    {
        /// <summary>Gets the path of the folder.</summary>
        public string Path { get; } = path;

        /// <summary>Returns the path of a file inside the folder.</summary>
        /// <param name="fileName">The name of the file.</param>
        /// <returns>The full path of the file.</returns>
        public string File(string fileName) => System.IO.Path.Combine(Path, fileName);

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A test that leaves a lock behind must not fail because of the cleanup.
            }
        }
    }
}
