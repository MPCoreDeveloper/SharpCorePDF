using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Signatures;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The engine's implementation of <see cref="IPdfDocumentSignatureValidator"/>: it finds every signature of a
/// document and checks what each one claims.
/// </summary>
/// <remarks>
/// <para>
/// Integrity and trust are reported separately, because they answer different questions: the digest is
/// arithmetic over the file and needs no trust in anybody, while the chain is measured against the anchors the
/// caller named — and against nothing else, since this library ships no root store and never consults the
/// machine's.
/// </para>
/// <para>
/// A signature that covers an earlier revision is reported as intact and as not covering the whole document.
/// That is the ordinary state of the first of two signatures, and reporting it as broken would be wrong: the
/// bytes it signed are exactly the bytes that are still there.
/// </para>
/// </remarks>
public sealed class PdfDocumentSignatureValidator : IPdfDocumentSignatureValidator
{
    private readonly PdfEngineOptions engineOptions;

    /// <summary>Initializes a new instance of the <see cref="PdfDocumentSignatureValidator"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfDocumentSignatureValidator(PdfEngineOptions? options = null) =>
        engineOptions = options ?? PdfEngineOptions.Default;

    /// <summary>Gets the options this engine was created with.</summary>
    public PdfEngineOptions Options => engineOptions;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The engine options are not usable.</exception>
    /// <exception cref="Domain.Exceptions.PdfSourceFormatException">The stream does not contain a PDF document.</exception>
    public async Task<IReadOnlyList<PdfSignatureValidation>> ValidateAsync(
        string sourceName,
        Stream content,
        PdfTrustOptions? trust = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);

        var problems = engineOptions.Validate();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The engine options are not usable: {string.Join(" ", problems)}");
        }

        var bytes = await PdfByteReader.ReadAllAsync(content, engineOptions.MaxSourceBytes, sourceName, cancellationToken).ConfigureAwait(false);
        var reader = new PdfFileReader(bytes, sourceName, engineOptions, password);

        return SignatureValidator.ValidateAll(reader, trust);
    }
}
