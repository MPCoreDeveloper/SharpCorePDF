using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Merges one or more PDF documents into a single document.
/// </summary>
/// <remarks>
/// <para>
/// This is the primary capability of SharpCorePDF and the only interface most callers need:
/// </para>
/// <code>
/// var plan = MergePlan.Create()
///     .AddFile(@"C:\reports\summary.pdf")
///     .AddFile(@"C:\reports\appendix.pdf", pages: "1-3");
///
/// await merger.MergeToFileAsync(plan, @"C:\reports\combined.pdf");
/// </code>
/// <para>
/// Implementations must be thread safe: a single instance is expected to serve concurrent calls.
/// </para>
/// </remarks>
public interface IPdfMerger
{
    /// <summary>Merges the sources of <paramref name="plan"/> into <paramref name="destination"/>.</summary>
    /// <param name="plan">The validated plan describing which pages, from which documents, end up where.</param>
    /// <param name="destination">A writable stream that receives the merged document. The caller owns the stream; it is flushed but not closed.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    /// <returns>A summary of what was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="Exceptions.MergePlanValidationException">The plan violates an invariant, or a page selection falls outside its source document.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">A source is not a readable PDF document.</exception>
    /// <exception cref="Exceptions.PdfEncryptionException">A source is encrypted. Inspect documents with <see cref="IPdfInspector"/> to detect this without an exception.</exception>
    Task<MergeResult> MergeAsync(
        MergePlan plan,
        Stream destination,
        CancellationToken cancellationToken = default);
}
