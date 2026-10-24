using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// One page an authored document contributes, in the order the caller added it: either a canvas it
/// drew, or a page copied out of another document.
/// </summary>
internal interface IAuthoredPage;

/// <summary>A page whose content the caller wrote with the canvas of <see cref="PdfDocumentBuilder"/>.</summary>
internal sealed class DrawnPage : IAuthoredPage
{
    /// <summary>Initializes a new instance of the <see cref="DrawnPage"/> class.</summary>
    /// <param name="canvas">The canvas the page was drawn with.</param>
    public DrawnPage(PdfPageCanvas canvas) =>
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    /// <summary>Gets the canvas the page was drawn with.</summary>
    public PdfPageCanvas Canvas { get; }
}

/// <summary>
/// A page copied from another document, with the reader that holds that document. The source is parsed
/// when the caller imports from it, so a source that cannot be read fails at the call and not when the
/// document is written.
/// </summary>
internal sealed class ImportedPage : IAuthoredPage
{
    /// <summary>Initializes a new instance of the <see cref="ImportedPage"/> class.</summary>
    /// <param name="reader">The reader of the document the page comes from.</param>
    /// <param name="pageNumber">The one-based page number in that document.</param>
    public ImportedPage(PdfFileReader reader, int pageNumber)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        PageNumber = pageNumber;
    }

    /// <summary>Gets the reader of the document the page comes from.</summary>
    public PdfFileReader Reader { get; }

    /// <summary>Gets the one-based page number in that document.</summary>
    public int PageNumber { get; }

    /// <summary>Gets the display name of that document, as the caller's source states it.</summary>
    public string DisplayName => Reader.SourceName;
}
