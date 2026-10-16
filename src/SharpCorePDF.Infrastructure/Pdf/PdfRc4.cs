namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The RC4 stream cipher, exactly as the Standard Security Handler of revisions 2 to 4 needs it.
/// </summary>
/// <remarks>
/// <para>
/// RC4 is not a modern cipher, and this class exists for one reason only: the PDF specification
/// defines RC4 as the encryption of documents written with security handler revisions 2, 3 and 4, so
/// those documents cannot be decrypted without it. The engine uses it to <em>read</em> such
/// documents; it never chooses RC4 for output unless a caller explicitly asks for that.
/// </para>
/// <para>
/// RC4 is its own inverse: the same call both encrypts and decrypts. The algorithm is the textbook
/// key scheduling and pseudo-random generation of the specification, operating on a 256 byte state.
/// </para>
/// </remarks>
internal static class PdfRc4
{
    /// <summary>Transforms bytes with RC4 in place; the operation is its own inverse.</summary>
    /// <param name="key">The key, which may be any length but must not be empty.</param>
    /// <param name="data">The bytes to transform.</param>
    public static void TransformInPlace(ReadOnlySpan<byte> key, Span<byte> data)
    {
        if (key.IsEmpty || data.IsEmpty)
        {
            return;
        }

        // The state is a permutation of the 256 possible byte values, so it lives on the stack: a
        // document decrypted object by object would otherwise allocate a small array per object.
        Span<byte> state = stackalloc byte[256];

        for (var index = 0; index < 256; index++)
        {
            state[index] = (byte)index;
        }

        var j = 0;

        for (var index = 0; index < 256; index++)
        {
            j = (j + state[index] + key[index % key.Length]) & 0xFF;
            (state[index], state[j]) = (state[j], state[index]);
        }

        var i = 0;
        j = 0;

        for (var index = 0; index < data.Length; index++)
        {
            i = (i + 1) & 0xFF;
            j = (j + state[i]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
            data[index] ^= state[(state[i] + state[j]) & 0xFF];
        }
    }

    /// <summary>Transforms bytes with RC4 and returns a new array, leaving the input untouched.</summary>
    /// <param name="key">The key, which may be any length but must not be empty.</param>
    /// <param name="data">The bytes to transform.</param>
    /// <returns>The transformed bytes.</returns>
    public static byte[] Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        var result = data.ToArray();
        TransformInPlace(key, result);
        return result;
    }
}
