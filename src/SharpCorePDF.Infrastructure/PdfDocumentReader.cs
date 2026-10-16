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
/// are reconstructed, missing page boxes are reported as warnings, and an encrypted document that
/// cannot be opened is described with <see cref="PdfDocumentInfo.IsReadable"/> cleared instead of an
/// exception.
/// </para>
/// <para>
/// An encrypted document is opened with the password the caller supplies, which may be either the
/// user or the owner password. A document whose encryption stays closed because the password was
/// missing or wrong is reported with <see cref="PdfDocumentInfo.RequiresPassword"/> set, so a caller
/// can ask for the right one; a document whose security handler the engine does not implement is
/// reported without that flag, because no password would help.
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

        var reader = new PdfFileReader(bytes, sourceName, options, password);

        return PdfDocumentInspection.Describe(reader, bytes.LongLength);
    }
}
