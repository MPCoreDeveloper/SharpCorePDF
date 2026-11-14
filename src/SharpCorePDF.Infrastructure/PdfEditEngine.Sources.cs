using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The source half of <see cref="PdfEditEngine"/>: the document being edited plus every document the
/// plan copies pages from, each with the reader and the copier it needs.
/// </summary>
public sealed partial class PdfEditEngine
{
    /// <summary>Reads every document the plan copies a page from.</summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="plan">The plan being applied.</param>
    /// <param name="warnings">The list that receives the remarks of the documents that were read.</param>
    /// <param name="cancellationToken">A token to cancel the edit.</param>
    /// <returns>The documents of the edit, the one being edited first.</returns>
    /// <exception cref="PdfEncryptionException">A source is encrypted and could not be opened.</exception>
    /// <exception cref="PdfEditPlanValidationException">A source has no page, or a page that is inserted or layered does not come from a one-page document.</exception>
    private async Task<EditSources> OpenSourcesAsync(
        PdfFileReader document,
        PdfEditPlan plan,
        List<PdfWarning> warnings,
        CancellationToken cancellationToken)
    {
        // Operations are records, so two operations that name the same source compare equal; the map
        // is keyed by identity to keep every operation pointing at the document it actually opened.
        var documents = new Dictionary<PdfEditOperation, PdfFileReader>(ReferenceEqualityComparer.Instance);

        // A stationery operation names several documents at once, so its sources are keyed by the source
        // itself. The same source is opened once even when several assignments name it.
        var stationery = new Dictionary<IPdfContentSource, PdfFileReader>(ReferenceEqualityComparer.Instance);
        var readers = new List<PdfFileReader> { document };

        foreach (var operation in plan.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (operation is PdfStationeryOperation { Stationery: { } templates })
            {
                foreach (var assignment in templates.Assignments)
                {
                    await OpenStationeryAsync(
                        assignment.Source,
                        assignment.Password,
                        stationery,
                        readers,
                        warnings,
                        cancellationToken).ConfigureAwait(false);
                }

                if (templates.Default is { } fallback)
                {
                    await OpenStationeryAsync(
                        fallback.Source,
                        fallback.Password,
                        stationery,
                        readers,
                        warnings,
                        cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            var (content, password, onePage) = operation switch
            {
                PdfImportPageOperation import => (import.Source, import.Password, false),
                PdfInsertPageOperation insert => (insert.Page, null, true),
                PdfAppendOperation append => (append.Source, append.Password, false),
                PdfOverlayOperation overlay => (overlay.Overlay, overlay.Password, true),
                _ => ((IPdfContentSource?)null, (string?)null, false),
            };

            if (content is null)
            {
                continue;
            }

            documents[operation] = await ReadSourceAsync(content, password, onePage, warnings, cancellationToken)
                .ConfigureAwait(false);
            readers.Add(documents[operation]);
        }

        return new EditSources(document, readers, documents, stationery);
    }

    /// <summary>Opens one stationery document, unless an earlier assignment already opened it.</summary>
    /// <param name="content">The stationery document, or <see langword="null"/> for a page left bare.</param>
    /// <param name="password">The password of the document, when it is encrypted.</param>
    /// <param name="stationery">The documents opened for stationery so far, by source.</param>
    /// <param name="readers">The documents of the edit, which the reader is appended to.</param>
    /// <param name="warnings">The list that receives the remarks of the document that was read.</param>
    /// <param name="cancellationToken">A token to cancel the edit.</param>
    private async Task OpenStationeryAsync(
        IPdfContentSource? content,
        string? password,
        Dictionary<IPdfContentSource, PdfFileReader> stationery,
        List<PdfFileReader> readers,
        List<PdfWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (content is null || stationery.ContainsKey(content))
        {
            return;
        }

        var reader = await ReadSourceAsync(content, password, onePage: false, warnings, cancellationToken)
            .ConfigureAwait(false);

        stationery[content] = reader;
        readers.Add(reader);
    }

    /// <summary>Reads one document into a reader the edit can copy objects from.</summary>
    /// <param name="content">The document to read.</param>
    /// <param name="password">The password of the document, when it is encrypted.</param>
    /// <param name="onePage">Whether the document must hold exactly one page.</param>
    /// <param name="warnings">The list that receives the remarks of the document that was read.</param>
    /// <param name="cancellationToken">A token to cancel the edit.</param>
    /// <returns>The reader of the document.</returns>
    /// <exception cref="PdfEncryptionException">The document is encrypted and could not be opened.</exception>
    /// <exception cref="PdfEditPlanValidationException">The document has no page, or must hold one and does not.</exception>
    private async Task<PdfFileReader> ReadSourceAsync(
        IPdfContentSource content,
        string? password,
        bool onePage,
        List<PdfWarning> warnings,
        CancellationToken cancellationToken)
    {
        var displayName = content.DisplayName;

        await using var stream = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await PdfByteReader
            .ReadAllAsync(stream, options.MaxSourceBytes, displayName, cancellationToken)
            .ConfigureAwait(false);

        var reader = new PdfFileReader(bytes, displayName, options, password);

        if (reader.IsEncrypted && reader.Security is null)
        {
            throw new PdfEncryptionException(displayName, reader.RequiresPassword);
        }

        if (reader.Pages.Count == 0)
        {
            throw new PdfEditPlanValidationException(
                $"'{displayName}' has no pages, so no page can be copied from it.");
        }

        if (onePage && reader.Pages.Count != 1)
        {
            throw new PdfEditPlanValidationException(
                $"the page to insert must come from a one-page document, but '{displayName}' has {reader.Pages.Count} page(s).");
        }

        foreach (var warning in reader.Warnings.Where(warning => !warnings.Contains(warning)))
        {
            warnings.Add(warning);
        }

        return reader;
    }
}

/// <summary>
/// The documents of one edit while it runs: the document being edited, the documents pages are copied
/// from, and one copier per document writing into the single result.
/// </summary>
internal sealed class EditSources
{
    private readonly IReadOnlyList<PdfFileReader> readers;
    private readonly Dictionary<PdfFileReader, PdfObjectCopier> copiers = [];
    private readonly Dictionary<PdfEditOperation, PdfFileReader> documents;
    private readonly Dictionary<IPdfContentSource, PdfFileReader> stationery;

    /// <summary>Initializes a new instance of the <see cref="EditSources"/> class.</summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="readers">Every document of the edit, <paramref name="document"/> first.</param>
    /// <param name="documents">The document each importing operation reads from.</param>
    /// <param name="stationery">The document each stationery source names.</param>
    public EditSources(
        PdfFileReader document,
        IReadOnlyList<PdfFileReader> readers,
        Dictionary<PdfEditOperation, PdfFileReader> documents,
        Dictionary<IPdfContentSource, PdfFileReader> stationery)
    {
        Document = document;
        this.readers = readers;
        this.documents = documents;
        this.stationery = stationery;
    }

    /// <summary>Gets the document being edited.</summary>
    public PdfFileReader Document { get; }

    /// <summary>Gets the documents of the edit, the one being edited first.</summary>
    public IEnumerable<PdfFileReader> Readers => readers;

    /// <summary>Gets the copiers in the order of <see cref="Readers"/>.</summary>
    public IEnumerable<(PdfFileReader Reader, PdfObjectCopier Copier)> Copiers =>
        readers.Select(reader => (reader, copiers[reader]));

    /// <summary>Finds the document an importing operation reads from.</summary>
    /// <param name="operation">The import, append or insert operation.</param>
    /// <returns>The document that operation copies pages from.</returns>
    public PdfFileReader SourceOf(PdfEditOperation operation) => documents[operation];

    /// <summary>Finds the document a stationery assignment reads from.</summary>
    /// <param name="content">The stationery source.</param>
    /// <returns>The document that source names.</returns>
    public PdfFileReader SourceOf(IPdfContentSource content) => stationery[content];

    /// <summary>Finds the copier that writes the objects of a document into the result.</summary>
    /// <param name="reader">One of the documents of the edit.</param>
    /// <returns>The copier of that document.</returns>
    public PdfObjectCopier CopierOf(PdfFileReader reader) => copiers[reader];

    /// <summary>Creates the copier of every document.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <remarks>
    /// Object numbers are only unique inside one document, so every document gets a copier of its own.
    /// All of them write into the same result, which is what makes a copied page indistinguishable
    /// from a page that was already there.
    /// </remarks>
    public void Attach(PdfWriter writer)
    {
        foreach (var reader in readers)
        {
            copiers[reader] = new PdfObjectCopier(reader, writer);
        }
    }

    /// <summary>Writes the objects that were reserved while the pages and the catalog were built.</summary>
    public void Flush()
    {
        foreach (var reader in readers)
        {
            copiers[reader].Flush();
        }
    }
}
