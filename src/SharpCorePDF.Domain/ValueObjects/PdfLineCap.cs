namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The shape painted at the end of an open stroked subpath.</summary>
public enum PdfLineCap
{
    /// <summary>The stroke stops at the end point.</summary>
    Butt = 0,

    /// <summary>A semicircle extends beyond the end point.</summary>
    Round = 1,

    /// <summary>A square extends beyond the end point.</summary>
    Square = 2,
}
