using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The inspection use case: open a content source and describe what it contains, without changing
/// anything and without throwing for an encrypted document it cannot open.
/// </summary>
/// <remarks>The class is stateless and thread safe.</remarks>
public sealed class PdfInspectorService : IPdfInspector
{
    private readonly IPdfDocumentReader reader;

    /// <summary>Initializes a new instance of the <see cref="PdfInspectorService"/> class.</summary>
    /// <param name="reader">The reader used to inspect the document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public PdfInspectorService(IPdfDocumentReader reader) =>
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <inheritdoc />
    public async Task<PdfDocumentInfo> InspectAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);
    }
}
