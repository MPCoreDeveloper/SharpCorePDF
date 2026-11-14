using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One read-only report about a document: its structure, the fonts it uses, the signatures it carries,
/// an optional archival pre-flight, and the warnings the sub-reads made.
/// </summary>
/// <remarks>
/// <para>
/// The report is assembled from the existing use cases — inspection, the font inspector, the signature
/// validator and the archival writer — so it stays in step with them. It names sources and describes
/// structure; it never returns stream contents, which is what makes it safe to log or attach to a support
/// ticket.
/// </para>
/// <para>
/// <see cref="Warnings"/> is the merged list of the sub-reads, de-duplicated by the remark's kind and
/// message. An empty list is a clean document; a document that had to be repaired still produces a
/// report, with the repair named here.
/// </para>
/// </remarks>
public sealed record PdfDiagnosticsReport
{
    /// <summary>Gets the description of the document.</summary>
    public required PdfDocumentInfo Document { get; init; }

    /// <summary>Gets the fonts the document uses, in the order the pages first select them.</summary>
    public IReadOnlyList<PdfFontInfo> Fonts { get; init; } = [];

    /// <summary>Gets one validation per signature, in revision order; empty when the document is unsigned.</summary>
    public IReadOnlyList<PdfSignatureValidation> Signatures { get; init; } = [];

    /// <summary>
    /// Gets the archival pre-flight, or <see langword="null"/> when no profile was asked for or the
    /// document could not be read. When it is present, <see cref="PdfAConversionResult.IsWritten"/> says
    /// whether the writer would write the document and <see cref="PdfAConversionResult.UnsatisfiedRequirements"/>
    /// names every reason it would not.
    /// </summary>
    public PdfAConversionResult? Archival { get; init; }

    /// <summary>Gets the merged, de-duplicated warnings of every sub-read.</summary>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];

    /// <summary>Gets a value indicating whether any sub-read reported a warning.</summary>
    public bool HasFindings => Warnings.Count > 0;

    /// <summary>Formats the report for diagnostics.</summary>
    /// <returns>The page count, the fonts, the signatures and the finding count, on one line.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{Document.PageCount} page(s), {Fonts.Count} font(s), {Signatures.Count} signature(s), {Warnings.Count} finding(s)");
}
