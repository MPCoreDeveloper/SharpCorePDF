using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Reads the structure of a PDF document from a stream and keeps the bytes it read. Implemented by the PDF
/// engine.
/// </summary>
/// <remarks>
/// <para>
/// A second, narrower port rather than a member on <see cref="IPdfDocumentReader"/>, so that an implementor
/// of that port still compiles and a use case given a reader without this capability keeps working by
/// reading the stream again. The use case prefers this one when the reader it was given implements it.
/// </para>
/// <para>
/// The reason it exists: a merge and an edit both inspect their source before writing it out, and without
/// the bytes the engine reads the same document a second time. On a 100 MB source that is 100 MB of garbage
/// per operation — see [ADR-004](../../../docs/adr/0004-buffer-model.md) and `docs/measurements.md` §10.
/// </para>
/// </remarks>
public interface IPdfDocumentBytesReader
{
    /// <summary>Reads the structure of a document and keeps the bytes it was read from.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What the document contains, and the bytes it contains it in.</returns>
    Task<PdfDocumentBytes> ReadWithBytesAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);
}
