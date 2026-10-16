using System.Text;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The encryption a merge writes its result with. A merged document is written in the clear unless
/// these options are supplied.
/// </summary>
/// <remarks>
/// <para>
/// Only the two kinds of object a PDF encrypts — strings and the bodies of streams — are encrypted, and
/// they are encrypted as they are written, so a document that is merged out of encrypted sources and
/// re-encrypted is protected by the new passwords alone: the passwords of the sources are not needed
/// again and leave no trace in the result.
/// </para>
/// <para>
/// The passwords are text. Revisions 2 to 4 predate any agreement on what that text means, so a
/// password that survives a Latin-1 round trip is written as Latin-1 and anything else as UTF-8 — Adobe,
/// qpdf and pypdf all hash the Latin-1 bytes of the same text, which is what makes a document written
/// here open in them — while revisions 5 and 6 are UTF-8 by definition. SASL preparation is not applied,
/// which matches what the reader accepts.
/// </para>
/// <para>
/// A password longer than the revision defines is truncated, which is what the specification
/// prescribes rather than a bug: thirty-two bytes for revisions 2 to 4 and 127 bytes for revision 6.
/// Both passwords are checked, so a caller learns that the password they set is not the password that
/// will be needed.
/// </para>
/// <para>
/// An empty user password means the document opens without being asked for one, which is the shape a
/// document that is only protected against editing has: the owner password is what a reader must
/// supply to get past the permissions. Encrypting without any password at all is allowed but pointless
/// — anyone can derive the key from the file — and is not refused, because the difference between the
/// two is a decision for the caller.
/// </para>
/// </remarks>
public sealed record PdfEncryptionOptions
{
    /// <summary>Gets the default options: AES-256 with no password and no restriction.</summary>
    public static PdfEncryptionOptions Default { get; } = new();

    /// <summary>Gets the cryptosystem to write with.</summary>
    public PdfEncryptionAlgorithm Algorithm { get; init; } = PdfEncryptionAlgorithm.AesWith256BitKey;

    /// <summary>
    /// Gets the password that opens the document and is subject to <see cref="Permissions"/>. An empty
    /// password means the document opens without one.
    /// </summary>
    public string UserPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the password that opens the document with every permission. When it is empty the user
    /// password is used as the owner password as well, so a document never needs more credentials than
    /// it was written with.
    /// </summary>
    public string OwnerPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets what a reader that supplies the user password may do. Ignored by a reader that supplies the
    /// owner password, which may do anything.
    /// </summary>
    public PdfPermissions Permissions { get; init; } = PdfPermissions.All;

    /// <summary>Validates these options.</summary>
    /// <param name="targetVersion">The version the document is written as.</param>
    /// <returns>Every problem found, or an empty list when the options are usable.</returns>
    public IReadOnlyList<string> Validate(PdfVersion targetVersion)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Algorithm))
        {
            errors.Add($"encryption algorithm '{(int)Algorithm}' is not defined.");
        }

        if ((Permissions & ~PdfPermissions.All) != 0)
        {
            errors.Add(
                $"the permissions '{(int)Permissions}' contain bits that are not one of the defined permissions.");
        }

        // The crypt filters that AES-128 is written with arrived with PDF 1.5; AES-256 followed in
        // PDF 1.7, so the version the document declares must be at least the one that defines it.
        if (Algorithm == PdfEncryptionAlgorithm.AesWith128BitKey && targetVersion.CompareTo(PdfVersion.V1_5) < 0)
        {
            errors.Add(
                $"target version {targetVersion} is too old for AES-128: the crypt filters it is written with need PDF 1.5 or newer.");
        }

        if (Algorithm == PdfEncryptionAlgorithm.AesWith256BitKey && targetVersion.CompareTo(PdfVersion.V1_7) < 0)
        {
            errors.Add($"target version {targetVersion} is too old for AES-256: it needs PDF 1.7 or newer.");
        }

        // A password is truncated by the algorithm rather than rejected, so a caller that sets one the
        // document cannot distinguish from a shorter one is told here instead of finding out later.
        var maximum = Algorithm == PdfEncryptionAlgorithm.AesWith256BitKey ? 127 : 32;

        foreach (var (name, password) in new[] { ("user", UserPassword), ("owner", OwnerPassword) })
        {
            if (password is null)
            {
                errors.Add($"the {name} password must be a string; use an empty one for a document that opens without a password.");
                continue;
            }

            if (Encoding.UTF8.GetByteCount(password) > maximum)
            {
                errors.Add(
                    $"the {name} password does not fit the {maximum} bytes {Algorithm} can use, so it would be truncated.");
            }
        }

        return errors;
    }
}
