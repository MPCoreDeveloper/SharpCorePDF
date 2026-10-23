using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.Ports;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Everything a signature needs besides the document: the certificate and its key, the digest, the field
/// it is written into and the entries a reader shows.
/// </summary>
/// <remarks>
/// <para>
/// A certificate without a private key is refused before any byte is written, because a signature cannot
/// be produced without it and writing a revision that then fails would leave the caller with a document
/// that looks signed and is not.
/// </para>
/// <para>
/// The timestamp is the caller's: this library never opens a socket. When
/// <see cref="TimestampProvider"/> is set, the value of the signature is offered to it, and a token that
/// comes back is embedded as the signature timestamp attribute of the CMS. When the provider throws, the
/// signature is not written and the exception is surfaced with the field it was for.
/// </para>
/// </remarks>
public sealed record PdfSignatureOptions
{
    /// <summary>Gets the certificate the signature is made with; it must carry its private key.</summary>
    public required X509Certificate2 Certificate { get; init; }

    /// <summary>
    /// Gets certificates to embed along with the signer's, so a reader that trusts the root can build the
    /// chain from the document alone.
    /// </summary>
    public IReadOnlyList<X509Certificate2>? Chain { get; init; }

    /// <summary>Gets the digest the signature is computed with.</summary>
    public PdfHashAlgorithm HashAlgorithm { get; init; } = PdfHashAlgorithm.Sha256;

    /// <summary>Gets the name of the signature field, which is what a reader lists.</summary>
    public string FieldName { get; init; } = "Signature1";

    /// <summary>Gets the reason for the signature, written as the <c>/Reason</c> entry.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the place the signature was made, written as the <c>/Location</c> entry.</summary>
    public string? Location { get; init; }

    /// <summary>Gets the contact information of the signer, written as the <c>/ContactInfo</c> entry.</summary>
    public string? ContactInfo { get; init; }

    /// <summary>Gets the visible appearance, or <see langword="null"/> to write an invisible widget.</summary>
    public PdfSignatureAppearance? Appearance { get; init; }

    /// <summary>Gets the provider of a timestamp token, or <see langword="null"/> to sign without one.</summary>
    public IPdfTimestampProvider? TimestampProvider { get; init; }

    /// <summary>
    /// Gets a value indicating whether the signature is written as CAdES (<c>ETSI.CAdES.detached</c>)
    /// rather than as a plain PKCS#7 signature (<c>adbe.pkcs7.detached</c>).
    /// </summary>
    /// <remarks>
    /// CAdES adds the signing-certificate attribute of ETSI EN 319 122, which is what the PDF 2.0 and
    /// PAdES readers expect. The signature value is a detached CMS either way.
    /// </remarks>
    public bool UseCades { get; init; }

    /// <summary>
    /// Gets the moment written as the signing time, or <see langword="null"/> to use the current moment.
    /// </summary>
    /// <remarks>
    /// A fixed moment is what makes two signatures of the same document byte-for-byte comparable in a test
    /// or an audit, exactly as <c>PdfEditOutputOptions.ModificationDate</c> does for an edit.
    /// </remarks>
    public DateTimeOffset? SigningTime { get; init; }

    /// <summary>Validates these options.</summary>
    /// <returns>Every problem found, or an empty list when the options are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(FieldName))
        {
            problems.Add("FieldName must be a name a reader can show.");
        }
        else if (FieldName.Any(character => character is < ' ' or > '~' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%' or '#'))
        {
            problems.Add($"FieldName '{FieldName}' contains a character a PDF name cannot hold.");
        }

        if (Appearance is { } appearance)
        {
            problems.AddRange(appearance.Validate());
        }

        return problems;
    }
}
