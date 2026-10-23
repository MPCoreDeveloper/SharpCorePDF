namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The document an export writes the reading of a page into: the archival XML formats a repository
/// ingests, the OCR flavour of XHTML a search tool expects, or the text of the page placed on a canvas.
/// </summary>
/// <remarks>
/// <para>
/// Every format carries the same three things — the text, the area each word covers and the order the
/// page is read in — and differs in the vocabulary a consumer already parses. A format never adds a
/// word the read did not find and never renders the page: nothing here produces an image, embeds a font
/// or copies the bytes of the source document.
/// </para>
/// <para>
/// Coordinates are written in page space with the origin at the top left corner and the y axis running
/// down, which is the convention of the authoring surface and of all four formats. The read reports page
/// space with the origin at the bottom left, so each export flips the y coordinate against the height of
/// the page; nothing else about a box changes.
/// </para>
/// <para>
/// The bytes of an export are reproducible: no timestamp, random identifier or machine name is written,
/// so the same document read with the same options produces the same file on every run (FR-019). A new
/// member is additive and a caller keeps a <see langword="default"/> arm.
/// </para>
/// </remarks>
public enum PdfLayoutFormat
{
    /// <summary>
    /// ALTO XML, one <c>alto</c> document holding a <c>Page</c> per page, with the text blocks, lines
    /// and words of each page and its print space.
    /// </summary>
    /// <remarks>
    /// The measurement unit is <c>pixel</c> in the sense ALTO gives it — one unit of the page's own
    /// coordinate space, a point — because this library does not render the page an image would be
    /// measured in. The coordinates are therefore the page's numbers, and a consumer that needs a
    /// physical length divides them by 72 to get inches.
    /// </remarks>
    Alto,

    /// <summary>
    /// PAGE XML, one <c>PcGts</c> document holding a <c>Page</c> per page, with a text region per block,
    /// a text line per line and the reading order that names them.
    /// </summary>
    PageXml,

    /// <summary>
    /// hOCR, one XHTML document holding an <c>ocr_page</c> per page, an <c>ocr_carea</c> and
    /// <c>ocr_par</c> per block, an <c>ocr_line</c> per line and an <c>ocrx_word</c> per word, each
    /// carrying its bounding box.
    /// </summary>
    Hocr,

    /// <summary>
    /// SVG text, one <c>svg</c> document holding a group per page and a <c>text</c> element per word,
    /// placed on the baseline it is drawn on.
    /// </summary>
    /// <remarks>
    /// The export is text on a canvas: no raster is embedded, no font is embedded and no path is drawn,
    /// so the file is small and a consumer reads it as a text layer rather than as a picture of the
    /// page.
    /// </remarks>
    SvgText,
}
