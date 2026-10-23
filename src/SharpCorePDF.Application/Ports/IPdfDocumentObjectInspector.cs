using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Reads a document's object graph and the operations of one of its content streams from a stream.
/// Implemented by the PDF engine.
/// </summary>
/// <remarks>
/// The engine-side counterpart of <see cref="Domain.Ports.IPdfObjectInspector"/>, exactly as
/// <see cref="IPdfPageLayoutReader"/> mirrors <see cref="Domain.Ports.IPdfLayoutReader"/>: the use case
/// opens a content source and hands the stream to this port, which keeps the engine free of the domain's
/// source abstraction and of I/O policy. Every member reads the stream from its current position to its
/// end and does not dispose it.
/// </remarks>
public interface IPdfDocumentObjectInspector
{
    /// <summary>Reads one node per object the document lists, in ascending object number.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The nodes of the graph, with the warnings the read made.</returns>
    Task<PdfReadResult<PdfObjectView>> ReadObjectsAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the operations of one page's content stream, in the order the page paints them.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="pageNumber">The one-based number of the page.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The operations of the page, with the warnings the read made.</returns>
    Task<PdfReadResult<PdfContentOperationView>> ReadOperationsAsync(
        string sourceName,
        Stream content,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default);
}
