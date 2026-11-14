using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case behind <see cref="IPdfFontInspector"/>: it opens a content source and asks the engine which
/// fonts the document uses.
/// </summary>
/// <remarks>
/// The service owns the source and the stream, which is why the engine never sees either: a source is
/// opened once per call, read to its end and disposed. Nothing is written and no temporary file is created
/// (NFR-7.7).
/// </remarks>
public sealed class PdfFontInspectorService : IPdfFontInspector
{
    private readonly IPdfDocumentFontInspector reader;

    /// <summary>Initializes a new instance of the <see cref="PdfFontInspectorService"/> class.</summary>
    /// <param name="reader">The engine that reads the fonts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public PdfFontInspectorService(IPdfDocumentFontInspector reader) =>
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfFontInfo>> ReadFontsAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ReadFontsAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);
    }
}
