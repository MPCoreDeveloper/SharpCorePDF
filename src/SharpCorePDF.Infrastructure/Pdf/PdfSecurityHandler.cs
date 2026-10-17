using System.Security.Cryptography;
using SharpCorePDF.Domain.ValueObjects;

// Reading a document whose security handler is revision 2 to 4 is impossible without MD5 and RC4,
// which the specification defines as the cryptography of those revisions. The algorithms themselves
// live in PdfStandardSecurity, so that reading and writing use exactly the same code.
#pragma warning disable CA5350 // Do not use weak cryptographic algorithms
#pragma warning disable CA5351 // Do not use broken cryptographic algorithms

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>The way one class of data is encrypted in a document.</summary>
internal enum PdfCryptMethod
{
    /// <summary>The data is not encrypted, which is what a crypt filter of <c>None</c> means.</summary>
    None,

    /// <summary>RC4 with a key derived per object; the method of revisions 2, 3 and 4.</summary>
    Rc4,

    /// <summary>AES-128 in CBC mode with a key derived per object; the <c>AESV2</c> crypt filter.</summary>
    AesV2,

    /// <summary>AES-256 in CBC mode with the file key itself; the <c>AESV3</c> crypt filter of revision 5 and 6.</summary>
    AesV3,
}

/// <summary>
/// The outcome of trying to open the encryption of a document.
/// </summary>
/// <param name="Handler">The handler that decrypts, or <see langword="null"/> when the document stays closed.</param>
/// <param name="RequiresPassword">
/// <see langword="true"/> when the document declares a user password that the supplied password did not
/// match, so a caller can ask for the right one instead of being told the document is simply unreadable.
/// </param>
internal readonly record struct PdfDecryptionResult(PdfSecurityHandler? Handler, bool RequiresPassword)
{
    /// <summary>Gets a value indicating whether the document can be read.</summary>
    public bool IsAvailable => Handler is not null;

    /// <summary>Gets a result that leaves a declared encryption closed, without asking for a password.</summary>
    public static PdfDecryptionResult Closed { get; } = new(null, false);
}

/// <summary>
/// Decrypts the strings and streams of a document protected by the Standard Security Handler.
/// </summary>
/// <remarks>
/// <para>
/// The handler implements the security handler of the PDF specification for revisions 2 to 6: the RC4
/// revisions (2, 3 and part of 4), AES-128 (<c>AESV2</c>) and AES-256 (<c>AESV3</c>, revisions 5 and 6,
/// including the hardened hash of algorithm 2.B that revision 6 introduced).
/// </para>
/// <para>
/// A password arrives as text, and the revisions disagree on what that text means. Revisions 2 to 4
/// predate any convention, so a password that survives a Latin-1 round trip is read as Latin-1 and
/// anything else as UTF-8; revisions 5 and 6 are UTF-8 by definition and use at most 127 bytes. SASL
/// preparation is not applied, so a password whose normalized form differs from the form its producer
/// wrote must be supplied as the producer wrote it.
/// </para>
/// <para>
/// Only two kinds of object are encrypted in a PDF file — strings and the bodies of streams — and the
/// engine decrypts exactly those, as they are parsed, so nothing downstream ever sees ciphertext. The
/// values that must <em>not</em> be decrypted (the file identifier, the <c>Encrypt</c> dictionary
/// itself, cross-reference streams, and the objects inside an object stream, whose bytes were already
/// part of the stream that carried them) never pass through this class, because the readers that
/// produce them are built without it.
/// </para>
/// <para>
/// One handler belongs to one reader. The AES instance it needs is created per call rather than shared,
/// so no state at all is shared between documents and the handler carries no disposable lifetime.
/// </para>
/// </remarks>
internal sealed class PdfSecurityHandler
{
    private readonly byte[] fileKey;
    private readonly PdfCryptMethod stringMethod;
    private readonly PdfCryptMethod streamMethod;
    private readonly int objectKeyLength;

    private PdfSecurityHandler(
        byte[] fileKey,
        PdfCryptMethod stringMethod,
        PdfCryptMethod streamMethod,
        int objectKeyLength,
        bool encryptsMetadata)
    {
        this.fileKey = fileKey;
        this.stringMethod = stringMethod;
        this.streamMethod = streamMethod;
        this.objectKeyLength = objectKeyLength;
        EncryptsMetadata = encryptsMetadata;
    }

    /// <summary>
    /// Gets a value indicating whether the document says its metadata stream is encrypted. A document
    /// that clears <c>EncryptMetadata</c> stores that one stream in the clear, so that a tool can read
    /// the metadata of a document it cannot otherwise open.
    /// </summary>
    /// <remarks>
    /// A reader has to act on this value: the metadata stream of such a document is stored as it
    /// stands, so decrypting it turns the XMP into rubbish. <see cref="PdfFileReader"/> keeps that
    /// object away from this class for exactly that reason.
    /// </remarks>
    public bool EncryptsMetadata { get; }

    /// <summary>Decrypts the bytes of a string.</summary>
    /// <param name="data">The bytes as they were stored.</param>
    /// <param name="objectNumber">The object number of the indirect object the string belongs to.</param>
    /// <param name="generation">The generation number of that object.</param>
    /// <returns>The decrypted bytes.</returns>
    public byte[] DecryptString(ReadOnlySpan<byte> data, long objectNumber, int generation) =>
        Decrypt(data, objectNumber, generation, stringMethod);

    /// <summary>Decrypts the bytes of a stream body.</summary>
    /// <param name="data">The bytes as they were stored.</param>
    /// <param name="objectNumber">The object number of the stream.</param>
    /// <param name="generation">The generation number of the stream.</param>
    /// <returns>The decrypted bytes.</returns>
    public byte[] DecryptStream(ReadOnlySpan<byte> data, long objectNumber, int generation) =>
        Decrypt(data, objectNumber, generation, streamMethod);

    private byte[] Decrypt(
        ReadOnlySpan<byte> data,
        long objectNumber,
        int generation,
        PdfCryptMethod method)
    {
        if (method == PdfCryptMethod.None || data.IsEmpty)
        {
            return data.ToArray();
        }

        if (method == PdfCryptMethod.Rc4)
        {
            return PdfRc4.Transform(ObjectKey(objectNumber, generation, salted: false), data);
        }

        // Both AES methods store a random initialization vector as the first sixteen bytes of the value,
        // with the ciphertext behind it. AES-256 uses the file key itself instead of a derived one.
        if (data.Length < PdfStandardSecurity.AesBlockSize)
        {
            return [];
        }

        var key = method == PdfCryptMethod.AesV2
            ? ObjectKey(objectNumber, generation, salted: true)
            : fileKey;

        return PdfStandardSecurity.DecryptAesCbc(
            key,
            data[..PdfStandardSecurity.AesBlockSize],
            data[PdfStandardSecurity.AesBlockSize..],
            removePadding: true);
    }

    /// <summary>
    /// Derives the key of one object: the file key extended with the object number, the generation
    /// number and — for AES — the <c>sAlT</c> marker that algorithm 1 adds for backward compatibility.
    /// </summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="generation">The generation number.</param>
    /// <param name="salted">Whether the AES marker is added.</param>
    /// <returns>The key, which never exceeds sixteen bytes.</returns>
    private byte[] ObjectKey(long objectNumber, int generation, bool salted) =>
        PdfStandardSecurity.DeriveObjectKey(fileKey, objectKeyLength, objectNumber, generation, salted);

    /// <summary>
    /// Tries to open the encryption of a document with the supplied password.
    /// </summary>
    /// <param name="encryption">The <c>Encrypt</c> dictionary, which is never itself encrypted.</param>
    /// <param name="fileIdentifier">The first element of the trailer <c>ID</c>, which the keys of revisions 2 to 4 are derived from.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="password">The user or owner password, or <see langword="null"/> when the caller knows none.</param>
    /// <param name="warnings">The list that receives non-fatal remarks.</param>
    /// <returns>The handler that decrypts, or a result that explains why the document stays closed.</returns>
    public static PdfDecryptionResult TryCreate(
        PdfDictionary encryption,
        ReadOnlySpan<byte> fileIdentifier,
        string sourceName,
        string? password,
        List<PdfWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(warnings);

        if (!TryAcceptHandler(encryption, sourceName, warnings, out var version, out var revision)
            || !TryResolveMethods(encryption, version, sourceName, warnings, out var stringMethod, out var streamMethod, out var cryptFilterBits))
        {
            return PdfDecryptionResult.Closed;
        }

        if (GetBytes(encryption, "O") is not { } ownerEntry || GetBytes(encryption, "U") is not { } userEntry)
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionDictionaryIncomplete,
                $"'{sourceName}': the encryption dictionary has no /O or /U entry, so the document cannot be read."));
            return PdfDecryptionResult.Closed;
        }

        // The encryption dictionary states the key length in bits; a crypt filter of revision 4 and 5
        // states its own, in bytes, and it is the one the file key is actually derived with.
        var declaredBits = (int)(encryption.GetInteger("Length") ?? 40);
        var keyBits = version >= 4 && cryptFilterBits is { } filterBits ? filterBits : declaredBits;
        var encryptsMetadata = ReadEncryptsMetadata(encryption);

        byte[]? fileKey;
        int objectKeyLength;

        if (version <= 4)
        {
            if (stringMethod == PdfCryptMethod.AesV3 || streamMethod == PdfCryptMethod.AesV3)
            {
                warnings.Add(new(
                    PdfWarningKind.EncryptionCombinationUnsupported,
                    $"'{sourceName}': the document declares the AES-256 crypt filter with security handler /V {version}, which is not a combination the engine can make sense of."));
                return PdfDecryptionResult.Closed;
            }

            objectKeyLength = revision == 2 ? 5 : Math.Clamp(keyBits / 8, 5, 16);
            fileKey = TryOpenLegacy(encryption, ownerEntry, userEntry, revision, objectKeyLength, fileIdentifier, password);
        }
        else
        {
            if (!TryReadAes256Entries(encryption, ownerEntry, userEntry, sourceName, warnings, out var ownerKey, out var userKey))
            {
                return PdfDecryptionResult.Closed;
            }

            objectKeyLength = 32;
            fileKey = TryOpenAes256(revision, ownerEntry, userEntry, ownerKey, userKey, password);
        }

        if (fileKey is null)
        {
            // The password that was supplied — possibly none at all — did not open the document.
            return new PdfDecryptionResult(null, RequiresPassword: true);
        }

        warnings.Add(new(
            PdfWarningKind.EncryptionOpened,
            $"'{sourceName}': the document is encrypted; it was opened and its strings and streams are read decrypted."));

        return new PdfDecryptionResult(
            new PdfSecurityHandler(fileKey, stringMethod, streamMethod, objectKeyLength, encryptsMetadata),
            RequiresPassword: false);
    }

    /// <summary>Reads an entry that must be a string.</summary>
    /// <param name="dictionary">The dictionary to read from.</param>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The bytes of the string, or <see langword="null"/>.</returns>
    private static byte[]? GetBytes(PdfDictionary dictionary, string key) =>
        dictionary.Get(key) is PdfString text ? text.Bytes : null;

    /// <summary>
    /// Determines whether the document encrypts its metadata stream, which the file-key algorithms of
    /// revisions 2 to 4 take into account.
    /// </summary>
    /// <param name="encryption">The encryption dictionary.</param>
    /// <returns><see langword="true"/> unless the <c>EncryptMetadata</c> entry says <c>false</c>.</returns>
    private static bool ReadEncryptsMetadata(PdfDictionary encryption) =>
        encryption.Get("EncryptMetadata") is not PdfBoolean { Value: false };

    /// <summary>
    /// Determines whether the document names a security handler this engine implements, and at which
    /// version and revision.
    /// </summary>
    /// <param name="encryption">The encryption dictionary.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="warnings">The list that receives non-fatal remarks.</param>
    /// <param name="version">The value of the <c>V</c> entry.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <returns><see langword="true"/> when the handler is the Standard one at a revision the engine reads.</returns>
    private static bool TryAcceptHandler(
        PdfDictionary encryption,
        string sourceName,
        List<PdfWarning> warnings,
        out int version,
        out int revision)
    {
        version = (int)(encryption.GetInteger("V") ?? 0);
        revision = (int)(encryption.GetInteger("R") ?? 0);

        var filter = encryption.GetName("Filter") ?? "Standard";

        if (!string.Equals(filter, "Standard", StringComparison.Ordinal))
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionHandlerUnsupported,
                $"'{sourceName}': the document is encrypted with the '{filter}' security handler, which the engine does not implement, so its pages cannot be read."));
            return false;
        }

        if (encryption.ContainsKey("SubFilter"))
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionHandlerUnsupported,
                $"'{sourceName}': the document declares a security handler sub-filter, which the engine does not implement, so its pages cannot be read."));
            return false;
        }

        if (version is < 1 or > 5 || revision is < 2 or > 6)
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionRevisionUnsupported,
                $"'{sourceName}': the document is encrypted with security handler /V {version} /R {revision}, which the engine does not implement, so its pages cannot be read."));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the <c>OE</c> and <c>UE</c> entries, which a document of revision 5 or 6 needs in addition
    /// to the <c>O</c> and <c>U</c> ones.
    /// </summary>
    /// <param name="encryption">The encryption dictionary.</param>
    /// <param name="ownerEntry">The bytes of the <c>O</c> entry.</param>
    /// <param name="userEntry">The bytes of the <c>U</c> entry.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="warnings">The list that receives non-fatal remarks.</param>
    /// <param name="ownerKey">The bytes of the <c>OE</c> entry.</param>
    /// <param name="userKey">The bytes of the <c>UE</c> entry.</param>
    /// <returns><see langword="true"/> when the entries are all present and long enough.</returns>
    private static bool TryReadAes256Entries(
        PdfDictionary encryption,
        byte[] ownerEntry,
        byte[] userEntry,
        string sourceName,
        List<PdfWarning> warnings,
        out byte[] ownerKey,
        out byte[] userKey)
    {
        ownerKey = [];
        userKey = [];

        // The entry of a revision 5 document is a 32 byte hash followed by two 8 byte salts; anything
        // shorter cannot be one, and the algorithms below would read past its end.
        if (userEntry.Length < 48
            || ownerEntry.Length < 48
            || GetBytes(encryption, "UE") is not { } userKeyValue
            || GetBytes(encryption, "OE") is not { } ownerKeyValue)
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionDictionaryIncomplete,
                $"'{sourceName}': the encryption dictionary of a /V 5 document is incomplete, so the document cannot be read."));
            return false;
        }

        ownerKey = ownerKeyValue;
        userKey = userKeyValue;
        return true;
    }

    /// <summary>
    /// Determines how strings and streams are encrypted. Revisions 2 and 3 state the method directly,
    /// while revisions 4 and 5 declare it through the crypt filters that <c>CF</c> holds.
    /// </summary>
    /// <param name="encryption">The encryption dictionary.</param>
    /// <param name="version">The value of the <c>V</c> entry.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="warnings">The list that receives non-fatal remarks.</param>
    /// <param name="stringMethod">The method that applies to strings.</param>
    /// <param name="streamMethod">The method that applies to stream bodies.</param>
    /// <param name="cryptFilterBits">The key length a crypt filter declares, when one does.</param>
    /// <returns><see langword="true"/> when both methods are ones the engine implements.</returns>
    private static bool TryResolveMethods(
        PdfDictionary encryption,
        int version,
        string sourceName,
        List<PdfWarning> warnings,
        out PdfCryptMethod stringMethod,
        out PdfCryptMethod streamMethod,
        out int? cryptFilterBits)
    {
        cryptFilterBits = null;
        stringMethod = PdfCryptMethod.None;
        streamMethod = PdfCryptMethod.None;

        if (version <= 3)
        {
            // Up to /V 3 everything is RC4, with a key whose length /Length states in bits.
            stringMethod = PdfCryptMethod.Rc4;
            streamMethod = PdfCryptMethod.Rc4;
            return true;
        }

        var filters = encryption.GetDictionary("CF");

        if (!TryResolveCryptMethod(encryption, "StmF", filters, sourceName, warnings, out streamMethod, out var streamBits))
        {
            return false;
        }

        if (!TryResolveCryptMethod(encryption, "StrF", filters, sourceName, warnings, out stringMethod, out var stringBits))
        {
            return false;
        }

        cryptFilterBits = streamBits ?? stringBits;
        return true;
    }

    /// <summary>Resolves the crypt filter that one entry of the encryption dictionary names.</summary>
    /// <param name="encryption">The encryption dictionary.</param>
    /// <param name="entryName">The entry to resolve, <c>StmF</c> or <c>StrF</c>.</param>
    /// <param name="filters">The <c>CF</c> dictionary, when the document has one.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="warnings">The list that receives non-fatal remarks.</param>
    /// <param name="method">The method the filter applies.</param>
    /// <param name="keyBits">The key length the filter declares, in bits.</param>
    /// <returns><see langword="true"/> when the filter is usable.</returns>
    private static bool TryResolveCryptMethod(
        PdfDictionary encryption,
        string entryName,
        PdfDictionary? filters,
        string sourceName,
        List<PdfWarning> warnings,
        out PdfCryptMethod method,
        out int? keyBits)
    {
        method = PdfCryptMethod.None;
        keyBits = null;

        // An entry that is absent means the identity filter, which encrypts nothing: that is the
        // specification's default, and it is how a document encrypts streams but not strings.
        var filterName = encryption.GetName(entryName);

        if (filterName is null or "Identity")
        {
            return true;
        }

        if (filters?.GetDictionary(filterName) is not { } filter)
        {
            warnings.Add(new(
                PdfWarningKind.CryptFilterUndefined,
                $"'{sourceName}': the encryption dictionary names the crypt filter '{filterName}' in /{entryName} but does not define it, so its pages cannot be read."));
            return false;
        }

        switch (filter.GetName("CFM") ?? "None")
        {
            case "V2":
                method = PdfCryptMethod.Rc4;
                break;

            case "AESV2":
                method = PdfCryptMethod.AesV2;
                break;

            case "AESV3":
                method = PdfCryptMethod.AesV3;
                break;

            case "None" or "Identity":
                return true;

            default:
                warnings.Add(new(
                    PdfWarningKind.CryptFilterMethodUnsupported,
                    $"'{sourceName}': the crypt filter '{filterName}' uses the method '{filter.GetName("CFM")}', which the engine does not implement, so its pages cannot be read."));
                return false;
        }

        // Unlike the /Length of the encryption dictionary, the one of a crypt filter counts bytes.
        if (filter.GetInteger("Length") is { } length)
        {
            keyBits = (int)(length * 8);
        }

        return true;
    }

    /// <summary>
    /// Derives the file key of a document whose security handler is revision 2, 3 or 4, by algorithms
    /// 2, 6 and 7 of the specification. The dictionary also supplies the permissions and the metadata
    /// flag those algorithms take, so both are read here rather than passed in beside it.
    /// </summary>
    /// <param name="encryption">The encryption dictionary.</param>
    /// <param name="ownerEntry">The bytes of the <c>O</c> entry.</param>
    /// <param name="userEntry">The bytes of the <c>U</c> entry.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="keyLength">The number of bytes of the file key.</param>
    /// <param name="fileIdentifier">The first element of the trailer <c>ID</c>.</param>
    /// <param name="password">The password the caller supplied, or <see langword="null"/>.</param>
    /// <returns>The file key, or <see langword="null"/> when the password did not open the document.</returns>
    private static byte[]? TryOpenLegacy(
        PdfDictionary encryption,
        byte[] ownerEntry,
        byte[] userEntry,
        int revision,
        int keyLength,
        ReadOnlySpan<byte> fileIdentifier,
        string? password)
    {
        var permissions = unchecked((uint)(encryption.GetInteger("P") ?? 0));
        var encryptsMetadata = ReadEncryptsMetadata(encryption);
        var padded = PdfStandardSecurity.PadPassword(PdfStandardSecurity.EncodeLegacyPassword(password));

        // Algorithm 7: an owner password opens the document by yielding the user password, which the
        // file key is then computed from, so the owner password is what is tried first.
        var recovered = PdfStandardSecurity.RecoverUserPassword(
            PdfStandardSecurity.ComputeOwnerKey(padded, revision, keyLength),
            ownerEntry,
            revision);

        var key = PdfStandardSecurity.ComputeFileKey(
            recovered,
            revision,
            keyLength,
            ownerEntry,
            permissions,
            fileIdentifier,
            encryptsMetadata);

        if (MatchesUserEntry(key, userEntry, revision, fileIdentifier))
        {
            return key;
        }

        // Algorithm 6: the password is the user password itself.
        key = PdfStandardSecurity.ComputeFileKey(
            padded,
            revision,
            keyLength,
            ownerEntry,
            permissions,
            fileIdentifier,
            encryptsMetadata);

        return MatchesUserEntry(key, userEntry, revision, fileIdentifier) ? key : null;
    }

    /// <summary>Algorithm 6: determines whether a key is the one the <c>U</c> entry describes.</summary>
    /// <param name="key">The candidate file key.</param>
    /// <param name="userEntry">The bytes of the <c>U</c> entry.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="fileIdentifier">The first element of the trailer <c>ID</c>.</param>
    /// <returns><see langword="true"/> when the entry matches the key.</returns>
    private static bool MatchesUserEntry(
        byte[] key,
        byte[] userEntry,
        int revision,
        ReadOnlySpan<byte> fileIdentifier)
    {
        // Revision 3 and later only define the first sixteen bytes of the entry, the rest being
        // arbitrary padding, so only those are compared.
        var compared = revision == 2 ? 32 : 16;

        // The comparison is constant time because it decides whether a guessed password is right.
        return userEntry.Length >= compared
            && CryptographicOperations.FixedTimeEquals(
                PdfStandardSecurity.ComputeUserEntry(key, revision, fileIdentifier).AsSpan(0, compared),
                userEntry.AsSpan(0, compared));
    }

    /// <summary>
    /// Derives the file key of a document whose security handler is revision 5 or 6, by algorithms 12,
    /// 13 and 2.A or 2.B of the specification.
    /// </summary>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="ownerEntry">The bytes of the <c>O</c> entry.</param>
    /// <param name="userEntry">The bytes of the <c>U</c> entry.</param>
    /// <param name="ownerKey">The bytes of the <c>OE</c> entry.</param>
    /// <param name="userKey">The bytes of the <c>UE</c> entry.</param>
    /// <param name="password">The password the caller supplied, or <see langword="null"/>.</param>
    /// <returns>The file key, or <see langword="null"/> when the password did not open the document.</returns>
    private static byte[]? TryOpenAes256(
        int revision,
        byte[] ownerEntry,
        byte[] userEntry,
        byte[] ownerKey,
        byte[] userKey,
        string? password)
    {
        var encoded = PdfStandardSecurity.EncodeAes256Password(password);

        // The /O and /U entries are a 32 byte hash, an 8 byte validation salt and an 8 byte key salt,
        // and the file key itself is stored encrypted in /OE and /UE under the key that the key salt
        // produces. The owner password is checked first, which is the order the specification gives.
        if (MatchesAes256Hash(revision, encoded, ownerEntry, userEntry, isOwner: true))
        {
            return DecryptAes256(
                PdfStandardSecurity.ComputeAes256Hash(revision, encoded, ownerEntry.AsSpan(40, 8), userEntry),
                ownerKey);
        }

        if (MatchesAes256Hash(revision, encoded, userEntry, [], isOwner: false))
        {
            return DecryptAes256(
                PdfStandardSecurity.ComputeAes256Hash(revision, encoded, userEntry.AsSpan(40, 8), []),
                userKey);
        }

        return null;
    }

    /// <summary>Determines whether a password reproduces the hash an <c>O</c> or <c>U</c> entry holds.</summary>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="password">The encoded password.</param>
    /// <param name="entry">The <c>O</c> or <c>U</c> entry to compare against.</param>
    /// <param name="userEntry">The <c>U</c> entry, which the owner hash of an <c>O</c> entry incorporates.</param>
    /// <param name="isOwner">Whether the entry being checked is the <c>O</c> entry.</param>
    /// <returns><see langword="true"/> when the password matches.</returns>
    private static bool MatchesAes256Hash(
        int revision,
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> entry,
        ReadOnlySpan<byte> userEntry,
        bool isOwner)
    {
        if (entry.Length < 48)
        {
            return false;
        }

        // The validation salt is the second of the three parts: bytes 32 to 39 of the entry. The hash
        // of the owner password is taken over the user entry as well, which ties the two together.
        var hash = PdfStandardSecurity.ComputeAes256Hash(revision, password, entry.Slice(32, 8), isOwner ? userEntry : []);

        // The comparison is constant time because it decides whether a guessed password is right.
        return CryptographicOperations.FixedTimeEquals(hash.AsSpan(0, 32), entry[..32]);
    }

    /// <summary>Decrypts the <c>UE</c> or <c>OE</c> entry, which holds the file key.</summary>
    /// <param name="key">The key the entry was encrypted with.</param>
    /// <param name="value">The bytes of the entry.</param>
    /// <returns>The file key.</returns>
    private static byte[] DecryptAes256(byte[] key, ReadOnlySpan<byte> value) =>
        // Algorithm 13 encrypts these entries without padding, so nothing may be stripped from the
        // thirty-two bytes that come back: the file key is exactly what the cipher produced.
        PdfStandardSecurity.DecryptAesCbc(key, new byte[PdfStandardSecurity.AesBlockSize], value, removePadding: false);
}
