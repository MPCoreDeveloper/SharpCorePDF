namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The cryptosystem a merged document is written with.
/// </summary>
/// <remarks>
/// The four values are the four combinations the engine implements: the two RC4 revisions a document
/// of the nineties needs, AES-128 with the crypt filters that PDF 1.5 introduced, and today's
/// AES-256. Revision 5 of the standard security handler is deliberately absent: it hashes a password
/// with a single SHA-256 pass, which revision 6 replaced with a hardened hash precisely because the
/// first was too cheap to guess.
/// </remarks>
public enum PdfEncryptionAlgorithm
{
    /// <summary>RC4 with a 40 bit key, security handler revision 2 — the encryption of PDF 1.4 and older.</summary>
    /// <remarks>
    /// Neither the cryptosystem nor the key length is a protection by modern standards; this exists
    /// for a workflow that some other tool in it cannot go beyond.
    /// </remarks>
    Rc4With40BitKey = 0,

    /// <summary>RC4 with a 128 bit key, security handler revision 3 — the encryption of PDF 1.4.</summary>
    Rc4With128BitKey = 1,

    /// <summary>
    /// AES-128 in CBC mode, security handler revision 4 — the <c>AESV2</c> crypt filter of PDF 1.5.
    /// </summary>
    AesWith128BitKey = 2,

    /// <summary>
    /// AES-256 in CBC mode, security handler revision 6 — the <c>AESV3</c> crypt filter, and the
    /// default.
    /// </summary>
    AesWith256BitKey = 3,
}
