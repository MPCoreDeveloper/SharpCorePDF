namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The encoded format of an image source.</summary>
public enum PdfImageFormat
{
    /// <summary>The format has not been identified.</summary>
    Unknown = 0,

    /// <summary>A JPEG image.</summary>
    Jpeg = 1,

    /// <summary>A PNG image.</summary>
    Png = 2,

    /// <summary>A BMP image.</summary>
    Bmp = 3,

    /// <summary>A GIF image; only the first frame is used.</summary>
    Gif = 4,
}
