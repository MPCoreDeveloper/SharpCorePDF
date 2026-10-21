using System.Globalization;
using System.Text;

namespace SharpCorePDF.Tests;

/// <summary>
/// The documents the read tests open: small, hand-built PDFs whose contents are stated in code, so an
/// assertion can name the exact byte, filter or destination it expects.
/// </summary>
internal static class PdfContentFixtures
{
    /// <summary>Gets the stream of the JPEG image, which a DCT filter must return untouched.</summary>
    public static byte[] JpegData { get; } =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9];

    /// <summary>Gets the samples of the Flate image, which is 2 x 2 RGB.</summary>
    public static byte[] ImageSamples { get; } = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120];

    /// <summary>Gets the samples of the soft mask.</summary>
    public static byte[] MaskSamples { get; } = [0, 64, 128, 255];

    /// <summary>Gets the bytes of the inline image.</summary>
    public static byte[] InlineSamples { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 65];

    /// <summary>Gets the bytes of the JPX image, which this library does not decode.</summary>
    public static byte[] JpxData { get; } = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20];

    /// <summary>
    /// A two page document: page 1 paints a JPEG twice and a Flate image with a soft mask; page 2 paints
    /// the same Flate image, a JPX image, an image reachable only through a form, and an inline image.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Images()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page1 = builder.Reserve();
        var page2 = builder.Reserve();
        var content1 = builder.Reserve();
        var content2 = builder.Reserve();

        var jpeg = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode",
            JpegData);

        var flate = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(ImageSamples));

        var jpx = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /JPXDecode",
            JpxData);

        var mask = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(MaskSamples));

        var masked = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /SMask {mask} 0 R",
            PdfBuilder.Compress(ImageSamples));

        var form = builder.AddStream(
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << /XObject << /Im1 {jpeg} 0 R >> >>",
            Encoding.Latin1.GetBytes("/Im1 Do"));

        builder.Set(
            content1,
            Content(
                "q 50 0 0 50 10 20 cm /Im1 Do Q q 50 0 0 50 100 100 cm /Im1 Do Q "
                + "q 100 0 0 100 0 0 cm /Im2 Do Q q 20 0 0 20 0 0 cm /Im4 Do Q"));

        builder.Set(content2, InlineContent());

        builder.Set(page1, Page(pages, $"/Im1 {jpeg} 0 R /Im2 {flate} 0 R /Im4 {masked} 0 R", content1));
        builder.Set(page2, Page(pages, $"/Im2 {flate} 0 R /Im3 {jpx} 0 R /Fm1 {form} 0 R", content2));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page1} 0 R {page2} 0 R] /Count 2 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>A one page document whose only image is far larger than the default inline budget.</summary>
    /// <param name="size">The number of samples, which is also the decoded payload length.</param>
    /// <returns>The bytes of the document.</returns>
    public static byte[] LargeImage(int size = 400_000)
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var samples = Noise(size);

        var image = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 100 /Height 100 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(samples));

        builder.Set(content, Content("/Big Do"));
        builder.Set(page, Page(pages, $"/Big {image} 0 R", content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A one page document whose painting is split across two content streams. The first ends with the
    /// <c>Do</c> keyword and the second begins with <c>Q</c>, so a reader that does not put whitespace
    /// between the streams reads a single word <c>DoQ</c> and paints nothing at all.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] SplitContent()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var first = builder.Reserve();
        var second = builder.Reserve();

        var image = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(ImageSamples));

        builder.Set(first, Content("q 10 0 0 10 0 0 cm /Half Do"));
        builder.Set(second, Content("Q"));
        builder.Set(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] "
            + $"/Resources << /XObject << /Half {image} 0 R >> >> /Contents [{first} 0 R {second} 0 R] >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    private static string Page(int pages, string xobjects, int content) =>
        $"<< /Type /Page /Parent {pages} 0 R /Resources << /XObject << {xobjects} >> >> /Contents {content} 0 R >>";

    private static byte[] Content(string text) =>
        PdfBuilder.StreamBody(string.Empty, Encoding.Latin1.GetBytes(text));

    private static byte[] InlineContent()
    {
        var prefix = Encoding.Latin1.GetBytes(
            "q 100 0 0 100 0 0 cm /Im2 Do Q q 10 0 0 10 5 5 cm /Fm1 Do Q "
            + "q 10 0 0 10 0 0 cm /Im3 Do Q q 10 0 0 10 0 0 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID ");

        var suffix = Encoding.Latin1.GetBytes(" EI Q");
        var body = new byte[prefix.Length + InlineSamples.Length + suffix.Length];

        prefix.CopyTo(body, 0);
        InlineSamples.CopyTo(body, prefix.Length);
        suffix.CopyTo(body, prefix.Length + InlineSamples.Length);

        return PdfBuilder.StreamBody(string.Empty, body);
    }

    /// <summary>
    /// One page carrying a text annotation, a highlight, a stamp, an annotation of a subtype this
    /// library does not model, three links — an external one, a direct destination, a named one that
    /// resolves — and a named destination that does not.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Annotations()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();
        var appearance = builder.AddStream(string.Empty, Encoding.Latin1.GetBytes("q Q"));

        builder.Set(content, Content(string.Empty));
        builder.Set(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Contents {content} 0 R /Annots ["
            + "<< /Subtype /Text /Rect [10 10 20 20] /Contents (Note) /T (Alice) /M (D:20240101120000Z) /F 4 /NM (note-1) "
            + $"/AP << /N {appearance} 0 R >> >> "
            + "<< /Subtype /Highlight /Rect [0 0 100 100] /Contents (Marked) >> "
            + "<< /Subtype /Stamp /Rect [50 50 150 150] /Name /Approved >> "
            + "<< /Subtype /FooBar /Rect [1 2 3 4] >> "
            + "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /URI /URI (https://example.com/a?b=c) >> >> "
            + $"<< /Subtype /Link /Rect [0 0 10 10] /Dest [{page} 0 R /XYZ 5 6 0] >> "
            + "<< /Subtype /Link /Rect [0 0 10 10] /Dest (chapter1) >> "
            + "<< /Subtype /Link /Rect [0 0 10 10] /Dest (missing) >> "
            + "] >>");

        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(
            catalog,
            $"<< /Type /Catalog /Pages {pages} 0 R /Dests << /chapter1 [{page} 0 R /Fit] >> >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page whose form has a nested field, a button with three widgets, a signature field and a
    /// text field with no value at all.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Form()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();
        var acroForm = builder.Reserve();
        var person = builder.Reserve();
        var name = builder.Reserve();
        var nameWidget = builder.Reserve();
        var group = builder.Reserve();
        var groupWidget1 = builder.Reserve();
        var groupWidget2 = builder.Reserve();
        var groupWidget3 = builder.Reserve();
        var signature = builder.Reserve();
        var signatureWidget = builder.Reserve();
        var empty = builder.Reserve();

        builder.Set(content, Content(string.Empty));
        builder.Set(nameWidget, $" << /Type /Annot /Subtype /Widget /Rect [10 10 110 30] /P {page} 0 R >>");
        builder.Set(name, $"<< /T (name) /FT /Tx /V (Alice) /DV (Bob) /Ff 1 /Kids [{nameWidget} 0 R] >>");
        builder.Set(person, $"<< /T (person) /Kids [{name} 0 R] >>");
        builder.Set(groupWidget1, $" << /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent {group} 0 R >>");
        builder.Set(groupWidget2, $" << /Type /Annot /Subtype /Widget /Rect [20 0 30 10] /Parent {group} 0 R >>");
        builder.Set(groupWidget3, $" << /Type /Annot /Subtype /Widget /Rect [40 0 50 10] /Parent {group} 0 R >>");
        builder.Set(
            group,
            $"<< /T (group) /FT /Btn /V /Yes /Ff 2 /Kids [{groupWidget1} 0 R {groupWidget2} 0 R {groupWidget3} 0 R] >>");

        builder.Set(signatureWidget, "<< /Type /Annot /Subtype /Widget /Rect [0 0 50 50] >>");
        builder.Set(signature, $"<< /T (sig) /FT /Sig /V << /Type /Sig >> /Kids [{signatureWidget} 0 R] >>");
        builder.Set(empty, "<< /T (empty) /FT /Tx >>");
        builder.Set(acroForm, $"<< /Fields [{person} 0 R {group} 0 R {signature} 0 R {empty} 0 R] >>");
        builder.Set(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Contents {content} 0 R /Annots "
            + $"[{nameWidget} 0 R {groupWidget1} 0 R {groupWidget2} 0 R {groupWidget3} 0 R {signatureWidget} 0 R] >>");

        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /AcroForm {acroForm} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Gets the XMP packet the metadata fixture carries.</summary>
    public static string XmpText => """
        <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
         <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
          <rdf:Description rdf:about=""
            xmlns:dc="http://purl.org/dc/elements/1.1/"
            xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
            xmlns:xmp="http://ns.adobe.com/xap/1.0/">
           <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Sample title</rdf:li><rdf:li xml:lang="de">Beispiel</rdf:li></rdf:Alt></dc:title>
           <dc:creator><rdf:Seq><rdf:li>Ada</rdf:li><rdf:li>Grace</rdf:li></rdf:Seq></dc:creator>
           <pdf:Producer>SharpCorePDF tests</pdf:Producer>
           <xmp:CreatorTool>Unit test</xmp:CreatorTool>
          </rdf:Description>
         </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    /// <summary>Gets the bytes of the compressed embedded file the metadata fixture carries.</summary>
    public static byte[] EmbeddedBinary { get; } = Encoding.Latin1.GetBytes("binary payload");

    /// <summary>
    /// One document carrying an XMP packet and three filename tree entries, two of which share a name.
    /// </summary>
    /// <param name="wellFormed">Whether the packet is XML this library can parse.</param>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Metadata(bool wellFormed = true)
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();
        var metadata = builder.Reserve();
        var tree = builder.Reserve();
        var spec1 = builder.Reserve();
        var spec2 = builder.Reserve();
        var spec3 = builder.Reserve();

        var xmp = wellFormed ? XmpText : "<x:xmpmeta><rdf:RDF";

        builder.Set(content, Content(string.Empty));
        builder.Set(metadata, PdfBuilder.StreamBody(string.Empty, Encoding.UTF8.GetBytes(xmp)));

        var first = builder.AddStream(
            "<< /Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size 5 /ModDate (D:20240101120000Z) >>",
            Encoding.Latin1.GetBytes("hello"));

        var second = builder.AddStream(
            "<< /Type /EmbeddedFile /Subtype /text#2Fplain",
            Encoding.Latin1.GetBytes("second"));

        var third = builder.AddStream(
            "<< /Type /EmbeddedFile /Subtype /application#2Foctet-stream /Filter /FlateDecode",
            PdfBuilder.Compress(EmbeddedBinary));

        builder.Set(spec1, $"<< /Type /Filespec /F (a.txt) /UF (a.txt) /AFRelationship /Data /EF << /F {first} 0 R >> >>");
        builder.Set(spec2, $"<< /Type /Filespec /F (a.txt) /UF (a.txt) /AFRelationship /Data /EF << /F {second} 0 R >> >>");
        builder.Set(spec3, $"<< /Type /Filespec /F (b.bin) /UF (b.bin) /AFRelationship /Source /EF << /F {third} 0 R >> >>");
        builder.Set(
            tree,
            $"<< /Names [(a.txt) {spec1} 0 R (a.txt) {spec2} 0 R (b.bin) {spec3} 0 R] >>");

        builder.Set(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Contents {content} 0 R >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(
            catalog,
            $"<< /Type /Catalog /Pages {pages} 0 R /Metadata {metadata} 0 R /Names << /EmbeddedFiles {tree} 0 R >> >>");

        return builder.Build(catalog);
    }

    /// <summary>A one page document with neither an XMP packet nor an embedded file.</summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] WithoutMetadata()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        builder.Set(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// Two pages with a three level outline: a direct destination, a named one that resolves, one that
    /// does not, a closed branch, a bookmark that targets another file and one that targets a URL.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Outline()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page1 = builder.Reserve();
        var page2 = builder.Reserve();
        var outlines = builder.Reserve();
        var chapter1 = builder.Reserve();
        var section11 = builder.Reserve();
        var topic111 = builder.Reserve();
        var section12 = builder.Reserve();
        var chapter2 = builder.Reserve();
        var hidden = builder.Reserve();
        var otherFile = builder.Reserve();
        var website = builder.Reserve();

        builder.Set(page1, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] >>");
        builder.Set(page2, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page1} 0 R {page2} 0 R] /Count 2 /MediaBox [0 0 200 200] >>");

        builder.Set(
            chapter1,
            $"<< /Title (Chapter 1) /Parent {outlines} 0 R /Dest [{page1} 0 R /Fit] "
            + $"/First {section11} 0 R /Last {section12} 0 R /Next {chapter2} 0 R /Count 2 >>");

        builder.Set(
            section11,
            $"<< /Title (Section 1.1) /Parent {chapter1} 0 R /Dest [{page2} 0 R /XYZ 0 700 0] "
            + $"/First {topic111} 0 R /Count 1 /Next {section12} 0 R >>");

        builder.Set(topic111, $"<< /Title (Topic 1.1.1) /Parent {section11} 0 R /Dest (chapter2) >>");
        builder.Set(section12, $"<< /Title (Section 1.2) /Parent {chapter1} 0 R /Dest (missing) >>");

        builder.Set(
            chapter2,
            $"<< /Title (Chapter 2) /Parent {outlines} 0 R /A << /S /GoTo /D [{page2} 0 R /Fit] >> "
            + $"/Count -1 /First {hidden} 0 R /Next {otherFile} 0 R >>");

        builder.Set(hidden, $"<< /Title (Hidden) /Parent {chapter2} 0 R /Dest [{page1} 0 R /Fit] >>");

        builder.Set(
            otherFile,
            $"<< /Title (Other file) /Parent {outlines} 0 R "
            + $"/A << /S /GoToR /F (other.pdf) /D [0 /Fit] >> /Next {website} 0 R >>");

        builder.Set(
            website,
            $"<< /Title (Website) /Parent {outlines} 0 R /A << /S /URI /URI (https://example.com) >> >>");

        builder.Set(outlines, $"<< /Type /Outlines /First {chapter1} 0 R /Last {website} 0 R /Count 4 >>");
        builder.Set(
            catalog,
            $"<< /Type /Catalog /Pages {pages} 0 R /Outlines {outlines} 0 R /Dests << /chapter2 [{page2} 0 R /Fit] >> >>");

        return builder.Build(catalog);
    }

    /// <summary>A one page document whose only image declares Flate compression it does not contain.</summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] DamagedImage()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var image = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode",
            [1, 2, 3]);

        builder.Set(content, Content("/Broken Do"));
        builder.Set(page, Page(pages, $"/Broken {image} 0 R", content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Gets the samples of the predicted image: five rows of four grey samples.</summary>
    public static byte[] PredictedSamples { get; } =
    [
        10, 20, 30, 40,
        50, 60, 70, 80,
        90, 100, 110, 120,
        130, 140, 150, 160,
        170, 180, 190, 200,
    ];

    /// <summary>Builds a deterministic sample stream, so a large payload is not the same byte twice.</summary>
    /// <param name="size">The number of samples.</param>
    /// <returns>The samples.</returns>
    public static byte[] Noise(int size)
    {
        var samples = new byte[size];
        var state = 0x2F6E2B1u;

        for (var index = 0; index < samples.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            samples[index] = (byte)(state & 0xFF);
        }

        return samples;
    }

    /// <summary>
    /// A one page document whose only image is Flate compressed with PNG prediction, one row per filter
    /// type 0 to 4, so a decoder that ignores the per-row filter byte produces the wrong samples.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] PredictorImage()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var image = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 4 /Height 5 /ColorSpace /DeviceGray /BitsPerComponent 8 "
            + "/Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 1 /BitsPerComponent 8 /Columns 4 >>",
            PdfBuilder.Compress(EncodePngRows(PredictedSamples, 4, 1)));

        builder.Set(content, Content("/Predicted Do"));
        builder.Set(page, Page(pages, $"/Predicted {image} 0 R", content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A one page document whose only image is predicted and far larger than the default inline budget,
    /// so the budget rule and the copy route can be compared on the same payload.
    /// </summary>
    /// <param name="size">The number of samples, which is also the decoded payload length.</param>
    /// <returns>The bytes of the document.</returns>
    public static byte[] LargePredictorImage(int size = 400_000)
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var columns = 100;
        var rows = size / columns;
        var image = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width {columns} /Height {rows} /ColorSpace /DeviceGray "
            + "/BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 1 "
            + $"/BitsPerComponent 8 /Columns {columns} >>",
            PdfBuilder.Compress(EncodePngRows(Noise(rows * columns), columns, 1)));

        builder.Set(content, Content("/Big Do"));
        builder.Set(page, Page(pages, $"/Big {image} 0 R", content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    // The encoder is the inverse of the decoder under test: each row is preceded by its filter type, and
    // the type cycles through every PNG filter so the fixture covers all five.
    private static byte[] EncodePngRows(byte[] samples, int columns, int bytesPerPixel)
    {
        var rows = samples.Length / columns;
        var encoded = new byte[rows * (columns + 1)];
        var previous = new byte[columns];

        for (var row = 0; row < rows; row++)
        {
            var filterType = row % 5;
            var source = samples.AsSpan(row * columns, columns);
            var target = encoded.AsSpan(row * (columns + 1));

            target[0] = (byte)filterType;

            for (var index = 0; index < columns; index++)
            {
                var left = index >= bytesPerPixel ? source[index - bytesPerPixel] : (byte)0;
                var above = previous[index];
                var upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : (byte)0;
                var prediction = filterType switch
                {
                    1 => left,
                    2 => above,
                    3 => (byte)((left + above) / 2),
                    4 => Paeth(left, above, upperLeft),
                    _ => (byte)0,
                };

                target[index + 1] = (byte)(source[index] - prediction);
            }

            source.CopyTo(previous);
        }

        return encoded;
    }

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        var estimate = left + above - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var aboveDistance = Math.Abs(estimate - above);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);

        if (leftDistance <= aboveDistance && leftDistance <= upperLeftDistance)
        {
            return left;
        }

        return aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    /// <summary>Gets the samples the chained-filter image carries.</summary>
    public static byte[] ChainSamples { get; } = [7, 14, 21, 28];

    /// <summary>
    /// A one page document painting four images: a chain of ASCII85 and Flate, an ASCII hexadecimal
    /// image, and one image per filter the library does not decode.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] FilterChains()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var header = "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 ";

        var chained = builder.AddStream(
            header + "/Filter [/ASCII85Decode /FlateDecode]",
            EncodeAscii85(PdfBuilder.Compress(ChainSamples)));

        var hexadecimal = builder.AddStream(header + "/Filter /ASCIIHexDecode", EncodeAsciiHex(ChainSamples));
        var lzw = builder.AddStream(header + "/Filter /LZWDecode", ChainSamples);
        var runLength = builder.AddStream(header + "/Filter /RunLengthDecode", ChainSamples);

        builder.Set(content, Content("/Chained Do /Hex Do /Lzw Do /Rle Do"));
        builder.Set(
            page,
            Page(
                pages,
                $"/Chained {chained} 0 R /Hex {hexadecimal} 0 R /Lzw {lzw} 0 R /Rle {runLength} 0 R",
                content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    // The two ASCII filters are encoded exactly as the specification states them, including the
    // terminator each decoder stops at.
    private static byte[] EncodeAsciiHex(byte[] data) =>
        Encoding.Latin1.GetBytes(Convert.ToHexString(data) + ">");

    private static byte[] EncodeAscii85(byte[] data)
    {
        var builder = new StringBuilder();

        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var count = Math.Min(4, data.Length - offset);
            var value = 0u;

            for (var index = 0; index < 4; index++)
            {
                value = (value * 256u) + (index < count ? data[offset + index] : (byte)0);
            }

            if (count == 4 && value == 0)
            {
                builder.Append('z');
                continue;
            }

            var digits = new byte[5];

            for (var index = 4; index >= 0; index--)
            {
                digits[index] = (byte)((value % 85) + 33);
                value /= 85;
            }

            builder.Append(Encoding.Latin1.GetString(digits, 0, count + 1));
        }

        builder.Append("~>");

        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>Gets the samples of the inline image that holds a space followed by the letters EI.</summary>
    public static byte[] InlineTerminatorSamples { get; } = [0x20, 0x45, 0x49, 0x20];

    /// <summary>
    /// A one page document with two inline images: one whose samples contain a space followed by "EI",
    /// and one whose terminator is followed directly by a delimiter rather than by whitespace.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] InlineVariants()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var jpeg = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode",
            JpegData);

        byte[][] parts =
        [
            Encoding.Latin1.GetBytes("q 10 0 0 10 0 0 cm BI /W 2 /H 2 /CS /G /BPC 8 ID "),
            InlineTerminatorSamples,
            Encoding.Latin1.GetBytes(" EI/Im1 Do Q q 10 0 0 10 0 0 cm BI /W 2 /H 2 /CS /G /BPC 8 ID "),
            [1, 2, 3, 4],
            Encoding.Latin1.GetBytes(" EI Q"),
        ];

        var body = new byte[parts.Sum(part => part.Length)];
        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(body, offset);
            offset += part.Length;
        }

        builder.Set(content, PdfBuilder.StreamBody(string.Empty, body));
        builder.Set(page, Page(pages, $"/Im1 {jpeg} 0 R", content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Gets the samples of the inline image that is never terminated.</summary>
    public static byte[] UnterminatedSamples { get; } = [9, 8, 7, 6];

    /// <summary>
    /// A one page document whose inline image has no EI, so its data runs to the end of the content
    /// stream and the read has to say so rather than throw.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] UnterminatedInline()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var prefix = Encoding.Latin1.GetBytes("q 10 0 0 10 0 0 cm BI /W 2 /H 2 /CS /G /BPC 8 ID ");
        var body = new byte[prefix.Length + UnterminatedSamples.Length];

        prefix.CopyTo(body, 0);
        UnterminatedSamples.CopyTo(body, prefix.Length);

        builder.Set(content, PdfBuilder.StreamBody(string.Empty, body));
        builder.Set(page, Page(pages, string.Empty, content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Gets the samples of the large soft mask, a decoded payload of 400 000 bytes.</summary>
    public static byte[] LargeMaskSamples { get; } = Noise(400_000);

    /// <summary>
    /// A one page document whose image carries a soft mask far larger than the default inline budget, so
    /// the mask's own payload rule and its copy route can be exercised.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] LargeMaskedImage()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var mask = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 100 /Height 4000 /ColorSpace /DeviceGray "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(LargeMaskSamples));

        var image = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 "
            + $"/Filter /FlateDecode /SMask {mask} 0 R",
            PdfBuilder.Compress(ImageSamples));

        builder.Set(content, Content("/Masked Do"));
        builder.Set(page, Page(pages, $"/Masked {image} 0 R", content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Gets the sample ranges the colour key mask excludes.</summary>
    public static double[] ColorKey { get; } = [0, 1, 0, 1, 0, 1];

    /// <summary>
    /// A one page document whose image states a colour key mask, which is a list of sample ranges rather
    /// than an image and therefore carries no payload.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] ColorKeyMaskedImage()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var image = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 "
            + "/Filter /FlateDecode /Mask [0 1 0 1 0 1]",
            PdfBuilder.Compress(ImageSamples));

        builder.Set(content, Content("/Keyed Do"));
        builder.Set(page, Page(pages, $"/Keyed {image} 0 R", content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page drawn with a simple font: a line, and the line under it, near enough to be the same
    /// block. One code states its width and the rest fall back on the default width of the font.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] Text()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding "
            + "/FirstChar 87 /Widths [944 500] >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (Hello World) Tj 1 0 0 1 72 706 Tm (Second line) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page drawn with two paragraphs, the lower one first, so that the drawing order of the content
    /// and the reading order of the page are not the same.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextBlocks()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 600 Tm (Later paragraph) Tj "
            + "1 0 0 1 72 720 Tm (Earlier paragraph) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page whose two lines are drawn with <c>TJ</c>: the first tightens a pair of glyphs and keeps
    /// one word, the second leaves a gap wide enough to split one.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextKerning()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm [(Ke) 120 (rn)] TJ "
            + "1 0 0 1 72 600 Tm [(Out) -2000 (er)] TJ ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page drawn with a composite font whose <c>/ToUnicode</c> map states three codes one at a time
    /// and three more as a range, and whose descendant font states its widths in both of the forms
    /// <c>/W</c> allows.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextComposite()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var descendant = builder.Add(
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /TestCID "
            + "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            + "/DW 1000 /W [1 [500 700 300] 4 6 600] >>");

        var toUnicode = builder.AddStream(
            string.Empty,
            Encoding.Latin1.GetBytes(
                "/CIDInit /ProcSet findresource begin 12 dict begin begincmap "
                + "1 begincodespacerange <0000> <FFFF> endcodespacerange "
                + "3 beginbfchar <0001> <0048> <0002> <0069> <0003> <00660069> endbfchar "
                + "1 beginbfrange <0004> <0006> <0061> endbfrange "
                + "endcmap end end"));

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type0 /BaseFont /TestCID-Identity-H /Encoding /Identity-H "
            + $"/DescendantFonts [{descendant} 0 R] /ToUnicode {toUnicode} 0 R >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm <000100020003000400050006> Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page drawn with a simple font whose <c>/Differences</c> names the character of one code and
    /// leaves the rest to the base encoding.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextDifferences()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Type /Encoding "
            + "/BaseEncoding /WinAnsiEncoding /Differences [65 /eacute] >> >>");

        var content = builder.Add(Content("BT /F1 12 Tf 1 0 0 1 72 720 Tm (AZ) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// Two pages: the first draws with a composite font that states no <c>/ToUnicode</c> map, the second
    /// with a simple font that can be read.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextUnreadable()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var first = builder.Reserve();
        var second = builder.Reserve();

        var descendant = builder.Add("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /TestCID >>");
        var composite = builder.Add(
            "<< /Type /Font /Subtype /Type0 /BaseFont /TestCID-Identity-H /Encoding /Identity-H "
            + $"/DescendantFonts [{descendant} 0 R] >>");
        var simple = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var unreadable = builder.Add(Content("BT /F1 12 Tf 1 0 0 1 72 720 Tm <00010002> Tj ET"));
        var readable = builder.Add(Content("BT /F1 12 Tf 1 0 0 1 72 720 Tm (Readable) Tj ET"));

        builder.Set(first, TextPage(pages, $"/F1 {composite} 0 R", unreadable));
        builder.Set(second, TextPage(pages, $"/F1 {simple} 0 R", readable));
        builder.Set(pages, PageTree(first, second));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Two pages, each drawing one line, so that a range can select either of them.</summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextPages()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var first = builder.Reserve();
        var second = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var one = builder.Add(Content("BT /F1 12 Tf 1 0 0 1 72 720 Tm (Page one) Tj ET"));
        var two = builder.Add(Content("BT /F1 12 Tf 1 0 0 1 72 720 Tm (Page two) Tj ET"));

        builder.Set(first, TextPage(pages, $"/F1 {font} 0 R", one));
        builder.Set(second, TextPage(pages, $"/F1 {font} 0 R", two));
        builder.Set(pages, PageTree(first, second));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page that paints a form which names no font of its own: the form inherits the font and the size
    /// of the page around it, and its <c>/Matrix</c> moves the text it draws away from the origin.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextForm()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var form = builder.AddStream(
            "/Type /XObject /Subtype /Form /BBox [0 0 300 300] /Matrix [1 0 0 1 50 60]",
            Content("BT 1 0 0 1 10 10 Tm (Form text) Tj ET"));

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (Page text) Tj ET q /Fm1 Do Q"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content, $"/Fm1 {form} 0 R"));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page whose middle line is drawn inside <c>q</c> and <c>Q</c> at four times the size of the lines
    /// around it, so that the size the closing <c>Q</c> restores shows in the last line.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextSavedState()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (Outer) Tj ET "
            + "q BT /F1 48 Tf 1 0 0 1 72 600 Tm (Inner) Tj ET Q "
            + "BT 1 0 0 1 72 500 Tm (After) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Builds the body of a page that names the fonts given and draws the content given.</summary>
    /// <param name="parent">The object number of the page tree the page belongs to.</param>
    /// <param name="fonts">The entries of the <c>/Font</c> dictionary of the page.</param>
    /// <param name="content">The object number of the content stream of the page.</param>
    /// <param name="xobjects">The entries of the <c>/XObject</c> dictionary of the page, when it has one.</param>
    /// <returns>The body of the page.</returns>
    private static string TextPage(int parent, string fonts, int content, string? xobjects = null) =>
        $"<< /Type /Page /Parent {parent} 0 R /MediaBox [0 0 612 792] "
        + $"/Resources << /Font << {fonts} >>"
        + (xobjects is null ? " " : $" /XObject << {xobjects} >> ")
        + $">> /Contents {content} 0 R >>";

    /// <summary>Builds the body of a page tree that holds the pages given.</summary>
    /// <param name="kids">The object numbers of the pages.</param>
    /// <returns>The body of the page tree.</returns>
    private static string PageTree(params int[] kids) =>
        $"<< /Type /Pages /Kids [{string.Join(' ', kids.Select(kid => $"{kid} 0 R"))}] "
        + $"/Count {kids.Length} /MediaBox [0 0 612 792] >>";
}

