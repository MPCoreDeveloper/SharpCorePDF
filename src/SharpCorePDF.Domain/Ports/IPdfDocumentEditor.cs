using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Applies an immutable edit plan to one PDF document and writes a new document.
/// </summary>
/// <remarks>
/// <para>
/// The source is never modified and no temporary file is created beside it. The common case needs no
/// plan configuration at all:
/// </para>
/// <code>
/// var plan = PdfEditPlan.Create()
///     .RotatePage(1, PageRotation.Clockwise90)
///     .DeletePage(4);
///
/// await editor.ApplyAsync(source, plan, destination);
/// </code>
/// <para>
/// Implementations must be thread safe: a single instance is expected to serve concurrent calls.
/// </para>
/// </remarks>
public interface IPdfDocumentEditor
{
    /// <summary>Applies <paramref name="plan"/> to <paramref name="source"/> and writes the result.</summary>
    /// <param name="source">The document to read; it is never modified.</param>
    /// <param name="plan">The operations to apply and the options that control the result.</param>
    /// <param name="destination">A writable stream that receives the edited document. The caller owns the stream; it is flushed but not closed.</param>
    /// <param name="password">The user or owner password, when the source is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the edit.</param>
    /// <returns>A summary of what was written.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="Exceptions.PdfEditPlanValidationException">The plan violates an invariant, or an operation reaches beyond the page count of the source.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The source is not a readable PDF document.</exception>
    /// <exception cref="Exceptions.PdfEncryptionException">The source is encrypted and could not be opened.</exception>
    Task<PdfEditResult> ApplyAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>Applies <paramref name="plan"/> to <paramref name="source"/> and returns the result.</summary>
    /// <param name="source">The document to read; it is never modified.</param>
    /// <param name="plan">The operations to apply and the options that control the result.</param>
    /// <param name="password">The user or owner password, when the source is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the edit.</param>
    /// <returns>The bytes of the edited document.</returns>
    /// <remarks>
    /// Use this only when the result fits in memory. For large results, apply the plan to a stream.
    /// </remarks>
    Task<byte[]> ApplyToArrayAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the operations of <paramref name="plan"/> and writes the result as several documents,
    /// cut where its <c>SplitDocument</c> operation says.
    /// </summary>
    /// <param name="source">The document to read; it is never modified.</param>
    /// <param name="plan">
    /// The operations to apply. It must contain exactly one <c>SplitDocument</c> operation, which
    /// decides how many pages each part receives; a plan without one is refused before anything is
    /// written.
    /// </param>
    /// <param name="password">The user or owner password, when the source is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the split.</param>
    /// <returns>The parts, in page order, together with their bytes and remarks.</returns>
    /// <remarks>
    /// Splitting produces more than one document, which is why it has a method of its own: a single
    /// destination stream cannot receive it. Every other operation of the plan runs first, so the
    /// parts are cut from the edited document and share its metadata, rotations and page geometry.
    /// The parts are returned in memory; the caller writes them wherever they belong.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfEditPlanValidationException">The plan is not executable, or does not contain exactly one split.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The source is not a readable PDF document.</exception>
    /// <exception cref="Exceptions.PdfEncryptionException">The source is encrypted and could not be opened.</exception>
    Task<PdfSplitResult> SplitAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        string? password = null,
        CancellationToken cancellationToken = default);
}
