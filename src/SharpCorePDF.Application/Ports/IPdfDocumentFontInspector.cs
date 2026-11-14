using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Ports;

/// <summary>
/// Reads the fonts a document uses from a stream. Implemented by the PDF engine.
/// </summary>
/// <remarks>
/// The engine-side counterpart of <see cref="Domain.Ports.IPdfFontInspector"/>, exactly as
/// <see cref="IPdfDocumentObjectInspector"/> mirrors <see cref="Domain.Ports.IPdfObjectInspector"/>: the
/// use case opens a content source and hands the stream to this port, which keeps the engine free of the
/// domain's source abstraction and of I/O policy. The member reads the stream from its current position to
/// its end and does not dispose it.
/// </remarks>
public interface IPdfDocumentFontInspector
{
    /// <summary>Reads one entry per font the document uses, in the order the pages first select them.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">A readable stream positioned at the start of the document.</param>
    /// <param name="password">The user or owner password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The fonts, with the warnings the read made.</returns>
    Task<PdfReadResult<PdfFontInfo>> ReadFontsAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default);
}
