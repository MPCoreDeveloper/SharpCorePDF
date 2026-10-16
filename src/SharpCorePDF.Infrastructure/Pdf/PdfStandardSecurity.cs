using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

// The PDF specification defines MD5 and RC4 as the cryptography of the Standard Security Handler
// revisions 2 to 4, so a document written or read with those revisions is impossible without them.
// Nothing here chooses a cipher: the algorithms are the specification's, in both directions.
#pragma warning disable CA5350 // Do not use weak cryptographic algorithms
#pragma warning disable CA5351 // Do not use broken cryptographic algorithms

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The algorithms of the Standard Security Handler of the PDF specification, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is one numbered algorithm of the specification and nothing else, so the class
/// that decrypts a document (<see cref="PdfSecurityHandler"/>) and the one that encrypts a merged
/// document (<see cref="PdfSecurityWriter"/>) cannot drift apart: the file key a document is written
/// with is derived by exactly the code that recovers it again.
/// </para>
/// <para>
/// Revisions 2 to 4 hash with MD5 and encrypt with RC4 because that is what the specification defines
/// for them; revisions 5 and 6 hash with SHA-2 instead. Revisions 2 to 6 derive their keys from the
/// password, the file identifier and the permissions, so none of this replaces a real secret store.
/// </para>
/// </remarks>
internal static class PdfStandardSecurity
{
    /// <summary>AES works in 16 byte blocks, which is also the size of the initialization vector.</summary>
    public const int AesBlockSize = 16;

    /// <summary>How much of a Unicode password revision 5 and 6 use.</summary>
    public const int MaximumPasswordBytes = 127;

    /// <summary>
    /// The padding string of algorithm 2. A password shorter than 32 bytes is padded with its leading
    /// bytes, and an empty password is replaced by the whole string.
    /// </summary>
    public static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    /// <summary>Algorithm 2: pads a password to the thirty-two bytes that the legacy revisions hash.</summary>
    /// <param name="password">The encoded password, which may be longer than thirty-two bytes.</param>
    /// <returns>The padded password.</returns>
    public static byte[] PadPassword(ReadOnlySpan<byte> password)
    {
        var result = new byte[PasswordPadding.Length];

        if (password.Length >= PasswordPadding.Length)
        {
            // A password longer than thirty-two bytes is truncated to the first thirty-two.
            password[..PasswordPadding.Length].CopyTo(result);
            return result;
        }

        password.CopyTo(result);
        PasswordPadding.AsSpan(0, PasswordPadding.Length - password.Length)
            .CopyTo(result.AsSpan(password.Length));

        return result;
    }

    /// <summary>Encodes a password the way revisions 2 to 4 expect it.</summary>
    /// <param name="password">The password, or <see langword="null"/>.</param>
    /// <returns>The bytes that are padded into the 32 byte password the algorithms hash.</returns>
    /// <remarks>
    /// These revisions predate any agreement on the encoding of a password, so a producer writes its
    /// host encoding. Latin-1 is the reading that cannot corrupt a European password; text that does
    /// not fit is encoded as UTF-8, which is what a Unicode producer most likely wrote.
    /// </remarks>
    public static byte[] EncodeLegacyPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return [];
        }

        var latin1 = Encoding.Latin1.GetBytes(password);

        return string.Equals(Encoding.Latin1.GetString(latin1), password, StringComparison.Ordinal)
            ? latin1
            : Encoding.UTF8.GetBytes(password);
    }

    /// <summary>Uses the password of revision 5 and 6 the way those revisions define it.</summary>
    /// <param name="password">The password, or <see langword="null"/>.</param>
    /// <returns>The bytes that are fed to the hash.</returns>
    public static byte[] EncodeAes256Password(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return [];
        }

        // Revision 5 and 6 define the password as UTF-8, truncated to its first 127 bytes.
        var utf8 = Encoding.UTF8.GetBytes(password);

        return utf8.Length <= MaximumPasswordBytes ? utf8 : utf8[..MaximumPasswordBytes];
    }

    /// <summary>
    /// Algorithm 3: derives the RC4 key that the <c>O</c> entry is encrypted with, from a padded
    /// password.
    /// </summary>
    /// <param name="padded">The padded password.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="keyLength">The number of bytes of the file key.</param>
    /// <returns>The key.</returns>
    public static byte[] ComputeOwnerKey(ReadOnlySpan<byte> padded, int revision, int keyLength)
    {
        var digest = MD5.HashData(padded); // NOSONAR

        if (revision >= 3)
        {
            for (var round = 0; round < 50; round++)
            {
                digest = MD5.HashData(digest); // NOSONAR
            }
        }

        return digest[..keyLength];
    }

    /// <summary>Algorithm 3, backwards: recovers the user password from the <c>O</c> entry.</summary>
    /// <param name="ownerKey">The key produced from the supplied owner password.</param>
    /// <param name="ownerEntry">The bytes of the <c>O</c> entry.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <returns>The candidate user password, still padded.</returns>
    public static byte[] RecoverUserPassword(ReadOnlySpan<byte> ownerKey, ReadOnlySpan<byte> ownerEntry, int revision)
    {
        if (revision == 2)
        {
            return PdfRc4.Transform(ownerKey, ownerEntry);
        }

        // Revision 3 encrypts the entry twenty times, with the key of each round differing from the
        // original by the round counter, so decryption walks the counters backwards.
        var current = ownerEntry.ToArray();

        for (var counter = 19; counter >= 0; counter--)
        {
            var roundKey = new byte[ownerKey.Length];

            for (var index = 0; index < roundKey.Length; index++)
            {
                roundKey[index] = (byte)(ownerKey[index] ^ counter);
            }

            current = PdfRc4.Transform(roundKey, current);
        }

        return current;
    }

    /// <summary>Algorithm 3, forwards: builds the <c>O</c> entry from both passwords.</summary>
    /// <param name="paddedOwner">The padded owner password.</param>
    /// <param name="paddedUser">The padded user password.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="keyLength">The number of bytes of the file key.</param>
    /// <returns>The thirty-two bytes of the <c>O</c> entry.</returns>
    public static byte[] ComputeOwnerEntry(
        ReadOnlySpan<byte> paddedOwner,
        ReadOnlySpan<byte> paddedUser,
        int revision,
        int keyLength)
    {
        var key = ComputeOwnerKey(paddedOwner, revision, keyLength);

        if (revision == 2)
        {
            return PdfRc4.Transform(key, paddedUser);
        }

        // Revision 3 and later encrypt the padded user password twenty times, with the key of each
        // round differing from the original by the round counter.
        var entry = PdfRc4.Transform(key, paddedUser);

        for (var counter = 1; counter <= 19; counter++)
        {
            var roundKey = new byte[key.Length];

            for (var index = 0; index < roundKey.Length; index++)
            {
                roundKey[index] = (byte)(key[index] ^ counter);
            }

            entry = PdfRc4.Transform(roundKey, entry);
        }

        return entry;
    }

    /// <summary>Algorithm 2: computes the file key from a padded password.</summary>
    /// <param name="password">The padded password, or the user password recovered from the <c>O</c> entry.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="keyLength">The number of bytes of the file key.</param>
    /// <param name="ownerEntry">The bytes of the <c>O</c> entry.</param>
    /// <param name="permissions">The value of the <c>P</c> entry, as the specification's unsigned integer.</param>
    /// <param name="fileIdentifier">The first element of the trailer <c>ID</c>.</param>
    /// <param name="encryptsMetadata">Whether the document encrypts its metadata stream.</param>
    /// <returns>The file key.</returns>
    public static byte[] ComputeFileKey(
        ReadOnlySpan<byte> password,
        int revision,
        int keyLength,
        ReadOnlySpan<byte> ownerEntry,
        uint permissions,
        ReadOnlySpan<byte> fileIdentifier,
        bool encryptsMetadata)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5); // NOSONAR
        hash.AppendData(password);
        hash.AppendData(ownerEntry);

        Span<byte> flags = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(flags, permissions);
        hash.AppendData(flags);
        hash.AppendData(fileIdentifier);

        if (revision >= 4 && !encryptsMetadata)
        {
            hash.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        var digest = hash.GetHashAndReset();

        if (revision >= 3)
        {
            for (var round = 0; round < 50; round++)
            {
                digest = MD5.HashData(digest.AsSpan(0, keyLength)); // NOSONAR
            }
        }

        return digest[..keyLength];
    }

    /// <summary>Algorithms 4 and 5: computes the value the <c>U</c> entry should hold for a key.</summary>
    /// <param name="key">The file key.</param>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="fileIdentifier">The first element of the trailer <c>ID</c>.</param>
    /// <returns>The value, which is thirty-two bytes.</returns>
    public static byte[] ComputeUserEntry(byte[] key, int revision, ReadOnlySpan<byte> fileIdentifier)
    {
        if (revision == 2)
        {
            return PdfRc4.Transform(key, PasswordPadding);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5); // NOSONAR
        hash.AppendData(PasswordPadding);
        hash.AppendData(fileIdentifier);

        var entry = PdfRc4.Transform(key, hash.GetHashAndReset());

        for (var counter = 1; counter <= 19; counter++)
        {
            var roundKey = new byte[key.Length];

            for (var index = 0; index < roundKey.Length; index++)
            {
                roundKey[index] = (byte)(key[index] ^ counter);
            }

            entry = PdfRc4.Transform(roundKey, entry);
        }

        // The specification asks for sixteen more bytes of arbitrary padding, which the comparison
        // never looks at; zeros are as arbitrary as anything else.
        var result = new byte[32];
        entry.CopyTo(result, 0);
        return result;
    }

    /// <summary>
    /// Algorithm 1: derives the key of one object from the file key, the object number and the
    /// generation number.
    /// </summary>
    /// <param name="fileKey">The file key of the document.</param>
    /// <param name="objectKeyLength">The number of bytes of the file key that take part.</param>
    /// <param name="objectNumber">The object number of the indirect object.</param>
    /// <param name="generation">The generation number of that object.</param>
    /// <param name="salted">Whether the AES marker is added for backward compatibility.</param>
    /// <returns>The key, which never exceeds sixteen bytes.</returns>
    public static byte[] DeriveObjectKey(
        ReadOnlySpan<byte> fileKey,
        int objectKeyLength,
        long objectNumber,
        int generation,
        bool salted)
    {
        Span<byte> material = stackalloc byte[16 + 5 + 4];
        var length = objectKeyLength;
        fileKey[..Math.Min(length, fileKey.Length)].CopyTo(material);

        // The low order three bytes of the object number and the low order two bytes of the generation
        // number, in that order and low-order byte first.
        material[length] = (byte)objectNumber;
        material[length + 1] = (byte)(objectNumber >> 8);
        material[length + 2] = (byte)(objectNumber >> 16);
        material[length + 3] = (byte)generation;
        material[length + 4] = (byte)(generation >> 8);
        length += 5;

        if (salted)
        {
            material[length++] = 0x73;
            material[length++] = 0x41;
            material[length++] = 0x6C;
            material[length++] = 0x54;
        }

        // The hash is required to be MD5 by algorithm 1; it is a key derivation here, not a signature
        // or a password check, so its collision weakness buys an attacker nothing that matters.
        var hash = MD5.HashData(material[..length]); // NOSONAR
        return hash[..Math.Min(objectKeyLength + 5, 16)];
    }

    /// <summary>Removes the PKCS#7 padding that the AES methods add to a value.</summary>
    /// <param name="data">The decrypted bytes.</param>
    /// <returns>The bytes without their padding.</returns>
    /// <remarks>
    /// A value whose tail does not hold a well formed padding is returned as it is. A damaged object
    /// should surface as damaged content rather than make the whole document unreadable, so this
    /// neither throws nor guesses.
    /// </remarks>
    public static byte[] RemovePadding(byte[] data)
    {
        var count = data.Length == 0 ? 0 : data[^1];

        if (count is 0 or > AesBlockSize || count > data.Length)
        {
            return data;
        }

        for (var index = data.Length - count; index < data.Length; index++)
        {
            if (data[index] != count)
            {
                return data;
            }
        }

        return data[..(data.Length - count)];
    }

    /// <summary>Decrypts a value with AES in CBC mode.</summary>
    /// <param name="key">The key, which is sixteen bytes for AES-128 and the file key for AES-256.</param>
    /// <param name="iv">The sixteen byte initialization vector that was stored in front of the ciphertext.</param>
    /// <param name="ciphertext">The ciphertext.</param>
    /// <param name="removePadding">Whether the PKCS#7 padding of the value is removed.</param>
    /// <returns>The decrypted bytes.</returns>
    public static byte[] DecryptAesCbc(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> ciphertext,
        bool removePadding)
    {
        // A ciphertext whose length is not a whole number of blocks was not written by a conforming
        // producer, and the cipher itself refuses to touch it, so it is answered with nothing.
        if (ciphertext.IsEmpty || ciphertext.Length % AesBlockSize != 0)
        {
            return [];
        }

        using var aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.IV = iv.ToArray();

        // The padding is stripped here rather than by the cipher, whose own handling raises an
        // exception for a damaged value where an empty answer is wanted.
        var plain = aes.DecryptCbc(ciphertext, aes.IV, PaddingMode.None);

        return removePadding ? RemovePadding(plain) : plain;
    }

    /// <summary>Encrypts a value with AES in CBC mode.</summary>
    /// <param name="key">The key, which is sixteen bytes for AES-128 and the file key for AES-256.</param>
    /// <param name="iv">The sixteen byte initialization vector.</param>
    /// <param name="plain">The bytes to encrypt.</param>
    /// <param name="addPadding">Whether PKCS#7 padding is added, which the <c>UE</c>, <c>OE</c> and
    /// <c>Perms</c> entries of revision 5 and 6 are encrypted without.</param>
    /// <returns>The ciphertext.</returns>
    public static byte[] EncryptAesCbc(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> plain,
        bool addPadding)
    {
        using var aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.IV = iv.ToArray();

        // Algorithm 2.B repeats the block of revision 6 sixty-four times, and sixty-four is a multiple
        // of sixteen, so that block is already whole and padding it would add a block the
        // specification does not define.
        return aes.EncryptCbc(plain, aes.IV, addPadding ? PaddingMode.PKCS7 : PaddingMode.None);
    }

    /// <summary>Encrypts a sixteen byte block with AES-256 in ECB mode, which the <c>Perms</c> entry uses.</summary>
    /// <param name="key">The thirty-two byte file key.</param>
    /// <param name="block">The sixteen bytes of the block.</param>
    /// <returns>The encrypted block.</returns>
    public static byte[] EncryptAesEcb(ReadOnlySpan<byte> key, ReadOnlySpan<byte> block)
    {
        using var aes = Aes.Create();
        aes.Key = key.ToArray();

        return aes.EncryptEcb(block, PaddingMode.None);
    }

    /// <summary>
    /// Algorithm 2.A for revision 5 and algorithm 2.B for revision 6: hashes a password with a salt and,
    /// for the owner password, the <c>U</c> entry.
    /// </summary>
    /// <param name="revision">The value of the <c>R</c> entry.</param>
    /// <param name="password">The encoded password.</param>
    /// <param name="salt">The validation or key salt of the entry.</param>
    /// <param name="userEntry">The <c>U</c> entry when the owner password is hashed, otherwise empty.</param>
    /// <returns>The hash, whose first thirty-two bytes are the one that is compared or used as a key.</returns>
    public static byte[] ComputeAes256Hash(
        int revision,
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> userEntry)
    {
        var seed = new byte[password.Length + salt.Length + userEntry.Length];
        password.CopyTo(seed);
        salt.CopyTo(seed.AsSpan(password.Length));
        userEntry.CopyTo(seed.AsSpan(password.Length + salt.Length));

        var digest = SHA256.HashData(seed);

        if (revision < 6)
        {
            return digest;
        }

        // Revision 5 hashed the seed once. Revision 6 multiplies the cost of guessing a password by
        // encrypting that seed over and over, choosing the next digest from the output itself, so the
        // work per guess is hundreds of rounds rather than one.
        var rounds = 0;

        // What each round encrypts is the password, the digest of the round before and the user entry,
        // repeated until the block is sixty-four times as long as those three together. The digest
        // grows from thirty-two bytes to sixty-four when a round selects SHA-384 or SHA-512, so the
        // block is built in a buffer that is sized for the longest of them and sliced per round.
        var block = new byte[(password.Length + 64 + userEntry.Length) * 64];

        while (true)
        {
            rounds++;

            var unit = password.Length + digest.Length + userEntry.Length;
            var length = unit * 64;

            for (var offset = 0; offset < length; offset += unit)
            {
                password.CopyTo(block.AsSpan(offset));
                digest.CopyTo(block.AsSpan(offset + password.Length));
                userEntry.CopyTo(block.AsSpan(offset + password.Length + digest.Length));
            }

            // The key and the initialization vector of the round are the two halves of the digest,
            // which is why every digest of the loop is at least thirty-two bytes long.
            var encrypted = EncryptAesCbc(
                digest.AsSpan(0, AesBlockSize),
                digest.AsSpan(AesBlockSize, AesBlockSize),
                block.AsSpan(0, length),
                addPadding: false);

            // The next digest is chosen by the sum of the first sixteen bytes modulo three, which is
            // the value of the whole block read as a big-endian number modulo three, since 256 and one
            // leave the same remainder; summing the bytes needs no arithmetic wider than an int.
            var selector = 0;

            for (var index = 0; index < AesBlockSize; index++)
            {
                selector += encrypted[index];
            }

            digest = (selector % 3) switch
            {
                0 => SHA256.HashData(encrypted),
                1 => SHA384.HashData(encrypted),
                _ => SHA512.HashData(encrypted),
            };

            // The loop ends when the last byte of the encrypted block has fallen below the number of
            // rounds less thirty-two, which the specification guarantees within a few hundred rounds.
            if (rounds >= 64 && encrypted[^1] <= rounds - 32)
            {
                break;
            }
        }

        return digest[..32];
    }
}
