namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// A port for obtaining the bytes of a PDF document. Implementations live in the infrastructure
/// layer and cover files, streams, byte arrays or anything else; the domain only ever sees this
/// contract, which is what keeps the merging rules free of I/O.
/// </summary>
public interface IPdfContentSource
{
    /// <summary>Gets a human readable name for the content, used in messages and diagnostics.</summary>
    string DisplayName { get; }

    /// <summary>Gets the length in bytes when it is known without reading the content.</summary>
    long? Length { get; }

    /// <summary>
    /// Opens the content for reading. Each call returns an independent stream, so a source can be
    /// inspected and merged, or merged twice, without interfering with itself.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A readable stream that the caller disposes.</returns>
    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}
