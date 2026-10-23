using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>Finds the name a certificate offers for its subject.</summary>
internal static class CertificateNames
{
    /// <summary>Finds the name a certificate lists for its subject.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The simple name when the certificate has one, its subject otherwise.</returns>
    /// <remarks>
    /// The simple name is what a reader shows — the common name for a person, the organisation for a company.
    /// Some subjects are not valid distinguished names at all, so the fallback matters: a report that names a
    /// certificate by its raw subject still names it.
    /// </remarks>
    public static string Simple(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        try
        {
            var name = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (CryptographicException)
        {
            // A subject this platform cannot parse still has a printable distinguished name.
        }

        return certificate.Subject;
    }
}
