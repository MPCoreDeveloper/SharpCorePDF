using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>One image XObject of an authored document, with its soft mask when it has one.</summary>
internal sealed class ImageResource
{
    private readonly PngImage image;
    private readonly int maskNumber;

    /// <summary>Initializes a new instance of the <see cref="ImageResource"/> class.</summary>
    /// <param name="writer">The writer of the document, which allocates the object numbers.</param>
    /// <param name="image">The prepared image.</param>
    /// <param name="key">The key that identifies the prepared bytes.</param>
    /// <param name="name">The resource name, for example <c>Im1</c>.</param>
    public ImageResource(PdfWriter writer, PngImage image, string key, string name)
    {
        this.image = image;
        Key = key;
        Name = name;
        Number = writer.AllocateObject();
        maskNumber = image.AlphaData is null ? 0 : writer.AllocateObject();
    }

    /// <summary>Gets the key that identifies the prepared bytes.</summary>
    public string Key { get; }

    /// <summary>Gets the resource name the pages select the image with.</summary>
    public string Name { get; }

    /// <summary>Gets the object number of the image XObject.</summary>
    public int Number { get; }

    /// <summary>Writes the image and its soft mask.</summary>
    /// <param name="writer">The writer of the document.</param>
    public void Write(PdfWriter writer)
    {
        if (image.AlphaData is { } alpha)
        {
            writer.WriteObject(
                maskNumber,
                new PdfStream(
                    new PdfDictionary()
                        .SetName("Type", "XObject")
                        .SetName("Subtype", "Image")
                        .SetInteger("Width", image.Width)
                        .SetInteger("Height", image.Height)
                        .SetName("ColorSpace", "DeviceGray")
                        .SetInteger("BitsPerComponent", 8)
                        .SetName("Filter", "FlateDecode")
                        .Set("DecodeParms", Predictor(1)),
                    alpha));
        }

        writer.WriteObject(Number, new PdfStream(BuildDictionary(), image.ColorData));
    }

    /// <summary>Builds the dictionary of the colour image.</summary>
    /// <returns>The dictionary.</returns>
    private PdfDictionary BuildDictionary()
    {
        var dictionary = new PdfDictionary()
            .SetName("Type", "XObject")
            .SetName("Subtype", "Image")
            .SetInteger("Width", image.Width)
            .SetInteger("Height", image.Height)
            .Set("ColorSpace", image.ColorSpace)
            .SetInteger("BitsPerComponent", 8)
            .SetName("Filter", "FlateDecode")
            .Set("DecodeParms", Predictor(image.Colors));

        if (image.AlphaData is not null)
        {
            dictionary.Set("SMask", new PdfReference(maskNumber, 0));
        }
        else if (image.ColorKey is { } key)
        {
            dictionary.Set("Mask", new PdfArray(key.Select(value => (PdfObject)new PdfInteger(value))));
        }

        return dictionary;
    }

    /// <summary>Builds the decode parameters of a PNG predictor.</summary>
    /// <param name="colors">The number of components per sample.</param>
    /// <returns>The dictionary.</returns>
    private PdfDictionary Predictor(int colors) =>
        new PdfDictionary()
            .SetInteger("Predictor", 15)
            .SetInteger("Colors", colors)
            .SetInteger("BitsPerComponent", 8)
            .SetInteger("Columns", image.Width);
}

/// <summary>The fonts and images one page selects, named as the page's resource dictionary names them.</summary>
internal sealed class PageResources
{
    private readonly AuthoringResources owner;
    private readonly Dictionary<PdfTypeface, FontResource> fonts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ImageResource> images = new(StringComparer.Ordinal);
    private PdfDictionary? graphicsStates;

    /// <summary>Initializes a new instance of the <see cref="PageResources"/> class.</summary>
    /// <param name="owner">The resources of the document.</param>
    public PageResources(AuthoringResources owner) => this.owner = owner;

    /// <summary>Gets the font resource a face is drawn with on this page.</summary>
    /// <param name="typeface">The face.</param>
    /// <returns>The resource.</returns>
    public FontResource Font(PdfTypeface typeface)
    {
        if (!fonts.TryGetValue(typeface, out var resource))
        {
            resource = owner.UseFont(typeface);
            fonts[typeface] = resource;
        }

        return resource;
    }

    /// <summary>Gets the image resource an image is drawn with on this page.</summary>
    /// <param name="image">The prepared image.</param>
    /// <returns>The resource.</returns>
    public ImageResource Image(PngImage image)
    {
        var resource = owner.UseImage(image);
        images.TryAdd(resource.Key, resource);
        return resource;
    }

    /// <summary>Adds the graphics states the content stream selects.</summary>
    /// <param name="registry">The registry of the page's content stream.</param>
    public void AddGraphicsStates(ContentResourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        graphicsStates = registry.ToDictionary();
    }

    /// <summary>Builds the resource dictionary of the page.</summary>
    /// <returns>The dictionary.</returns>
    public PdfDictionary ToDictionary()
    {
        var resources = new PdfDictionary();

        if (fonts.Count > 0)
        {
            var entries = new PdfDictionary();

            foreach (var font in fonts.Values)
            {
                entries.Set(font.Name, new PdfReference(font.ObjectNumber, 0));
            }

            resources.Set("Font", entries);
        }

        if (images.Count > 0)
        {
            var entries = new PdfDictionary();

            foreach (var image in images.Values)
            {
                entries.Set(image.Name, new PdfReference(image.Number, 0));
            }

            resources.Set("XObject", entries);
        }

        if (graphicsStates is not null)
        {
            resources.Set("ExtGState", graphicsStates);
        }

        return resources;
    }
}
