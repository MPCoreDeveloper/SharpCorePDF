using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The engine's implementation of <see cref="IPdfDocumentReader"/>: it reads a document into memory
/// and describes everything the application layer needs to plan a merge.
/// </summary>
/// <remarks>
/// <para>
/// The reader never throws for a document that merely looks unusual: damaged cross-reference tables
/// are reconstructed, missing page boxes are reported as warnings, and an encrypted document is
/// described with <see cref="PdfDocumentInfo.IsEncrypted"/> set instead of an exception.
/// </para>
/// <para>
/// Because the engine does not attempt decryption, it cannot tell whether an encrypted document would
/// open with an empty user password. Such a document is therefore reported with
/// <see cref="PdfDocumentInfo.RequiresPassword"/> set to <see langword="false"/>, and the merge
/// use case refuses it with a plain "encrypted documents cannot be merged" message.
/// </para>
/// </remarks>
public sealed class PdfDocumentReader : IPdfDocumentReader
{
    private readonly PdfEngineOptions options;

    /// <summary>Initializes a new instance of the <see cref="PdfDocumentReader"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfDocumentReader(PdfEngineOptions? options = null) => this.options = options ?? PdfEngineOptions.Default;

    /// <summary>Gets the options this reader was created with.</summary>
    public PdfEngineOptions Options => options;

    /// <inheritdoc />
    public async Task<PdfDocumentInfo> ReadAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);

        var bytes = await PdfByteReader
            .ReadAllAsync(content, options.MaxSourceBytes, sourceName, cancellationToken)
            .ConfigureAwait(false);

        var reader = new PdfFileReader(bytes, sourceName, options);

        return PdfDocumentInspection.Describe(reader, bytes.LongLength);
    }
}
