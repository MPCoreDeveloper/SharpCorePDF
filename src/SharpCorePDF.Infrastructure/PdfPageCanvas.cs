using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The drawing surface of one page an authoring builder creates.
/// </summary>
/// <remarks>
/// <para>
/// The coordinate system has its origin in the top left corner of the page, with x growing to the right
/// and y growing downwards — the coordinates a layout is usually written in, not the bottom-left origin
/// PDF stores. <see cref="DrawText"/> places the baseline of the text at <c>y</c>;
/// <c>DrawImage</c> places the top-left corner of the image at <c>(x, y)</c>.
/// </para>
/// <para>
/// Every call is independent: the same page carries as many separate strings and images as the caller
/// draws, each at its own position, size and colour. There is no word wrap, table or flow layout: a line
/// break is a second call at a different <c>y</c>.
/// </para>
/// <para>
/// An instance is created by <see cref="PdfDocumentBuilder"/> and is only handed to the drawing callback
/// of one page. It is not thread safe.
/// </para>
/// </remarks>
public sealed class PdfPageCanvas
{
    private readonly List<IPageOperation> operations = [];

    internal PdfPageCanvas(PdfPageSize size)
    {
        if (size.WidthPoints <= 0d || size.HeightPoints <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "A page needs a finite width and height greater than zero.");
        }

        Size = size;
    }

    /// <summary>Gets the size of the page.</summary>
    public PdfPageSize Size { get; }

    /// <summary>Gets the width of the page in points.</summary>
    public double Width => Size.WidthPoints;

    /// <summary>Gets the height of the page in points.</summary>
    public double Height => Size.HeightPoints;

    /// <summary>Gets the drawing operations, in the order they were added.</summary>
    internal IReadOnlyList<IPageOperation> Operations => operations;

    /// <summary>Draws one line of text.</summary>
    /// <param name="text">The text to draw; it must not contain a line break.</param>
    /// <param name="x">The x coordinate of the start of the baseline, in points from the left edge.</param>
    /// <param name="y">The y coordinate of the baseline, in points from the top edge.</param>
    /// <param name="typeface">The face to set the text in.</param>
    /// <param name="fontSize">The size of the font in points.</param>
    /// <param name="color">The fill colour; black when omitted.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="typeface"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate or the font size is not a finite number, or the size is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains a line break.</exception>
    public PdfPageCanvas DrawText(
        string text,
        double x,
        double y,
        PdfTypeface typeface,
        double fontSize,
        PdfColor? color = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typeface);
        ValidatePosition(x, y, nameof(x), nameof(y));

        if (!double.IsFinite(fontSize) || fontSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "The font size must be a finite number greater than zero.");
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            throw new ArgumentException(
                "A text operation draws one line. Draw every line with its own call, at its own y coordinate.",
                nameof(text));
        }

        if (text.Length > 0)
        {
            operations.Add(new TextOperation(text, x, y, typeface, fontSize, color ?? PdfColor.Black));
        }

        return this;
    }

    /// <summary>Draws a PNG image at an explicit size.</summary>
    /// <param name="png">The bytes of a PNG file.</param>
    /// <param name="x">The x coordinate of the top-left corner, in points from the left edge.</param>
    /// <param name="y">The y coordinate of the top-left corner, in points from the top edge.</param>
    /// <param name="width">The width the image occupies on the page, in points.</param>
    /// <param name="height">The height the image occupies on the page, in points.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <exception cref="ArgumentException"><paramref name="png"/> is empty, or is not a PNG file.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate, the width or the height is not a finite number, or a size is not positive.</exception>
    /// <exception cref="NotSupportedException">The PNG is interlaced, sixteen bit, or of a colour type the engine does not embed.</exception>
    public PdfPageCanvas DrawImage(ReadOnlyMemory<byte> png, double x, double y, double width, double height)
    {
        ValidatePosition(x, y, nameof(x), nameof(y));
        ValidateSize(width, height);

        return AddImage(PngImage.Parse(png), x, y, width, height);
    }

    /// <summary>Draws a PNG image at its natural size.</summary>
    /// <param name="png">The bytes of a PNG file.</param>
    /// <param name="x">The x coordinate of the top-left corner, in points from the left edge.</param>
    /// <param name="y">The y coordinate of the top-left corner, in points from the top edge.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    /// <remarks>
    /// The natural size comes from the <c>pHYs</c> chunk of the PNG, which states how many pixels the
    /// image has per metre. A PNG without that chunk, or with a unit it does not define, is drawn at one
    /// point per pixel.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="png"/> is empty, or is not a PNG file.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a finite number.</exception>
    /// <exception cref="NotSupportedException">The PNG is interlaced, sixteen bit, or of a colour type the engine does not embed.</exception>
    public PdfPageCanvas DrawImage(ReadOnlyMemory<byte> png, double x, double y)
    {
        ValidatePosition(x, y, nameof(x), nameof(y));

        // The image is parsed once: the natural size is a property of the parsed file, and parsing it a
        // second time would copy its IDAT data again.
        var image = PngImage.Parse(png);

        return AddImage(
            image,
            x,
            y,
            image.NaturalWidthPoints ?? image.Width,
            image.NaturalHeightPoints ?? image.Height);
    }

    /// <summary>Validates the size an image is drawn at.</summary>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not a finite number greater than zero.</exception>
    private static void ValidateSize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The image width must be a finite number greater than zero.");
        }

        if (!double.IsFinite(height) || height <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The image height must be a finite number greater than zero.");
        }
    }

    /// <summary>Records an image operation.</summary>
    /// <param name="image">The parsed image.</param>
    /// <param name="x">The x coordinate of the top-left corner.</param>
    /// <param name="y">The y coordinate of the top-left corner.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <returns>This canvas, so calls can be chained.</returns>
    private PdfPageCanvas AddImage(PngImage image, double x, double y, double width, double height)
    {
        operations.Add(new ImageOperation(image, x, y, width, height));
        return this;
    }

    /// <summary>Checks that a position can be written to the page.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="xName">The parameter name of the x coordinate.</param>
    /// <param name="yName">The parameter name of the y coordinate.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a finite number.</exception>
    private static void ValidatePosition(double x, double y, string xName, string yName)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(xName, x, "The x coordinate must be a finite number.");
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(yName, y, "The y coordinate must be a finite number.");
        }
    }
}

/// <summary>One drawing operation of a page.</summary>
internal interface IPageOperation;

/// <summary>One line of text at a position.</summary>
/// <param name="Text">The text.</param>
/// <param name="X">The x coordinate of the baseline.</param>
/// <param name="Y">The y coordinate of the baseline.</param>
/// <param name="Typeface">The face the text is set in.</param>
/// <param name="Size">The font size in points.</param>
/// <param name="Color">The fill colour.</param>
internal sealed record TextOperation(
    string Text,
    double X,
    double Y,
    PdfTypeface Typeface,
    double Size,
    PdfColor Color) : IPageOperation;

/// <summary>One image at a position and size.</summary>
/// <param name="Image">The parsed image.</param>
/// <param name="X">The x coordinate of the top-left corner.</param>
/// <param name="Y">The y coordinate of the top-left corner.</param>
/// <param name="Width">The width in points.</param>
/// <param name="Height">The height in points.</param>
internal sealed record ImageOperation(
    PngImage Image,
    double X,
    double Y,
    double Width,
    double Height) : IPageOperation;
