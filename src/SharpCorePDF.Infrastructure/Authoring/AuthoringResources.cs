using System.Buffers.Binary;
using System.Security.Cryptography;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// The fonts and images of one authored document, shared by its pages and written once.
/// </summary>
/// <remarks>
/// A font object and an image object belong to the document, not to a page: two pages that draw the
/// same logo share one image XObject, and the same face is one font object whose subset collects the
/// glyphs of every page. Resource names (<c>F1</c>, <c>Im1</c>) are therefore issued here, and each page
/// states only the ones it uses.
/// </remarks>
internal sealed class AuthoringResources
{
    private readonly PdfWriter writer;
    private readonly Dictionary<PdfTypeface, FontResource> embedded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<StandardFont, FontResource> standard = [];
    private readonly Dictionary<string, ImageResource> images = new(StringComparer.Ordinal);
    private readonly List<FontResource> fontsInOrder = [];
    private readonly List<ImageResource> imagesInOrder = [];
    private int fontCount;
    private int imageCount;

    /// <summary>Initializes a new instance of the <see cref="AuthoringResources"/> class.</summary>
    /// <param name="writer">The writer of the document.</param>
    public AuthoringResources(PdfWriter writer) => this.writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <summary>Starts the resource set of one page.</summary>
    /// <returns>The resource set.</returns>
    public PageResources BeginPage() => new(this);

    /// <summary>Gets the resource of a face, creating it when it is used for the first time.</summary>
    /// <param name="typeface">The face.</param>
    /// <returns>The resource.</returns>
    public FontResource UseFont(PdfTypeface typeface)
    {
        if (typeface.IsEmbedded)
        {
            if (!embedded.TryGetValue(typeface, out var resource))
            {
                resource = new FontResource(writer, typeface, $"F{++fontCount}");
                embedded[typeface] = resource;
                fontsInOrder.Add(resource);
            }

            return resource;
        }

        var family = typeface.Family!.Value;

        if (!standard.TryGetValue(family, out var standardResource))
        {
            standardResource = new FontResource(writer, typeface, $"F{++fontCount}");
            standard[family] = standardResource;
            fontsInOrder.Add(standardResource);
        }

        return standardResource;
    }

    /// <summary>Gets the resource of an image, creating it when it is used for the first time.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The resource.</returns>
    public ImageResource UseImage(PngImage image)
    {
        var key = KeyFor(image);

        if (!images.TryGetValue(key, out var resource))
        {
            resource = new ImageResource(writer, image, key, $"Im{++imageCount}");
            images[key] = resource;
            imagesInOrder.Add(resource);
        }

        return resource;
    }

    /// <summary>Writes every font and image object of the document.</summary>
    /// <param name="hash">The digest that identifies the document, which receives the resource bytes.</param>
    public void WriteObjects(IncrementalHash hash)
    {
        foreach (var image in imagesInOrder)
        {
            image.Write(writer);
            hash.AppendData(Convert.FromHexString(image.Key));
        }

        foreach (var font in fontsInOrder)
        {
            font.Write(writer);
            hash.AppendData(font.Fingerprint);
        }
    }

    /// <summary>Builds a stable key for the prepared bytes of an image.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The key as a hexadecimal string.</returns>
    private static string KeyFor(PngImage image)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(size, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(size[4..], image.Height);
        hash.AppendData(size);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(image.ColorSpace.ToString() ?? string.Empty));
        hash.AppendData(image.ColorData);

        if (image.AlphaData is { } alpha)
        {
            hash.AppendData(alpha);
        }

        foreach (var value in image.ColorKey ?? [])
        {
            BinaryPrimitives.WriteInt32BigEndian(size, value);
            hash.AppendData(size[..4]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
