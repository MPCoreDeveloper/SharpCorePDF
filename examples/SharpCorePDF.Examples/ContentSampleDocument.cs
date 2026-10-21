using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SharpCorePDF.Examples;

/// <summary>
/// Builds the one sample document whose content there is something to read: two pages, an image, a comment,
/// a link, a form field, a bookmark, an attachment and an XMP packet.
/// </summary>
/// <remarks>
/// Like <see cref="SampleDocuments"/>, this is not part of the library and not a recommendation; a real caller
/// reads documents it already has. It exists so that the content-reading example has a document to read and
/// never depends on a fixture file, and so that every read model the example prints is populated by
/// something a document really carries.
/// </remarks>
internal static class ContentSampleDocument
{
    /// <summary>The file name of the sample.</summary>
    public const string FileName = "RichContent.pdf";

    /// <summary>The name of the sample's only attachment.</summary>
    public const string AttachmentName = "notes.txt";

    /// <summary>Writes the sample and returns its path.</summary>
    /// <param name="folder">The folder to write it to.</param>
    /// <returns>The full path of the written document.</returns>
    public static string Write(string folder)
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, FileName);
        File.WriteAllBytes(path, Build());

        return path;
    }

    /// <summary>Builds the document.</summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Build()
    {
        var objects = new Objects();

        var catalog = objects.Reserve();
        var pages = objects.Reserve();
        var firstPage = objects.Reserve();
        var secondPage = objects.Reserve();
        var image = objects.Reserve();
        var firstContent = objects.Reserve();
        var note = objects.Reserve();
        var link = objects.Reserve();
        var secondContent = objects.Reserve();
        var outlines = objects.Reserve();
        var introduction = objects.Reserve();
        var fileTree = objects.Reserve();
        var fileSpec = objects.Reserve();
        var fileData = objects.Reserve();
        var metadata = objects.Reserve();
        var form = objects.Reserve();
        var widget = objects.Reserve();
        var font = objects.Reserve();

        objects.Set(
            catalog,
            $"<< /Type /Catalog /Pages {pages} 0 R /Outlines {outlines} 0 R /AcroForm {form} 0 R "
            + $"/Names << /EmbeddedFiles {fileTree} 0 R >> /Metadata {metadata} 0 R >>");

        objects.Set(
            pages,
            $"<< /Type /Pages /Kids [{firstPage} 0 R {secondPage} 0 R] /Count 2 /MediaBox [0 0 300 300] >>");

        objects.Set(
            firstPage,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 300 300] "
            + $"/Resources << /XObject << /Im1 {image} 0 R >> /Font << /F1 {font} 0 R >> >> "
            + $"/Contents {firstContent} 0 R /Annots [{note} 0 R {link} 0 R {widget} 0 R] >>");

        objects.Set(
            secondPage,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 300 300] "
            + $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {secondContent} 0 R >>");


        // A two by two image, compressed exactly as a writer would store it.
        objects.Set(
            image,
            Objects.StreamBody(
                "/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 "
                + "/Filter /FlateDecode",
                Objects.Compress([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0])));

        objects.Set(
            firstContent,
            Objects.Content("BT /F1 14 Tf 20 265 Td (Quarterly figures) Tj ET q 120 0 0 120 90 90 cm /Im1 Do Q"));
        objects.Set(
            secondContent,
            Objects.Content("BT /F1 18 Tf 40 250 Td (Appendix) Tj 0 -22 Td (Supporting material) Tj ET"));

        objects.Set(
            note,
            "<< /Type /Annot /Subtype /Text /Rect [20 20 40 40] /Contents (Please check these numbers.) "
            + "/T (Alice) /M (D:20260101120000Z) /F 4 >>");

        objects.Set(
            link,
            $"<< /Type /Annot /Subtype /Link /Rect [50 50 150 70] /A << /S /GoTo /D [{secondPage} 0 R /Fit] >> >>");

        objects.Set(
            outlines,
            $"<< /Type /Outlines /First {introduction} 0 R /Last {introduction} 0 R /Count 1 >>");

        objects.Set(
            introduction,
            $"<< /Title (Introduction) /Parent {outlines} 0 R /Dest [{firstPage} 0 R /Fit] >>");

        objects.Set(fileTree, $"<< /Names [({AttachmentName}) {fileSpec} 0 R] >>");

        objects.Set(
            fileSpec,
            $"<< /Type /Filespec /F ({AttachmentName}) /UF ({AttachmentName}) /Desc (Reading notes) "
            + $"/EF << /F {fileData} 0 R >> >>");

        objects.Set(
            fileData,
            Objects.StreamBody(
                "/Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size 64 /ModDate (D:20260101120000Z) >>",
                Encoding.Latin1.GetBytes("The reading notes travel with the document. Do not rewrite them.")));

        objects.Set(metadata, Objects.StreamBody("/Type /Metadata /Subtype /XML", Encoding.UTF8.GetBytes(Xmp)));

        objects.Set(form, $"<< /Fields [{widget} 0 R] >>");

        objects.Set(
            widget,
            $"<< /Type /Annot /Subtype /Widget /FT /Tx /T (person.name) /V (Alice) /Rect [40 120 200 140] "
            + $"/P {firstPage} 0 R >>");

        objects.Set(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        return objects.Build(catalog);
    }

    /// <summary>The XMP packet the sample carries, with the properties the example prints.</summary>
    private const string Xmp = """
        <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
         <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
          <rdf:Description rdf:about=""
            xmlns:dc="http://purl.org/dc/elements/1.1/"
            xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
            xmlns:xmp="http://ns.adobe.com/xap/1.0/">
           <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Content inspection sample</rdf:li></rdf:Alt></dc:title>
           <pdf:Producer>SharpCorePDF.Examples</pdf:Producer>
           <xmp:CreatorTool>SharpCorePDF.Examples</xmp:CreatorTool>
          </rdf:Description>
         </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    /// <summary>
    /// The objects of the document, written in number order with a classic cross-reference table, which is
    /// what lets the reader open the result with no tolerance at all.
    /// </summary>
    private sealed class Objects
    {
        private readonly List<byte[]?> bodies = [];

        /// <summary>Reserves an object number for a body that is set later.</summary>
        /// <returns>The object number.</returns>
        public int Reserve()
        {
            bodies.Add(null);
            return bodies.Count;
        }

        /// <summary>Sets the body of an object.</summary>
        /// <param name="number">The object number.</param>
        /// <param name="body">The body, without its <c>obj</c> and <c>endobj</c> markers.</param>
        public void Set(int number, string body) => bodies[number - 1] = Encoding.Latin1.GetBytes(body);

        /// <summary>Sets the body of an object to the given bytes.</summary>
        /// <param name="number">The object number.</param>
        /// <param name="body">The body, without its <c>obj</c> and <c>endobj</c> markers.</param>
        public void Set(int number, byte[] body) => bodies[number - 1] = body;

        /// <summary>Builds the body of a stream object.</summary>
        /// <param name="entries">The stream dictionary, without its <c>/Length</c>.</param>
        /// <param name="data">The bytes of the stream, written as they are.</param>
        /// <returns>The body of the object.</returns>
        public static byte[] StreamBody(string entries, byte[] data)
        {
            var header = Encoding.Latin1.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Length {data.Length} >>\nstream\n"));

            var footer = Encoding.Latin1.GetBytes("\nendstream");
            var body = new byte[header.Length + data.Length + footer.Length];

            header.CopyTo(body, 0);
            data.CopyTo(body, header.Length);
            footer.CopyTo(body, header.Length + data.Length);

            return body;
        }

        /// <summary>Builds a content stream from its operators.</summary>
        /// <param name="operators">The content stream, as text.</param>
        /// <returns>The body of the object.</returns>
        public static byte[] Content(string operators) =>
            StreamBody(string.Empty, Encoding.Latin1.GetBytes(operators));

        /// <summary>Compresses bytes with the zlib wrapper a Flate stream uses.</summary>
        /// <param name="data">The bytes to compress.</param>
        /// <returns>The compressed bytes.</returns>
        public static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();

            using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                compressor.Write(data);
            }

            return output.ToArray();
        }

        /// <summary>Writes the document.</summary>
        /// <param name="root">The object number of the catalog.</param>
        /// <returns>The bytes of the document.</returns>
        public byte[] Build(int root)
        {
            using var output = new MemoryStream();
            var offsets = new long[bodies.Count];

            Write(output, "%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");

            for (var index = 0; index < bodies.Count; index++)
            {
                offsets[index] = output.Position;

                Write(output, string.Create(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n"));
                output.Write(bodies[index] ?? Encoding.Latin1.GetBytes("null"));
                Write(output, "\nendobj\n");
            }

            var startXref = output.Position;

            Write(
                output,
                string.Create(CultureInfo.InvariantCulture, $"xref\n0 {bodies.Count + 1}\n0000000000 65535 f \n"));

            foreach (var offset in offsets)
            {
                Write(output, string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
            }

            Write(
                output,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"trailer\n<< /Size {bodies.Count + 1} /Root {root} 0 R >>\nstartxref\n{startXref}\n%%EOF\n"));

            return output.ToArray();
        }

        private static void Write(Stream destination, string text) =>
            destination.Write(Encoding.Latin1.GetBytes(text));
    }
}
