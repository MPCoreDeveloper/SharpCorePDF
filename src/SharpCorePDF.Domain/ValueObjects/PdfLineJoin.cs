namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The shape painted where two stroked segments meet.</summary>
public enum PdfLineJoin
{
    /// <summary>The outer edges meet in a point.</summary>
    Miter = 0,

    /// <summary>The corner is rounded.</summary>
    Round = 1,

    /// <summary>The corner is cut off.</summary>
    Bevel = 2,
}
