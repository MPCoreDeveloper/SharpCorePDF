using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Reads the object graph of a PDF document and the operations of its content streams, without changing
/// either.
/// </summary>
/// <remarks>
/// <para>
/// The graph is the document as the reader found it: every object the cross-reference table lists becomes
/// one node, references are reported with the number they name rather than followed, and a reference to an
/// object that is not there stays in the graph with <see cref="PdfObjectView.IsResolved"/> cleared and a
/// <see cref="ValueObjects.PdfWarningKind.DanglingReference"/> warning. Reading the graph therefore
/// resolves every object of the document, which is what makes it the place to look when a document
/// misbehaves.
/// </para>
/// <para>
/// A document that is encrypted and could not be opened yields no objects and the warning that says why,
/// exactly as the other reads report it. Only content that is not a PDF at all raises
/// <see cref="Exceptions.PdfSourceFormatException"/>.
/// </para>
/// </remarks>
public interface IPdfObjectInspector
{
    /// <summary>Reads one node per object the document lists, in ascending object number.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The nodes of the graph, with the warnings the read made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfObjectView>> ReadObjectsAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the operations of one page's content stream, in the order the page paints them.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="pageNumber">The one-based number of the page.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operations of the page, with the warnings the read made.</returns>
    /// <remarks>
    /// A page beyond the last page of the document produces no operation rather than an error: reading is a
    /// question about a document, and "there is no page 500 in a three page file" is an answer.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageNumber"/> is less than one.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfContentOperationView>> ReadOperationsAsync(
        IPdfContentSource source,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default);
}
