using System.Buffers;
using System.Globalization;
using System.Text;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Infrastructure.Annotations;

/// <summary>
/// Writes the operator stream of an appearance: the numbers, the operators and the text an appearance
/// consists of, with the escaping and the number formatting PDF states.
/// </summary>
/// <remarks>
/// The bytes are accumulated in one buffer and never re-read, so an appearance is built in a single
/// pass and compressed once, by whoever wraps it in a stream. Numbers are formatted with
/// <see cref="Pdf.PdfNumberFormat"/>, which is the same formatter the page content writer uses, so an
/// appearance and a page cannot disagree about how a coordinate is spelled.
/// </remarks>
internal sealed class AppearanceContent
{
    private readonly ArrayBufferWriter<byte> bytes = new();

    /// <summary>Writes a number and the space that separates it from the next token.</summary>
    /// <param name="value">The number.</param>
    /// <returns>This writer.</returns>
    public AppearanceContent Number(double value)
    {
        Span<byte> scratch = stackalloc byte[32];

        if (!Pdf.PdfNumberFormat.TryFormat(value, scratch, out var written))
        {
            throw new InvalidOperationException($"The number {value} could not be formatted for an appearance stream.");
        }

        Write(scratch[..written]);
        WriteByte((byte)' ');
        return this;
    }

    /// <summary>Writes operators and ends the line.</summary>
    /// <param name="operators">The operators, as ASCII.</param>
    /// <returns>This writer.</returns>
    public AppearanceContent Line(string operators)
    {
        Write(Encoding.ASCII.GetBytes(operators));
        WriteByte((byte)'\n');
        return this;
    }

    /// <summary>Writes a resource name, which is a slash and the name.</summary>
    /// <param name="name">The name, without the slash.</param>
    /// <returns>This writer.</returns>
    public AppearanceContent Name(string name)
    {
        WriteByte((byte)'/');
        Write(Encoding.ASCII.GetBytes(name));
        WriteByte((byte)' ');
        return this;
    }

    /// <summary>Writes a colour as the three components of a device RGB fill.</summary>
    /// <param name="red">The red component, from 0 to 255.</param>
    /// <param name="green">The green component, from 0 to 255.</param>
    /// <param name="blue">The blue component, from 0 to 255.</param>
    /// <returns>This writer.</returns>
    public AppearanceContent Rgb(byte red, byte green, byte blue) => Number(red / 255.0)
        .Number(green / 255.0)
        .Number(blue / 255.0);

    /// <summary>Writes text as a PDF literal string, escaped and encoded in <c>WinAnsiEncoding</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>This writer.</returns>
    /// <exception cref="ArgumentException">The text holds a character the encoding has no code for.</exception>
    public AppearanceContent Literal(string text)
    {
        WriteByte((byte)'(');

        foreach (var character in text)
        {
            if (!WinAnsiEncoding.TryGetCode(character, out var code))
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The appearance draws '{character}' (U+{(int)character:X4}), which WinAnsiEncoding has no code for, so the text would not read back as it was written."),
                    nameof(text));
            }

            switch (code)
            {
                case (byte)'(' or (byte)')' or (byte)'\\':
                    WriteByte((byte)'\\');
                    WriteByte(code);
                    break;

                case < 32 or > 126:
                    // An octal escape, always three digits, so the code cannot run into the digits
                    // of the character that follows it.
                    WriteByte((byte)'\\');
                    WriteByte((byte)('0' + ((code >> 6) & 7)));
                    WriteByte((byte)('0' + ((code >> 3) & 7)));
                    WriteByte((byte)('0' + (code & 7)));
                    break;

                default:
                    WriteByte(code);
                    break;
            }
        }

        WriteByte((byte)')');
        return this;
    }

    /// <summary>Gets the bytes written so far.</summary>
    /// <returns>The operators.</returns>
    public byte[] ToBytes() => bytes.WrittenSpan.ToArray();

    private void Write(ReadOnlySpan<byte> value)
    {
        value.CopyTo(bytes.GetSpan(value.Length));
        bytes.Advance(value.Length);
    }

    private void WriteByte(byte value)
    {
        bytes.GetSpan(1)[0] = value;
        bytes.Advance(1);
    }
}
