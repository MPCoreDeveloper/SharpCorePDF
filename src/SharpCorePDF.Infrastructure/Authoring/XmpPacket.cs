using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Builds the XMP packet a PDF/A document carries: the identification of the profile, and the document
/// information mirrored into the properties ISO 19005 requires.
/// </summary>
/// <remarks>
/// <para>
/// The packet is written as UTF-8 and is never compressed, which PDF/A-1 requires and the later parts
/// allow. Every value of the document information dictionary is mirrored here, because a profile
/// requires the two to agree; the caller therefore writes the dictionary from the same values this
/// packet states, rather than converting one into the other afterwards.
/// </para>
/// <para>
/// Only the standard document information keys have a home in the packet. A custom key would need an
/// extension schema, which this release does not write, so the writer drops custom entries before it
/// builds both the dictionary and the packet and reports that it did.
/// </para>
/// </remarks>
internal static class XmpPacket
{
    /// <summary>The packet identifier every XMP reader recognises.</summary>
    private const string PacketId = "W5M0MpCehiHzreSzNTczkc9d";

    /// <summary>Builds the XMP packet of a PDF/A document.</summary>
    /// <param name="profile">The profile the document is written for.</param>
    /// <param name="metadata">The document information that is also written as the information dictionary.</param>
    /// <returns>The bytes of the packet, into which the file is embedded.</returns>
    public static byte[] Build(PdfAProfile profile, PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var xml = new StringBuilder(1024);

        xml.Append("<?xpacket begin=\"\uFEFF\" id=\"").Append(PacketId).Append("\"?>\n");
        xml.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"SharpCorePDF\">\n");
        xml.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");

        WriteIdentification(xml, profile);
        WriteDublinCore(xml, metadata);
        WriteDocumentProperties(xml, metadata);

        xml.Append(" </rdf:RDF>\n");
        xml.Append("</x:xmpmeta>\n");
        xml.Append("<?xpacket end=\"w\"?>");

        return Encoding.UTF8.GetBytes(xml.ToString());
    }

    /// <summary>Writes the description that identifies the profile.</summary>
    /// <param name="xml">The packet being built.</param>
    /// <param name="profile">The profile.</param>
    private static void WriteIdentification(StringBuilder xml, PdfAProfile profile)
    {
        xml.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n");
        xml.Append("   <pdfaid:part>")
            .Append(PdfAProfileRules.Part(profile).ToString(CultureInfo.InvariantCulture))
            .Append("</pdfaid:part>\n");
        xml.Append("   <pdfaid:conformance>")
            .Append(PdfAProfileRules.Conformance(profile))
            .Append("</pdfaid:conformance>\n");
        xml.Append("  </rdf:Description>\n");
    }

    /// <summary>Writes the Dublin Core properties the document information mirrors.</summary>
    /// <param name="xml">The packet being built.</param>
    /// <param name="metadata">The document information.</param>
    private static void WriteDublinCore(StringBuilder xml, PdfMetadata metadata)
    {
        if (metadata.Title is null && metadata.Author is null && metadata.Subject is null)
        {
            return;
        }

        xml.Append("  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");

        WriteLocalised(xml, "dc:title", metadata.Title);
        WriteSequence(xml, "dc:creator", metadata.Author);
        WriteLocalised(xml, "dc:description", metadata.Subject);

        xml.Append("  </rdf:Description>\n");
    }

    /// <summary>Writes the PDF and XMP properties the document information mirrors.</summary>
    /// <param name="xml">The packet being built.</param>
    /// <param name="metadata">The document information.</param>
    private static void WriteDocumentProperties(StringBuilder xml, PdfMetadata metadata)
    {
        xml.Append("  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"");
        xml.Append(" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">\n");

        WriteSimple(xml, "pdf:Keywords", metadata.Keywords);
        WriteSimple(xml, "pdf:Producer", metadata.Producer);
        WriteSimple(xml, "xmp:CreatorTool", metadata.Creator);
        WriteDate(xml, "xmp:CreateDate", metadata.CreationDate);
        WriteDate(xml, "xmp:ModifyDate", metadata.ModificationDate);
        WriteDate(xml, "xmp:MetadataDate", metadata.ModificationDate);

        xml.Append("  </rdf:Description>\n");
    }

    /// <summary>Writes a simple property, when it has a value.</summary>
    /// <param name="xml">The packet being built.</param>
    /// <param name="name">The qualified property name.</param>
    /// <param name="value">The value.</param>
    private static void WriteSimple(StringBuilder xml, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        xml.Append("   <").Append(name).Append('>')
            .Append(Escape(value))
            .Append("</").Append(name).Append(">\n");
    }

    /// <summary>Writes a property whose value is a date, when it has one.</summary>
    /// <param name="xml">The packet being built.</param>
    /// <param name="name">The qualified property name.</param>
    /// <param name="value">The date.</param>
    private static void WriteDate(StringBuilder xml, string name, DateTimeOffset? value)
    {
        if (value is not { } moment)
        {
            return;
        }

        var text = moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        WriteSimple(xml, name, text);
    }

    /// <summary>Writes a property that carries one localised value in an alternative array.</summary>
    /// <param name="xml">The packet being built.</param>
    /// <param name="name">The qualified property name.</param>
    /// <param name="value">The value.</param>
    private static void WriteLocalised(StringBuilder xml, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        xml.Append("   <").Append(name).Append("><rdf:Alt><rdf:li xml:lang=\"x-default\">")
            .Append(Escape(value))
            .Append("</rdf:li></rdf:Alt></").Append(name).Append(">\n");
    }

    /// <summary>Writes a property that carries an ordered sequence of values.</summary>
    /// <param name="xml">The packet being built.</param>
    /// <param name="name">The qualified property name.</param>
    /// <param name="value">The value.</param>
    private static void WriteSequence(StringBuilder xml, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        xml.Append("   <").Append(name).Append("><rdf:Seq><rdf:li>")
            .Append(Escape(value))
            .Append("</rdf:li></rdf:Seq></").Append(name).Append(">\n");
    }

    /// <summary>Escapes the characters XML reserves.</summary>
    /// <param name="value">The text to escape.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);
}
