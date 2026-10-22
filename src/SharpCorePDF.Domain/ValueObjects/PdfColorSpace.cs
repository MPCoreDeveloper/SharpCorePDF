namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The colour space a paint or image uses.</summary>
/// <remarks>
/// The authoring surface writes device colour spaces only. ICC-based output is not part of 1.1.
/// </remarks>
public enum PdfColorSpace
{
    /// <summary>A single luminance component.</summary>
    DeviceGray = 0,

    /// <summary>Red, green and blue components.</summary>
    DeviceRGB = 1,

    /// <summary>Cyan, magenta, yellow and black components.</summary>
    DeviceCMYK = 2,
}
