using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Signatures;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The engine's implementation of <see cref="IPdfDocumentSigner"/>: it appends one signed revision to a
/// document and reports what that revision states.
/// </summary>
/// <remarks>
/// <para>
/// The signature is written as an incremental update, so the bytes of the source document are copied through
/// untouched and an earlier signature stays valid. The revision carries the signature dictionary, the
/// widget, the appearance when one was asked for, the page that lists the widget and the catalog that lists
/// the field.
/// </para>
/// <para>
/// The report is read back from the bytes that were written rather than assembled from the options, so what
/// the caller receives describes the file it now holds: the byte range, the revision, the length of the
/// signature value and the signer all come from the document.
/// </para>
/// </remarks>
public sealed class PdfDocumentSigner : IPdfDocumentSigner
{
    private readonly PdfEngineOptions engineOptions;

    /// <summary>Initializes a new instance of the <see cref="PdfDocumentSigner"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfDocumentSigner(PdfEngineOptions? options = null) => engineOptions = options ?? PdfEngineOptions.Default;

    /// <summary>Gets the options this engine was created with.</summary>
    public PdfEngineOptions Options => engineOptions;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// The destination is not writable, or the signature options are not usable.
    /// </exception>
    /// <exception cref="InvalidOperationException">The engine options are not usable.</exception>
    /// <exception cref="Domain.Exceptions.PdfUnsupportedFeatureException">
    /// The document is encrypted, or the digest is not implemented.
    /// </exception>
    /// <exception cref="Domain.Exceptions.PdfSourceFormatException">The document has no page to sign.</exception>
    public async Task<PdfSignatureInfo> SignAsync(
        string sourceName,
        Stream content,
        Stream destination,
        PdfSignatureOptions options,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        // A certificate without its key cannot produce a signature, so it is refused before the source is read
        // and before a byte is written: a revision that then failed would leave a document that looks signed.
        if (!options.Certificate.HasPrivateKey)
        {
            throw new ArgumentException(
                $"The certificate '{options.Certificate.Subject}' carries no private key, so it cannot sign.",
                nameof(options));
        }

        var optionProblems = options.Validate();

        if (optionProblems.Count > 0)
        {
            throw new ArgumentException(
                $"The signature options are not usable: {string.Join(" ", optionProblems)}",
                nameof(options));
        }

        var engineProblems = engineOptions.Validate();

        if (engineProblems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The engine options are not usable: {string.Join(" ", engineProblems)}");
        }

        var bytes = await PdfByteReader.ReadAllAsync(content, engineOptions.MaxSourceBytes, sourceName, cancellationToken).ConfigureAwait(false);
        var reader = new PdfFileReader(bytes, sourceName, engineOptions, password);
        var signed = await SignatureFieldWriter.WriteAsync(reader, options, cancellationToken).ConfigureAwait(false);

        await destination.WriteAsync(signed, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        return ReadBack(signed, sourceName, options.FieldName);
    }

    /// <summary>Finds the signature that was just written, in the bytes that were written.</summary>
    /// <param name="signed">The signed document.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="fieldName">The name of the field the signature was written into.</param>
    /// <returns>What the written signature states.</returns>
    /// <exception cref="InvalidOperationException">The signed document does not carry the signature.</exception>
    private PdfSignatureInfo ReadBack(byte[] signed, string sourceName, string fieldName)
    {
        var reader = new PdfFileReader(signed, sourceName, engineOptions);
        var fields = SignatureValidator.Collect(reader);
        SignatureField? chosen = null;

        for (var index = fields.Count - 1; index >= 0 && chosen is null; index--)
        {
            if (string.Equals(fields[index].FieldName, fieldName, StringComparison.Ordinal))
            {
                chosen = fields[index];
            }
        }

        // A document that carries no form leaves the field unnamed on its page widget, so the signature that
        // was just appended is the last one the document lists.
        chosen ??= fields.Count > 0 ? fields[^1] : null;

        var field = chosen ?? throw new InvalidOperationException(
            $"The signed revision of '{sourceName}' does not carry the signature field '{fieldName}' that was just written.");

        return SignatureValidator.Validate(reader, field, trust: null).Signer;
    }
}
