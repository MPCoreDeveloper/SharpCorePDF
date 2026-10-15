using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// The PDF engine contract the application layer drives: everything the merging use case needs from
/// a PDF implementation, expressed without a single engine specific type.
/// </summary>
/// <remarks>
/// This port exists so the use case can be unit tested against a fake engine, and so a different
/// engine — a GPU backed one, a wrapper around a licensed library — can be swapped in without
/// touching the domain or the application layer.
/// </remarks>
public interface IPdfMergeEngine
{
    /// <summary>Writes the merged document described by <paramref name="request"/>.</summary>
    /// <param name="request">The sources with their resolved page lists, plus the output options.</param>
    /// <param name="destination">A writable stream that receives the document; it is not disposed.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    /// <returns>A summary of what was written.</returns>
    Task<MergeResult> MergeAsync(
        MergeEngineRequest request,
        Stream destination,
        CancellationToken cancellationToken = default);
}
