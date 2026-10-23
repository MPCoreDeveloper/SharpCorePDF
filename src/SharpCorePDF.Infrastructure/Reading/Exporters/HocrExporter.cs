using System.Globalization;
using System.Xml;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

namespace SharpCorePDF.Infrastructure.Reading.Exporters;

/// <summary>
/// Writes a read as hOCR: one XHTML document, an <c>ocr_page</c> per page, an <c>ocr_carea</c> and
/// <c>ocr_par</c> per block, an <c>ocr_line</c> per line and an <c>ocrx_word</c> per word.
/// </summary>
/// <remarks>
/// <para>
/// hOCR is HTML that states what an OCR engine found, and it is the format a search tool or a browser reads
/// without a schema. The export keeps the nesting a consumer of that format expects — a page holds content
/// areas, an area holds paragraphs, a paragraph holds lines and a line holds words — and gives every text
/// element a <c>bbox</c> in its <c>title</c>, because the box is what a browser overlays and what a
/// downstream tool aligns against.
/// </para>
/// <para>
/// The boxes are page coordinates with the origin at the top left corner, whole numbers, and the words of a
/// line are separated by a space in the document so that the text of an <c>ocr_line</c> reads as the line
/// does. The document states no date and no engine version of its own beyond the library's name, so the same
/// read produces the same bytes on every run (FR-019).
/// </para>
/// </remarks>
internal static class HocrExporter
{
    /// <summary>The namespace of XHTML, which an hOCR document is written in.</summary>
    private const string Namespace = "http://www.w3.org/1999/xhtml";

    /// <summary>Writes the document.</summary>
    /// <param name="pages">The pages of the range, in ascending page order.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    public static void Write(IReadOnlyList<LayoutExportPage> pages, Stream destination)
    {
        using var writer = LayoutExportXml.Create(destination);

        writer.WriteStartDocument();
        writer.WriteDocType("html", null, null, null);

        Start(writer, "html");

        Start(writer, "head");
        SelfClosing(writer, "meta", ("http-equiv", "Content-Type"), ("content", "text/html; charset=utf-8"));
        SelfClosing(writer, "meta", ("name", "ocr-system"), ("content", "SharpCorePDF"));
        SelfClosing(
            writer,
            "meta",
            ("name", "ocr-capabilities"),
            ("content", "ocr_page ocr_carea ocr_par ocr_line ocrx_word"));

        Start(writer, "title");
        writer.WriteString("SharpCorePDF layout export");
        End(writer);

        End(writer);

        Start(writer, "body");

        foreach (var page in pages)
        {
            WritePage(writer, page);
        }

        End(writer);
        End(writer);
        writer.WriteEndDocument();
    }

    /// <summary>Writes one page as an <c>ocr_page</c> holding the blocks of the read.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page to write.</param>
    private static void WritePage(XmlWriter writer, LayoutExportPage page)
    {
        Start(writer, "div");
        Attribute(writer, "class", "ocr_page");
        Attribute(writer, "id", string.Create(CultureInfo.InvariantCulture, $"page_{page.PageNumber}"));
        Attribute(
            writer,
            "title",
            string.Create(
                CultureInfo.InvariantCulture,
                $"bbox 0 0 {LayoutExportXml.Unit(page.Width)} {LayoutExportXml.Unit(page.Height)}; "
                + $"ppageno {page.PageNumber - 1}"));

        foreach (var block in page.Blocks)
        {
            WriteBlock(writer, page, block);
        }

        End(writer);
    }

    /// <summary>Writes one block as a content area holding a paragraph.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the block sits on.</param>
    /// <param name="block">The block to write.</param>
    /// <remarks>
    /// A block of a layout read is a paragraph-sized region, so it is written twice: as the content area a
    /// consumer groups regions by, and as the paragraph that holds the lines. Both carry the same box, which
    /// is what hOCR's own documentation does for a region that is one paragraph.
    /// </remarks>
    private static void WriteBlock(XmlWriter writer, LayoutExportPage page, LayoutExportBlock block)
    {
        var bbox = Bbox(block.Bounds, page.Height);
        var id = string.Create(CultureInfo.InvariantCulture, $"{page.PageNumber}_{block.ReadingOrder}");

        Start(writer, "div");
        Attribute(writer, "class", "ocr_carea");
        Attribute(writer, "id", $"block_{id}");
        Attribute(writer, "title", $"bbox {bbox}");

        Start(writer, "p");
        Attribute(writer, "class", "ocr_par");
        Attribute(writer, "id", $"par_{id}");
        Attribute(writer, "title", $"bbox {bbox}");

        for (var index = 0; index < block.Lines.Count; index++)
        {
            WriteLine(writer, page, block, block.Lines[index], index + 1);
        }

        End(writer);
        End(writer);
    }

    /// <summary>Writes one line and the words on it, separated the way the line reads.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the line sits on.</param>
    /// <param name="block">The block the line belongs to.</param>
    /// <param name="line">The line to write.</param>
    /// <param name="lineNumber">The one-based position of the line in its block.</param>
    private static void WriteLine(
        XmlWriter writer,
        LayoutExportPage page,
        LayoutExportBlock block,
        TextLine line,
        int lineNumber)
    {
        Start(writer, "span");
        Attribute(writer, "class", "ocr_line");
        Attribute(
            writer,
            "id",
            string.Create(
                CultureInfo.InvariantCulture,
                $"line_{page.PageNumber}_{block.ReadingOrder}_{lineNumber}"));
        Attribute(writer, "title", $"bbox {Bbox(line.Bounds, page.Height)}");

        for (var index = 0; index < line.Words.Count; index++)
        {
            if (index > 0)
            {
                writer.WriteString(" ");
            }

            WriteWord(writer, page, block, lineNumber, line.Words[index], index + 1);
        }

        End(writer);
    }

    /// <summary>Writes one word.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the word sits on.</param>
    /// <param name="block">The block the word belongs to.</param>
    /// <param name="lineNumber">The one-based position of the line the word sits on.</param>
    /// <param name="word">The word to write.</param>
    /// <param name="wordNumber">The one-based position of the word in its line.</param>
    private static void WriteWord(
        XmlWriter writer,
        LayoutExportPage page,
        LayoutExportBlock block,
        int lineNumber,
        PdfWord word,
        int wordNumber)
    {
        Start(writer, "span");
        Attribute(writer, "class", "ocrx_word");
        Attribute(
            writer,
            "id",
            string.Create(
                CultureInfo.InvariantCulture,
                $"word_{page.PageNumber}_{block.ReadingOrder}_{lineNumber}_{wordNumber}"));
        Attribute(writer, "title", $"bbox {Bbox(word.BoundingBox, page.Height)}");
        writer.WriteString(LayoutExportXml.Clean(word.Text));
        End(writer);
    }

    /// <summary>States a box as the four numbers hOCR puts in a <c>title</c>.</summary>
    /// <param name="box">The box, in page space.</param>
    /// <param name="pageHeight">The height of the page the box sits on.</param>
    /// <returns>The box as <c>left top right bottom</c>.</returns>
    private static string Bbox(PdfRectangle box, double pageHeight)
    {
        var (left, top, right, bottom) = LayoutExportXml.Box(box, pageHeight);

        return string.Create(CultureInfo.InvariantCulture, $"{left} {top} {right} {bottom}");
    }

    /// <summary>Starts an element of XHTML.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="name">The local name of the element.</param>
    private static void Start(XmlWriter writer, string name) => writer.WriteStartElement(name, Namespace);

    /// <summary>Writes an empty element with the attributes it states.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="name">The local name of the element.</param>
    /// <param name="attributes">The attributes, as name and value pairs.</param>
    private static void SelfClosing(XmlWriter writer, string name, params (string Name, string Value)[] attributes)
    {
        Start(writer, name);

        foreach (var (attributeName, value) in attributes)
        {
            Attribute(writer, attributeName, value);
        }

        End(writer);
    }

    /// <summary>Writes an attribute value, made safe for XML.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="value">The value, which may come from the text of the document.</param>
    private static void Attribute(XmlWriter writer, string name, string value) =>
        writer.WriteAttributeString(name, LayoutExportXml.Clean(value));

    /// <summary>Ends the element the writer is in.</summary>
    /// <param name="writer">The writer of the document.</param>
    private static void End(XmlWriter writer) => writer.WriteEndElement();
}
