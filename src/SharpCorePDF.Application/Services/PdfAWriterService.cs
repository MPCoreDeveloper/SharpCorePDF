using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case behind <see cref="IPdfAWriter"/>: it checks the arguments, opens a content source,
/// converts it through the engine and disposes the source it opened.
/// </summary>
/// <remarks>
/// <para>
/// The service owns the source, which is why the engine never sees it: a source is opened once per
/// call, read to its end and disposed. The destination belongs to the caller and is flushed but not
/// closed.
/// </para>
/// <para>
/// Everything a caller can get wrong — an undefined profile, an unusable option, a destination that
/// cannot be written — is refused before the source is read, so a document that cannot be written
/// costs no work and leaves the destination untouched.
/// </para>
/// </remarks>
public sealed class PdfAWriterService : IPdfAWriter
{
    private readonly IPdfDocumentAWriter writer;

    /// <summary>Initializes a new instance of the <see cref="PdfAWriterService"/> class.</summary>
    /// <param name="writer">The engine that writes the archival document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public PdfAWriterService(IPdfDocumentAWriter writer) =>
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <inheritdoc />
    public async Task<PdfAConversionResult> ConvertAsync(
        IPdfContentSource source,
        Stream destination,
        PdfAProfile profile,
        PdfAConversionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                profile,
                "The profile is not one the engine writes.");
        }

        var effective = options ?? PdfAConversionOptions.Default;
        var problems = effective.Validate();

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"The PDF/A options are not usable: {string.Join(" ", problems)}",
                nameof(options));
        }

        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        return await writer
            .ConvertAsync(source.DisplayName, content, destination, profile, effective, password, cancellationToken)
            .ConfigureAwait(false);
    }
}
