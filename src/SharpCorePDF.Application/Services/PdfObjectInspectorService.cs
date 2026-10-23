using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case behind <see cref="IPdfObjectInspector"/>: it opens a content source and asks the engine for
/// the document's object graph or for the operations of one of its pages.
/// </summary>
/// <remarks>
/// <para>
/// The service owns the source and the stream, which is why the engine never sees either: a source is
/// opened once per call, read to its end and disposed. Nothing is written and no temporary file is created
/// (NFR-7.7).
/// </para>
/// <para>
/// A page number below one is refused before the source is opened, so a caller with a wrong argument does
/// not pay for a read it cannot use; a page number past the last page is answered with no operation,
/// because that is a fact about the document rather than a mistake in the call.
/// </para>
/// </remarks>
public sealed class PdfObjectInspectorService : IPdfObjectInspector
{
    private readonly IPdfDocumentObjectInspector reader;

    /// <summary>Initializes a new instance of the <see cref="PdfObjectInspectorService"/> class.</summary>
    /// <param name="reader">The engine that reads the graph and the content streams.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public PdfObjectInspectorService(IPdfDocumentObjectInspector reader) =>
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfObjectView>> ReadObjectsAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadObjectsAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfContentOperationView>> ReadOperationsAsync(
        IPdfContentSource source,
        int pageNumber,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadOperationsAsync(source.DisplayName, content, pageNumber, password, cancellationToken)
            .ConfigureAwait(false);
    }
}
