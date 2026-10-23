using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>Decides whether the certificate that made a signature leads to an anchor the caller named.</summary>
/// <remarks>
/// <para>
/// The chain is built against exactly the anchors in <see cref="PdfTrustOptions.ExtraRoots"/>, never against the
/// machine store: a library that answers "trusted" on a machine it did not choose is answering a different
/// question than the caller asked. The certificates the signature embeds are offered as intermediates, so a
/// signature that carries its own chain needs nothing from the caller beyond the root.
/// </para>
/// <para>
/// Nothing here touches the network. Certificate downloads are disabled on the policy and revocation is
/// checked only from data the signature carries, which is why asking for an online check yields a problem
/// instead of a request. The chain of an embedded timestamp token is likewise not built: this release reports
/// that a token is present, and leaves deciding whether to trust its authority to the caller.
/// </para>
/// </remarks>
internal static class TrustEvaluator
{
    /// <summary>Evaluates the chain of the certificate that made a signature.</summary>
    /// <param name="fieldName">The name of the field, for the report.</param>
    /// <param name="cms">The parsed signature, when it parsed.</param>
    /// <param name="trust">The anchors and boundaries the caller chose.</param>
    /// <param name="problems">The list problems are added to.</param>
    /// <returns>Whether the chain reaches an anchor the caller named.</returns>
    public static PdfTrustResult Evaluate(
        string fieldName,
        CmsSignature? cms,
        PdfTrustOptions trust,
        List<PdfSignatureProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(problems);

        if (trust.AllowOnlineRevocationCheck)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.OnlineRevocationUnsupported,
                "No revocation check was made online: this release makes no network call during validation, so only revocation data embedded in the signature is available."));
        }

        if (cms?.Certificate is not { } certificate)
        {
            return PdfTrustResult.Untrusted;
        }

        using var chain = Chain(certificate, cms, trust);

        try
        {
            if (chain.Build(certificate))
            {
                return PdfTrustResult.Trusted;
            }
        }
        catch (CryptographicException exception)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.ChainIncomplete,
                $"The chain of '{fieldName}' could not be built: {exception.Message}"));

            return PdfTrustResult.Untrusted;
        }

        var flags = chain.ChainStatus.Aggregate(
            X509ChainStatusFlags.NoError,
            (all, status) => all | status.Status);

        if ((flags & X509ChainStatusFlags.UntrustedRoot) != 0)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.ChainUntrusted,
                $"The chain of '{fieldName}' ends in a root the caller did not name: {Describe(chain)}."));
        }
        else
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.ChainIncomplete,
                $"The chain of '{fieldName}' does not reach an anchor: {Describe(chain)}."));
        }

        return PdfTrustResult.Untrusted;
    }

    /// <summary>Builds a chain policy that knows the caller's anchors and the embedded intermediates.</summary>
    /// <param name="certificate">The signer certificate.</param>
    /// <param name="cms">The parsed signature.</param>
    /// <param name="trust">The anchors and boundaries the caller chose.</param>
    /// <returns>The chain, with its policy already set.</returns>
    private static X509Chain Chain(X509Certificate2 certificate, CmsSignature cms, PdfTrustOptions trust)
    {
        var chain = new X509Chain();
        var policy = chain.ChainPolicy;

        policy.RevocationMode = X509RevocationMode.NoCheck;
        policy.VerificationFlags = X509VerificationFlags.NoFlag;
        policy.DisableCertificateDownloads = true;
        policy.TrustMode = X509ChainTrustMode.CustomRootTrust;

        foreach (var root in trust.ExtraRoots ?? Array.Empty<X509Certificate2>())
        {
            if (root is null)
            {
                continue;
            }

            policy.CustomTrustStore.Add(root);
            policy.ExtraStore.Add(root);
        }

        foreach (var embedded in cms.Certificates.Where(
            embedded => !string.Equals(embedded.Thumbprint, certificate.Thumbprint, StringComparison.Ordinal)))
        {
            policy.ExtraStore.Add(embedded);
        }

        return chain;
    }

    /// <summary>Describes why a chain did not build, in the words of the platform.</summary>
    /// <param name="chain">The chain that failed.</param>
    /// <returns>The status of the chain, joined into one sentence fragment.</returns>
    private static string Describe(X509Chain chain) =>
        string.Join(
            "; ",
            chain.ChainStatus.Select(status => string.IsNullOrWhiteSpace(status.StatusInformation)
                ? status.Status.ToString()
                : $"{status.Status}: {status.StatusInformation.Trim()}"));
}
