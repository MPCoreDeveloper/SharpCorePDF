using System.Security.Cryptography.X509Certificates;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The trust anchors a validation is measured against, and the two boundaries this release states rather
/// than crosses.
/// </summary>
/// <remarks>
/// Passing no <see cref="PdfTrustOptions"/> at all reports <see cref="PdfTrustResult.NotEvaluated"/>.
/// Passing them evaluates the chain against exactly <see cref="ExtraRoots"/>: the machine store is never
/// consulted implicitly, because a library that answers "trusted" on a machine it did not choose is
/// answering a different question than the caller asked.
/// </remarks>
public sealed record PdfTrustOptions
{
    /// <summary>
    /// Gets the roots the caller trusts. An empty list still evaluates the chain, against no anchor at
    /// all, which is what proves a document is signed by a certificate the caller already knows.
    /// </summary>
    public IReadOnlyList<X509Certificate2>? ExtraRoots { get; init; }

    /// <summary>
    /// Gets a value indicating whether a revocation check should be attempted online.
    /// </summary>
    /// <remarks>
    /// Not implemented in this release, on purpose: a PDF library that quietly contacts a responder
    /// during validation is a privacy and a latency problem. Setting this to <see langword="true"/>
    /// yields a <see cref="PdfSignatureProblemKind.OnlineRevocationUnsupported"/> problem and no network
    /// call; revocation data embedded in the CMS is still reported.
    /// </remarks>
    public bool AllowOnlineRevocationCheck { get; init; }

    /// <summary>Gets a value indicating whether the time of an embedded timestamp is itself checked.</summary>
    /// <remarks>
    /// Reserved for the release that verifies tokens. This one embeds the caller's token and reports one it
    /// cannot read as <see cref="PdfSignatureProblemKind.TimestampInvalid"/>, but it verifies neither the
    /// signature of the authority nor the anchors behind it, so the value has no effect yet. It is here so
    /// that the options record keeps its shape when that check arrives.
    /// </remarks>
    public bool CheckTimestampTrust { get; init; } = true;
}
