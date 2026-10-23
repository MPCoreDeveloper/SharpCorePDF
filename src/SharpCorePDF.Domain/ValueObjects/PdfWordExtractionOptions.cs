namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The rules a word read applies: which order the letters are taken in and how far apart two letters may
/// sit and still be one word.
/// </summary>
/// <remarks>
/// <para>
/// The two distances are stated in ems — fractions of the size of the letters being grouped — so one rule
/// serves a 6pt footnote and a 30pt title, and a document that draws a line at half size needs no second
/// set of thresholds. A gap wider than <see cref="MaxHorizontalGap"/> is the space between two words; a
/// baseline that drifts further than <see cref="MaxVerticalGap"/> belongs to another line.
/// </para>
/// <para>
/// The options are immutable and validate themselves when they are created: a negative distance or a
/// strategy this release does not implement is refused at the call site, before a document is opened, so a
/// read never pays for a document it cannot group.
/// </para>
/// </remarks>
public sealed record PdfWordExtractionOptions
{
    private PdfWordExtractionStrategy strategy = PdfWordExtractionStrategy.NearestNeighbour;
    private double maxHorizontalGap = 0.25d;
    private double maxVerticalGap = 0.5d;

    /// <summary>Initializes a new instance of the <see cref="PdfWordExtractionOptions"/> record.</summary>
    public PdfWordExtractionOptions()
    {
    }

    /// <summary>
    /// Gets the order the letters are taken in: the page's own order by default, or the order of the
    /// content stream.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a strategy this release implements.</exception>
    public PdfWordExtractionStrategy Strategy
    {
        get => strategy;
        init => strategy = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A word extraction strategy must be one this library implements.");
    }

    /// <summary>
    /// Gets how wide the gap between two letters may be and still be a gap inside a word, as a fraction of
    /// the em. A space is about a quarter of an em in most fonts, which is why that is the default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double MaxHorizontalGap
    {
        get => maxHorizontalGap;
        init => maxHorizontalGap = Validate(value, nameof(MaxHorizontalGap));
    }

    /// <summary>
    /// Gets how far two baselines may sit apart and still be one line, as a fraction of the em. Half an em
    /// tolerates a superscript and a line drawn with a small rise without merging two lines.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double MaxVerticalGap
    {
        get => maxVerticalGap;
        init => maxVerticalGap = Validate(value, nameof(MaxVerticalGap));
    }

    /// <summary>
    /// Gets a value indicating whether the whitespace letters a page draws are kept. When they are, a space
    /// belongs to the word it follows and appears in that word's text, so concatenating the words of a page
    /// reproduces the text the page draws from its first inked letter.
    /// </summary>
    public bool IncludeWhitespace { get; init; }

    /// <summary>Validates a distance a caller supplied.</summary>
    /// <param name="value">The distance, in ems.</param>
    /// <param name="name">The name of the property, for the exception.</param>
    /// <returns>The distance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    private static double Validate(double value, string name) =>
        double.IsFinite(value) && value >= 0d
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "A gap must be a finite, non-negative fraction of an em.");
}
