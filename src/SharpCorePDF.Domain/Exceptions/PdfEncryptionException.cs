namespace SharpCorePDF.Domain.Exceptions;

/// <summary>
/// Thrown when a source document is encrypted in a way the engine cannot open, so that it cannot take
/// part in a merge.
/// </summary>
/// <remarks>
/// The engine opens the standard security handler with the password a source carries, so this is thrown
/// only for a document whose encryption stayed closed. <see cref="RequiresPassword"/> says which of the
/// two reasons applies: the password was missing or wrong, in which case asking for one is the answer,
/// or the document uses a security handler or a crypt filter the engine does not implement, in which
/// case no password would help. Call <see cref="Ports.IPdfInspector.InspectAsync"/> first to learn that
/// without an exception; it reports <see cref="ReadModels.PdfDocumentInfo.IsReadable"/> instead of
/// throwing.
/// </remarks>
public sealed class PdfEncryptionException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionException"/> class.</summary>
    /// <param name="sourceName">The display name of the encrypted source.</param>
    /// <param name="requiresPassword">
    /// <see langword="true"/> when a password is what stands in the way; <see langword="false"/> when the
    /// encryption itself is not one the engine implements.
    /// </param>
    public PdfEncryptionException(string sourceName, bool requiresPassword)
        : base(requiresPassword
            ? $"'{sourceName}' is encrypted and could not be opened: merge it with its user or owner password."
            : $"'{sourceName}' is encrypted with a security handler this engine cannot open, so it cannot be merged.")
    {
        SourceName = sourceName;
        RequiresPassword = requiresPassword;
    }

    /// <summary>Gets the display name of the encrypted source.</summary>
    public string SourceName { get; }

    /// <summary>Gets a value indicating whether a password is what stands in the way.</summary>
    public bool RequiresPassword { get; }
}
