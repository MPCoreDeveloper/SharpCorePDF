using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case behind <see cref="IPdfSignatureValidator"/>: it opens a content source and asks the engine
/// for the signatures the document carries and what each of them is worth.
/// </summary>
/// <remarks>
/// The service owns the source and the stream, which is why the engine never sees either: a source is
/// opened once per call, read to its end and disposed. Nothing is written and no temporary file is created
/// (NFR-7.7). A document without a signature is answered with an empty list, which is a fact about the
/// document rather than an error.
/// </remarks>
public sealed class PdfSignatureValidatorService : IPdfSignatureValidator
{
    private readonly IPdfDocumentSignatureValidator reader;

    /// <summary>Initializes a new instance of the <see cref="PdfSignatureValidatorService"/> class.</summary>
    /// <param name="reader">The engine that reads and checks the signatures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public PdfSignatureValidatorService(IPdfDocumentSignatureValidator reader) =>
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <inheritdoc />
    public async Task<IReadOnlyList<PdfSignatureValidation>> ValidateAsync(
        IPdfContentSource source,
        PdfTrustOptions? trust = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await reader
            .ValidateAsync(source.DisplayName, content, trust, password, cancellationToken)
            .ConfigureAwait(false);
    }
}
