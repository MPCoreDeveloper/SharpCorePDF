using System.Globalization;
using System.Xml;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure.Reading.Exporters;

/// <summary>
/// Writes a read as SVG text: one document, a group per page, a group per block and a <c>text</c> element per
/// word, placed where the word is drawn.
/// </summary>
/// <remarks>
/// <para>
/// This is the export a viewer opens: the text of the page on a canvas the size of the page, with nothing
/// else on it. One <c>text</c> element per word keeps every position exact and the document free of the
/// whitespace questions a run of <c>tspan</c> elements raises — the text of a word never depends on where the
/// indentation of the file falls — and the words appear in reading order, so a consumer that walks the
/// elements reads the page.
/// </para>
/// <para>
/// A word is placed on its baseline: <c>y</c> is the baseline of the line it sits on, measured from the top
/// of the page, and <c>x</c> is its left edge. No raster is embedded, no font is embedded and no path is
/// drawn, so the file stays a text layer rather than a picture; the size of the canvas is stated in points so
/// that the viewer's own scaling keeps the page's proportions.
/// </para>
/// </remarks>
internal static class SvgTextExporter
{
    /// <summary>The namespace of SVG, the version 1.1 document this exporter writes.</summary>
    private const string Namespace = "http://www.w3.org/2000/svg";

    /// <summary>Writes the document.</summary>
    /// <param name="pages">The pages of the range, in ascending page order.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    public static void Write(IReadOnlyList<LayoutExportPage> pages, Stream destination)
    {
        using var writer = LayoutExportXml.Create(destination);

        // A range may hold pages of more than one size; the canvas is the largest of them, so that no page
        // is cropped and every page keeps its own viewBox-relative position. A range that selected no page
        // still writes a canvas of one unit, because a viewBox of zero has no coordinate system.
        var width = pages.Count > 0 ? pages.Max(page => page.Width) : 0d;
        var height = pages.Count > 0 ? pages.Max(page => page.Height) : 0d;
        var canvasWidth = width > 0d ? width : 1d;
        var canvasHeight = height > 0d ? height : 1d;

        writer.WriteStartDocument();
        Start(writer, "svg");
        Attribute(writer, "version", "1.1");
        Attribute(writer, "width", $"{LayoutExportXml.Number(canvasWidth)}pt");
        Attribute(writer, "height", $"{LayoutExportXml.Number(canvasHeight)}pt");
        Attribute(
            writer,
            "viewBox",
            $"0 0 {LayoutExportXml.Number(canvasWidth)} {LayoutExportXml.Number(canvasHeight)}");

        foreach (var page in pages)
        {
            WritePage(writer, page);
        }

        End(writer);
        writer.WriteEndDocument();
    }

    /// <summary>Writes one page as a group of blocks.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page to write.</param>
    private static void WritePage(XmlWriter writer, LayoutExportPage page)
    {
        Start(writer, "g");
        Attribute(writer, "id", string.Create(CultureInfo.InvariantCulture, $"page-{page.PageNumber}"));

        foreach (var block in page.Blocks)
        {
            WriteBlock(writer, page, block);
        }

        End(writer);
    }

    /// <summary>Writes one block as a group holding the words of its lines.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the block sits on.</param>
    /// <param name="block">The block to write.</param>
    private static void WriteBlock(XmlWriter writer, LayoutExportPage page, LayoutExportBlock block)
    {
        Start(writer, "g");
        Attribute(
            writer,
            "id",
            string.Create(CultureInfo.InvariantCulture, $"block-{page.PageNumber}-{block.ReadingOrder}"));

        foreach (var line in block.Lines)
        {
            foreach (var word in line.Words)
            {
                WriteWord(writer, page, word);
            }
        }

        End(writer);
    }

    /// <summary>Writes one word at the place it is drawn.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the word sits on.</param>
    /// <param name="word">The word to write.</param>
    private static void WriteWord(XmlWriter writer, LayoutExportPage page, PdfWord word)
    {
        Start(writer, "text");
        Attribute(writer, "x", LayoutExportXml.Number(word.BoundingBox.Left));
        Attribute(writer, "y", LayoutExportXml.Number(page.Height - word.BaselineY));
        Attribute(writer, "font-size", LayoutExportXml.Number(word.FontSize));

        if (!string.IsNullOrEmpty(word.FontName))
        {
            Attribute(writer, "font-family", word.FontName);
        }

        writer.WriteString(LayoutExportXml.Clean(word.Text));
        End(writer);
    }

    /// <summary>Starts an element of SVG.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="name">The local name of the element.</param>
    private static void Start(XmlWriter writer, string name) => writer.WriteStartElement(name, Namespace);

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
