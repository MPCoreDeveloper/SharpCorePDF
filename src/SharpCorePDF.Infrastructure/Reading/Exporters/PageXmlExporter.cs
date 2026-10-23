using System.Globalization;
using System.Xml;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

namespace SharpCorePDF.Infrastructure.Reading.Exporters;

/// <summary>
/// Writes a read as PAGE XML: one document, a <c>Page</c> per page, a <c>TextRegion</c> per block, a
/// <c>TextLine</c> per line and the reading order that names the regions in the order the read found them.
/// </summary>
/// <remarks>
/// <para>
/// PAGE XML is the format transcription and layout tools exchange, so the export carries the two things
/// those tools read first: the polygon of every region and line, and an explicit reading order. The order is
/// written twice — once as <c>RegionRefIndexed</c> entries, which is the authoritative statement, and once as
/// the order the regions appear in, which is what a tool that ignores the reading order falls back to.
/// </para>
/// <para>
/// Coordinates are page coordinates with the origin at the top left corner, whole numbers, written as the
/// polygon PAGE XML expects: left-top, right-top, right-bottom, left-bottom. The document names no creation
/// date, so the same read produces the same bytes on every run (FR-019), and it names no image file of its
/// own beyond the page it describes, because an export is text and does not render the page.
/// </para>
/// </remarks>
internal static class PageXmlExporter
{
    /// <summary>The namespace of the PAGE XML version this exporter writes.</summary>
    private const string Namespace = "http://schema.primaresearch.org/PAGE/gts/pagecontent/2019-07-15";

    /// <summary>Writes the document.</summary>
    /// <param name="pages">The pages of the range, in ascending page order.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    public static void Write(IReadOnlyList<LayoutExportPage> pages, Stream destination)
    {
        using var writer = LayoutExportXml.Create(destination);

        writer.WriteStartDocument();
        Start(writer, "PcGts");

        Start(writer, "Metadata");
        Start(writer, "Creator");
        writer.WriteString("SharpCorePDF");
        End(writer);
        End(writer);

        foreach (var page in pages)
        {
            WritePage(writer, page);
        }

        End(writer);
        writer.WriteEndDocument();
    }

    /// <summary>Writes one page, its reading order and its regions.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page to write.</param>
    private static void WritePage(XmlWriter writer, LayoutExportPage page)
    {
        Start(writer, "Page");
        Attribute(writer, "imageFilename", string.Create(CultureInfo.InvariantCulture, $"page-{page.PageNumber}"));
        Attribute(writer, "imageWidth", LayoutExportXml.Count(LayoutExportXml.Unit(page.Width)));
        Attribute(writer, "imageHeight", LayoutExportXml.Count(LayoutExportXml.Unit(page.Height)));

        if (page.Blocks.Count > 0)
        {
            Start(writer, "ReadingOrder");
            Start(writer, "OrderedGroup");
            Attribute(writer, "id", string.Create(CultureInfo.InvariantCulture, $"reading-order-{page.PageNumber}"));

            for (var index = 0; index < page.Blocks.Count; index++)
            {
                Start(writer, "RegionRefIndexed");
                Attribute(writer, "index", LayoutExportXml.Count(index));
                Attribute(writer, "regionRef", BlockId(page, page.Blocks[index]));
                End(writer);
            }

            End(writer);
            End(writer);
        }

        foreach (var block in page.Blocks)
        {
            WriteBlock(writer, page, block);
        }

        End(writer);
    }

    /// <summary>Writes one text region and the lines it holds.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the region sits on.</param>
    /// <param name="block">The block the region describes.</param>
    private static void WriteBlock(XmlWriter writer, LayoutExportPage page, LayoutExportBlock block)
    {
        var id = BlockId(page, block);

        Start(writer, "TextRegion");
        Attribute(writer, "id", id);
        WriteCoords(writer, block.Bounds, page.Height);

        for (var index = 0; index < block.Lines.Count; index++)
        {
            WriteLine(writer, page, block, block.Lines[index], index + 1);
        }

        End(writer);
    }

    /// <summary>Writes one text line with its polygon and the text it reads as.</summary>
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
        Start(writer, "TextLine");
        Attribute(
            writer,
            "id",
            string.Create(
                CultureInfo.InvariantCulture,
                $"line_{page.PageNumber}_{block.ReadingOrder}_{lineNumber}"));
        WriteCoords(writer, line.Bounds, page.Height);

        Start(writer, "TextEquiv");
        Start(writer, "Unicode");
        writer.WriteString(LayoutExportXml.Clean(string.Join(' ', line.Words.Select(word => word.Text))));
        End(writer);
        End(writer);

        End(writer);
    }

    /// <summary>Writes the four-corner polygon of a box, in the top-left coordinates of the format.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="box">The box, in page space.</param>
    /// <param name="pageHeight">The height of the page the box sits on.</param>
    private static void WriteCoords(XmlWriter writer, PdfRectangle box, double pageHeight)
    {
        var (left, top, right, bottom) = LayoutExportXml.Box(box, pageHeight);

        Start(writer, "Coords");
        Attribute(
            writer,
            "points",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{left},{top} {right},{top} {right},{bottom} {left},{bottom}"));
        End(writer);
    }

    /// <summary>Names a region the way its reading order entry refers to it.</summary>
    /// <param name="page">The page the region sits on.</param>
    /// <param name="block">The block the region describes.</param>
    /// <returns>The identifier of the region.</returns>
    private static string BlockId(LayoutExportPage page, LayoutExportBlock block) =>
        string.Create(CultureInfo.InvariantCulture, $"block_{page.PageNumber}_{block.ReadingOrder}");

    /// <summary>Starts an element of the PAGE XML vocabulary.</summary>
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
