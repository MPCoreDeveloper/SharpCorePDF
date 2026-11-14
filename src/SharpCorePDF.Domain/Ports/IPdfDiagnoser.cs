using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Inspects a document once and returns the report a support engineer needs: what the document is, the
/// fonts it uses, the signatures it carries, an optional archival pre-flight, and every warning the
/// sub-reads made.
/// </summary>
/// <remarks>
/// <para>
/// The report is assembled from the existing use cases rather than from a second walk of the file, so it
/// cannot disagree with them. It is read-only and redaction-explicit: it names sources and describes
/// structure, and it never returns stream contents.
/// </para>
/// <para>
/// A document that is encrypted and stays closed is reported, not thrown: <see cref="PdfDocumentInfo.IsReadable"/>
/// and <see cref="PdfDocumentInfo.RequiresPassword"/> answer what stands in the way, exactly as the other
/// reads do. Only content that is not a PDF at all raises
/// <see cref="Exceptions.PdfSourceFormatException"/>.
/// </para>
/// </remarks>
public interface IPdfDiagnoser
{
    /// <summary>Reads one report about a document.</summary>
    /// <param name="source">The content to diagnose.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="archivalProfile">
    /// The PDF/A profile to pre-flight, or <see langword="null"/> to skip the archival check. The check is
    /// the writer's own, so the report names exactly the requirements the writer would refuse.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report: the document, its fonts, its signatures, the archival result and the warnings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfDiagnosticsReport> DiagnoseAsync(
        IPdfContentSource source,
        string? password = null,
        PdfAProfile? archivalProfile = null,
        CancellationToken cancellationToken = default);
}
