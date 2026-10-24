using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>One image XObject of an authored document, with its soft mask when it has one.</summary>
internal sealed class ImageResource
{
    private readonly IEmbeddedImage image;
    private readonly int maskNumber;
    private readonly int profileNumber;

    /// <summary>Initializes a new instance of the <see cref="ImageResource"/> class.</summary>
    /// <param name="writer">The writer of the document, which allocates the object numbers.</param>
    /// <param name="image">The prepared image.</param>
    /// <param name="key">The key that identifies the prepared bytes.</param>
    /// <param name="name">The resource name, for example <c>Im1</c>.</param>
    public ImageResource(PdfWriter writer, IEmbeddedImage image, string key, string name)
    {
        this.image = image;
        Key = key;
        Name = name;
        Number = writer.AllocateObject();
        maskNumber = image.AlphaData is null ? 0 : writer.AllocateObject();
        profileNumber = image.IccProfile is null ? 0 : writer.AllocateObject();
    }

    /// <summary>Gets the key that identifies the prepared bytes.</summary>
    public string Key { get; }

    /// <summary>Gets the resource name the pages select the image with.</summary>
    public string Name { get; }

    /// <summary>Gets the object number of the image XObject.</summary>
    public int Number { get; }

    /// <summary>Writes the image, its soft mask and the colour profile it states.</summary>
    /// <param name="writer">The writer of the document.</param>
    public void Write(PdfWriter writer)
    {
        if (image.IccProfile is { } profile)
        {
            // An ICCBased colour space names a stream, so the profile is written before the image that
            // refers to it. Flate is what PDF expects of a profile stream and keeps the file small.
            writer.WriteObject(
                profileNumber,
                new PdfStream(
                    new PdfDictionary()
                        .SetInteger("N", profile.Components)
                        .SetName("Alternate", profile.Alternate)
                        .SetName("Filter", "FlateDecode"),
                    EmbeddedImage.Compress(profile.Data)));
        }

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
                        .Set("DecodeParms", EmbeddedImage.Predictor(1, image.Width)),
                    alpha));
        }

        writer.WriteObject(Number, new PdfStream(BuildDictionary(), image.ColorData));
    }

    /// <summary>Builds the dictionary of the colour image.</summary>
    /// <returns>The dictionary.</returns>
    private PdfDictionary BuildDictionary()
    {
        // A profile the file states wins over the device colour space the samples alone would name.
        var colorSpace = image.IccProfile is null
            ? image.ColorSpace
            : new PdfArray([new PdfName("ICCBased"), new PdfReference(profileNumber, 0)]);

        var dictionary = new PdfDictionary()
            .SetName("Type", "XObject")
            .SetName("Subtype", "Image")
            .SetInteger("Width", image.Width)
            .SetInteger("Height", image.Height)
            .Set("ColorSpace", colorSpace)
            .SetInteger("BitsPerComponent", image.BitsPerComponent)
            .SetName("Filter", image.Filter);

        if (image.DecodeParms is { } parms)
        {
            dictionary.Set("DecodeParms", parms);
        }

        if (image.Decode is { } decode)
        {
            dictionary.Set("Decode", new PdfArray(decode.Select(value => (PdfObject)new PdfInteger(value))));
        }

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
    public ImageResource Image(IEmbeddedImage image)
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
