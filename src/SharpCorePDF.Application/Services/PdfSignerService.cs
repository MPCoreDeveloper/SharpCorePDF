using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case behind <see cref="IPdfSigner"/>: it opens a content source, signs the document into the
/// caller's stream and disposes the source it opened.
/// </summary>
/// <remarks>
/// <para>
/// The service owns the source, which is why the engine never sees it: a source is opened once per call,
/// read to its end and disposed. The destination belongs to the caller and is flushed but not closed.
/// </para>
/// <para>
/// The arguments a caller can get wrong are refused before the source is opened — a null certificate, a
/// certificate without a private key, an unusable field name, a destination that cannot be written — so a
/// caller with a wrong argument does not pay for a read it cannot use and never receives a document that
/// looks signed and is not.
/// </para>
/// </remarks>
public sealed class PdfSignerService : IPdfSigner
{
    private readonly IPdfDocumentSigner signer;

    /// <summary>Initializes a new instance of the <see cref="PdfSignerService"/> class.</summary>
    /// <param name="signer">The engine that writes the signature revision.</param>
    /// <exception cref="ArgumentNullException"><paramref name="signer"/> is <see langword="null"/>.</exception>
    public PdfSignerService(IPdfDocumentSigner signer) =>
        this.signer = signer ?? throw new ArgumentNullException(nameof(signer));

    /// <inheritdoc />
    public async Task<PdfSignatureInfo> SignAsync(
        IPdfContentSource source,
        Stream destination,
        PdfSignatureOptions options,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (options.Certificate is null)
        {
            throw new ArgumentException("A signature needs a certificate.", nameof(options));
        }

        if (!options.Certificate.HasPrivateKey)
        {
            throw new ArgumentException(
                $"The certificate '{options.Certificate.Subject}' has no private key, so it cannot sign.",
                nameof(options));
        }

        var problems = options.Validate();

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"The signature options are not usable: {string.Join(" ", problems)}",
                nameof(options));
        }

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await signer
            .SignAsync(source.DisplayName, content, destination, options, password, cancellationToken)
            .ConfigureAwait(false);
    }
}
