using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// The engine's implementation of <see cref="IPdfDocumentObjectInspector"/>: it opens a document and hands
/// back one node per object the file lists, or the operations one page's content stream states, as read-only
/// projections.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here walks the document. A reference is projected as a node and never followed, so the graph a
/// caller receives stays finite even when the document points back at itself, and a reference to an object
/// the file does not list is a node with <c>IsResolved = false</c> plus a single
/// <see cref="PdfWarningKind.DanglingReference"/> remark for that target. That restraint is the inspector's
/// whole value: it answers "what is in this file" without answering "what does it mean".
/// </para>
/// <para>
/// The adapter is a thin shell around <see cref="PdfPageContentReader"/> — the same page walk, the same
/// decoders, the same encryption outcome — so that the graph surface and the content surface cannot
/// disagree about what a page holds. It writes nothing, keeps no state per document and can be shared
/// between threads, because each call opens its own read of the stream it is given (NFR-7.7).
/// </para>
/// </remarks>
public sealed class PdfObjectInspector : IPdfDocumentObjectInspector
{
    private readonly PdfPageContentReader reader;

    /// <summary>Initializes a new instance of the <see cref="PdfObjectInspector"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfObjectInspector(PdfEngineOptions? options = null) => reader = new PdfPageContentReader(options);

    /// <summary>Gets the options this inspector was created with.</summary>
    public PdfEngineOptions Options => reader.Options;

    /// <inheritdoc />
    /// <remarks>
    /// An object the file lists but that cannot be opened — a broken offset, a stream that will not decode —
    /// is still a node, of kind <see cref="PdfObjectViewKind.Null"/> and unresolved, so that a caller sees a
    /// gap in the file rather than a gap in the answer. An encrypted document that could not be opened
    /// lists no objects at all and explains itself with one remark.
    /// </remarks>
    public async Task<PdfReadResult<PdfObjectView>> ReadObjectsAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var document = await reader
            .OpenAsync(sourceName, content, password, cancellationToken)
            .ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        PdfPageContentReader.NoteClosedEncryption(document, warnings);

        return PdfPageContentReader.Result(document, Objects(document, warnings), warnings);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A page number past the last page yields no operation and no remark, because a document without a page
    /// 500 is a fact about the file rather than a mistake in the call; the application service refuses a page
    /// number below one before the source is opened at all.
    /// </remarks>
    public async Task<PdfReadResult<PdfContentOperationView>> ReadOperationsAsync(
        string sourceName,
        Stream content,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var document = await reader
            .OpenAsync(sourceName, content, password, cancellationToken)
            .ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        PdfPageContentReader.NoteClosedEncryption(document, warnings);

        return PdfPageContentReader.Result(document, Operations(document, pageNumber, warnings), warnings);
    }

    /// <summary>Projects every object the document lists, in ascending object number.</summary>
    /// <param name="document">The opened document.</param>
    /// <param name="warnings">The list that receives the remark per dangling reference target.</param>
    /// <returns>The nodes of the graph; empty when the document could not be opened.</returns>
    private static IReadOnlyList<PdfObjectView> Objects(PdfFileReader document, List<PdfWarning> warnings)
    {
        if (IsUnreadable(document))
        {
            return [];
        }

        return ObjectGraphView.Project(document, warnings);
    }

    /// <summary>Reads the operations of one page's content streams.</summary>
    /// <param name="document">The opened document.</param>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="warnings">The list that receives the remarks the read makes.</param>
    /// <returns>The operations, in the order the page states them; empty when there are none.</returns>
    private IReadOnlyList<PdfContentOperationView> Operations(
        PdfFileReader document,
        int pageNumber,
        List<PdfWarning> warnings)
    {
        var pages = document.Pages;

        if (IsUnreadable(document) || pageNumber < 1 || pageNumber > pages.Count)
        {
            return [];
        }

        var content = PdfPageContentReader.ReadPageContent(document, pages[pageNumber - 1], warnings, pageNumber);

        if (content is null)
        {
            return [];
        }

        return ContentOperationReader.Read(content, pageNumber, Options, warnings, document.SourceName);
    }

    /// <summary>Determines whether a document is encrypted and could not be decrypted.</summary>
    /// <param name="document">The opened document.</param>
    /// <returns><see langword="true"/> when nothing but the page tree can be read from it.</returns>
    private static bool IsUnreadable(PdfFileReader document) => document.IsEncrypted && document.Security is null;
}
