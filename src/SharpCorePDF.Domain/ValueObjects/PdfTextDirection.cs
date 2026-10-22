namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The direction in which a text layout is read.</summary>
/// <remarks>
/// Complex-script shaping is out of contract. A right-to-left layout is only accepted when the font
/// supplies the required glyphs and is refused with an explanation otherwise.
/// </remarks>
public enum PdfTextDirection
{
    /// <summary>Text runs from left to right.</summary>
    LeftToRight = 0,

    /// <summary>Text runs from right to left.</summary>
    RightToLeft = 1,
}
