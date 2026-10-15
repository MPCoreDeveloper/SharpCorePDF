namespace SharpCorePDF.Domain.Exceptions;

/// <summary>
/// Thrown when a source document is encrypted and therefore cannot participate in a merge.
/// </summary>
/// <remarks>
/// Decryption is intentionally out of scope for version 1: merging encrypted material would require
/// per-document cryptography and a decision about the encryption of the result. Call
/// <see cref="Ports.IPdfInspector.InspectAsync"/> first to detect encryption without an exception;
/// it reports <see cref="ReadModels.PdfDocumentInfo.IsEncrypted"/> instead of throwing.
/// </remarks>
public sealed class PdfEncryptionException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionException"/> class.</summary>
    /// <param name="sourceName">The display name of the encrypted source.</param>
    /// <param name="requiresPassword">
    /// <see langword="true"/> when the document cannot even be opened without a user password;
    /// <see langword="false"/> when the document opens with an empty password but its streams are encrypted.
    /// </param>
    public PdfEncryptionException(string sourceName, bool requiresPassword)
        : base(requiresPassword
            ? $"'{sourceName}' is encrypted and requires a password. Encrypted documents cannot be merged."
            : $"'{sourceName}' is encrypted. Encrypted documents cannot be merged.")
    {
        SourceName = sourceName;
        RequiresPassword = requiresPassword;
    }

    /// <summary>Gets the display name of the encrypted source.</summary>
    public string SourceName { get; }

    /// <summary>Gets a value indicating whether a user password is required to open the document.</summary>
    public bool RequiresPassword { get; }
}
