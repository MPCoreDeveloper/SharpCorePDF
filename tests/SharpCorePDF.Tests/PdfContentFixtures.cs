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

    /// <summary>Gets the samples of the palette image of <see cref="ColorSpaces"/>, which are indices.</summary>
    public static byte[] PaletteSamples { get; } = [0, 1, 2, 3];

    /// <summary>Gets the lookup table of the palette image: red, green, blue and white.</summary>
    public static byte[] PaletteLookup { get; } = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];

    /// <summary>Gets the lookup table of the grey palette image, stored in the document as a string.</summary>
    public static byte[] GrayPaletteLookup { get; } = [0, 128, 255, 64];

    /// <summary>
    /// Gets the samples of the bilevel image: two rows of five bits, each row padded to a whole byte.
    /// </summary>
    public static byte[] BilevelSamples { get; } = [0b1010_0000, 0b0101_0000];

    /// <summary>
    /// Gets the samples of the four-bit image, which is three samples wide: one nibble per sample, high
    /// nibble first, and each row padded to a whole byte. The rows are <c>0</c>, <c>15</c>, <c>10</c> and
    /// <c>1</c>, <c>2</c>, <c>3</c> before they are scaled.
    /// </summary>
    public static byte[] FourBitSamples { get; } = [0x0F, 0xA0, 0x12, 0x30];

    /// <summary>
    /// Gets the samples of the two-bit image, which is five samples wide: the row is ten bits of samples
    /// and six of padding, so the pixels are <c>0</c>, <c>1</c>, <c>2</c>, <c>3</c> and <c>1</c> before
    /// they are scaled.
    /// </summary>
    public static byte[] TwoBitSamples { get; } = [0b0001_1011, 0b0100_0000];

    /// <summary>Gets the samples of the CMYK image, four per pixel.</summary>
    public static byte[] CmykSamples { get; } = [0, 128, 255, 0, 255, 0, 128, 255];

    /// <summary>Gets the samples of the ICCBased image, three per pixel.</summary>
    public static byte[] IccSamples { get; } = [1, 2, 3];

    /// <summary>
    /// A one page document whose images each name their colour space in a different shape: a palette
    /// whose table is a stream, a palette whose table is a string, a bilevel grey space, a four-bit and a
    /// two-bit grey space, a device CMYK space, an ICCBased space, a Separation space and a palette image
    /// whose samples index past the table it states.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] ColorSpaces()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var lookup = builder.AddStream("<< /Filter /FlateDecode", PdfBuilder.Compress(PaletteLookup));
        var profile = builder.AddStream("<< /N 3 /Alternate /DeviceRGB", [0x00]);
        var tint = builder.Add("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>");

        var palette = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace [/Indexed /DeviceRGB 3 {lookup} 0 R] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(PaletteSamples));

        var grayPalette = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 3 /Height 1 /ColorSpace [/Indexed /DeviceGray 3 <0080FF40>] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 1, 2]));

        var bilevel = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 5 /Height 2 /ColorSpace /DeviceGray "
            + "/BitsPerComponent 1 /Filter /FlateDecode",
            PdfBuilder.Compress(BilevelSamples));

        var fourBit = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 3 /Height 2 /ColorSpace /DeviceGray "
            + "/BitsPerComponent 4 /Filter /FlateDecode",
            PdfBuilder.Compress(FourBitSamples));

        var twoBit = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 5 /Height 1 /ColorSpace /DeviceGray "
            + "/BitsPerComponent 2 /Filter /FlateDecode",
            PdfBuilder.Compress(TwoBitSamples));

        var cmyk = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceCMYK "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(CmykSamples));

        var icc = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace [/ICCBased {profile} 0 R] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress(IccSamples));

        var separation = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width 1 /Height 2 /ColorSpace [/Separation /Spot /DeviceCMYK {tint} 0 R] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 255]));

        var overrun = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 4 /Height 1 /ColorSpace [/Indexed /DeviceRGB 1 <FF000000FF00>] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 1, 2, 3]));

        builder.Set(
            content,
            Content("/Pal Do /GrayPal Do /Bilevel Do /Four Do /Two Do /Cmyk Do /Icc Do /Sep Do /Over Do"));

        builder.Set(
            page,
            Page(
                pages,
                $"/Pal {palette} 0 R /GrayPal {grayPalette} 0 R /Bilevel {bilevel} 0 R /Four {fourBit} 0 R "
                + $"/Two {twoBit} 0 R /Cmyk {cmyk} 0 R /Icc {icc} 0 R /Sep {separation} 0 R /Over {overrun} 0 R",
                content));

        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A one page document whose images each state a colour space at the edge of what the read model
    /// accepts: the calibrated forms of grey and RGB, a palette over a palette, a palette over a space
    /// that needs a function, a palette whose declared index is zero, one that declares 255 entries and
    /// holds five, an image whose index runs past that table, an <c>ICCBased</c> array with no profile in
    /// it, a one-component and a four-component profile, a palette whose table holds nothing, four-bit
    /// RGB, an image whose width or height is zero, and one that declares no filter at all.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] ColorSpaceEdges()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var tint = builder.Add("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>");

        var calGray = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 "
            + "/ColorSpace [/CalGray << /WhitePoint [0.9505 1 1.089] >>] /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 64, 128, 255]));

        var calRgb = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 3 /Height 1 "
            + "/ColorSpace [/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [2.2 2.2 2.2] >>] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([1, 2, 3, 4, 5, 6, 7, 8, 9]));

        var overPalette = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 "
            + "/ColorSpace [/Indexed [/Indexed /DeviceRGB 1 <FF000000FF00>] 1 <FF0000FF00FF>] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0]));

        var overFunction = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width 2 /Height 1 "
            + $"/ColorSpace [/Indexed [/Separation /Spot /DeviceCMYK {tint} 0 R] 1 <FF000000FF00>] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 1]));

        var singleEntry = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 4 /Height 1 "
            + "/ColorSpace [/Indexed /DeviceGray 0 <00>] /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 0, 0, 0]));

        var wideHival = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 3 /Height 2 "
            + "/ColorSpace [/Indexed /DeviceGray 255 <00FF008000>] /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 1, 2, 3, 4, 1]));

        var overIndex = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 4 /Height 2 "
            + "/ColorSpace [/Indexed /DeviceGray 255 <00FF008000>] /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 1, 2, 5, 0, 0, 0, 0]));

        var bareIcc = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 6 /Height 1 /ColorSpace [/ICCBased] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([1, 2, 3, 4, 5, 6]));

        // A profile states what its samples mean through /N, so a one-component profile is a grey image
        // and a four-component one is ink: both are read by that count rather than by the family name.
        var grayProfile = builder.AddStream("<< /N 1 /Alternate /DeviceGray", [0x00]);

        var cmykProfile = builder.AddStream("<< /N 4 /Alternate /DeviceCMYK", [0x00]);

        var grayIcc = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width 5 /Height 1 /ColorSpace [/ICCBased {grayProfile} 0 R] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([7, 8, 9, 10, 11]));

        var cmykIcc = builder.AddStream(
            $"<< /Type /XObject /Subtype /Image /Width 1 /Height 2 /ColorSpace [/ICCBased {cmykProfile} 0 R] "
            + "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([1, 2, 3, 4, 5, 6, 7, 8]));

        // A lookup table that holds nothing states no colour for any index, which is a table this library
        // cannot look anything up in rather than an empty palette that happens to be read.
        var emptyLookup = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 7 /Height 1 "
            + "/ColorSpace [/Indexed /DeviceGray 1 ()] /BitsPerComponent 8 /Filter /FlateDecode",
            PdfBuilder.Compress([0, 0, 0, 0, 0, 0, 0]));

        // A sample narrower than a byte does not have to be a single channel: four-bit RGB packs three
        // nibbles per pixel, so the component offset is computed rather than read beside the last one.
        var subByteRgb = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 3 /ColorSpace /DeviceRGB "
            + "/BitsPerComponent 4 /Filter /FlateDecode",
            PdfBuilder.Compress([0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0x01, 0x23, 0x45]));

        // An image that states no extent draws nothing, and the specification forbids one: the samples of
        // such a statement are not pixels whatever the payload happens to hold. Both carry no filter and no
        // bytes, which is the one shape in which a zero extent is the only thing refusing them.
        var noWidth = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 0 /Height 3 /ColorSpace /DeviceGray "
            + "/BitsPerComponent 8",
            []);

        var noHeight = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 3 /Height 0 /ColorSpace /DeviceGray "
            + "/BitsPerComponent 8",
            []);

        // An image that declares no filter at all: the payload is already the samples, so the simplest case
        // of all is not the one to lose.
        var noFilter = builder.AddStream(
            "<< /Type /XObject /Subtype /Image /Width 3 /Height 3 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            [
                1, 2, 3, 4, 5, 6, 7, 8, 9,
                10, 11, 12, 13, 14, 15, 16, 17, 18,
                19, 20, 21, 22, 23, 24, 25, 26, 27,
            ]);

        builder.Set(
            content,
            Content(
                "/CalGrayImg Do /CalRgbImg Do /OverPalette Do /OverFunction Do "
                + "/SingleEntry Do /WideHival Do /OverIndex Do /BareIcc Do "
                + "/GrayIcc Do /CmykIcc Do /EmptyLookup Do /SubByteRgb Do /NoWidth Do /NoHeight Do "
                + "/NoFilter Do"));

        builder.Set(
            page,
            Page(
                pages,
                $"/CalGrayImg {calGray} 0 R /CalRgbImg {calRgb} 0 R /OverPalette {overPalette} 0 R "
                + $"/OverFunction {overFunction} 0 R /SingleEntry {singleEntry} 0 R "
                + $"/WideHival {wideHival} 0 R /OverIndex {overIndex} 0 R /BareIcc {bareIcc} 0 R "
                + $"/GrayIcc {grayIcc} 0 R /CmykIcc {cmykIcc} 0 R /EmptyLookup {emptyLookup} 0 R "
                + $"/SubByteRgb {subByteRgb} 0 R /NoWidth {noWidth} 0 R /NoHeight {noHeight} 0 R "
                + $"/NoFilter {noFilter} 0 R",
                content));

        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
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
    /// One page whose inline images use the two-letter entries a producer writes in practice: a
    /// computed sample count, an indexed colour space stated as an array, an image mask with its
    /// decode array, an ASCIIHex payload whose length the header does not state, a CMYK image, and a
    /// header carrying a comment and a literal string.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] InlineAbbreviations()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        builder.Set(content, InlineVariantsContent());
        builder.Set(page, Page(pages, string.Empty, content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>One page whose only inline image runs to the end of the content stream with no <c>EI</c>.</summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] UnterminatedInlineImage()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var body = new List<byte>(Encoding.Latin1.GetBytes("q 10 0 0 10 0 0 cm BI /W 2 /H 2 /BPC 8 /CS /G ID "));
        body.AddRange([0x01, 0x02]);

        builder.Set(content, PdfBuilder.StreamBody(string.Empty, [.. body]));
        builder.Set(page, Page(pages, string.Empty, content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    private static byte[] InlineVariantsContent()
    {
        using var body = new MemoryStream();

        // Marked content with a property list, and a TJ array holding a nested array: two shapes the
        // scanner has to step over to reach the operators after them.
        WriteText(body, "/Span << /ActualText (label) >> BDC [ (Text) 40 [ 1 2 ] ] TJ EMC\n");

        Inline(
            body,
            "/W 2 /H 2 /BPC 8 /CS /G",
            [0x10, 0x20, 0x30, 0x40]);

        Inline(
            body,
            "/W 2 /H 2 /BPC 8 /CS [/Indexed /DeviceRGB 1] /DL <00FF>",
            [0x00, 0x10, 0x20, 0x30]);

        Inline(
            body,
            "/W 2 /H 2 /BPC 1 /IM true /D [1 0] /I false",
            [0x80]);

        Inline(
            body,
            "/W 2 /H 2 /BPC 8 /CS /RGB /F /AHx",
            Encoding.Latin1.GetBytes("0A0B0C0D0E0F101112131415>"));

        Inline(
            body,
            "/W 1 /H 1 /BPC 8 /CS /CMYK",
            [0x01, 0x02, 0x03, 0x04]);

        Inline(
            body,
            "% abbreviated header\n/W 2 /H 2 /CS /RGB /BPC 8 /NM (inline note)",
            InlineSamples);

        return PdfBuilder.StreamBody(string.Empty, body.ToArray());
    }

    private static void Inline(Stream body, string header, byte[] data)
    {
        WriteText(body, "q 10 0 0 10 0 0 cm BI " + header + " ID ");
        body.Write(data);
        WriteText(body, " EI Q\n");
    }

    private static void WriteText(Stream body, string text) => body.Write(Encoding.Latin1.GetBytes(text));

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

    /// <summary>
    /// A one page document whose form is a chain of nested text fields, each the single child of the one
    /// above it, so a reader that follows the tree to a depth limit has fields above the cut to return.
    /// </summary>
    /// <param name="depth">The number of levels in the chain, the root counting as the first.</param>
    /// <returns>The bytes of the document.</returns>
    public static byte[] DeepForm(int depth = 8)
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();
        var acroForm = builder.Reserve();

        var levels = new int[depth];

        for (var index = 0; index < depth; index++)
        {
            levels[index] = builder.Reserve();
        }

        builder.Set(content, Content(string.Empty));

        for (var index = depth - 1; index >= 0; index--)
        {
            var kids = index + 1 < depth ? $" /Kids [{levels[index + 1]} 0 R]" : string.Empty;
            builder.Set(levels[index], $"<< /T (level{index}) /FT /Tx{kids} >>");
        }

        builder.Set(acroForm, $"<< /Fields [{levels[0]} 0 R] >>");
        builder.Set(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Contents {content} 0 R >>");
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

    /// <summary>A one page document that carries two embedded files, one of which states no relationship.</summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] WithAssociatedFiles()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();
        var metadata = builder.Reserve();
        var tree = builder.Reserve();
        var stated = builder.Reserve();
        var unstated = builder.Reserve();

        builder.Set(content, Content(string.Empty));
        builder.Set(metadata, PdfBuilder.StreamBody(string.Empty, Encoding.UTF8.GetBytes(XmpText)));

        var first = builder.AddStream(
            "<< /Type /EmbeddedFile /Subtype /text#2Fplain",
            Encoding.Latin1.GetBytes("stated"));

        var second = builder.AddStream(
            "<< /Type /EmbeddedFile /Subtype /text#2Fplain",
            Encoding.Latin1.GetBytes("unstated"));

        builder.Set(
            stated,
            $"<< /Type /Filespec /F (stated.txt) /UF (stated.txt) /AFRelationship /Source /EF << /F {first} 0 R >> >>");

        builder.Set(
            unstated,
            $"<< /Type /Filespec /F (unstated.txt) /UF (unstated.txt) /EF << /F {second} 0 R >> >>");

        builder.Set(tree, $"<< /Names [(stated.txt) {stated} 0 R (unstated.txt) {unstated} 0 R] >>");

        builder.Set(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Contents {content} 0 R >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(
            catalog,
            $"<< /Type /Catalog /Pages {pages} 0 R /Metadata {metadata} 0 R /Names << /EmbeddedFiles {tree} 0 R >> >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A document whose embedded file hangs under a child node and is written as a direct dictionary, which
    /// is the shape a producer that inlines its file specifications writes.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] WithNestedAssociatedFiles()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();
        var metadata = builder.Reserve();
        var root = builder.Reserve();
        var child = builder.Reserve();

        builder.Set(content, Content(string.Empty));
        builder.Set(metadata, PdfBuilder.StreamBody(string.Empty, Encoding.UTF8.GetBytes(XmpText)));

        var file = builder.AddStream(
            "<< /Type /EmbeddedFile /Subtype /text#2Fplain",
            Encoding.Latin1.GetBytes("direct"));

        builder.Set(root, $"<< /Kids [{child} 0 R] >>");
        builder.Set(
            child,
            $"<< /Names [(direct.txt) << /Type /Filespec /F (direct.txt) /UF (direct.txt) /EF << /F {file} 0 R >> >>] >>");

        builder.Set(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Contents {content} 0 R >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(
            catalog,
            $"<< /Type /Catalog /Pages {pages} 0 R /Metadata {metadata} 0 R /Names << /EmbeddedFiles {root} 0 R >> >>");

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
    /// image, an LZW image, and an image behind a filter the library does not decode.
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
        var lzw = builder.AddStream(header + "/Filter /LZWDecode", TestLzw.Encode(ChainSamples));
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
    /// One page whose content states its transform outside a <c>q</c>, which is the shape a browser's
    /// print output has: the transform is still in force when the page ends, so every stream that
    /// follows it is drawn with it.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] OpenState()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            ".24 0 0 -.24 0 595.2756 cm BT /F1 12 Tf 1 0 0 -1 300 2000 Tm (page) Tj ET"));

        builder.Set(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 419.52756 595.2756] "
            + $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page whose line is drawn from the characters a markup document reads as structure: an angle
    /// bracket and an ampersand, which an export has to escape rather than write.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextMarkup()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (a < b & c > d) Tj ET"));

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
    /// One page laid out in two columns, drawn right column first, so that the order of the content is not
    /// the order a reader takes the page in.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextTwoColumns()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 330 720 Tm (Right one) Tj "
            + "1 0 0 1 330 706 Tm (Right two) Tj "
            + "1 0 0 1 72 720 Tm (Left one) Tj "
            + "1 0 0 1 72 706 Tm (Left two) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page whose first line is a rule drawn as a run of hyphens, far enough above the prose under it to
    /// be a block of its own: ink the page draws that carries no letter and no digit.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextDecoration()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 72 760 Tm (----) Tj 1 0 0 1 72 700 Tm (Prose) Tj ET"));

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

    /// <summary>
    /// One page that draws its words in the wrong order: the second word of the top line before the first,
    /// and the top line before the line under it is drawn, so that the order of the content and the order of
    /// the page are not the same.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextOutOfOrder()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 1 0 0 1 108 720 Tm (World) Tj "
            + "1 0 0 1 72 720 Tm (Hello) Tj "
            + "1 0 0 1 72 706 Tm (Second) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page whose text is drawn with character spacing, word spacing and horizontal scaling: the
    /// spacing widens the advance of every letter and of the space, and the scaling halves both the width
    /// a glyph states and the advance it makes.
    /// </summary>
    /// <remarks>
    /// Helvetica states no <c>/Widths</c>, so every glyph of this fixture is the default half em: 6pt at
    /// 12pt, and 3pt once <c>Tz 50</c> halves it. The three letters sit at 100, 104 and 110 points from
    /// the origin, which is what the advance arithmetic (<c>Tc</c>, <c>Tw</c>, <c>Tz</c>) must produce.
    /// </remarks>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextSpacing()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 12 Tf 2 Tc 4 Tw 50 Tz 1 0 0 1 100 700 Tm (A B) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page drawn with a Type1 font that states no <c>/Encoding</c> and no <c>/ToUnicode</c> in its
    /// dictionary: what its codes mean is stated only in the clear-text header of the font program it
    /// embeds, which is the shape a pdfTeX subset of a Computer Modern math font has.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    /// <remarks>
    /// The program below is a header only — everything a reader of text needs. Code 65 is <c>A</c>, 66 is
    /// <c>B</c>, and code 200 is a bullet through the program's own table, which no dictionary of the
    /// document states.
    /// </remarks>
    /// <param name="stateDictionaryEncoding">
    /// When <see langword="true"/> the font dictionary states <c>/WinAnsiEncoding</c>, so the two statements
    /// of what code 200 means disagree: the dictionary says <c>È</c> and the program says a bullet.
    /// </param>
    public static byte[] TextType1BuiltInEncoding(bool stateDictionaryEncoding = false)
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var header = Encoding.Latin1.GetBytes(
            "%!PS-AdobeFont-1.0: TestType1 001.001\n"
            + "/FontName /TestType1 def\n"
            + "/Encoding 256 array\n"
            + "0 1 255 {1 index exch /.notdef put} for\n"
            + "dup 65 /A put\n"
            + "dup 66 /B put\n"
            + "dup 200 /bullet put\n"
            + "readonly def\n"
            + "currentfile eexec\n");

        var program = builder.AddStream($"/Length1 {header.Length}", header);
        var descriptor = builder.Add(
            $"<< /Type /FontDescriptor /FontName /TestType1 /Flags 4 /FontBBox [0 0 1000 1000] "
            + $"/ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 "
            + $"/FontFile {program} 0 R >>");
        var font = builder.Add(
            $"<< /Type /Font /Subtype /Type1 /BaseFont /TestType1 /FontDescriptor {descriptor} 0 R"
            + (stateDictionaryEncoding ? " /Encoding /WinAnsiEncoding >>" : " >>"));

        var content = builder.Add(Content("BT /F1 12 Tf 1 0 0 1 72 700 Tm <4142C8> Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }


    /// the second row in a band of its own rather than folding it into the first.
    /// </summary>
    /// <remarks>
    /// 10pt glyphs occupy <c>Y - 2</c> to <c>Y + 8</c>, so a row at 690 has its top exactly on the bottom of
    /// a row at 700. The content is meant to be read with the raw segmentation, which keeps each string a
    /// block: the default segmentation would merge the two rows of a column into one block by their leading,
    /// and then no band rule is on trial.
    /// </remarks>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextTouchingRows()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 10 Tf "
            + "1 0 0 1 300 700 Tm (Top) Tj "
            + "1 0 0 1 72 690 Tm (Bottom) Tj "
            + "ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page laid out as a two-column table of three rows, with the second row drawn right cell first and
    /// the third row exactly touching the second: the row-band order must read each row left to right from
    /// the top, and must not merge the touching row into the one above it.
    /// </summary>
    /// <remarks>
    /// Every glyph is 10pt, so a line occupies <c>Y - 2</c> to <c>Y + 8</c>: the rows at 700, 650 and 640
    /// put the bottom of row two (648) exactly on the top of row three, which is the boundary the band rule
    /// has to decide with a strict comparison rather than a generous one.
    /// </remarks>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextTableBands()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content(
            "BT /F1 10 Tf "
            + "1 0 0 1 72 700 Tm (Alpha) Tj "
            + "1 0 0 1 300 700 Tm (Beta) Tj "
            + "1 0 0 1 300 650 Tm (Charlie) Tj "
            + "1 0 0 1 72 650 Tm (Delta) Tj "
            + "1 0 0 1 300 640 Tm (Foxtrot) Tj "
            + "1 0 0 1 72 640 Tm (Echo) Tj "
            + "ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// One page whose text runs right to left: a horizontal scaling of minus one hundred percent turns the
    /// advance of every glyph round, so the baseline of the run goes towards the left edge.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] TextRightToLeft()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        var font = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = builder.Add(Content("BT /F1 12 Tf -100 Tz 1 0 0 1 200 700 Tm (AB) Tj ET"));

        builder.Set(page, TextPage(pages, $"/F1 {font} 0 R", content));
        builder.Set(pages, PageTree(page));
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }


    /// <summary>Gets the compressed bytes the object graph fixture writes into its stream.</summary>
    public static byte[] GraphStreamData { get; } =
        PdfBuilder.Compress(Encoding.Latin1.GetBytes("graph stream payload"));

    /// <summary>
    /// A one page document whose objects name each other in a cycle, name an object the file does not
    /// hold and carry a stream that declares a chain of filters.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    /// <remarks>
    /// The objects are numbered so a test can name them: 1 the catalog, 2 the page tree, 3 the page,
    /// 4 its content, 5 and 6 a pair that point at each other and 7 the compressed stream. Both the page
    /// and object 5 name object 99, which the file does not hold, so a graph must report the hole once
    /// rather than once per mention.
    /// </remarks>
    public static byte[] ObjectGraph()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();
        var first = builder.Reserve();
        var second = builder.Reserve();

        var stream = builder.AddStream(
            "<< /Type /Metadata /Subtype /XML /Filter [/FlateDecode /ASCIIHexDecode]",
            GraphStreamData);

        builder.Set(content, Content("0 0 m 10 10 l S"));
        builder.Set(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] /Contents {content} 0 R "
            + $"/Resources << >> /Example 99 0 R /Also 99 0 R /Cycle {first} 0 R >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(first, $"<< /Peer {second} 0 R /Missing 99 0 R >>");
        builder.Set(second, $"<< /Peer {first} 0 R >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Metadata {stream} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A one page document whose content states a graphics state change, a matrix, an inline image and an
    /// XObject invocation, so the operations of a page can be read in the order the page states them.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    /// <remarks>
    /// The samples are the ones <see cref="InlineSamples"/> gives, whose last byte is the letter A: a read
    /// that ended an inline image on a delimiter rather than on EI standing on its own would take the
    /// samples for an operator and never see the Q and the Do that follow them.
    /// </remarks>
    public static byte[] ContentOperations()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var prefix = Encoding.Latin1.GetBytes("q 1 0 0 1 10 20 cm BI /W 1 /H 1 /CS /G /BPC 8 ID ");
        var suffix = Encoding.Latin1.GetBytes(" EI Q /Fm1 Do");
        var body = new byte[prefix.Length + InlineSamples.Length + suffix.Length];

        prefix.CopyTo(body, 0);
        InlineSamples.CopyTo(body, prefix.Length);
        suffix.CopyTo(body, prefix.Length + InlineSamples.Length);

        builder.Set(content, PdfBuilder.StreamBody(string.Empty, body));
        builder.Set(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 200 200] "
            + $"/Resources << >> /Contents {content} 0 R >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A one page document whose content nests arrays as deep as asked and then states two operators, so a
    /// read that stops descending can be shown to carry on with the rest of the page.
    /// </summary>
    /// <param name="depth">The number of arrays the content nests, one inside the other.</param>
    /// <returns>The bytes of the document.</returns>
    public static byte[] NestedContent(int depth)
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        builder.Set(content, Content(new string('[', depth) + new string(']', depth) + " S BT ET"));
        builder.Set(page, Page(pages, string.Empty, content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A one page document whose trailer names an encryption this library does not implement, so the page
    /// tree is the only part of it a read can see.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    public static byte[] UnopenedEncryption()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var encrypt = builder.Add("<</Filter/Standard/V 9/R 9/O <00>/U <00>>>");

        builder.Set(content, Content("q Q"));
        builder.Set(page, Page(pages, string.Empty, content));
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog, $"/Encrypt {encrypt} 0 R");
    }

    /// <summary>
    /// A four page document whose fonts state every shape the inspector reports: an embedded subset with a
    /// <c>/ToUnicode</c> map, selected by two pages; a standard font that states no descriptor; a
    /// <c>Type0</c> font that resolves to its <c>CIDFontType2</c> descendant; a form XObject's own font, and
    /// a form nested inside that form; resources a page inherits from the page tree; a page that states no
    /// resources at all; a Type 3 font; a Multiple Master font; a <c>Type0</c> font with no descendant; two
    /// names that only look like a subset tag; and a resource that names an object the file does not
    /// contain.
    /// </summary>
    /// <returns>The bytes of the document.</returns>
    /// <remarks>
    /// Each shape is here because a branch of the walk reads it: the inherited resources are what the
    /// fallback from the page to its ancestors exists for, the nested form is what the depth limit is
    /// measured against, and the two look-alike names are what the subset test has to reject.
    /// </remarks>
    public static byte[] Fonts()
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var root = builder.Reserve();
        var firstBranch = builder.Reserve();
        var secondBranch = builder.Reserve();
        var thirdBranch = builder.Reserve();
        var page1 = builder.Reserve();
        var page2 = builder.Reserve();
        var page3 = builder.Reserve();
        var page4 = builder.Reserve();
        var content = builder.Reserve();
        var toUnicode = builder.Reserve();

        var program = builder.AddStream(string.Empty, [0, 1, 0, 0]);
        var subsetDescriptor = builder.Add(
            $"<< /Type /FontDescriptor /FontName /ABCDEF+Arial /Flags 32 /FontFile2 {program} 0 R >>");
        var subset = builder.Add(
            $"<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+Arial /Encoding /WinAnsiEncoding "
            + $"/FontDescriptor {subsetDescriptor} 0 R /ToUnicode {toUnicode} 0 R >>");
        var standard = builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        var cidDescriptor = builder.Add(
            $"<< /Type /FontDescriptor /FontName /XYZABC+SomeFont /Flags 4 /FontFile2 {program} 0 R >>");
        var descendant = builder.Add(
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /XYZABC+SomeFont "
            + $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            + $"/FontDescriptor {cidDescriptor} 0 R >>");
        var composite = builder.Add(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /XYZABC+SomeFont /Encoding /Identity-H "
            + $"/DescendantFonts [{descendant} 0 R] >>");

        // Two forms, one inside the other: the outer one's font is reachable at depth 1, the inner one's at
        // depth 2, which is what the depth limit is measured against.
        var formFont = builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>");
        var deepFont = builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>");
        var deepForm = builder.AddStream(
            $"/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << /Font << /F12 {deepFont} 0 R >> >>",
            []);
        var form = builder.AddStream(
            $"/Type /XObject /Subtype /Form /BBox [0 0 10 10] "
            + $"/Resources << /Font << /F5 {formFont} 0 R >> /XObject << /Fm2 {deepForm} 0 R >> >>",
            []);

        // The fonts page 3 reaches through the resources its page tree states.
        var type3 = builder.Add(
            "<< /Type /Font /Subtype /Type3 /BaseFont /ZapfType3 /CharProcs << /a 1 0 R >> "
            + "/FontMatrix [0.001 0 0 0.001 0 0] /Encoding << /Differences [97 /a] >> >>");
        var multipleMaster = builder.Add("<< /Type /Font /Subtype /MMType1 /BaseFont /MinionMM >>");
        var loose = builder.Add("<< /Type /Font /Subtype /Type0 /BaseFont /Loose /Encoding /Identity-H >>");
        var emptyType3 = builder.Add(
            "<< /Type /Font /Subtype /Type3 /BaseFont /EmptyType3 /FontMatrix [0.001 0 0 0.001 0 0] >>");

        // A composite font with a CIDFontType0 descendant, an encoding dictionary that names no base
        // encoding, and a subset tag that is exactly the eight characters the tag has.
        var cidType0Descriptor = builder.Add("<< /Type /FontDescriptor /FontName /ABCDEF+X >>");
        var cidType0Descendant = builder.Add(
            $"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /ABCDEF+X "
            + $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            + $"/FontDescriptor {cidType0Descriptor} 0 R >>");
        var custom = builder.Add(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+X "
            + $"/Encoding << /Differences [0 /a] >> /DescendantFonts [{cidType0Descendant} 0 R] >>");

        // One font per place a program can live, and one encoding dictionary that does name a base
        // encoding, so every arm of the two lookups is exercised.
        var type1Descriptor = builder.Add(
            $"<< /Type /FontDescriptor /FontName /FontFileOne /Flags 4 /FontFile {program} 0 R >>");
        var type1Program = builder.Add(
            $"<< /Type /Font /Subtype /Type1 /BaseFont /FontFileOne /FontDescriptor {type1Descriptor} 0 R >>");
        var cffDescriptor = builder.Add(
            $"<< /Type /Font /Subtype /Type1 /FontName /CffOne /Flags 4 /FontFile3 {program} 0 R >>");
        var cffProgram = builder.Add(
            $"<< /Type /Font /Subtype /Type1 /BaseFont /CffOne /FontDescriptor {cffDescriptor} 0 R >>");
        var baseEncoded = builder.Add(
            "<< /Type /Font /Subtype /TrueType /BaseFont /BaseEncoded "
            + "/Encoding << /BaseEncoding /MacRomanEncoding /Differences [65 /a] >> >>");
        // Six capitals without the plus, and a plus without six capitals: neither is a subset tag.
        var tagged = builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /ABCDEFHelvetica >>");
        var lower = builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /abcDEF+Weird >>");

        builder.Set(toUnicode, PdfBuilder.StreamBody(string.Empty, "begincmap endcmap"u8.ToArray()));
        builder.Set(content, Content("BT /F1 12 Tf ET"));
        builder.Set(
            page1,
            TextPage(firstBranch, $"/F1 {subset} 0 R /F2 {standard} 0 R /F4 99 0 R", content, $"/Fm1 {form} 0 R"));
        builder.Set(page2, TextPage(firstBranch, $"/F1 {subset} 0 R /F3 {composite} 0 R", content));
        builder.Set(page3, $"<< /Type /Page /Parent {secondBranch} 0 R /MediaBox [0 0 612 792] /Contents {content} 0 R >>");
        builder.Set(page4, $"<< /Type /Page /Parent {thirdBranch} 0 R /MediaBox [0 0 612 792] >>");

        // The resources sit on one branch of the tree, not on the page and not on the root: the page that
        // inherits them is the one the fallback from a page to its ancestors exists for, and the last
        // branch states no resources at all.
        builder.Set(
            secondBranch,
            $"<< /Type /Pages /Parent {root} 0 R /Kids [{page3} 0 R] /Count 1 /MediaBox [0 0 612 792] "
            + $"/Resources << /Font << /F6 {type3} 0 R /F7 {multipleMaster} 0 R /F8 {loose} 0 R "
            + $"/F9 {tagged} 0 R /F10 {lower} 0 R /F11 {emptyType3} 0 R /F12 {custom} 0 R "
            + $"/F13 {type1Program} 0 R /F14 {cffProgram} 0 R /F15 {baseEncoded} 0 R >> >> >>");
        builder.Set(
            firstBranch,
            $"<< /Type /Pages /Parent {root} 0 R /Kids [{page1} 0 R {page2} 0 R] /Count 2 /MediaBox [0 0 612 792] >>");
        builder.Set(
            thirdBranch,
            $"<< /Type /Pages /Parent {root} 0 R /Kids [{page4} 0 R] /Count 1 /MediaBox [0 0 612 792] >>");
        builder.Set(
            root,
            $"<< /Type /Pages /Kids [{firstBranch} 0 R {secondBranch} 0 R {thirdBranch} 0 R] /Count 4 "
            + "/MediaBox [0 0 612 792] >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {root} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A one page document that paints a chain of nested forms, each with a font of its own, so a test can
    /// see exactly which level of the chain a read reached.
    /// </summary>
    /// <param name="depth">The number of forms in the chain.</param>
    /// <returns>The bytes of the document.</returns>
    /// <remarks>
    /// The fonts are named <c>Level1</c> to <c>Level{n}</c> from the outside in, and the page sits directly
    /// under the root of the page tree so the fixture stays parseable at a small limit: the limit also
    /// bounds how deeply a syntax structure may nest, and a page dictionary is three levels deep before any
    /// form is reached.
    /// </remarks>
    public static byte[] FontsInForms(int depth = 5)
    {
        var builder = new PdfBuilder();

        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();
        var content = builder.Reserve();

        var inner = 0;

        for (var level = depth; level >= 1; level--)
        {
            var font = builder.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /Level{level} >>");
            var xobject = inner == 0 ? string.Empty : $" /XObject << /Next{level} {inner} 0 R >>";

            inner = builder.AddStream(
                $"/Type /XObject /Subtype /Form /BBox [0 0 10 10] "
                + $"/Resources << /Font << /F{level} {font} 0 R >>{xobject} >>",
                []);
        }

        builder.Set(content, Content("/Fm1 Do"));
        builder.Set(page, TextPage(pages, string.Empty, content, $"/Fm1 {inner} 0 R"));
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
