namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>What a sample of an image stands for, once its colour space has been resolved.</summary>
internal enum PdfSampleKind
{
    /// <summary>
    /// Not a space whose samples are colours: one that needs a function to be understood
    /// (<c>Separation</c>, <c>DeviceN</c>, <c>Lab</c>), or one this library does not know.
    /// </summary>
    None,

    /// <summary>One sample per pixel, a level of grey.</summary>
    Gray,

    /// <summary>Three samples per pixel: red, green and blue.</summary>
    Rgb,

    /// <summary>Four samples per pixel: cyan, magenta, yellow and black.</summary>
    Cmyk,
}

/// <summary>
/// The colour space of an image, resolved as far as what a sample means.
/// </summary>
/// <remarks>
/// <para>
/// A document names a colour space in one of three shapes: a name, an array whose first item is the
/// family and whose other items state the mapping, or a name the resource dictionary defines in terms
/// of either. The third shape is why an image cannot be understood from the text of its
/// <c>/ColorSpace</c> alone, and the second is why a palette needs a second object read: an
/// <c>Indexed</c> space keeps its lookup table beside the base space, not inside it.
/// </para>
/// <para>
/// Resolution stops where a conversion would begin. A <c>Separation</c> or <c>DeviceN</c> space states
/// its colours through a tint transform, which is a function rather than samples, so this record reports
/// <see cref="PdfSampleKind.None"/> instead of evaluating it and presenting the result as if the document
/// had stored colours. The same is true of <c>Lab</c>, whose three samples are a lightness and two
/// opponents rather than three channels a caller can paint with.
/// </para>
/// </remarks>
/// <param name="Kind">What a sample, or a palette entry, stands for.</param>
/// <param name="Components">The number of samples each pixel of the image stores.</param>
/// <param name="IsIndexed">Whether a sample is an index into <paramref name="Palette"/>.</param>
/// <param name="Palette">The lookup table of an indexed space, or <see langword="null"/>.</param>
/// <param name="MaxIndex">The largest index the lookup table defines.</param>
internal readonly record struct PdfSampleSpace(
    PdfSampleKind Kind,
    int Components,
    bool IsIndexed,
    byte[]? Palette,
    int MaxIndex)
{
    /// <summary>Gets a space whose samples carry no colour this library is willing to state.</summary>
    public static PdfSampleSpace None => default;

    /// <summary>Gets the number of bytes a palette entry occupies.</summary>
    public int PaletteStride => Kind switch
    {
        PdfSampleKind.Gray => 1,
        PdfSampleKind.Rgb => 3,
        PdfSampleKind.Cmyk => 4,
        _ => 0,
    };
}

/// <summary>
/// Resolves the colour space of an image to the meaning of its samples.
/// </summary>
/// <remarks>
/// The mappings this library reads are the ones that need no colour management to be truthful:
/// <c>DeviceGray</c>, <c>DeviceRGB</c>, <c>DeviceCMYK</c>, their calibrated forms, an <c>ICCBased</c>
/// space whose profile states its component count, and an <c>Indexed</c> space over one of those. A
/// profile is not applied — the samples are returned as the document stores them — and neither is a
/// tint transform.
/// </remarks>
internal static class PdfSampleSpaces
{
    /// <summary>
    /// The largest lookup table an indexed space is read from: the 256 entries the specification allows,
    /// each of up to four samples.
    /// </summary>
    private const int MaxPaletteBytes = 256 * 4;

    /// <summary>Resolves a <c>/ColorSpace</c> value.</summary>
    /// <param name="reader">The document, used to follow references.</param>
    /// <param name="value">The value of the <c>/ColorSpace</c> entry, or <see langword="null"/>.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>
    /// The resolved space, or <see cref="PdfSampleSpace.None"/> when its samples are not colours this
    /// library can state.
    /// </returns>
    public static PdfSampleSpace Resolve(PdfFileReader reader, PdfObject? value, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.Resolve(value) is PdfName name)
        {
            return FromName(name.Value);
        }

        if (reader.ResolveArray(value) is not { Count: > 0 } array
            || reader.Resolve(array.Items[0]) is not PdfName family)
        {
            return PdfSampleSpace.None;
        }

        return family.Value switch
        {
            "Indexed" or "I" => Indexed(reader, array, sourceName),
            "ICCBased" => IccBased(reader, array),
            "CalGray" => FromName("DeviceGray"),
            "CalRGB" => FromName("DeviceRGB"),
            _ => PdfSampleSpace.None,
        };
    }

    /// <summary>Reads the spaces a single name can state.</summary>
    /// <remarks>
    /// A calibrated space is never a name — <c>CalGray</c> and <c>CalRGB</c> are arrays whose second item
    /// carries the mapping — so only the device names and the abbreviations an inline image uses are here.
    /// </remarks>
    /// <param name="name">The name, without its leading slash.</param>
    /// <returns>The resolved space, or <see cref="PdfSampleSpace.None"/>.</returns>
    private static PdfSampleSpace FromName(string name) => name switch
    {
        "DeviceGray" or "G" => new(PdfSampleKind.Gray, 1, false, null, 0),
        "DeviceRGB" or "RGB" => new(PdfSampleKind.Rgb, 3, false, null, 0),
        "DeviceCMYK" or "CMYK" => new(PdfSampleKind.Cmyk, 4, false, null, 0),
        _ => PdfSampleSpace.None,
    };

    /// <summary>Reads an <c>[/Indexed base hival lookup]</c> space, palette included.</summary>
    /// <param name="reader">The document, used to follow references.</param>
    /// <param name="array">The array that states the space.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The resolved space, or <see cref="PdfSampleSpace.None"/>.</returns>
    private static PdfSampleSpace Indexed(PdfFileReader reader, PdfArray array, string sourceName)
    {
        if (array.Count < 4)
        {
            return PdfSampleSpace.None;
        }

        var baseSpace = Resolve(reader, array.Items[1], sourceName);

        // A palette over a palette, or over a space that needs a function, would be an index into
        // something this library cannot state, so neither is followed.
        if (baseSpace.IsIndexed || baseSpace.Kind == PdfSampleKind.None)
        {
            return PdfSampleSpace.None;
        }

        // The specification bounds the index to what eight bits can address, because the samples of an
        // indexed image are never wider than that.
        if (reader.Resolve(array.Items[2]) is not PdfInteger high || high.Value is < 0 or > 255)
        {
            return PdfSampleSpace.None;
        }

        if (ReadLookup(reader, array.Items[3], sourceName) is not { } lookup)
        {
            return PdfSampleSpace.None;
        }

        var stride = baseSpace.PaletteStride;
        var entries = lookup.Length / stride;

        // A table that holds nothing — an empty string, or fewer bytes than one entry — states no colour
        // for any index, so it is refused here rather than carried as a palette with no entries.
        if (entries == 0)
        {
            return PdfSampleSpace.None;
        }

        // A document that declares more entries than its table holds leaves the last ones undefined; the
        // table's own length is what an index of the image can be checked against, so that is what is kept.
        var last = (int)Math.Min(high.Value, entries - 1);

        return new PdfSampleSpace(baseSpace.Kind, 1, true, lookup, last);
    }

    /// <summary>Reads an <c>[/ICCBased profile]</c> space by the component count its profile states.</summary>
    /// <param name="reader">The document, used to follow references.</param>
    /// <param name="array">The array that states the space.</param>
    /// <returns>The resolved space, or <see cref="PdfSampleSpace.None"/>.</returns>
    private static PdfSampleSpace IccBased(PdfFileReader reader, PdfArray array)
    {
        if (array.Count < 2 || reader.Resolve(array.Items[1]) is not PdfStream profile)
        {
            return PdfSampleSpace.None;
        }

        var components = profile.Dictionary.GetInteger("N");

        // A profile states what its samples mean through /N: one, three or four of them. Another count —
        // a named-colour or a device-link profile — is stated through /Alternate, which is no use here:
        // any alternate this library can read has one, three or four components, so it could never take
        // the same number of samples as a profile of another size.
        return components switch
        {
            1 => FromName("DeviceGray"),
            3 => FromName("DeviceRGB"),
            4 => FromName("DeviceCMYK"),
            _ => PdfSampleSpace.None,
        };
    }

    /// <summary>Reads the lookup table of an indexed space, from the stream or the string that holds it.</summary>
    /// <param name="reader">The document, used to follow references.</param>
    /// <param name="value">The fourth item of the array.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The entries of the table, or <see langword="null"/> when they cannot be read.</returns>
    private static byte[]? ReadLookup(PdfFileReader reader, PdfObject? value, string sourceName)
    {
        switch (reader.Resolve(value))
        {
            case PdfString text:
                return text.Bytes;

            case PdfStream stream:
                // A table is small by construction, so the budget it is read under covers every table the
                // specification allows and refuses one larger than any palette could be.
                var payload = PdfImageFilters.Decode(stream, MaxPaletteBytes, sourceName);

                return payload.IsDecodable && payload.Data is { Length: > 0 } data ? data.ToArray() : null;

            default:
                return null;
        }
    }
}
