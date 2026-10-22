using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// The typeface, size, colour and text-state parameters used to draw text.
/// </summary>
/// <remarks>
/// The style is immutable and can be reused across pages. Character spacing, word spacing and
/// horizontal scaling are written into the content stream as PDF text state, not applied to the
/// string by the caller.
/// </remarks>
public sealed record PdfTextStyle
{
    /// <summary>Initializes a new instance of the <see cref="PdfTextStyle"/> record.</summary>
    /// <param name="typeface">The face to draw with.</param>
    /// <param name="fontSize">The size in points.</param>
    /// <param name="color">The fill colour; null means black.</param>
    /// <param name="leading">The distance between baselines, in points.</param>
    /// <param name="characterSpacing">Extra space after every glyph, in points.</param>
    /// <param name="wordSpacing">Extra space after every space character, in points.</param>
    /// <param name="horizontalScale">The horizontal scale as a percentage.</param>
    /// <param name="rise">The baseline offset, in points.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeface"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The font size or horizontal scale is not greater than zero, or a value is not finite.</exception>
    public PdfTextStyle(
        PdfTypeface typeface,
        double fontSize,
        PdfColor? color = null,
        double leading = 0.0,
        double characterSpacing = 0.0,
        double wordSpacing = 0.0,
        double horizontalScale = 100.0,
        double rise = 0.0)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        if (!double.IsFinite(fontSize) || fontSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "A font size must be finite and greater than zero.");
        }

        if (!double.IsFinite(horizontalScale) || horizontalScale <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalScale),
                horizontalScale,
                "A horizontal scale must be finite and greater than zero.");
        }

        if (!double.IsFinite(leading))
        {
            throw new ArgumentOutOfRangeException(nameof(leading), leading, "A leading must be finite.");
        }

        if (!double.IsFinite(characterSpacing))
        {
            throw new ArgumentOutOfRangeException(nameof(characterSpacing), characterSpacing, "A character spacing must be finite.");
        }

        if (!double.IsFinite(wordSpacing))
        {
            throw new ArgumentOutOfRangeException(nameof(wordSpacing), wordSpacing, "A word spacing must be finite.");
        }

        if (!double.IsFinite(rise))
        {
            throw new ArgumentOutOfRangeException(nameof(rise), rise, "A rise must be finite.");
        }

        Typeface = typeface;
        FontSize = fontSize;
        Color = color ?? PdfColor.Black;
        Leading = leading;
        CharacterSpacing = characterSpacing;
        WordSpacing = wordSpacing;
        HorizontalScale = horizontalScale;
        Rise = rise;
    }

    /// <summary>Gets the face to draw with.</summary>
    public PdfTypeface Typeface { get; }

    /// <summary>Gets the size in points.</summary>
    public double FontSize { get; }

    /// <summary>Gets the fill colour.</summary>
    public PdfColor Color { get; }

    /// <summary>Gets the distance between baselines, in points.</summary>
    public double Leading { get; }

    /// <summary>Gets extra space after every glyph, in points.</summary>
    public double CharacterSpacing { get; }

    /// <summary>Gets extra space after every space character, in points.</summary>
    public double WordSpacing { get; }

    /// <summary>Gets the horizontal scale as a percentage.</summary>
    public double HorizontalScale { get; }

    /// <summary>Gets the baseline offset, in points.</summary>
    public double Rise { get; }
}
