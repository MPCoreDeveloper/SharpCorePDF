using System.Text;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// A PDF string. Both the literal <c>(text)</c> form and the hexadecimal <c>&lt;74657874&gt;</c>
/// form keep their bytes verbatim: PDF strings are byte sequences, not characters, and re-encoding
/// them would corrupt text that uses PDFDocEncoding or UTF-16.
/// </summary>
internal sealed class PdfString : PdfObject
{
    /// <summary>Initializes a new instance of the <see cref="PdfString"/> class.</summary>
    /// <param name="bytes">The raw bytes of the string.</param>
    /// <param name="isHexadecimal">Whether the string was written in hexadecimal form.</param>
    public PdfString(byte[] bytes, bool isHexadecimal)
    {
        Bytes = bytes;
        IsHexadecimal = isHexadecimal;
    }

    /// <summary>Gets the raw bytes of the string.</summary>
    public byte[] Bytes { get; }

    /// <summary>Gets a value indicating whether the string was written in hexadecimal form.</summary>
    public bool IsHexadecimal { get; }

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.String;

    /// <summary>Creates a string from text, using PDFDocEncoding compatible bytes when possible.</summary>
    /// <param name="text">The text to store.</param>
    /// <returns>The created string.</returns>
    public static PdfString FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var latin1 = Encoding.Latin1.GetBytes(text);
        var isRoundTrippable = string.Equals(Encoding.Latin1.GetString(latin1), text, StringComparison.Ordinal);

        // Text that does not survive a single byte round trip (anything outside Latin-1) is stored as
        // UTF-16BE with a byte order mark, the convention PDF 1.7 defines for text strings.
        return isRoundTrippable
            ? new PdfString(latin1, false)
            : new PdfString([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)], true);
    }

    /// <summary>Decodes the string as text, understanding the UTF-16 byte order marks.</summary>
    /// <returns>The decoded text.</returns>
    public string ToText()
    {
        if (Bytes.Length >= 2 && Bytes[0] == 0xFE && Bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(Bytes, 2, Bytes.Length - 2);
        }

        return Encoding.Latin1.GetString(Bytes);
    }

    /// <inheritdoc />
    public override string ToString() => ToText();
}

/// <summary>
/// A PDF name. The leading slash is not part of the value; the value is stored decoded, so
/// <c>/A#20B</c> is held as <c>A B</c> and re-encoded on write.
/// </summary>
internal sealed class PdfName : PdfObject
{
    /// <summary>Initializes a new instance of the <see cref="PdfName"/> class.</summary>
    /// <param name="value">The decoded name, without the leading slash.</param>
    public PdfName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Gets the decoded name.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Name;

    /// <summary>Creates a name if it is not null or empty.</summary>
    /// <param name="value">The decoded name.</param>
    /// <returns>The created name, or <see langword="null"/>.</returns>
    public static PdfName? From(string? value) => string.IsNullOrEmpty(value) ? null : new PdfName(value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
