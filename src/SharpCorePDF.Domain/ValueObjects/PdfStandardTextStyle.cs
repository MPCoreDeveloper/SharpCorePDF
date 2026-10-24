using System.Globalization;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The typeface, size and colour of text the engine draws itself: the appearance of a free-text
/// annotation, and the default appearance of a form field.
/// </summary>
/// <remarks>
/// <para>
/// Only the twelve standard fonts are available here, and they are never embedded. An appearance is
/// part of the page and must stay small and portable: a widget that carried a font program would make
/// every filled field carry a copy of it, and a reader that opens the document without the font would
/// still draw Helvetica exactly as another reader does.
/// </para>
/// <para>
/// Because the standard fonts are written with <c>WinAnsiEncoding</c>, the text must be expressible in
/// it — Latin-1 plus the typographic characters of Windows-1252. Text outside that set is refused when
/// the appearance is written rather than replaced with question marks, so a field never shows a
/// different value from the one the caller stored.
/// </para>
/// </remarks>
public sealed record PdfStandardTextStyle
{
    /// <summary>Gets the style a caller gets when none is stated: Helvetica, twelve points, black.</summary>
    public static PdfStandardTextStyle Default { get; } = new(StandardFont.Helvetica, 12.0);

    /// <summary>Initializes a new instance of the <see cref="PdfStandardTextStyle"/> record.</summary>
    /// <param name="font">The standard font to draw with.</param>
    /// <param name="size">The size in points; greater than zero.</param>
    /// <param name="color">The fill colour, or <see langword="null"/> for black.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a finite number greater than zero.</exception>
    /// <exception cref="ArgumentException"><paramref name="font"/> is not a standard font the engine knows.</exception>
    public PdfStandardTextStyle(StandardFont font, double size, PdfColor? color = null)
    {
        if (!Enum.IsDefined(font))
        {
            throw new ArgumentException($"'{(int)font}' is not a standard font the engine writes.", nameof(font));
        }

        if (!double.IsFinite(size) || size <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A font size must be finite and greater than zero.");
        }

        Font = font;
        Size = size;
        Color = color ?? PdfColor.Black;
    }

    /// <summary>Gets the standard font the text is drawn with.</summary>
    public StandardFont Font { get; }

    /// <summary>Gets the size in points.</summary>
    public double Size { get; }

    /// <summary>Gets the fill colour.</summary>
    public PdfColor Color { get; }

    /// <summary>Validates the style.</summary>
    /// <returns>Every problem found, or an empty list when the style is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Font))
        {
            errors.Add($"font '{(int)Font}' is not one the engine writes.");
        }

        if (!double.IsFinite(Size) || Size <= 0.0)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the font size {Size} is not a positive number of points."));
        }

        return errors;
    }

    /// <summary>Formats the style for diagnostics.</summary>
    /// <returns>The font and the size.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Font} {Size}pt {Color}");
}
