using System.Text;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of merging into an encrypted result: the passwords the caller chooses protect the document
/// that is written, and every revision the engine writes is one it can open again.
/// </summary>
/// <remarks>
/// These tests share the options of the merge class, which have verification enabled, so each of them
/// also has the engine read the document it has just written back with the password it was written
/// with. That read-back is what makes the encryption more than self-consistent: it is the same code
/// that opens documents written by other producers.
/// </remarks>
public sealed partial class PdfMergeEngineTests
{
    /// <summary>The user password the encrypted results of these tests are written with.</summary>
    private const string MergedUserPassword = "merged-secret";

    /// <summary>The owner password the encrypted results of these tests are written with.</summary>
    private const string MergedOwnerPassword = "merged-owner";

    /// <summary>The four algorithms the engine writes, one per revision of the standard security handler.</summary>
    public static TheoryData<PdfEncryptionAlgorithm> Algorithms =>
    [
        PdfEncryptionAlgorithm.Rc4With40BitKey,
        PdfEncryptionAlgorithm.Rc4With128BitKey,
        PdfEncryptionAlgorithm.AesWith128BitKey,
        PdfEncryptionAlgorithm.AesWith256BitKey,
    ];

    /// <summary>
    /// A result written with any of the four algorithms opens with either of the passwords it was
    /// written with, and with neither of them when no password is supplied.
    /// </summary>
    /// <param name="algorithm">The algorithm to write the result with.</param>
    [Theory]
    [MemberData(nameof(Algorithms))]
    public async Task Writes_a_result_that_opens_with_the_password_it_was_written_with(PdfEncryptionAlgorithm algorithm)
    {
        var bytes = await MergeEncryptedAsync(Encryption(algorithm));

        // The content of the encrypted source page is not in the file at all: the only way to it is
        // through the encryption, whichever of the four algorithms was used.
        Assert.DoesNotContain(PageContent, Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);

        var withUser = await ReadAsync(bytes, MergedUserPassword);

        Assert.True(withUser.IsEncrypted);
        Assert.True(withUser.IsReadable);
        Assert.False(withUser.RequiresPassword);
        Assert.Equal(3, withUser.PageCount);

        // The title is a string of the encrypted source, so it survived being decrypted and being
        // encrypted again under a key that was derived from another password.
        Assert.Equal("Encrypted sample", withUser.Metadata.Title);

        var withOwner = await ReadAsync(bytes, MergedOwnerPassword);

        Assert.True(withOwner.IsReadable);
        Assert.Equal(3, withOwner.PageCount);

        var without = await ReadAsync(bytes, password: null);

        Assert.True(without.IsEncrypted);
        Assert.False(without.IsReadable);
        Assert.True(without.RequiresPassword);
    }

    /// <summary>
    /// The passwords of a source are not the passwords of the result: a document merged out of an
    /// encrypted one is opened with what the caller chose for it and with nothing else.
    /// </summary>
    [Fact]
    public async Task Does_not_open_with_the_password_of_the_source_it_was_made_from()
    {
        var bytes = await MergeEncryptedAsync(Encryption(PdfEncryptionAlgorithm.AesWith256BitKey));

        var info = await ReadAsync(bytes, TestAssets.EncryptedUserPassword);

        Assert.True(info.IsEncrypted);
        Assert.False(info.IsReadable);
        Assert.True(info.RequiresPassword);
    }

    /// <summary>
    /// The encryption dictionary states the revision the algorithm is written with, which is what makes
    /// the file readable by any implementation of the specification rather than only by this one.
    /// </summary>
    /// <param name="algorithm">The algorithm to write the result with.</param>
    /// <param name="version">The expected value of the <c>V</c> entry.</param>
    /// <param name="revision">The expected value of the <c>R</c> entry.</param>
    /// <param name="keyBits">The expected value of the <c>Length</c> entry, in bits.</param>
    /// <param name="cryptFilter">The expected method of the crypt filter, or <see langword="null"/> when the revision has none.</param>
    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Rc4With40BitKey, 1, 2, 40, null)]
    [InlineData(PdfEncryptionAlgorithm.Rc4With128BitKey, 2, 3, 128, null)]
    [InlineData(PdfEncryptionAlgorithm.AesWith128BitKey, 4, 4, 128, "AESV2")]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, 5, 6, 256, "AESV3")]
    public async Task Writes_the_encryption_dictionary_of_the_algorithm_it_was_asked_for(
        PdfEncryptionAlgorithm algorithm,
        int version,
        int revision,
        int keyBits,
        string? cryptFilter)
    {
        var dictionary = EncryptionDictionary(await MergeEncryptedAsync(Encryption(algorithm)));

        Assert.Equal("Standard", dictionary.GetName("Filter"));
        Assert.Equal(version, dictionary.GetInteger("V"));
        Assert.Equal(revision, dictionary.GetInteger("R"));
        Assert.Equal(keyBits, dictionary.GetInteger("Length"));

        // Every permission is allowed, which is the value every producer writes for that: the reserved
        // bits of the entry are set and the two low order bits are clear.
        Assert.Equal(-4, dictionary.GetInteger("P"));

        // Revision 6 hashes each password with a salt of its own and keeps the file key in the entry
        // that the other salt encrypts, so its entries are longer than the thirty-two bytes of the
        // revisions that derive the key from the password itself.
        var ownerEntry = dictionary.Get("O") as PdfString;
        var userEntry = dictionary.Get("U") as PdfString;

        Assert.NotNull(ownerEntry);
        Assert.NotNull(userEntry);
        Assert.Equal(keyBits == 256 ? 48 : 32, ownerEntry.Bytes.Length);
        Assert.Equal(ownerEntry.Bytes.Length, userEntry.Bytes.Length);

        if (cryptFilter is null)
        {
            // Revisions 2 and 3 name no crypt filter: everything is RC4 under a key derived per object.
            Assert.False(dictionary.ContainsKey("CF"));
            Assert.False(dictionary.ContainsKey("StmF"));
            Assert.False(dictionary.ContainsKey("EncryptMetadata"));
            return;
        }

        var filter = dictionary.GetDictionary("CF")?.GetDictionary("StdCF");

        Assert.NotNull(filter);
        Assert.Equal("CryptFilter", filter.GetName("Type"));
        Assert.Equal(cryptFilter, filter.GetName("CFM"));

        // The length of a crypt filter counts bytes where the one of the encryption dictionary counts
        // bits, and the metadata stream is encrypted like any other.
        Assert.Equal(keyBits / 8, filter.GetInteger("Length"));
        Assert.Equal("StdCF", dictionary.GetName("StmF"));
        Assert.Equal("StdCF", dictionary.GetName("StrF"));
        Assert.True((dictionary.Get("EncryptMetadata") as PdfBoolean)?.Value);

        if (algorithm != PdfEncryptionAlgorithm.AesWith256BitKey)
        {
            return;
        }

        // Revision 6 stores the file key twice, once per password, and stores the permissions a second
        // time so that a reader can tell whether they were edited.
        Assert.Equal(32, AsBytes(dictionary, "OE").Length);
        Assert.Equal(32, AsBytes(dictionary, "UE").Length);
        Assert.Equal(16, AsBytes(dictionary, "Perms").Length);
    }

    /// <summary>
    /// A document that is only protected against editing opens without a password, and the permissions
    /// it declares are the ones that were asked for, in the bits the specification defines them in.
    /// </summary>
    [Fact]
    public async Task Writes_a_document_that_opens_without_a_password_and_declares_its_permissions()
    {
        var encryption = new PdfEncryptionOptions
        {
            Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,
            OwnerPassword = MergedOwnerPassword,
            Permissions = PdfPermissions.PrintOnly,
        };

        var bytes = await MergeEncryptedAsync(encryption);
        var declared = (int)EncryptionDictionary(bytes).GetInteger("P")!;

        // Printing stays allowed and copying does not: the two bits a viewer acts on.
        Assert.NotEqual(0, declared & (int)PdfPermissions.Print);
        Assert.Equal(0, declared & (int)PdfPermissions.CopyContent);

        // The reserved bits are written as the specification prescribes, which is what makes the entry
        // of a document that allows anything at all the value -4.
        Assert.Equal(0, declared & 0b11);
        Assert.Equal(0b11, (declared >> 6) & 0b11);
        Assert.Equal(0xFFFFF, (declared >> 12) & 0xFFFFF);

        // The owner password is what opens it with every permission, but nothing is needed to read it.
        var info = await ReadAsync(bytes, password: null);

        Assert.True(info.IsReadable);
        Assert.False(info.RequiresPassword);
        Assert.Equal(3, info.PageCount);
    }

    /// <summary>
    /// Metadata a source kept in the clear is protected by the result: what happens to the metadata
    /// stream is decided by the encryption of the output, not by the document it was copied from.
    /// </summary>
    [Fact]
    public async Task Encrypts_the_metadata_of_a_source_that_kept_it_in_the_clear()
    {
        using var buffer = new MemoryStream();

        var plan = MergePlan.Create()
            .AddFile(TestAssets.EncryptedClearMetadata, password: TestAssets.EncryptedUserPassword)
            .WithOutput(MergeOutputOptions.Default with
            {
                Encryption = Encryption(PdfEncryptionAlgorithm.AesWith128BitKey),
            });

        await Merger.MergeAsync(plan, buffer, TestContext.Current.CancellationToken);

        var bytes = buffer.ToArray();

        // The XMP is no longer in the file as it stands, and it comes back with the password the
        // result was written with, which is the same round trip every other stream of a merge makes.
        Assert.DoesNotContain(
            TestAssets.ClearMetadataXmpTitle,
            Encoding.Latin1.GetString(bytes),
            StringComparison.Ordinal);

        Assert.Contains(
            TestAssets.ClearMetadataXmpTitle,
            TestAssets.ReadMetadata(bytes, MergedUserPassword),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An algorithm that keeps no random state repeats a merge byte for byte, and one that seals every
    /// value with its own initialization vector does not. Both are properties of the algorithms rather
    /// than of the merge, so a caller that needs a reproducible result asks for RC4 and one that needs
    /// the modern cipher accepts that two runs differ.
    /// </summary>
    [Fact]
    public async Task Repeats_a_merge_byte_for_byte_only_when_the_algorithm_keeps_no_random_state()
    {
        var moment = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var rc4 = Encryption(PdfEncryptionAlgorithm.Rc4With128BitKey);
        var aes = Encryption(PdfEncryptionAlgorithm.AesWith128BitKey);

        Assert.Equal(
            await MergeEncryptedAsync(rc4, moment),
            await MergeEncryptedAsync(rc4, moment));

        var first = await MergeEncryptedAsync(aes, moment);
        var second = await MergeEncryptedAsync(aes, moment);

        Assert.NotEqual(first, second);

        // What differs is the seal, not the document: both are the same merge under the same password.
        Assert.Equal(3, (await ReadAsync(first, MergedUserPassword)).PageCount);
        Assert.Equal(3, (await ReadAsync(second, MergedUserPassword)).PageCount);
    }

    /// <summary>Merges an encrypted source and a plain one into a result with the encryption asked for.</summary>
    /// <param name="encryption">The encryption to write the result with.</param>
    /// <param name="modificationDate">The moment to stamp the result with, when it has to be reproducible.</param>
    /// <returns>The bytes of the merged document.</returns>
    private static async Task<byte[]> MergeEncryptedAsync(
        PdfEncryptionOptions encryption,
        DateTimeOffset? modificationDate = null)
    {
        using var buffer = new MemoryStream();

        var plan = MergePlan.Create()
            .AddFile(TestAssets.Encrypted, password: TestAssets.EncryptedUserPassword)
            .AddFile(TestAssets.Modern)
            .WithOutput(MergeOutputOptions.Default with
            {
                Encryption = encryption,
                ModificationDate = modificationDate,
            });

        await Merger.MergeAsync(plan, buffer, TestContext.Current.CancellationToken);

        return buffer.ToArray();
    }

    /// <summary>Builds the encryption options of one algorithm with the passwords these tests use.</summary>
    /// <param name="algorithm">The algorithm to write with.</param>
    /// <returns>The options.</returns>
    private static PdfEncryptionOptions Encryption(PdfEncryptionAlgorithm algorithm) => new()
    {
        Algorithm = algorithm,
        UserPassword = MergedUserPassword,
        OwnerPassword = MergedOwnerPassword,
    };

    /// <summary>Reads the encryption dictionary of a document, which is never itself encrypted.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The encryption dictionary.</returns>
    private static PdfDictionary EncryptionDictionary(byte[] bytes)
    {
        var reader = new PdfFileReader(bytes, "merged.pdf", Options);

        return reader.ResolveDictionary(reader.CrossReferenceTable.Trailer.Get("Encrypt"))!;
    }

    /// <summary>Reads an entry that holds binary data.</summary>
    /// <param name="dictionary">The dictionary to read from.</param>
    /// <param name="key">The name of the entry.</param>
    /// <returns>The bytes of the entry.</returns>
    private static byte[] AsBytes(PdfDictionary dictionary, string key) =>
        ((PdfString)dictionary.Get(key)!).Bytes;

    /// <summary>Reads a merged document with a password.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <param name="password">The password, or <see langword="null"/> when the document needs none.</param>
    /// <returns>The document as described by the reader.</returns>
    private static async Task<PdfDocumentInfo> ReadAsync(byte[] bytes, string? password) =>
        await Reader.ReadAsync(
            "merged.pdf",
            new MemoryStream(bytes),
            password,
            TestContext.Current.CancellationToken);
}
