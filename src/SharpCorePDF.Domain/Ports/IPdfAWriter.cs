using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Writes a document for archival delivery: a complete PDF/A document, not an incremental update.
/// </summary>
/// <remarks>
/// <para>
/// The writer embeds every font it can and refuses a document whose fonts it cannot embed, writes the
/// XMP packet and the output intent the profile requires, and removes or refuses the features the
/// profile forbids. It never labels a document PDF/A that it knows is not one: when a requirement
/// cannot be satisfied, nothing is written and the result names every reason.
/// </para>
/// <para>
/// The source is only ever read. The destination receives a complete document and is flushed but not
/// closed. Encryption of the result is not offered, because the profile forbids it.
/// </para>
/// </remarks>
public interface IPdfAWriter
{
    /// <summary>Converts a document to a PDF/A profile.</summary>
    /// <param name="source">The document to convert.</param>
    /// <param name="destination">A writable stream that receives the archival document, when one is written.</param>
    /// <param name="profile">The profile to write for.</param>
    /// <param name="options">The document information, the identity and the output intent, or <see langword="null"/> for the defaults.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether a document was written, and what could not be satisfied when none was.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The destination is not writable, the profile is not defined, or the options are not usable.
    /// </exception>
    /// <exception cref="Exceptions.PdfEncryptionException">The source is encrypted and cannot be opened.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The source is not a readable PDF document.</exception>
    Task<PdfAConversionResult> ConvertAsync(
        IPdfContentSource source,
        Stream destination,
        PdfAProfile profile,
        PdfAConversionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
