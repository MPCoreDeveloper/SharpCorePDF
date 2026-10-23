using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// The engine's view of a PDF/A conversion: a source that is already open, and the profile to write
/// for.
/// </summary>
/// <remarks>
/// The application layer owns the source and hands over the stream it opened; the engine reads that
/// stream from its start and writes a complete document to the destination, which it flushes but does
/// not close.
/// </remarks>
public interface IPdfDocumentAWriter
{
    /// <summary>Converts the bytes of an open document to a PDF/A profile.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream over the source document.</param>
    /// <param name="destination">A writable stream that receives the archival document, when one is written.</param>
    /// <param name="profile">The profile to write for.</param>
    /// <param name="options">The document information, the identity and the output intent, or <see langword="null"/> for the defaults.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the conversion.</param>
    /// <returns>Whether a document was written, and what could not be satisfied when none was.</returns>
    Task<PdfAConversionResult> ConvertAsync(
        string sourceName,
        Stream content,
        Stream destination,
        PdfAProfile profile,
        PdfAConversionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
