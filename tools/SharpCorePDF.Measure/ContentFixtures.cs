using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SharpCorePDF.Measure;

/// <summary>
/// Writes the documents the content-reading rows measure: one with many pages to index, one with many
/// pages of text, and one with a single attachment whose payload is the size the row is named after.
/// </summary>
/// <remarks>
/// The harness writes these instead of reading them from the fixture corpus, because the corpus carries no
/// 200-page document and no attachment of a known size, and because a measurement has to state the exact
/// bytes it measured. Like the rest of the harness this is not part of the library.
/// </remarks>
internal static class ContentFixtures
    {
    /// <summary>Builds a document with one page per requested page, each drawing the same image.</summary>
    /// <param name="pages">The number of pages.</param>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Indexed(int pages)
    {
        var objects = new Objects();

        var catalog = objects.Reserve();
        var pageTree = objects.Reserve();
        var font = objects.Reserve();
        var image = objects.Reserve();
        var firstPage = objects.Reserve();
        var kids = new List<string>(pages);

        for (var index = 0; index < pages; index++)
        {
            var page = index == 0 ? firstPage : objects.Reserve();
            var content = objects.Reserve();
            var number = index + 1;

            kids.Add($"{page} 0 R");

            objects.Set(
                page,
                $"<< /Type /Page /Parent {pageTree} 0 R /MediaBox [0 0 595 842] "
                + $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im1 {image} 0 R >> >> "
                + $"/Contents {content} 0 R >>");

            objects.Set(
                content,
                Objects.Content(
                    $"BT /F1 12 Tf 40 780 Td (Page {number} of {pages}) Tj ET "
                    + "q 40 0 0 40 40 700 cm /Im1 Do Q"));
        }

        objects.Set(
            pageTree,
            $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages} /MediaBox [0 0 595 842] >>");

        objects.Set(catalog, $"<< /Type /Catalog /Pages {pageTree} 0 R >>");
        objects.Set(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        // One byte per sample, stored as a Flate stream, so every page decodes an image as well as a page.
        objects.Set(
            image,
            Objects.StreamBody(
                "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray "
                + "/BitsPerComponent 8 /Filter /FlateDecode",
                Objects.Compress([128])));

        return objects.Build(catalog);
    }

    /// <summary>Builds a document with one page per requested page, each drawing a paragraph of text.</summary>
    /// <param name="pages">The number of pages.</param>
    /// <returns>The bytes of the document.</returns>
    /// <remarks>
    /// Every page carries a heading and six lines of body text, drawn one <c>Tj</c> per line so that the
    /// read has to follow the text matrix, the leading and the word gaps rather than one long string.
    /// </remarks>
    public static byte[] Text(int pages)
    {
        const string Line = "The quick brown fox jumps over the lazy dog and keeps running south.";

        var objects = new Objects();

        var catalog = objects.Reserve();
        var pageTree = objects.Reserve();
        var font = objects.Reserve();
        var firstPage = objects.Reserve();
        var kids = new List<string>(pages);

        for (var index = 0; index < pages; index++)
        {
            var page = index == 0 ? firstPage : objects.Reserve();
            var content = objects.Reserve();
            var body = new StringBuilder(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"BT /F1 11 Tf 40 780 Td 14 TL (Page {index + 1} of {pages}) Tj T*"));

            for (var line = 0; line < 6; line++)
            {
                body.Append(" (").Append(Line).Append(") Tj T*");
            }

            body.Append(" ET");

            kids.Add($"{page} 0 R");

            objects.Set(
                page,
                $"<< /Type /Page /Parent {pageTree} 0 R /MediaBox [0 0 595 842] "
                + $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

            objects.Set(content, Objects.Content(body.ToString()));
        }

        objects.Set(
            pageTree,
            $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages} /MediaBox [0 0 595 842] >>");

        objects.Set(catalog, $"<< /Type /Catalog /Pages {pageTree} 0 R >>");
        objects.Set(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        return objects.Build(catalog);
    }

    /// <summary>Builds a one page document carrying one attachment of the requested size.</summary>
    /// <param name="payloadBytes">The size of the attachment's payload in bytes.</param>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Attachment(int payloadBytes)
    {
        var objects = new Objects();

        var catalog = objects.Reserve();
        var pageTree = objects.Reserve();
        var page = objects.Reserve();
        var content = objects.Reserve();
        var tree = objects.Reserve();
        var spec = objects.Reserve();
        var data = objects.Reserve();

        objects.Set(
            catalog,
            $"<< /Type /Catalog /Pages {pageTree} 0 R /Names << /EmbeddedFiles {tree} 0 R >> >>");

        objects.Set(pageTree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 595 842] >>");
        objects.Set(
            page,
            $"<< /Type /Page /Parent {pageTree} 0 R /MediaBox [0 0 595 842] /Contents {content} 0 R >>");

        objects.Set(content, Objects.Content("q Q"));
        objects.Set(tree, $"<< /Names [(payload.bin) {spec} 0 R] >>");
        objects.Set(
            spec,
            $"<< /Type /Filespec /F (payload.bin) /UF (payload.bin) /EF << /F {data} 0 R >> >>");

        // Stored without a filter, so the bytes the copy route writes are exactly these bytes and the row
        // measures the copy rather than a decoder.
        var payload = new byte[payloadBytes];
        var state = 0x2F6B1A47u;

        for (var index = 0; index < payload.Length; index++)
        {
            state = (state * 1664525) + 1013904223;
            payload[index] = (byte)(state >> 24);
        }

        objects.Set(
            data,
            Objects.StreamBody("/Type /EmbeddedFile /Subtype /application#2Foctet-stream", payload));

        return objects.Build(catalog);
    }

    /// <summary>
    /// The objects of a document, written in number order with a classic cross-reference table, which is
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
