using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Decodes the compression filters the engine must understand.
/// </summary>
/// <remarks>
/// Stream bytes are otherwise copied verbatim, so this class only runs for object streams and
/// cross-reference streams, which must be uncompressed or Flate compressed.
/// </remarks>
internal static partial class PdfFilters
{
    /// <summary>Determines whether a stream declares Flate compression as its first filter.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <returns><see langword="true"/> when the first filter is FlateDecode.</returns>
    public static bool HasFlateCompression(PdfDictionary dictionary)
    {
        var filters = ReadFilterNames(dictionary);
        return filters.Count > 0 && filters[0] is "FlateDecode" or "Fl";
    }

    /// <summary>Decodes a stream, applying every filter it declares in order.</summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="PdfUnsupportedFeatureException">The stream uses a filter the engine does not implement.</exception>
    /// <exception cref="PdfSourceFormatException">A compressed stream could not be decompressed.</exception>
    public static byte[] Decode(PdfStream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var declaredFilters = ReadFilterNames(stream.Dictionary);

        if (declaredFilters.Count == 0)
        {
            // An unfiltered stream is handed to a parser, and a parser reads an array: this is the one
            // place where a stream body is copied rather than viewed.
            return stream.Raw.ToArray();
        }

        var parameters = ReadDecodeParameters(stream.Dictionary);
        ReadOnlyMemory<byte> current = stream.Raw;
        byte[]? decoded = null;

        for (var index = 0; index < declaredFilters.Count; index++)
        {
            var name = declaredFilters[index];
            var parametersForFilter = index < parameters.Count ? parameters[index] : null;

            decoded = name switch
            {
                "FlateDecode" or "Fl" => ApplyFlate(current, parametersForFilter, sourceName),
                "ASCIIHexDecode" or "AHx" => DecodeAsciiHex(current),
                "ASCII85Decode" or "A85" => DecodeAscii85(current),
                _ => throw new PdfUnsupportedFeatureException(
                    sourceName,
                    name,
                    "the engine only decompresses object streams and cross-reference streams, which must be uncompressed, FlateDecode, ASCIIHexDecode or ASCII85Decode. All other streams are copied without being decoded."),
            };

            current = decoded;
        }

        return decoded ?? stream.Raw.ToArray();
    }

    /// <summary>Reads the names of the filters a stream declares, in the order it applies them.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <returns>The filter names, without their abbreviations being expanded.</returns>
    public static List<string> ReadFilterNames(PdfDictionary dictionary)
    {
        var names = new List<string>();

        switch (dictionary.Get("Filter"))
        {
            case PdfName name:
                names.Add(name.Value);
                break;

            case PdfArray array:
                foreach (var item in array.Items)
                {
                    if (item is PdfName itemName)
                    {
                        names.Add(itemName.Value);
                    }
                }

                break;
        }

        return names;
    }

    /// <summary>Reads the decode parameters that belong to each filter a stream declares.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <returns>One entry per parameter dictionary, in filter order; a filter without one has none.</returns>
    public static List<PdfDictionary?> ReadDecodeParameters(PdfDictionary dictionary)
    {
        var parameters = new List<PdfDictionary?>();

        switch (dictionary.Get("DecodeParms") ?? dictionary.Get("DP"))
        {
            case PdfDictionary single:
                parameters.Add(single);
                break;

            case PdfArray array:
                foreach (var item in array.Items)
                {
                    parameters.Add(item as PdfDictionary);
                }

                break;
        }

        return parameters;
    }
}
