using System.Globalization;
using System.Text;
using System.Xml;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading.Exporters;

/// <summary>
/// The conventions the four exporters share: how a document is written, how a coordinate is stated and
/// what happens to text a format cannot carry.
/// </summary>
/// <remarks>
/// <para>
/// A document is written with UTF-8 and a declaration, no byte order mark, two spaces of indentation and a
/// newline for every line of the file, so the same read produces the same bytes on every platform and on
/// every run (FR-019). The caller's stream is left open: an export writes its document and hands the stream
/// back exactly where it stopped.
/// </para>
/// <para>
/// The read states a box in page space with the origin at the bottom left; every format states it with the
/// origin at the top left. The translation is a single subtraction against the height of the page, which is
/// why the height travels with the page into every exporter.
/// </para>
/// </remarks>
internal static class LayoutExportXml
{
    /// <summary>Creates the writer an export writes its document with.</summary>
    /// <param name="destination">The stream the document is written to.</param>
    /// <returns>The writer; disposing it flushes the last bytes and leaves the stream open.</returns>
    public static XmlWriter Create(Stream destination) => XmlWriter.Create(destination, new XmlWriterSettings
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        OmitXmlDeclaration = false,
        CloseOutput = false,
        ConformanceLevel = ConformanceLevel.Document,
    });

    /// <summary>States a box in the top-left coordinates of an export.</summary>
    /// <param name="box">The box, in page space.</param>
    /// <param name="pageHeight">The height of the page the box sits on.</param>
    /// <returns>The four edges, rounded to whole units, with the origin at the top left.</returns>
    /// <remarks>
    /// The edges are rounded once and the width and height derived from them, so a box never reports a
    /// width its own edges contradict.
    /// </remarks>
    public static (int Left, int Top, int Right, int Bottom) Box(PdfRectangle box, double pageHeight) =>
        (Unit(box.Left), Unit(pageHeight - box.Top), Unit(box.Right), Unit(pageHeight - box.Bottom));

    /// <summary>States a box as the four whole-number attributes a format gives it.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="box">The box, in page space.</param>
    /// <param name="pageHeight">The height of the page the box sits on.</param>
    /// <param name="horizontal">The name of the attribute holding the left edge, for example <c>HPOS</c>.</param>
    /// <param name="vertical">The name of the attribute holding the top edge, for example <c>VPOS</c>.</param>
    /// <param name="width">The name of the attribute holding the width.</param>
    /// <param name="height">The name of the attribute holding the height.</param>
    public static void WriteBox(
        XmlWriter writer,
        PdfRectangle box,
        double pageHeight,
        string horizontal,
        string vertical,
        string width,
        string height)
    {
        var (left, top, right, bottom) = Box(box, pageHeight);

        writer.WriteAttributeString(horizontal, Count(left));
        writer.WriteAttributeString(vertical, Count(top));
        writer.WriteAttributeString(width, Count(right - left));
        writer.WriteAttributeString(height, Count(bottom - top));
    }

    /// <summary>States a length or a position whole, the way ALTO and hOCR want it.</summary>
    /// <param name="value">The value, in points.</param>
    /// <returns>The value rounded to the nearest whole unit.</returns>
    public static int Unit(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>States a number as a decimal a consumer parses without a culture in the way.</summary>
    /// <param name="value">The value, in points.</param>
    /// <returns>The value with at most three decimals and a full stop for the decimal separator.</returns>
    public static string Number(double value) => value == 0d
        ? "0"
        : value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>States a count or a page number for a document written in the invariant culture.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value as digits.</returns>
    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Makes a string safe to write into an XML document: a character XML 1.0 cannot carry — a control
    /// character the producer drew, a lone surrogate half — becomes the replacement character rather than
    /// aborting the export.
    /// </summary>
    /// <param name="text">The text a read produced.</param>
    /// <returns>The same text, with every character XML cannot carry replaced.</returns>
    /// <remarks>
    /// This is the only place an export changes what a read found, and it changes the encoding rather than
    /// the text: the exporter still writes every word it was given, and the replacement character is what a
    /// consumer sees for a byte the document should not have contained.
    /// </remarks>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder? builder = null;
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];

            if (char.IsHighSurrogate(character)
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]))
            {
                builder?.Append(character).Append(text[index + 1]);
                index += 2;

                continue;
            }

            if (IsCarriable(character))
            {
                builder?.Append(character);
            }
            else
            {
                builder ??= new StringBuilder(text, 0, index, text.Length);
                builder.Append('\uFFFD');
            }

            index++;
        }

        return builder?.ToString() ?? text;
    }

    /// <summary>Decides whether XML 1.0 can carry a character on its own.</summary>
    /// <param name="character">The character of a UTF-16 string.</param>
    /// <returns><see langword="true"/> when the character may appear in a document.</returns>
    private static bool IsCarriable(char character) =>
        character is '\t' or '\n' or '\r'
        || (character >= '\u0020' && character <= '\uD7FF')
        || (character >= '\uE000' && character <= '\uFFFD');
}
