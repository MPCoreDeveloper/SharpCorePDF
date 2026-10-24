namespace SharpCorePDF.Tests;

/// <summary>
/// Builds JPEG files in memory, so a test can state the frame type, the component count, the density
/// and the EXIF orientation it exercises without shipping a binary fixture for each shape.
/// </summary>
/// <remarks>
/// No entropy-coded scan data is written. A JPEG is embedded with <c>DCTDecode</c> and handed back
/// exactly as it was read, so nothing in these tests asks a decoder to walk the scan: the markers that
/// describe the image are the whole of what the reader looks at.
/// </remarks>
internal static class TestJpeg
{
    private const int FrameMarker = 0xC0;

    /// <summary>Builds a baseline JPEG with three or four components.</summary>
    /// <param name="width">The width the frame states.</param>
    /// <param name="height">The height the frame states.</param>
    /// <param name="components">The number of colour components.</param>
    /// <param name="adobeTransform">The Adobe APP14 transform to state, or minus one for no APP14 segment.</param>
    /// <param name="dpi">The JFIF density to state in dots per inch, when one is wanted.</param>
    /// <returns>The bytes of the JPEG.</returns>
    public static byte[] Baseline(
        int width,
        int height,
        int components = 3,
        int adobeTransform = -1,
        int? dpi = null)
    {
        return Build(FrameMarker, width, height, components, 8, adobeTransform, dpi, orientation: 1);
    }

    /// <summary>Builds a JPEG whose frame marker states a coding the embedder refuses.</summary>
    /// <param name="marker">The frame marker, such as <c>0xC9</c> for arithmetic coding.</param>
    /// <param name="width">The width the frame states.</param>
    /// <param name="height">The height the frame states.</param>
    /// <param name="components">The number of colour components.</param>
    /// <returns>The bytes of the JPEG.</returns>
    public static byte[] WithFrame(int marker, int width, int height, int components = 3)
    {
        return Build(marker, width, height, components, 8, adobeTransform: -1, dpi: null, orientation: 1);
    }

    /// <summary>Builds a JPEG whose frame states a sample precision other than eight bits.</summary>
    /// <param name="precision">The bits per sample.</param>
    /// <param name="width">The width the frame states.</param>
    /// <param name="height">The height the frame states.</param>
    /// <returns>The bytes of the JPEG.</returns>
    public static byte[] WithPrecision(int precision, int width, int height)
    {
        return Build(FrameMarker, width, height, components: 3, precision, adobeTransform: -1, dpi: null, orientation: 1);
    }

    /// <summary>Builds a JPEG whose EXIF block states an orientation tag.</summary>
    /// <param name="orientation">The orientation tag, one for "top-left".</param>
    /// <param name="width">The width the frame states.</param>
    /// <param name="height">The height the frame states.</param>
    /// <returns>The bytes of the JPEG.</returns>
    public static byte[] WithOrientation(int orientation, int width, int height)
    {
        return Build(FrameMarker, width, height, components: 3, 8, adobeTransform: -1, dpi: null, orientation);
    }

    /// <summary>Assembles the markers of the file.</summary>
    /// <param name="marker">The frame marker.</param>
    /// <param name="width">The width the frame states.</param>
    /// <param name="height">The height the frame states.</param>
    /// <param name="components">The number of colour components.</param>
    /// <param name="precision">The bits per sample.</param>
    /// <param name="adobeTransform">The Adobe APP14 transform, or minus one for none.</param>
    /// <param name="dpi">The JFIF density in dots per inch, or <see langword="null"/> for none.</param>
    /// <param name="orientation">The EXIF orientation tag, one for none.</param>
    /// <returns>The bytes of the JPEG.</returns>
    private static byte[] Build(
        int marker,
        int width,
        int height,
        int components,
        int precision,
        int adobeTransform,
        int? dpi,
        int orientation)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        if (dpi is { } density)
        {
            AppendJfif(bytes, density);
        }

        if (orientation != 1)
        {
            AppendExif(bytes, orientation);
        }

        if (adobeTransform >= 0)
        {
            AppendAdobe(bytes, adobeTransform);
        }

        AppendFrame(bytes, marker, width, height, components, precision);

        bytes.Add(0xFF);
        bytes.Add(0xD9);

        return [.. bytes];
    }

    /// <summary>Appends the JFIF APP0 segment that states the density.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="dpi">The density in dots per inch.</param>
    private static void AppendJfif(List<byte> bytes, int dpi)
    {
        var body = new List<byte>();
        body.AddRange("JFIF\0"u8.ToArray());
        body.AddRange([1, 1, 1]);
        body.AddRange([(byte)(dpi >> 8), (byte)dpi]);
        body.AddRange([(byte)(dpi >> 8), (byte)dpi]);
        body.AddRange([0, 0]);

        AppendSegment(bytes, 0xE0, body);
    }

    /// <summary>Appends the Adobe APP14 segment that states the colour transform.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="transform">The transform the segment states.</param>
    private static void AppendAdobe(List<byte> bytes, int transform)
    {
        var body = new List<byte>();
        body.AddRange("Adobe"u8.ToArray());
        body.AddRange([0x00, 0x64, 0x00, 0x00, 0x00, 0x00]);
        body.Add((byte)transform);

        AppendSegment(bytes, 0xEE, body);
    }

    /// <summary>Appends an EXIF APP1 segment holding one orientation tag.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="orientation">The orientation tag.</param>
    private static void AppendExif(List<byte> bytes, int orientation)
    {
        var body = new List<byte>();
        body.AddRange("Exif\0\0"u8.ToArray());

        // A little-endian TIFF block: the signature, the magic 42, the offset of the directory, then one
        // entry — tag 0x0112, type 3 (SHORT), one value — and a terminator saying no directory follows.
        body.AddRange("II"u8.ToArray());
        body.AddRange([42, 0, 8, 0, 0, 0]);
        body.AddRange([1, 0]);
        body.AddRange([0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0]);
        body.AddRange([0, 0, 0, 0]);

        AppendSegment(bytes, 0xE1, body);
    }

    /// <summary>Appends a frame header, which states the size, the precision and the components.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="marker">The frame marker.</param>
    /// <param name="width">The width to state.</param>
    /// <param name="height">The height to state.</param>
    /// <param name="components">The number of components to state.</param>
    /// <param name="precision">The bits per sample to state.</param>
    private static void AppendFrame(List<byte> bytes, int marker, int width, int height, int components, int precision)
    {
        var body = new List<byte>
        {
            (byte)precision,
            (byte)(height >> 8),
            (byte)height,
            (byte)(width >> 8),
            (byte)width,
            (byte)components,
        };

        for (var component = 0; component < components; component++)
        {
            // One sample of the component per pixel, quantised by the first table: a header a reader
            // walks is all this file has to be.
            body.AddRange([(byte)(component + 1), 0x11, 0x00]);
        }

        AppendSegment(bytes, (byte)marker, body);
    }

    /// <summary>Appends a marker segment with its two-byte length.</summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <param name="marker">The marker byte.</param>
    /// <param name="body">The bytes of the segment after the length.</param>
    private static void AppendSegment(List<byte> bytes, byte marker, List<byte> body)
    {
        var length = body.Count + 2;

        bytes.Add(0xFF);
        bytes.Add(marker);
        bytes.Add((byte)(length >> 8));
        bytes.Add((byte)length);
        bytes.AddRange(body);
    }
}
