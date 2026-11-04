namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// States the pixels of an image payload, when the payload is one the reader can state as samples.
/// </summary>
/// <remarks>
/// The rule is deliberately narrow: the bytes are exposed as pixels only when nothing is left to
/// interpret — a colour space whose samples are its channels, a palette the samples can be looked up
/// in, a geometry that agrees with the payload's length, and a bit depth this library can widen without
/// losing anything. A sixteen-bit image needs a rounding decision, a <c>Separation</c> image needs a
/// function evaluated, and a JPEG needs a decoder this library does not ship, so each stays a payload.
/// </remarks>
internal static class ImagePixels
{
    /// <summary>The format of an image with one channel per sample.</summary>
    public const string Gray8 = "Gray8";

    /// <summary>The format of an image with three channels interleaved per pixel.</summary>
    public const string Rgb24 = "Rgb24";

    /// <summary>The format of an image with four device samples interleaved per pixel.</summary>
    public const string Cmyk32 = "Cmyk32";

    /// <summary>Describes the pixels of a decoded payload.</summary>
    /// <param name="decoded">Whether the library decoded the payload at all.</param>
    /// <param name="filter">The filter chain the image declares, or <see langword="null"/>.</param>
    /// <param name="colorSpace">The colour space of the image, resolved to what a sample means.</param>
    /// <param name="width">The width of the image in samples, or <see langword="null"/>.</param>
    /// <param name="height">The height of the image in samples, or <see langword="null"/>.</param>
    /// <param name="bitsPerComponent">The bits of each sample, or <see langword="null"/>.</param>
    /// <param name="data">The decoded payload, or <see langword="null"/> when it was not buffered.</param>
    /// <param name="cap">The largest number of pixel bytes that may be produced.</param>
    /// <returns>The format and the pixels, or two <see langword="null"/> values.</returns>
    public static (string? Format, ReadOnlyMemory<byte>? Pixels) Of(
        bool decoded,
        string? filter,
        PdfSampleSpace colorSpace,
        int? width,
        int? height,
        int? bitsPerComponent,
        ReadOnlyMemory<byte>? data,
        int cap)
    {
        if (!decoded
            || data is not { } payload
            || filter?.Contains("DCTDecode", StringComparison.Ordinal) == true
            || colorSpace.Kind == PdfSampleKind.None
            || width is not > 0
            || height is not > 0
            || bitsPerComponent is not (1 or 2 or 4 or 8))
        {
            return (null, null);
        }

        var bits = bitsPerComponent.Value;
        var columns = width.Value;
        var rows = height.Value;

        // Every row of an image is padded to a whole byte, so a sample never straddles one: the payload
        // is the padded row length times the height, not the sample count divided by eight.
        var rowLength = ((long)columns * colorSpace.Components * bits + 7) / 8;

        // A payload that does not carry exactly what the geometry states is not pixels: an image whose
        // declared geometry disagrees with its data is reported as it is rather than reinterpreted.
        if (payload.Length != rowLength * rows)
        {
            return (null, null);
        }

        // One byte per sample of the space, which is also the width of one palette entry: a palette index
        // is stated as the colour the table holds, so an indexed pixel occupies its entry's stride.
        var channels = colorSpace.PaletteStride;
        var format = channels switch
        {
            1 => Gray8,
            3 => Rgb24,
            4 => Cmyk32,
            _ => null,
        };

        if (format is null)
        {
            return (null, null);
        }

        // Eight-bit samples of a space whose components are its channels are the payload itself: there is
        // nothing to unpack, so the caller is handed a view rather than a copy.
        if (bits == 8 && !colorSpace.IsIndexed)
        {
            return (format, payload);
        }

        var length = (long)columns * rows * channels;

        // Widening a sample multiplies the bytes, so a payload that just fits the budget can still expand
        // far past it. The expansion is a convenience, never a reason to allocate above the budget.
        if (length > cap)
        {
            return (null, null);
        }

        if (Widen(payload, colorSpace, columns, rows, bits, (int)rowLength, channels) is not { } pixels)
        {
            return (null, null);
        }

        return (format, pixels);
    }

    /// <summary>Unpacks samples narrower than a byte, and applies a palette, into one byte per channel.</summary>
    /// <param name="payload">The decoded payload.</param>
    /// <param name="colorSpace">The colour space of the image.</param>
    /// <param name="columns">The width of the image in pixels.</param>
    /// <param name="rows">The height of the image in pixels.</param>
    /// <param name="bits">The width of one sample in bits: one, two, four or eight.</param>
    /// <param name="rowLength">The length of one padded row of the payload.</param>
    /// <param name="channels">The number of bytes each pixel occupies in the result.</param>
    /// <returns>The pixels, or <see langword="null"/> when a sample cannot be stated.</returns>
    private static byte[]? Widen(
        ReadOnlyMemory<byte> payload,
        PdfSampleSpace colorSpace,
        int columns,
        int rows,
        int bits,
        int rowLength,
        int channels)
    {
        var pixels = new byte[(long)columns * rows * channels];
        var palette = colorSpace.Palette;
        var stride = colorSpace.PaletteStride;

        // A sample of n bits is a fraction of full scale, so widening to eight bits multiplies by the
        // exact ratio: 0 or 1 becomes 0 or 255, a two-bit sample steps by 85, a four-bit one by 17.
        var scale = 255 / ((1 << bits) - 1);
        var target = 0;

        for (var row = 0; row < rows; row++)
        {
            var source = payload.Span.Slice(row * rowLength, rowLength);

            for (var column = 0; column < columns; column++)
            {
                if (colorSpace.IsIndexed)
                {
                    var index = Read(source, column, bits);

                    // An index the palette does not define means the data and the space disagree, which
                    // is reported rather than guessed at.
                    if (index > colorSpace.MaxIndex)
                    {
                        return null;
                    }

                    palette.AsSpan(index * stride, stride).CopyTo(pixels.AsSpan(target));
                    target += stride;
                    continue;
                }

                for (var component = 0; component < channels; component++)
                {
                    var sample = Read(source, (column * channels) + component, bits);

                    pixels[target++] = (byte)(sample * scale);
                }
            }
        }

        return pixels;
    }

    /// <summary>Reads one sample of a row, most significant bit first.</summary>
    /// <param name="row">The padded row.</param>
    /// <param name="index">The position of the sample within the row.</param>
    /// <param name="bits">The width of one sample in bits.</param>
    /// <returns>The sample as stored, before any scaling.</returns>
    private static int Read(ReadOnlySpan<byte> row, int index, int bits)
    {
        if (bits == 8)
        {
            return row[index];
        }

        var offset = index * bits;
        var shift = 8 - bits - (offset & 7);

        return (row[offset >> 3] >> shift) & ((1 << bits) - 1);
    }
}

