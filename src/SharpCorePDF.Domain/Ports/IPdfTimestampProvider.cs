using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Obtains an RFC 3161 timestamp token for a signature value, from whatever authority the caller uses.
/// </summary>
/// <remarks>
/// The port exists so that the library never opens a socket: signing asks, the caller's implementation
/// answers, and a caller that has no authority simply leaves <see cref="PdfSignatureOptions.TimestampProvider"/>
/// unset. A provider that throws stops the signature from being written and the exception is surfaced with
/// the field it was for, so a half-signed document cannot be mistaken for a signed one.
/// </remarks>
public interface IPdfTimestampProvider
{
    /// <summary>Requests a timestamp token over a digest.</summary>
    /// <param name="digest">The digest of the signature value, computed with <paramref name="hashAlgorithm"/>.</param>
    /// <param name="hashAlgorithm">The algorithm the digest was computed with, which the request must repeat.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The DER-encoded <c>TimeStampToken</c> (a CMS <c>ContentInfo</c>) the authority returned.</returns>
    Task<ReadOnlyMemory<byte>> GetTimestampTokenAsync(
        ReadOnlyMemory<byte> digest,
        PdfHashAlgorithm hashAlgorithm,
        CancellationToken cancellationToken = default);
}
