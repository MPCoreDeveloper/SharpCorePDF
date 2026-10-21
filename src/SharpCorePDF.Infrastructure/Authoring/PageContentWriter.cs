using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Turns the drawing operations of one page into a content stream.
/// </summary>
/// <remarks>
/// The stream is written as PDF operators, in page space, with the top-left origin the public canvas
/// states converted here: a text baseline at <c>y</c> from the top becomes <c>pageHeight - y</c>, and an
/// image whose top edge is at <c>y</c> becomes <c>pageHeight - y - height</c>. Text is written in the
/// font's own codes — bytes of <c>WinAnsiEncoding</c> for a standard font, two byte glyph ids for an
/// embedded one — and an embedded font's string is written in hexadecimal form, which is half the size
/// of escaping every high byte as an octal sequence.
/// </remarks>
internal static class PageContentWriter
{
    /// <summary>Writes the content stream of a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="resources">The resources the page selects, which receive every face and image it uses.</param>
    /// <param name="hash">The digest of the document, which receives the bytes.</param>
    /// <returns>The content stream.</returns>
    public static byte[] Write(PdfPageCanvas page, PageResources resources, IncrementalHash hash)
    {
        using var content = new MemoryStream();

        foreach (var operation in page.Operations)
        {
            switch (operation)
            {
                case TextOperation text:
                    WriteText(content, page, text, resources);
                    break;

                case ImageOperation image:
                    WriteImage(content, page, image, resources);
                    break;
            }
        }

        var bytes = content.ToArray();
        hash.AppendData(bytes);
        return bytes;
    }

    /// <summary>Writes one text operation.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="page">The page, which states the height the baseline is measured from.</param>
    /// <param name="text">The operation.</param>
    /// <param name="resources">The resources of the page.</param>
    private static void WriteText(
        Stream destination,
        PdfPageCanvas page,
        TextOperation text,
        PageResources resources)
    {
        var font = resources.Font(text.Typeface);
        var codes = font.Encode(text.Text, out var hexadecimal);

        Write(destination, "BT\n");
        Write(destination, "/");
        Write(destination, font.Name);
        Write(destination, " ");
        WriteNumber(destination, text.Size);
        Write(destination, " Tf\n");
        WriteNumber(destination, text.Color.Red / 255d);
        Write(destination, " ");
        WriteNumber(destination, text.Color.Green / 255d);
        Write(destination, " ");
        WriteNumber(destination, text.Color.Blue / 255d);
        Write(destination, " rg\n");
        Write(destination, "1 0 0 1 ");
        WriteNumber(destination, text.X);
        Write(destination, " ");
        WriteNumber(destination, page.Height - text.Y);
        Write(destination, " Tm\n");
        WriteTextString(destination, codes, hexadecimal);
        Write(destination, " Tj\nET\n");
    }

    /// <summary>Writes one image operation.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="page">The page, which states the height the top edge is measured from.</param>
    /// <param name="image">The operation.</param>
    /// <param name="resources">The resources of the page.</param>
    private static void WriteImage(
        Stream destination,
        PdfPageCanvas page,
        ImageOperation image,
        PageResources resources)
    {
        var resource = resources.Image(image.Image);

        Write(destination, "q\n");
        WriteNumber(destination, image.Width);
        Write(destination, " 0 0 ");
        WriteNumber(destination, image.Height);
        Write(destination, " ");
        WriteNumber(destination, image.X);
        Write(destination, " ");
        WriteNumber(destination, page.Height - image.Y - image.Height);
        Write(destination, " cm\n/");
        Write(destination, resource.Name);
        Write(destination, " Do\nQ\n");
    }

    /// <summary>Writes a string operand in the form its font needs.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="codes">The codes of the string.</param>
    /// <param name="hexadecimal">Whether the codes are written as a hexadecimal string.</param>
    private static void WriteTextString(Stream destination, byte[] codes, bool hexadecimal)
    {
        if (hexadecimal)
        {
            destination.WriteByte((byte)'<');

            foreach (var code in codes)
            {
                destination.WriteByte((byte)HexDigit(code >> 4));
                destination.WriteByte((byte)HexDigit(code & 0x0F));
            }

            destination.WriteByte((byte)'>');
            return;
        }

        destination.WriteByte((byte)'(');

        foreach (var code in codes)
        {
            switch (code)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    destination.WriteByte((byte)'\\');
                    destination.WriteByte(code);
                    break;

                case < 32:
                case > 126:
                    destination.WriteByte((byte)'\\');
                    destination.WriteByte((byte)('0' + ((code >> 6) & 0x07)));
                    destination.WriteByte((byte)('0' + ((code >> 3) & 0x07)));
                    destination.WriteByte((byte)('0' + (code & 0x07)));
                    break;

                default:
                    destination.WriteByte(code);
                    break;
            }
        }

        destination.WriteByte((byte)')');
    }

    /// <summary>Writes a number in the form PDF expects.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="value">The number.</param>
    private static void WriteNumber(Stream destination, double value)
    {
        Span<byte> scratch = stackalloc byte[32];

        if (!PdfNumberFormat.TryFormat(value, scratch, out var written))
        {
            throw new InvalidOperationException($"The number {value} could not be formatted for a content stream.");
        }

        destination.Write(scratch[..written]);
    }

    /// <summary>Writes an ASCII keyword.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="text">The keyword.</param>
    private static void Write(Stream destination, string text) =>
        destination.Write(Encoding.ASCII.GetBytes(text));

    /// <summary>Gets the hexadecimal digit of a nibble.</summary>
    /// <param name="value">The nibble.</param>
    /// <returns>The digit, as an ASCII code.</returns>
    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}
