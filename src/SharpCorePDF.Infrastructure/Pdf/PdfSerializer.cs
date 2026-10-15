namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Serialises the object model back to PDF syntax.
/// </summary>
/// <remarks>
/// Strings keep their bytes instead of being re-encoded to text, names are re-escaped only where the
/// specification requires it, and stream bodies are written verbatim; a merged document therefore
/// carries the original resource bytes unchanged.
/// </remarks>
internal static partial class PdfSerializer
{
    /// <summary>Writes an object.</summary>
    /// <param name="value">The object to write.</param>
    /// <param name="output">The buffer to write to.</param>
    public static void Write(PdfObject? value, PdfOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);

        switch (value)
        {
            case null:
            case PdfNull:
                output.Write("null"u8);
                break;

            case PdfBoolean boolean:
                output.Write(boolean.Value ? "true"u8 : "false"u8);
                break;

            case PdfInteger integer:
                output.WriteInteger(integer.Value);
                break;

            case PdfReal real:
                output.WriteReal(real.Value);
                break;

            case PdfName name:
                WriteName(name, output);
                break;

            case PdfString text:
                WriteString(text, output);
                break;

            case PdfArray array:
                WriteArray(array, output);
                break;

            case PdfDictionary dictionary:
                WriteDictionary(dictionary, output);
                break;

            case PdfStream stream:
                WriteStream(stream, output);
                break;

            case PdfReference reference:
                output.WriteReference(reference.ObjectNumber, reference.Generation);
                break;

            default:
                output.Write("null"u8);
                break;
        }
    }

    /// <summary>Writes a name.</summary>
    /// <param name="name">The name to write.</param>
    /// <param name="output">The buffer to write to.</param>
    private static void WriteName(PdfName name, PdfOutputBuffer output) => WriteNameCharacters(name.Value, output);

    /// <summary>Writes the characters of a name behind its leading slash.</summary>
    /// <param name="value">The name without its leading slash.</param>
    /// <param name="output">The buffer to write to.</param>
    private static void WriteNameCharacters(string value, PdfOutputBuffer output)
    {
        output.WriteByte((byte)'/');

        foreach (var character in value)
        {
            // Names outside Latin-1 cannot be represented in PDF syntax; the replacement keeps the
            // file valid instead of failing in the middle of a write.
            var escaped = character > 0xFF ? (byte)0x3F : (byte)character;

            if (escaped <= (byte)' ' || escaped > (byte)'~' || PdfLexer.IsDelimiter(escaped) || escaped == (byte)'#')
            {
                WriteEscapedByte(escaped, output);
                continue;
            }

            output.WriteByte(escaped);
        }
    }

    private static void WriteEscapedByte(byte value, PdfOutputBuffer output)
    {
        output.WriteByte((byte)'#');
        WriteByteAsHex(value, output);
    }

    /// <summary>Writes one byte as two hexadecimal digits.</summary>
    /// <param name="value">The byte to write.</param>
    /// <param name="output">The buffer to write to.</param>
    private static void WriteByteAsHex(byte value, PdfOutputBuffer output)
    {
        const string digits = "0123456789ABCDEF";
        output.WriteByte((byte)digits[value >> 4]);
        output.WriteByte((byte)digits[value & 0x0F]);
    }

    private static void WriteString(PdfString text, PdfOutputBuffer output)
    {
        if (text.IsHexadecimal)
        {
            // A hexadecimal string holds its bytes as pairs of hex digits; the '#' escapes that names
            // use would turn it into a different string entirely.
            output.WriteByte((byte)'<');

            foreach (var value in text.Bytes)
            {
                WriteByteAsHex(value, output);
            }

            output.WriteByte((byte)'>');
            return;
        }

        output.WriteByte((byte)'(');

        foreach (var value in text.Bytes)
        {
            switch (value)
            {
                case (byte)'(':
                    output.Write("\\("u8);
                    break;

                case (byte)')':
                    output.Write("\\)"u8);
                    break;

                case (byte)'\\':
                    output.Write("\\\\"u8);
                    break;

                case (byte)'\n':
                    output.Write("\\n"u8);
                    break;

                case (byte)'\r':
                    output.Write("\\r"u8);
                    break;

                case (byte)'\t':
                    output.Write("\\t"u8);
                    break;

                case 8:
                    output.Write("\\b"u8);
                    break;

                case 12:
                    output.Write("\\f"u8);
                    break;

                default:
                    if (value < 32 || value > 126)
                    {
                        WriteOctalByte(value, output);
                        break;
                    }

                    output.WriteByte(value);
                    break;
            }
        }

        output.WriteByte((byte)')');
    }

    private static void WriteOctalByte(byte value, PdfOutputBuffer output)
    {
        output.WriteByte((byte)'\\');
        output.WriteByte((byte)('0' + ((value >> 6) & 0x07)));
        output.WriteByte((byte)('0' + ((value >> 3) & 0x07)));
        output.WriteByte((byte)('0' + (value & 0x07)));
    }

    private static void WriteArray(PdfArray array, PdfOutputBuffer output)
    {
        output.WriteByte((byte)'[');

        for (var index = 0; index < array.Items.Count; index++)
        {
            if (index > 0)
            {
                output.WriteByte((byte)' ');
            }

            Write(array.Items[index], output);
        }

        output.WriteByte((byte)']');
    }
}
