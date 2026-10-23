namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The visible half of a signature: the page and the rectangle the signature widget occupies, and the
/// text drawn inside it.
/// </summary>
/// <remarks>
/// <para>
/// A signature without an appearance is written as an invisible widget of zero size, which is what a
/// server-side signature usually wants. When an appearance is supplied it becomes a form XObject that
/// travels in the signed revision, so the drawing is covered by the signature rather than added later
/// (NFR: what was signed is what is drawn).
/// </para>
/// <para>
/// All measurements are in PDF points. <see cref="PageNumber"/> is one-based, like every page number in
/// this library.
/// </para>
/// </remarks>
public sealed record PdfSignatureAppearance
{
    /// <summary>Gets the one-based number of the page the widget is placed on.</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>Gets the rectangle the widget occupies, in page space.</summary>
    public required PdfRectangle Rectangle { get; init; }

    /// <summary>
    /// Gets the text drawn in the rectangle, or <see langword="null"/> to draw the signer's common name
    /// and the signing date from the certificate the signature was made with.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>Gets the size of the text in points.</summary>
    public double FontSize { get; init; } = 9d;

    /// <summary>Gets the width of the border drawn around the rectangle, in points; zero draws none.</summary>
    public double BorderWidth { get; init; } = 1d;

    /// <summary>Validates these options.</summary>
    /// <returns>Every problem found, or an empty list when the appearance is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (PageNumber < 1)
        {
            problems.Add($"PageNumber must be at least 1, but is {PageNumber}.");
        }

        if (Rectangle.IsEmpty)
        {
            problems.Add(
                $"Rectangle must have a positive width and height, but is {Rectangle.Width} by {Rectangle.Height}.");
        }

        if (FontSize is <= 0d or > 96d)
        {
            problems.Add($"FontSize must be greater than 0 and at most 96, but is {FontSize}.");
        }

        if (BorderWidth is < 0d or > 16d)
        {
            problems.Add($"BorderWidth must be between 0 and 16, but is {BorderWidth}.");
        }

        return problems;
    }
}
