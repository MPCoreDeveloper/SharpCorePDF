namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>How the lines of a text layout are aligned within its maximum width.</summary>
public enum PdfTextAlignment
{
    /// <summary>Lines start at the left edge.</summary>
    Left = 0,

    /// <summary>Lines are centred within the maximum width.</summary>
    Center = 1,

    /// <summary>Lines end at the right edge.</summary>
    Right = 2,

    /// <summary>Lines except the last are stretched to both edges by adjusting word spacing.</summary>
    Justify = 3,
}
