namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The rule that decides which parts of a path are inside when it is filled or clipped.</summary>
public enum PdfFillRule
{
    /// <summary>A point is inside when a ray crosses the path an odd number of times, counting direction.</summary>
    NonZero = 0,

    /// <summary>A point is inside when a ray crosses the path an odd number of times.</summary>
    EvenOdd = 1,
}
