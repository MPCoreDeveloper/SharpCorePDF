using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// The PDF engine contract the editor use case drives: apply an immutable plan to a source and write
/// the result, without a single engine specific type crossing the boundary.
/// </summary>
/// <remarks>
/// This port exists so the use case can be unit tested against a fake engine, and so a different
/// engine can be swapped in without touching the domain or the application layer.
/// </remarks>
public interface IPdfEditEngine
{
    /// <summary>Applies <paramref name="plan"/> to the document in <paramref name="content"/>.</summary>
    /// <param name="sourceName">The display name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the first byte of the document; it is not disposed.</param>
    /// <param name="plan">The operations to apply and the options that control the result.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="destination">A writable stream that receives the edited document; it is not disposed.</param>
    /// <param name="cancellationToken">A token to cancel the edit.</param>
    /// <returns>A summary of what was written.</returns>
    Task<PdfEditResult> ApplyAsync(
        string sourceName,
        Stream content,
        PdfEditPlan plan,
        string? password,
        Stream destination,
        CancellationToken cancellationToken = default);
}
