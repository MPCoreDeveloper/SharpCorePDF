namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The colour space of an image source.</summary>
public enum PdfImageColorSpace
{
    /// <summary>A single luminance component.</summary>
    Gray = 0,

    /// <summary>Red, green and blue components.</summary>
    Rgb = 1,

    /// <summary>Cyan, magenta, yellow and black components.</summary>
    Cmyk = 2,

    /// <summary>An index into a palette.</summary>
    Indexed = 3,

    /// <summary>An ICC-based colour space.</summary>
    IccBased = 4,
}
