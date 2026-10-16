using System.Buffers.Binary;
using System.Security.Cryptography;
using SharpCorePDF.Domain.ValueObjects;

// The writer can be asked for the RC4 revisions, which are defined by MD5 and RC4; that is the
// caller's choice of format and not a choice of cipher the engine makes on its own.
#pragma warning disable CA5350 // Do not use weak cryptographic algorithms
#pragma warning disable CA5351 // Do not use broken cryptographic algorithms

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Encrypts the strings and streams of a document that is being written, under the Standard Security
/// Handler of revisions 2 to 6.
/// </summary>
/// <remarks>
/// <para>
/// One writer belongs to one merge. It carries the file key, the encryption dictionary to write into
/// the trailer, and the method that applies to every object that passes through it, and it seals
/// nothing on its own: the serializer asks it for the encrypted form of a string or a stream body and
/// writes what comes back.
/// </para>
/// <para>
/// The algorithms are <see cref="PdfStandardSecurity"/>'s, so a document this class writes is opened
/// again by exactly the code that reads such a document — <see cref="PdfSecurityHandler"/> — and by
/// every other implementation of the specification.
/// </para>
/// <para>
/// The values that are never encrypted are the ones the specification keeps in the clear: the
/// encryption dictionary itself, the trailer with the file identifier it holds, and the
/// cross-reference table the writer produces. Those are written without asking this class, which is
/// the same exemption the reader relies on in the other direction.
/// </para>
/// </remarks>
internal sealed class PdfSecurityWriter
{
    private readonly byte[] fileKey;
    private readonly PdfCryptMethod method;
    private readonly int objectKeyLength;

    private PdfSecurityWriter(byte[] fileKey, PdfCryptMethod method, int objectKeyLength, PdfDictionary dictionary)
    {
        this.fileKey = fileKey;
        this.method = method;
        this.objectKeyLength = objectKeyLength;
        Dictionary = dictionary;
    }

    /// <summary>Gets the encryption dictionary that belongs in the trailer of the document.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Creates the writer for one document.</summary>
    /// <param name="options">The encryption the document is written with.</param>
    /// <param name="fileIdentifier">The identifier that is written into the trailer, which the keys of revisions 2 to 6 are derived from.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The options name an algorithm that is not one of the four the engine writes.</exception>
    /// <remarks>
    /// The identifier is needed before the first object is written, because it takes part in the
    /// derivation of the file key and of the <c>O</c> and <c>U</c> entries; the caller computes it once
    /// and writes the same value into the trailer.
    /// </remarks>
    public static PdfSecurityWriter Create(PdfEncryptionOptions options, ReadOnlySpan<byte> fileIdentifier)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Algorithm switch
        {
            PdfEncryptionAlgorithm.Rc4With40BitKey =>
                CreateLegacy(options, revision: 2, version: 1, keyLength: 5, PdfCryptMethod.Rc4, fileIdentifier),

            PdfEncryptionAlgorithm.Rc4With128BitKey =>
                CreateLegacy(options, revision: 3, version: 2, keyLength: 16, PdfCryptMethod.Rc4, fileIdentifier),

            PdfEncryptionAlgorithm.AesWith128BitKey =>
                CreateLegacy(options, revision: 4, version: 4, keyLength: 16, PdfCryptMethod.AesV2, fileIdentifier),

            PdfEncryptionAlgorithm.AesWith256BitKey => CreateAes256(options),

            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Algorithm,
                "The encryption algorithm is not one the engine writes."),
        };
    }

    /// <summary>Encrypts the bytes of a string.</summary>
    /// <param name="data">The bytes of the string as they would be written.</param>
    /// <param name="objectNumber">The object number of the indirect object the string belongs to.</param>
    /// <returns>The bytes to write instead.</returns>
    public byte[] EncryptString(ReadOnlySpan<byte> data, int objectNumber) => Encrypt(data, objectNumber);

    /// <summary>Encrypts the bytes of a stream body.</summary>
    /// <param name="data">The bytes of the stream as they would be written, which are already encoded by the filters the stream names.</param>
    /// <param name="objectNumber">The object number of the stream.</param>
    /// <returns>The bytes to write instead.</returns>
    /// <remarks>
    /// The filters of the stream run first and this runs on their output, which is the order the
    /// specification prescribes: what a reader decrypts is the encoded bytes, and only then does it
    /// inflate them.
    /// </remarks>
    public byte[] EncryptStream(ReadOnlySpan<byte> data, int objectNumber) => Encrypt(data, objectNumber);

    /// <summary>Writes a document of revision 2, 3 or 4, whose file key is derived from the passwords.</summary>
    /// <param name="options">The encryption the document is written with.</param>
    /// <param name="revision">The revision to declare.</param>
    /// <param name="version">The value of the <c>V</c> entry.</param>
    /// <param name="keyLength">The number of bytes of the file key.</param>
    /// <param name="method">The method that applies to strings and streams.</param>
    /// <param name="fileIdentifier">The identifier of the document.</param>
    /// <returns>The writer.</returns>
    private static PdfSecurityWriter CreateLegacy(
        PdfEncryptionOptions options,
        int revision,
        int version,
        int keyLength,
        PdfCryptMethod method,
        ReadOnlySpan<byte> fileIdentifier)
    {
        var permissions = PermissionBits(options.Permissions);
        var userPassword = PdfStandardSecurity.PadPassword(PdfStandardSecurity.EncodeLegacyPassword(options.UserPassword));
        var ownerPassword = PdfStandardSecurity.PadPassword(PdfStandardSecurity.EncodeLegacyPassword(OwnerPasswordOf(options)));

        // Algorithm 3 stores the user password in the /O entry under a key derived from the owner
        // password, which is what makes the owner password open the document and recover the other.
        var ownerEntry = PdfStandardSecurity.ComputeOwnerEntry(ownerPassword, userPassword, revision, keyLength);

        // The metadata stream is encrypted like any other, so nothing but the identifier is added to
        // what algorithm 2 hashes. Algorithm 6 then derives the file key from the user password.
        var fileKey = PdfStandardSecurity.ComputeFileKey(
            userPassword,
            revision,
            keyLength,
            ownerEntry,
            permissions,
            fileIdentifier,
            encryptsMetadata: true);

        var dictionary = new PdfDictionary()
            .SetName("Filter", "Standard")
            .SetInteger("V", version)
            .SetInteger("R", revision)
            .SetInteger("Length", keyLength * 8)
            .Set("O", HexString(ownerEntry))
            .Set("U", HexString(PdfStandardSecurity.ComputeUserEntry(fileKey, revision, fileIdentifier)));

        if (method == PdfCryptMethod.AesV2)
        {
            // AES-128 is written through the crypt filters that PDF 1.5 introduced, so the dictionary
            // names the filter both strings and streams are encrypted with. The length of a crypt
            // filter counts bytes, where the one of the encryption dictionary counts bits.
            dictionary
                .Set("CF", new PdfDictionary().Set("StdCF", new PdfDictionary()
                    .SetName("Type", "CryptFilter")
                    .SetName("CFM", "AESV2")
                    .SetInteger("Length", keyLength)
                    .SetName("AuthEvent", "DocOpen")))
                .SetName("StmF", "StdCF")
                .SetName("StrF", "StdCF")
                .Set("EncryptMetadata", PdfBoolean.From(true));
        }

        dictionary.SetInteger("P", unchecked((int)permissions));

        return new PdfSecurityWriter(fileKey, method, keyLength, dictionary);
    }

    /// <summary>
    /// Writes a document of revision 6, whose file key is random and whose entries therefore hold the
    /// key rather than deriving it from a password.
    /// </summary>
    /// <param name="options">The encryption the document is written with.</param>
    /// <returns>The writer.</returns>
    /// <remarks>
    /// This is algorithm 8, 9 and 10 of the specification: each password is hashed with two salts, the
    /// file key is stored encrypted under a key derived from each of them, and the permissions are
    /// stored a second time so that a reader can tell whether they were edited.
    /// </remarks>
    private static PdfSecurityWriter CreateAes256(PdfEncryptionOptions options)
    {
        // The file key is random rather than derived, so two documents written with the same passwords
        // share no key at all; the identifier takes no part in any of these algorithms.
        var fileKey = RandomNumberGenerator.GetBytes(32);
        var permissions = PermissionBits(options.Permissions);

        var userPassword = PdfStandardSecurity.EncodeAes256Password(options.UserPassword);
        var ownerPassword = PdfStandardSecurity.EncodeAes256Password(OwnerPasswordOf(options));

        var userValidationSalt = RandomNumberGenerator.GetBytes(8);
        var userKeySalt = RandomNumberGenerator.GetBytes(8);
        var userEntry = Entry(userPassword, userValidationSalt, userKeySalt, []);

        var ownerValidationSalt = RandomNumberGenerator.GetBytes(8);
        var ownerKeySalt = RandomNumberGenerator.GetBytes(8);

        // The owner entry is hashed over the user entry as well, which ties the two together: an owner
        // entry cannot be moved to a document whose user password is another one.
        var ownerEntry = Entry(ownerPassword, ownerValidationSalt, ownerKeySalt, userEntry);

        // The permissions are stored twice: in the clear in /P, and encrypted with the file key in
        // /Perms so that a reader can tell whether a document was edited to allow more than it declares.
        var perms = new byte[PdfStandardSecurity.AesBlockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(perms, permissions);
        perms[4] = 0xFF;
        perms[5] = 0xFF;
        perms[6] = 0xFF;
        perms[7] = 0xFF;
        perms[8] = (byte)'T';
        perms[9] = (byte)'a';
        perms[10] = (byte)'d';
        perms[11] = (byte)'b';
        RandomNumberGenerator.Fill(perms.AsSpan(12));

        var dictionary = new PdfDictionary()
            .SetName("Filter", "Standard")
            .SetInteger("V", 5)
            .SetInteger("R", 6)
            .SetInteger("Length", 256)
            .Set("CF", new PdfDictionary().Set("StdCF", new PdfDictionary()
                .SetName("Type", "CryptFilter")
                .SetName("CFM", "AESV3")
                .SetInteger("Length", 32)
                .SetName("AuthEvent", "DocOpen")))
            .SetName("StmF", "StdCF")
            .SetName("StrF", "StdCF")
            .Set("O", HexString(ownerEntry))
            .Set("U", HexString(userEntry))
            .Set("OE", HexString(EncryptFileKey(KeyFor(ownerPassword, ownerKeySalt, userEntry), fileKey)))
            .Set("UE", HexString(EncryptFileKey(KeyFor(userPassword, userKeySalt, []), fileKey)))
            .Set("Perms", HexString(PdfStandardSecurity.EncryptAesEcb(fileKey, perms)))
            .Set("EncryptMetadata", PdfBoolean.From(true))
            .SetInteger("P", unchecked((int)permissions));

        return new PdfSecurityWriter(fileKey, PdfCryptMethod.AesV3, 32, dictionary);
    }

    /// <summary>Builds an <c>O</c> or <c>U</c> entry: a hash of the password, then the two salts.</summary>
    /// <param name="password">The encoded password.</param>
    /// <param name="validationSalt">The salt the hash of the password is taken with.</param>
    /// <param name="keySalt">The salt the key that encrypts the file key is derived from.</param>
    /// <param name="userEntry">The user entry, when the owner entry is built.</param>
    /// <returns>The forty-eight bytes of the entry.</returns>
    private static byte[] Entry(
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> validationSalt,
        ReadOnlySpan<byte> keySalt,
        ReadOnlySpan<byte> userEntry)
    {
        var entry = new byte[48];

        PdfStandardSecurity.ComputeAes256Hash(6, password, validationSalt, userEntry).CopyTo(entry);
        validationSalt.CopyTo(entry.AsSpan(32));
        keySalt.CopyTo(entry.AsSpan(40));

        return entry;
    }

    /// <summary>Derives the key an <c>OE</c> or <c>UE</c> entry is encrypted with.</summary>
    /// <param name="password">The encoded password.</param>
    /// <param name="keySalt">The key salt of the entry.</param>
    /// <param name="userEntry">The user entry, when the owner entry is built.</param>
    /// <returns>The thirty-two byte key.</returns>
    private static byte[] KeyFor(ReadOnlySpan<byte> password, ReadOnlySpan<byte> keySalt, ReadOnlySpan<byte> userEntry) =>
        PdfStandardSecurity.ComputeAes256Hash(6, password, keySalt, userEntry)[..32];

    /// <summary>Encrypts the file key for one password, which algorithm 9 and 10 do without padding.</summary>
    /// <param name="key">The thirty-two byte key.</param>
    /// <param name="fileKey">The file key.</param>
    /// <returns>The thirty-two bytes of the entry.</returns>
    private static byte[] EncryptFileKey(ReadOnlySpan<byte> key, ReadOnlySpan<byte> fileKey) =>
        PdfStandardSecurity.EncryptAesCbc(
            key,
            new byte[PdfStandardSecurity.AesBlockSize],
            fileKey,
            addPadding: false);
    /// <summary>Encrypts the bytes of one object with the method this document declares.</summary>
    /// <param name="data">The bytes as they would be written.</param>
    /// <param name="objectNumber">The object number of the indirect object the bytes belong to.</param>
    /// <returns>The bytes to write instead.</returns>
    /// <remarks>
    /// Every object of the merged document is written with generation zero, so the generation number
    /// algorithm 1 hashes is always the same one.
    /// </remarks>
    private byte[] Encrypt(ReadOnlySpan<byte> data, int objectNumber)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        if (method == PdfCryptMethod.Rc4)
        {
            // RC4 is its own inverse and keeps the length of its input, so what is written is exactly
            // as long as what a reader decrypts again.
            return PdfRc4.Transform(
                PdfStandardSecurity.DeriveObjectKey(fileKey, objectKeyLength, objectNumber, 0, salted: false),
                data);
        }

        // Both AES methods store the initialization vector in front of the ciphertext; AES-128 derives
        // a key per object and AES-256 uses the file key itself. The vector is random per value, which
        // is what makes two encrypted documents of the same content differ byte for byte.
        var iv = RandomNumberGenerator.GetBytes(PdfStandardSecurity.AesBlockSize);
        var key = method == PdfCryptMethod.AesV2
            ? PdfStandardSecurity.DeriveObjectKey(fileKey, objectKeyLength, objectNumber, 0, salted: true)
            : fileKey;

        var ciphertext = PdfStandardSecurity.EncryptAesCbc(key, iv, data, addPadding: true);

        var result = new byte[iv.Length + ciphertext.Length];
        iv.CopyTo(result, 0);
        ciphertext.CopyTo(result, iv.Length);

        return result;
    }

    /// <summary>Turns the permissions into the value of the <c>P</c> entry.</summary>
    /// <param name="permissions">The permissions the document is written with.</param>
    /// <returns>The value of the entry, as the specification's unsigned integer.</returns>
    /// <remarks>
    /// The bits the specification reserves are written exactly as it prescribes: the two low order bits
    /// are clear and everything from bit seven on is set. The entry is therefore negative for every
    /// document that allows anything at all, which is what every producer of the format writes.
    /// </remarks>
    private static uint PermissionBits(PdfPermissions permissions) => (uint)permissions | 0xFFFFF0C0;

    /// <summary>Picks the owner password, which falls back to the user password when it is empty.</summary>
    /// <param name="options">The encryption the document is written with.</param>
    /// <returns>The password the owner entry is built from.</returns>
    private static string OwnerPasswordOf(PdfEncryptionOptions options) =>
        string.IsNullOrEmpty(options.OwnerPassword) ? options.UserPassword : options.OwnerPassword;

    /// <summary>Formats a value that holds binary data as a hexadecimal string.</summary>
    /// <param name="bytes">The bytes to write.</param>
    /// <returns>The string.</returns>
    private static PdfString HexString(ReadOnlySpan<byte> bytes) => new(bytes.ToArray(), isHexadecimal: true);
}
