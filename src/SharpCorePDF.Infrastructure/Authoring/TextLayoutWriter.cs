using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>Writes laid-out text runs into a content stream as PDF text objects.</summary>
/// <remarks>
/// Every run becomes one text object with its own font, size, colour and text state. The position is
/// stated in the canvas's top-left coordinates and converted to PDF user space here, so the canvas never
/// has to know the height of the page.
/// </remarks>
internal static class TextLayoutWriter
{
    /// <summary>Writes one text operation.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="page">The page, which states the height the y coordinate is measured from.</param>
    /// <param name="text">The operation.</param>
    /// <param name="resources">The resources of the page.</param>
    public static void Write(
        ContentStreamBuilder destination,
        PdfPageCanvas page,
        TextOperation text,
        PageResources resources)
    {
        var font = resources.Font(text.Typeface);
        var codes = font.Encode(text.Text, out var hexadecimal);

        destination.Write("BT\n");
        destination.Write("/");
        destination.Write(font.Name);
        destination.Write(" ");
        destination.WriteNumber(text.Size);
        destination.Write(" Tf\n");
        destination.WriteNumber(text.CharacterSpacing);
        destination.Write(" Tc\n");
        destination.WriteNumber(text.WordSpacing);
        destination.Write(" Tw\n");
        destination.WriteNumber(text.HorizontalScale);
        destination.Write(" Tz\n");
        destination.WriteNumber(text.Rise);
        destination.Write(" Ts\n");
        destination.WriteNumber(text.Color.Red / 255d);
        destination.Write(" ");
        destination.WriteNumber(text.Color.Green / 255d);
        destination.Write(" ");
        destination.WriteNumber(text.Color.Blue / 255d);
        destination.Write(" rg\n");
        destination.Write("1 0 0 1 ");
        destination.WriteNumber(text.X);
        destination.Write(" ");
        destination.WriteNumber(page.Height - text.Y);
        destination.Write(" Tm\n");
        WriteString(destination, codes, hexadecimal);
        destination.Write(" Tj\nET\n");
    }

    /// <summary>Writes a string operand in the form its font needs.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="codes">The codes of the string.</param>
    /// <param name="hexadecimal">Whether the codes are written as a hexadecimal string.</param>
    private static void WriteString(ContentStreamBuilder destination, byte[] codes, bool hexadecimal)
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

    /// <summary>Gets the hexadecimal digit of a nibble.</summary>
    /// <param name="value">The nibble.</param>
    /// <returns>The digit, as an ASCII code.</returns>
    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}