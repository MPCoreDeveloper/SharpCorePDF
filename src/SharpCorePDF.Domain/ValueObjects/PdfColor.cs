namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// A colour a page can be painted with, as red, green and blue components of eight bits each.
/// </summary>
/// <remarks>
/// The engine writes colours as <c>/DeviceRGB</c>, which every reader understands and which needs no
/// colour profile or external resource. A colour carries no alpha: transparency of an authored page
/// comes from the image it draws, not from its text.
/// </remarks>
public readonly record struct PdfColor
{
    /// <summary>Initializes a new instance of the <see cref="PdfColor"/> struct.</summary>
    /// <param name="red">The red component.</param>
    /// <param name="green">The green component.</param>
    /// <param name="blue">The blue component.</param>
    public PdfColor(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>Gets black, the colour text is drawn in when a caller states none.</summary>
    public static PdfColor Black { get; } = new(0, 0, 0);

    /// <summary>Gets white, the colour of an unpainted page.</summary>
    public static PdfColor White { get; } = new(255, 255, 255);

    /// <summary>Gets the red component.</summary>
    public byte Red { get; }

    /// <summary>Gets the green component.</summary>
    public byte Green { get; }

    /// <summary>Gets the blue component.</summary>
    public byte Blue { get; }

    /// <summary>Creates a colour from its three components.</summary>
    /// <param name="red">The red component.</param>
    /// <param name="green">The green component.</param>
    /// <param name="blue">The blue component.</param>
    /// <returns>The created colour.</returns>
    public static PdfColor FromRgb(byte red, byte green, byte blue) => new(red, green, blue);

    /// <summary>Creates a colour from a hexadecimal value.</summary>
    /// <param name="hexadecimal">
    /// The value, with or without a leading '#', as six hexadecimal digits: <c>#1F3A5F</c> or
    /// <c>1F3A5F</c>.
    /// </param>
    /// <returns>The created colour.</returns>
    /// <exception cref="ArgumentException"><paramref name="hexadecimal"/> is not six hexadecimal digits.</exception>
    public static PdfColor FromHex(string hexadecimal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hexadecimal);

        var digits = hexadecimal.AsSpan();

        if (digits[0] == '#')
        {
            digits = digits[1..];
        }

        if (digits.Length != 6)
        {
            throw new ArgumentException(
                $"'{hexadecimal}' is not a colour: six hexadecimal digits are expected, for example #1F3A5F.",
                nameof(hexadecimal));
        }

        if (!byte.TryParse(
                digits[..2],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var red)
            || !byte.TryParse(
                digits[2..4],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var green)
            || !byte.TryParse(
                digits[4..],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var blue))
        {
            throw new ArgumentException(
                $"'{hexadecimal}' is not a colour: six hexadecimal digits are expected, for example #1F3A5F.",
                nameof(hexadecimal));
        }

        return new PdfColor(red, green, blue);
    }

    /// <summary>Formats the colour as <c>#RRGGBB</c>.</summary>
    /// <returns>The formatted colour.</returns>
    public override string ToString() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"#{Red:X2}{Green:X2}{Blue:X2}");
}
