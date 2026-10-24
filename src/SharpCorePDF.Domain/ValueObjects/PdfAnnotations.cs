using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Creates the annotations the editor writes, so a caller never has to know which geometry a kind
/// stores or which defaults it needs to be valid.
/// </summary>
/// <remarks>
/// <para>
/// Every factory produces an annotation that passes <see cref="PdfAnnotation.Validate"/>: a markup
/// annotation gets the union of its quads as its rectangle, a line gets the bounding box of its ends and
/// an ink annotation gets the bounding box of all its points. A caller that needs a rectangle wider than
/// the geometry — so a reader has room to draw a border — sets <see cref="PdfAnnotation.Rectangle"/>
/// itself on the returned value.
/// </para>
/// <para>
/// The annotations are created with <see cref="PdfAnnotationFlags.Print"/>, because an annotation that
/// vanishes when the page is printed is almost never what a caller meant.
/// </para>
/// </remarks>
public static class PdfAnnotations
{
    /// <summary>Creates a link to a URL.</summary>
    /// <param name="rectangle">The clickable area, in page space.</param>
    /// <param name="uri">The absolute URL the link opens.</param>
    /// <returns>The link annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is blank.</exception>
    public static PdfAnnotation Link(PdfRectangle rectangle, string uri)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        return new PdfAnnotation
        {
            Kind = PdfAnnotationKind.Link,
            Rectangle = rectangle,
            Destination = new PdfDestination { Uri = uri },
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Creates a link to a place in this document, or to another file.</summary>
    /// <param name="rectangle">The clickable area, in page space.</param>
    /// <param name="destination">The destination; it must name exactly one of a page, a URL or a file.</param>
    /// <returns>The link annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    public static PdfAnnotation Link(PdfRectangle rectangle, PdfDestination destination)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentNullException.ThrowIfNull(destination);

        return new PdfAnnotation
        {
            Kind = PdfAnnotationKind.Link,
            Rectangle = rectangle,
            Destination = destination,
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Creates a sticky note: an icon on the page whose text the reader shows.</summary>
    /// <param name="rectangle">Where the icon is drawn, in page space.</param>
    /// <param name="contents">The text of the note.</param>
    /// <returns>The note annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    public static PdfAnnotation Text(PdfRectangle rectangle, string contents)
    {
        ArgumentNullException.ThrowIfNull(rectangle);

        return new PdfAnnotation
        {
            Kind = PdfAnnotationKind.Text,
            Rectangle = rectangle,
            Contents = contents,
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Creates text drawn on the page itself, in the rectangle the caller states.</summary>
    /// <param name="rectangle">The area the text is drawn in and clipped to, in page space.</param>
    /// <param name="contents">The text to draw.</param>
    /// <param name="style">The font, size and colour to draw it with.</param>
    /// <returns>The free-text annotation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static PdfAnnotation FreeText(PdfRectangle rectangle, string contents, PdfStandardTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentNullException.ThrowIfNull(style);

        return new PdfAnnotation
        {
            Kind = PdfAnnotationKind.FreeText,
            Rectangle = rectangle,
            Contents = contents,
            TextStyle = style,
            Border = PdfBorder.Default with { Width = 0.0 },
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Creates a stamp, such as <c>Approved</c> or <c>Draft</c>, drawn as a marked rectangle.</summary>
    /// <param name="rectangle">Where the stamp is drawn, in page space.</param>
    /// <param name="iconName">The name of the stamp icon.</param>
    /// <returns>The stamp annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="iconName"/> is blank.</exception>
    public static PdfAnnotation Stamp(PdfRectangle rectangle, string iconName)
    {
        ArgumentNullException.ThrowIfNull(rectangle);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconName);

        return new PdfAnnotation
        {
            Kind = PdfAnnotationKind.Stamp,
            Rectangle = rectangle,
            IconName = iconName,
            Color = StampColor,
            Border = PdfBorder.Default with { Width = 2.0 },
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Creates a mark that colours one or more quads of text.</summary>
    /// <param name="quads">The areas of text the mark covers, in page space.</param>
    /// <param name="color">The colour the quads are filled with.</param>
    /// <returns>The highlight annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="quads"/> is empty.</exception>
    public static PdfAnnotation Highlight(IReadOnlyList<PdfRectangle> quads, PdfColor color) =>
        Markup(PdfAnnotationKind.Highlight, quads, color);

    /// <summary>Creates a rule under one or more quads of text.</summary>
    /// <param name="quads">The areas of text the rule follows, in page space.</param>
    /// <param name="color">The colour of the rule.</param>
    /// <returns>The underline annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="quads"/> is empty.</exception>
    public static PdfAnnotation Underline(IReadOnlyList<PdfRectangle> quads, PdfColor color) =>
        Markup(PdfAnnotationKind.Underline, quads, color);

    /// <summary>Creates a rule through one or more quads of text.</summary>
    /// <param name="quads">The areas of text the rule crosses, in page space.</param>
    /// <param name="color">The colour of the rule.</param>
    /// <returns>The strike-out annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="quads"/> is empty.</exception>
    public static PdfAnnotation StrikeOut(IReadOnlyList<PdfRectangle> quads, PdfColor color) =>
        Markup(PdfAnnotationKind.StrikeOut, quads, color);

    /// <summary>Creates a rectangle outline.</summary>
    /// <param name="rectangle">The rectangle to draw, in page space.</param>
    /// <param name="borderColor">The colour of the outline.</param>
    /// <returns>The square annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    public static PdfAnnotation Square(PdfRectangle rectangle, PdfColor borderColor) =>
        Shape(PdfAnnotationKind.Square, rectangle, borderColor);

    /// <summary>Creates an ellipse outline inscribed in its rectangle.</summary>
    /// <param name="rectangle">The rectangle the ellipse is inscribed in, in page space.</param>
    /// <param name="borderColor">The colour of the outline.</param>
    /// <returns>The circle annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rectangle"/> is <see langword="null"/>.</exception>
    public static PdfAnnotation Circle(PdfRectangle rectangle, PdfColor borderColor) =>
        Shape(PdfAnnotationKind.Circle, rectangle, borderColor);

    /// <summary>Creates one straight line between two points.</summary>
    /// <param name="from">The point the line starts at, in page space.</param>
    /// <param name="to">The point the line ends at, in page space.</param>
    /// <param name="borderColor">The colour of the line.</param>
    /// <returns>The line annotation.</returns>
    /// <exception cref="ArgumentException">Both points are the same.</exception>
    public static PdfAnnotation Line(PdfPoint from, PdfPoint to, PdfColor borderColor)
    {
        if (Math.Abs(from.X - to.X) <= PointTolerance && Math.Abs(from.Y - to.Y) <= PointTolerance)
        {
            throw new ArgumentException("A line annotation needs two different points.", nameof(to));
        }

        return new PdfAnnotation
        {
            Kind = PdfAnnotationKind.Line,
            Rectangle = PdfRectangle.FromCorners(from.X, from.Y, to.X, to.Y),
            Color = borderColor,
            Border = PdfBorder.Default with { Width = 2.0 },
            LineFrom = from,
            LineTo = to,
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Creates a freehand ink annotation from one or more strokes.</summary>
    /// <param name="strokes">The strokes, each a list of at least two points in page space.</param>
    /// <param name="color">The colour the strokes are drawn in.</param>
    /// <returns>The ink annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="strokes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="strokes"/> is empty, or a stroke has fewer than two points.</exception>
    public static PdfAnnotation Ink(IReadOnlyList<IReadOnlyList<PdfPoint>> strokes, PdfColor color)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        if (strokes.Count == 0)
        {
            throw new ArgumentException("An ink annotation needs at least one stroke.", nameof(strokes));
        }

        if (strokes.Any(stroke => stroke is null || stroke.Count < 2))
        {
            throw new ArgumentException("Every stroke of an ink annotation needs at least two points.", nameof(strokes));
        }

        return new PdfAnnotation
        {
            Kind = PdfAnnotationKind.Ink,
            Rectangle = PointBounds(strokes),
            Color = color,
            Border = PdfBorder.Default with { Width = 2.0 },
            Strokes = strokes,
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Gets the colour a stamp is written in when the caller states none.</summary>
    private static PdfColor StampColor { get; } = new(0xC0, 0x2B, 0x1F);

    /// <summary>
    /// The distance, in points, below which two coordinates are the same point.
    /// </summary>
    private const double PointTolerance = 1e-4;

    /// <summary>Creates a markup annotation over the quads it is given.</summary>
    /// <param name="kind">The kind of markup.</param>
    /// <param name="quads">The quads it covers.</param>
    /// <param name="color">The colour it is drawn in.</param>
    /// <returns>The annotation.</returns>
    private static PdfAnnotation Markup(
        PdfAnnotationKind kind,
        IReadOnlyList<PdfRectangle> quads,
        PdfColor color)
    {
        ArgumentNullException.ThrowIfNull(quads);

        if (quads.Count == 0)
        {
            throw new ArgumentException("A markup annotation needs at least one quad.", nameof(quads));
        }

        return new PdfAnnotation
        {
            Kind = kind,
            Rectangle = QuadBounds(quads),
            Color = color,
            Quads = quads,
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Creates a shape annotation drawn as an outline.</summary>
    /// <param name="kind">The kind of shape.</param>
    /// <param name="rectangle">The rectangle it is inscribed in.</param>
    /// <param name="borderColor">The colour of its outline.</param>
    /// <returns>The annotation.</returns>
    private static PdfAnnotation Shape(PdfAnnotationKind kind, PdfRectangle rectangle, PdfColor borderColor)
    {
        ArgumentNullException.ThrowIfNull(rectangle);

        return new PdfAnnotation
        {
            Kind = kind,
            Rectangle = rectangle,
            Color = borderColor,
            Border = PdfBorder.Default with { Width = 2.0 },
            Flags = PdfAnnotationFlags.Print,
        };
    }

    /// <summary>Gets the smallest rectangle that covers every quad.</summary>
    /// <param name="quads">The quads.</param>
    /// <returns>The bounding rectangle.</returns>
    private static PdfRectangle QuadBounds(IReadOnlyList<PdfRectangle> quads) => PdfRectangle.FromCorners(
        quads.Min(quad => quad.Left),
        quads.Min(quad => quad.Bottom),
        quads.Max(quad => quad.Right),
        quads.Max(quad => quad.Top));

    /// <summary>Gets the smallest rectangle that covers every point of every stroke.</summary>
    /// <param name="strokes">The strokes.</param>
    /// <returns>The bounding rectangle.</returns>
    private static PdfRectangle PointBounds(IReadOnlyList<IReadOnlyList<PdfPoint>> strokes) => PdfRectangle.FromCorners(
        strokes.SelectMany(stroke => stroke).Min(point => point.X),
        strokes.SelectMany(stroke => stroke).Min(point => point.Y),
        strokes.SelectMany(stroke => stroke).Max(point => point.X),
        strokes.SelectMany(stroke => stroke).Max(point => point.Y));
}
