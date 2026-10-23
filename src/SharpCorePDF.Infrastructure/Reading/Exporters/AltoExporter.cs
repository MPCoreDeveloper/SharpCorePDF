using System.Globalization;
using System.Xml;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

namespace SharpCorePDF.Infrastructure.Reading.Exporters;

/// <summary>
/// Writes a read as ALTO XML: one document, a <c>Page</c> per page, the blocks, lines and words of each page
/// inside a print space.
/// </summary>
/// <remarks>
/// <para>
/// ALTO is the format an archive ingests when it wants the text of a page together with the box every word
/// occupies, and it is written here in the vocabulary that format names: a <c>TextBlock</c> per block of the
/// read, a <c>TextLine</c> per line of that block and a <c>String</c> per word. The reading order of the read
/// is the order the elements appear in, which is what a consumer that has no reading order of its own falls
/// back to.
/// </para>
/// <para>
/// The measurement unit is <c>pixel</c> — the space ALTO gives the coordinates of a page, one unit per point
/// — because this library does not render the page into an image whose pixels the coordinates would be.
/// Every coordinate is therefore the page's own number with the origin moved to the top left corner, and a
/// consumer that needs a physical length divides by 72 for inches. A page with no text is written as a page
/// with an empty print space: a valid document, and the honest statement of what the read found.
/// </para>
/// </remarks>
internal static class AltoExporter
{
    /// <summary>The namespace of ALTO 4, the version this exporter writes.</summary>
    private const string Namespace = "http://www.loc.gov/standards/alto/ns-v4#";

    /// <summary>Writes the document.</summary>
    /// <param name="pages">The pages of the range, in ascending page order.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    public static void Write(IReadOnlyList<LayoutExportPage> pages, Stream destination)
    {
        using var writer = LayoutExportXml.Create(destination);

        writer.WriteStartDocument();
        Start(writer, "alto");

        Start(writer, "Description");
        Start(writer, "MeasurementUnit");
        writer.WriteString("pixel");
        End(writer);
        End(writer);

        Start(writer, "Layout");

        foreach (var page in pages)
        {
            WritePage(writer, page);
        }

        End(writer);
        End(writer);
        writer.WriteEndDocument();
    }

    /// <summary>Writes one page and the print space that holds its blocks.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page to write.</param>
    private static void WritePage(XmlWriter writer, LayoutExportPage page)
    {
        Start(writer, "Page");
        Attribute(writer, "ID", string.Create(CultureInfo.InvariantCulture, $"page_{page.PageNumber}"));
        Attribute(writer, "PHYSICAL_IMG_NR", LayoutExportXml.Count(page.PageNumber));
        Attribute(writer, "WIDTH", LayoutExportXml.Count(LayoutExportXml.Unit(page.Width)));
        Attribute(writer, "HEIGHT", LayoutExportXml.Count(LayoutExportXml.Unit(page.Height)));

        Start(writer, "PrintSpace");
        Attribute(writer, "HPOS", "0");
        Attribute(writer, "VPOS", "0");
        Attribute(writer, "WIDTH", LayoutExportXml.Count(LayoutExportXml.Unit(page.Width)));
        Attribute(writer, "HEIGHT", LayoutExportXml.Count(LayoutExportXml.Unit(page.Height)));

        foreach (var block in page.Blocks)
        {
            WriteBlock(writer, page, block);
        }

        End(writer);
        End(writer);
    }

    /// <summary>Writes one block, its lines and the strings of each line.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the block sits on, which states the height the y axis is flipped against.</param>
    /// <param name="block">The block to write.</param>
    private static void WriteBlock(XmlWriter writer, LayoutExportPage page, LayoutExportBlock block)
    {
        Start(writer, "TextBlock");
        Attribute(
            writer,
            "ID",
            string.Create(CultureInfo.InvariantCulture, $"block_{page.PageNumber}_{block.ReadingOrder}"));
        LayoutExportXml.WriteBox(writer, block.Bounds, page.Height, "HPOS", "VPOS", "WIDTH", "HEIGHT");

        for (var index = 0; index < block.Lines.Count; index++)
        {
            WriteLine(writer, page, block, block.Lines[index], index + 1);
        }

        End(writer);
    }

    /// <summary>Writes one line and the words that sit on it.</summary>
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
            "ID",
            string.Create(
                CultureInfo.InvariantCulture,
                $"line_{page.PageNumber}_{block.ReadingOrder}_{lineNumber}"));
        LayoutExportXml.WriteBox(writer, line.Bounds, page.Height, "HPOS", "VPOS", "WIDTH", "HEIGHT");

        for (var index = 0; index < line.Words.Count; index++)
        {
            WriteString(writer, page, block, lineNumber, line.Words[index], index + 1);
        }

        End(writer);
    }

    /// <summary>Writes one word as an ALTO string.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page the word sits on.</param>
    /// <param name="block">The block the word belongs to.</param>
    /// <param name="lineNumber">The one-based position of the line the word sits on.</param>
    /// <param name="word">The word to write.</param>
    /// <param name="wordNumber">The one-based position of the word in its line.</param>
    private static void WriteString(
        XmlWriter writer,
        LayoutExportPage page,
        LayoutExportBlock block,
        int lineNumber,
        PdfWord word,
        int wordNumber)
    {
        Start(writer, "String");
        Attribute(
            writer,
            "ID",
            string.Create(
                CultureInfo.InvariantCulture,
                $"string_{page.PageNumber}_{block.ReadingOrder}_{lineNumber}_{wordNumber}"));
        Attribute(writer, "CONTENT", word.Text);
        LayoutExportXml.WriteBox(writer, word.BoundingBox, page.Height, "HPOS", "VPOS", "WIDTH", "HEIGHT");
        End(writer);
    }

    /// <summary>Starts an element of the ALTO vocabulary.</summary>
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
