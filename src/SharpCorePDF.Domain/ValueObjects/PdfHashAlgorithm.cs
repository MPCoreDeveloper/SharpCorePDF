namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The digest algorithm a signature is computed with.
/// </summary>
/// <remarks>
/// <para>
/// Only the SHA-2 family is part of the contract. SHA-1 is not offered even though older readers accept
/// it, because a signature is only worth as much as the digest under it.
/// </para>
/// <para>
/// The set is open: a later release may add SHA-3, which is additive rather than breaking, so a
/// <c>switch</c> over this enum must keep a <c>default</c> arm.
/// </para>
/// </remarks>
public enum PdfHashAlgorithm
{
    /// <summary>SHA-256, the default and the digest every current reader understands.</summary>
    Sha256,

    /// <summary>SHA-384, for callers whose certificate policy demands a 384-bit digest.</summary>
    Sha384,

    /// <summary>SHA-512, for callers whose certificate policy demands a 512-bit digest.</summary>
    Sha512,
}
