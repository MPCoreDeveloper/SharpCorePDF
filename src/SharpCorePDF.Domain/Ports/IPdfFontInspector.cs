using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.Ports;

/// <summary>
/// Reads the fonts a PDF document uses, without changing it.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IPdfObjectInspector"/> for one question: which fonts does this file
/// need, and does it carry them? A font that is not embedded prints differently on a machine that has no
/// copy of it, and a composite font without a <c>/ToUnicode</c> map draws text that cannot be extracted,
/// so both are reported as facts about the document rather than as errors.
/// </para>
/// <para>
/// A font is reported once, however many pages select it. A document that is encrypted and could not be
/// opened yields no font and the warning that says why, exactly as the other reads report it; only content
/// that is not a PDF at all raises <see cref="Exceptions.PdfSourceFormatException"/>.
/// </para>
/// </remarks>
public interface IPdfFontInspector
{
    /// <summary>Reads one entry per font the document uses, in the order the pages first select them.</summary>
    /// <param name="source">The content to read.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fonts, with the warnings the read made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.PdfSourceFormatException">The content is not a readable PDF document.</exception>
    Task<PdfReadResult<PdfFontInfo>> ReadFontsAsync(
        IPdfContentSource source,
        string? password = null,
        CancellationToken cancellationToken = default);
}
