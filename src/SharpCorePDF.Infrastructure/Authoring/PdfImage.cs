using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// An image opened once and drawn as often as a document needs it.
/// </summary>
/// <remarks>
/// <para>
/// A PNG, JPEG, BMP or GIF file is read when it is opened, not when it is drawn, so the bytes are
/// parsed once and the prepared samples are shared by every page that draws the image. Two
/// <see cref="PdfImage"/> instances opened from the same bytes prepare the same content, and the
/// document carries one image object for them: <see cref="Key"/> hashes everything that changes what
/// a reader shows.
/// </para>
/// <para>
/// The format is decided by the signature of the bytes, never by a file name, which is why
/// <see cref="Open"/> takes no extension. <see cref="FromPng"/>, <see cref="FromJpeg"/>,
/// <see cref="FromBmp"/> and <see cref="FromGif"/> are the same read with the format stated: a file
/// whose signature disagrees with the method called is refused rather than guessed at.
/// </para>
/// </remarks>
public sealed class PdfImage
{
    private PdfImage(IEmbeddedImage content, PdfImageFormat format)
    {
        Content = content;
        Format = format;
    }

    /// <summary>Gets the format the signature of the bytes states.</summary>
    public PdfImageFormat Format { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int WidthPixels => Content.Width;

    /// <summary>Gets the height in pixels.</summary>
    public int HeightPixels => Content.Height;

    /// <summary>Gets the colour space the samples of the file are in.</summary>
    public PdfImageColorSpace ColorSpace => Content.SourceColorSpace;

    /// <summary>Gets the number of bits per sample.</summary>
    public int BitsPerComponent => Content.BitsPerComponent;

    /// <summary>Gets a value indicating whether the image carries transparency.</summary>
    /// <remarks>
    /// A PNG with an alpha channel and a GIF whose frame states a transparent palette entry ship a soft
    /// mask, and a PNG whose <c>tRNS</c> chunk names a colour ships the colour key instead. A JPEG, a
    /// BMP — whose fourth byte is padding unless a mask names it — and an opaque GIF carry no mask at
    /// all.
    /// </remarks>
    public bool HasAlpha => Content.AlphaData is not null || Content.ColorKey is not null;

    /// <summary>Gets the width the image is drawn at when no size is given, in points.</summary>
    /// <remarks>
    /// The width comes from the resolution metadata of the file — <c>pHYs</c> for a PNG, the JFIF
    /// density for a JPEG, the pixels-per-metre field for a BMP — and falls back to one point per
    /// pixel when the file states none, which is what every GIF does.
    /// </remarks>
    public double NaturalWidth => Content.NaturalWidthPoints ?? Content.Width;

    /// <summary>Gets the height the image is drawn at when no size is given, in points.</summary>
    public double NaturalHeight => Content.NaturalHeightPoints ?? Content.Height;

    /// <summary>Gets the number of frames the file holds, which is one for any format but a GIF.</summary>
    /// <remarks>PDF has no animation: only the first frame is drawn, and this figure says so.</remarks>
    public int FrameCount => Content is GifImage gif ? gif.FrameCount : 1;

    /// <summary>Gets a stable key for the prepared content of the image.</summary>
    /// <remarks>
    /// The key is a SHA-256 hash of the samples, the filter, the colour space, the mask and the
    /// decoding, as an uppercase hexadecimal string. Two images opened from the same file share it, so
    /// the document embeds them once.
    /// </remarks>
    public string Key => EmbeddedImage.KeyFor(Content);

    /// <summary>Gets the prepared content, which is what a page stream selects.</summary>
    internal IEmbeddedImage Content { get; }

    /// <summary>Reads an image, deciding the format from the signature of its bytes.</summary>
    /// <param name="data">The bytes of a PNG, JPEG, BMP or GIF file.</param>
    /// <returns>The opened image.</returns>
    /// <exception cref="ArgumentException">The bytes are empty, or their signature is not one of the four formats.</exception>
    /// <exception cref="NotSupportedException">The format is recognised but the variant is not embedded.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">The file names a feature the engine refuses by name.</exception>
    public static PdfImage Open(ReadOnlyMemory<byte> data)
    {
        var content = EmbeddedImage.Parse(data);

        return new PdfImage(content, FormatOf(content));
    }

    /// <summary>Reads a PNG file.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The opened image.</returns>
    /// <exception cref="ArgumentException">The bytes are not a PNG file.</exception>
    /// <exception cref="NotSupportedException">The PNG is interlaced, sixteen bit, or of a colour type the engine does not embed.</exception>
    public static PdfImage FromPng(ReadOnlyMemory<byte> data)
    {
        return OpenAs(data, PdfImageFormat.Png, EmbeddedImage.IsPng);
    }

    /// <summary>Reads a JPEG file.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The opened image.</returns>
    /// <exception cref="ArgumentException">The bytes are not a JPEG file.</exception>
    /// <exception cref="NotSupportedException">The JPEG is not a baseline eight-bit image, or states more than four components.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">
    /// The EXIF orientation is not the identity, or a four-component JPEG states no Adobe transform.
    /// </exception>
    public static PdfImage FromJpeg(ReadOnlyMemory<byte> data)
    {
        return OpenAs(data, PdfImageFormat.Jpeg, EmbeddedImage.IsJpeg);
    }

    /// <summary>Reads a BMP file.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The opened image.</returns>
    /// <exception cref="ArgumentException">The bytes are not a BMP file.</exception>
    /// <exception cref="NotSupportedException">The BMP is compressed, or its depth is not one the engine reads.</exception>
    public static PdfImage FromBmp(ReadOnlyMemory<byte> data)
    {
        return OpenAs(data, PdfImageFormat.Bmp, EmbeddedImage.IsBmp);
    }

    /// <summary>Reads the first frame of a GIF file.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <returns>The opened image.</returns>
    /// <remarks>
    /// PDF has no animation, so only the first frame is drawn; <see cref="FrameCount"/> states how many
    /// frames the file holds. The frames after the first are not decoded.
    /// </remarks>
    /// <exception cref="ArgumentException">The bytes are not a GIF file.</exception>
    /// <exception cref="NotSupportedException">The GIF states a feature the engine does not decode.</exception>
    public static PdfImage FromGif(ReadOnlyMemory<byte> data)
    {
        return OpenAs(data, PdfImageFormat.Gif, EmbeddedImage.IsGif);
    }

    /// <summary>Reads the bytes after checking their signature states the format asked for.</summary>
    /// <param name="data">The bytes of the file.</param>
    /// <param name="expected">The format the caller stated.</param>
    /// <param name="signature">The signature test of that format.</param>
    /// <returns>The opened image.</returns>
    /// <exception cref="ArgumentException">The bytes are not of the stated format.</exception>
    private static PdfImage OpenAs(ReadOnlyMemory<byte> data, PdfImageFormat expected, SignatureTest signature)
    {
        if (!signature(data.Span))
        {
            throw new ArgumentException(
                $"The bytes are not a {expected} file: their signature states another format, or none.",
                nameof(data));
        }

        return new PdfImage(EmbeddedImage.Parse(data), expected);
    }

    /// <summary>Names the format of a prepared image.</summary>
    /// <param name="content">The prepared image, which was built by one of the four readers.</param>
    /// <returns>The format.</returns>
    private static PdfImageFormat FormatOf(IEmbeddedImage content)
    {
        return content switch
        {
            PngImage => PdfImageFormat.Png,
            JpegImage => PdfImageFormat.Jpeg,
            BmpImage => PdfImageFormat.Bmp,
            GifImage => PdfImageFormat.Gif,
            _ => PdfImageFormat.Unknown,
        };
    }

    /// <summary>Tests the signature of a file.</summary>
    /// <param name="data">The first bytes of the file.</param>
    /// <returns>Whether the bytes start with that format's signature.</returns>
    private delegate bool SignatureTest(ReadOnlySpan<byte> data);
}
