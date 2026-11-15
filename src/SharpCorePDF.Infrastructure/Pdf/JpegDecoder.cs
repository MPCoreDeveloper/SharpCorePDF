namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Decodes a JPEG — baseline, extended sequential or progressive — to its samples.
/// </summary>
/// <remarks>
/// <para>
/// The decoder reads the sequential form (SOF0/SOF1) and the progressive form (SOF2): a sequential scan
/// is decoded in one pass, while a progressive file's scans accumulate quantised coefficients that the
/// inverse DCT reconstructs once at the end. Both forms cover canonical Huffman coding, restart markers,
/// an 8 x 8 inverse DCT and the component layouts a PDF image states — Gray, RGB/YCbCr and CMYK/YCCK.
/// </para>
/// <para>
/// The samples are the values the JPEG stores, interleaved, one byte per component: <c>Gray8</c> for one
/// component, <c>Rgb24</c> for three (YCbCr is converted) and <c>Cmyk32</c> for four. The caller pairs the
/// length with the image's own geometry.
/// </para>
/// </remarks>
internal sealed class JpegDecoder
{
    private static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static readonly double[] Cosines = CreateCosines();

    private readonly ReadOnlyMemory<byte> data;
    private readonly int[][] quantisation = new int[4][];
    private readonly JpegHuffman?[] dcTables = new JpegHuffman?[4];
    private readonly JpegHuffman?[] acTables = new JpegHuffman?[4];

    private JpegComponent[] components = [];
    private int width;
    private int height;
    private int maxH;
    private int maxV;
    private int restartInterval;
    private int adobeTransform = -1;
    private bool progressive;
    private JpegComponent[] scanComponents = [];
    private int scanStart;
    private int scanEnd;
    private int scanHigh;
    private int scanLow;
    private int eobRun;

    private JpegDecoder(ReadOnlyMemory<byte> data) => this.data = data;

    /// <summary>Decodes a JPEG to its samples.</summary>
    /// <param name="data">The JPEG bytes.</param>
    /// <param name="maxBytes">The largest pixel buffer that is kept.</param>
    /// <param name="pixels">The interleaved samples, or null when the buffer is above the cap.</param>
    /// <param name="format">The pixel format the samples state, or null when nothing decoded.</param>
    /// <param name="width">The width of the JPEG in pixels.</param>
    /// <param name="height">The height of the JPEG in pixels.</param>
    /// <returns>True when a baseline JPEG decoded.</returns>
    public static bool TryDecode(
        ReadOnlyMemory<byte> data,
        int maxBytes,
        out byte[]? pixels,
        out string? format,
        out int width,
        out int height) =>
        new JpegDecoder(data).Run(maxBytes, out pixels, out format, out width, out height);

    private bool Run(int maxBytes, out byte[]? pixels, out string? format, out int width, out int height)
    {
        pixels = null;
        format = null;
        width = 0;
        height = 0;

        var span = data.Span;

        if (span.Length < 4 || span[0] != 0xFF || span[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;

        while (offset + 1 < span.Length)
        {
            if (span[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            while (offset < span.Length && span[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= span.Length)
            {
                break;
            }

            var marker = span[offset++];

            if (marker == 0xD9)
            {
                break;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (offset + 2 > span.Length)
            {
                return false;
            }

            var length = (span[offset] << 8) | span[offset + 1];

            if (length < 2 || offset + length > span.Length)
            {
                return false;
            }

            var segment = span.Slice(offset + 2, length - 2);

            switch (marker)
            {
                case 0xDB:
                    if (!ParseQuantisation(segment))
                    {
                        return false;
                    }

                    break;
                case 0xC4:
                    if (!ParseHuffman(segment))
                    {
                        return false;
                    }

                    break;
                case 0xC0:
                case 0xC1:
                    if (!ParseFrame(segment))
                    {
                        return false;
                    }

                    break;
                case 0xC2:
                    progressive = true;

                    if (!ParseFrame(segment))
                    {
                        return false;
                    }

                    break;
                case 0xDD:
                    if (segment.Length < 2)
                    {
                        return false;
                    }

                    restartInterval = (segment[0] << 8) | segment[1];
                    break;
                case 0xEE:
                    ParseAdobe(segment);
                    break;
                case 0xDA:
                    if (!ParseScan(segment))
                    {
                        return false;
                    }

                    if (!progressive)
                    {
                        if (!DecodeScan(offset + length, maxBytes, out pixels, out format, out width, out height))
                        {
                            return false;
                        }

                        return format is not null;
                    }

                    if (!DecodeProgressiveScan(offset + length, out var nextOffset))
                    {
                        return false;
                    }

                    offset = nextOffset;
                    continue;
            }

            offset += length;
        }

        if (!progressive || !ReconstructProgressive() || !BuildOutput(maxBytes, out pixels, out format))
        {
            return false;
        }

        width = this.width;
        height = this.height;
        return true;
    }

    private static double[] CreateCosines()
    {
        var table = new double[64];

        for (var x = 0; x < 8; x++)
        {
            for (var u = 0; u < 8; u++)
            {
                table[(x * 8) + u] = Math.Cos(((2 * x) + 1) * u * Math.PI / 16.0)
                    * (u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0);
            }
        }

        return table;
    }

    private bool ParseQuantisation(ReadOnlySpan<byte> segment)
    {
        var offset = 0;

        while (offset < segment.Length)
        {
            var information = segment[offset++];
            var precision = information >> 4;
            var table = information & 15;

            if (table > 3 || precision > 1)
            {
                return false;
            }

            var size = precision == 0 ? 64 : 128;

            if (offset + size > segment.Length)
            {
                return false;
            }

            var values = new int[64];

            for (var index = 0; index < 64; index++)
            {
                values[ZigZag[index]] = precision == 0
                    ? segment[offset + index]
                    : (segment[offset + (index * 2)] << 8) | segment[offset + (index * 2) + 1];
            }

            quantisation[table] = values;
            offset += size;
        }

        return true;
    }

    private bool ParseHuffman(ReadOnlySpan<byte> segment)
    {
        var offset = 0;

        while (offset < segment.Length)
        {
            if (offset + 17 > segment.Length)
            {
                return false;
            }

            var information = segment[offset++];
            var tableClass = information >> 4;
            var tableId = information & 15;

            if (tableId > 3 || tableClass > 1)
            {
                return false;
            }

            var counts = segment.Slice(offset, 16);
            offset += 16;

            var total = 0;

            foreach (var count in counts)
            {
                total += count;
            }

            if (total == 0 || offset + total > segment.Length)
            {
                return false;
            }

            var symbols = segment.Slice(offset, total);
            offset += total;

            var table = JpegHuffman.Create(counts, symbols);

            if (tableClass == 0)
            {
                dcTables[tableId] = table;
            }
            else
            {
                acTables[tableId] = table;
            }
        }

        return true;
    }

    private bool ParseFrame(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < 6 || segment[0] != 8)
        {
            return false;
        }

        height = (segment[1] << 8) | segment[2];
        width = (segment[3] << 8) | segment[4];
        var count = segment[5];

        if (width <= 0 || height <= 0 || count is < 1 or > 4 || segment.Length < 6 + (count * 3))
        {
            return false;
        }

        var parsed = new JpegComponent[count];
        var horizontal = 0;
        var vertical = 0;

        for (var index = 0; index < count; index++)
        {
            var offset = 6 + (index * 3);
            var component = new JpegComponent
            {
                Id = segment[offset],
                H = segment[offset + 1] >> 4,
                V = segment[offset + 1] & 15,
                Quantisation = segment[offset + 2],
            };

            if (component.H is < 1 or > 4 || component.V is < 1 or > 4 || component.Quantisation > 3)
            {
                return false;
            }

            parsed[index] = component;
            horizontal = Math.Max(horizontal, component.H);
            vertical = Math.Max(vertical, component.V);
        }

        this.components = parsed;
        maxH = horizontal;
        maxV = vertical;

        return true;
    }

    private bool ParseScan(ReadOnlySpan<byte> segment)
    {
        if (components.Length == 0 || segment.Length < 1)
        {
            return false;
        }

        var count = segment[0];

        if (count is < 1 or > 4 || segment.Length < 1 + (count * 2) + 3)
        {
            return false;
        }

        var named = new JpegComponent[count];

        for (var index = 0; index < count; index++)
        {
            var id = segment[1 + (index * 2)];
            var tables = segment[2 + (index * 2)];
            var component = Array.Find(components, candidate => candidate.Id == id);

            if (component is null)
            {
                return false;
            }

            component.DcTable = tables >> 4;
            component.AcTable = tables & 15;
            named[index] = component;
        }

        this.scanComponents = named;

        var tail = 1 + (count * 2);
        scanStart = segment[tail];
        scanEnd = segment[tail + 1];
        scanHigh = segment[tail + 2] >> 4;
        scanLow = segment[tail + 2] & 15;

        return true;
    }

    private void ParseAdobe(ReadOnlySpan<byte> segment)
    {
        if (segment.Length >= 12
            && segment[0] == (byte)'A'
            && segment[1] == (byte)'d'
            && segment[2] == (byte)'o'
            && segment[3] == (byte)'b'
            && segment[4] == (byte)'e')
        {
            adobeTransform = segment[11];
        }
    }

    private sealed class JpegComponent
    {
        public int Id { get; init; }

        public int H { get; init; }

        public int V { get; init; }

        public int Quantisation { get; init; }

        public int DcTable { get; set; }

        public int AcTable { get; set; }

        public byte[] Plane { get; set; } = [];

        public int Stride { get; set; }

        public int Predictor { get; set; }

        public int[] Coefficients { get; set; } = [];

        public int BlocksX { get; set; }

        public int BlocksY { get; set; }
    }

    private bool DecodeScan(
        int scanStart,
        int maxBytes,
        out byte[]? pixels,
        out string? format,
        out int width,
        out int height)
    {
        pixels = null;
        format = null;
        width = this.width;
        height = this.height;

        var mcuWidth = 8 * maxH;
        var mcuHeight = 8 * maxV;
        var mcuColumns = (this.width + mcuWidth - 1) / mcuWidth;
        var mcuRows = (this.height + mcuHeight - 1) / mcuHeight;

        foreach (var component in components)
        {
            component.Stride = mcuColumns * 8 * component.H;
            component.Plane = new byte[component.Stride * mcuRows * 8 * component.V];
            component.Predictor = 0;
        }

        var bits = new JpegBits(data.Span, scanStart);
        var coefficients = new int[64];
        var samples = new byte[64];
        var mcu = 0;
        var total = mcuColumns * mcuRows;

        for (var mcuY = 0; mcuY < mcuRows; mcuY++)
        {
            for (var mcuX = 0; mcuX < mcuColumns; mcuX++)
            {
                foreach (var component in components)
                {
                    for (var vertical = 0; vertical < component.V; vertical++)
                    {
                        for (var horizontal = 0; horizontal < component.H; horizontal++)
                        {
                            if (!DecodeBlock(ref bits, component, coefficients, samples))
                            {
                                return false;
                            }

                            var x = (mcuX * 8 * component.H) + (horizontal * 8);
                            var y = (mcuY * 8 * component.V) + (vertical * 8);

                            for (var row = 0; row < 8; row++)
                            {
                                samples
                                    .AsSpan(row * 8, 8)
                                    .CopyTo(component.Plane.AsSpan(((y + row) * component.Stride) + x));
                            }
                        }
                    }
                }

                mcu++;

                if (restartInterval > 0 && mcu % restartInterval == 0 && mcu < total)
                {
                    if (!bits.TryReadRestart())
                    {
                        return false;
                    }

                    foreach (var component in components)
                    {
                        component.Predictor = 0;
                    }
                }
            }
        }

        return BuildOutput(maxBytes, out pixels, out format);
    }

    private bool ReconstructProgressive()
    {
        var mcuWidth = 8 * maxH;
        var mcuHeight = 8 * maxV;
        var mcuColumns = (width + mcuWidth - 1) / mcuWidth;
        var mcuRows = (height + mcuHeight - 1) / mcuHeight;
        var samples = new byte[64];

        foreach (var component in components)
        {
            var quant = quantisation[component.Quantisation];

            if (quant is null)
            {
                return false;
            }

            component.Stride = mcuColumns * 8 * component.H;
            component.Plane = new byte[component.Stride * mcuRows * 8 * component.V];

            for (var blockY = 0; blockY < component.BlocksY; blockY++)
            {
                for (var blockX = 0; blockX < component.BlocksX; blockX++)
                {
                    var coefficients = component.Coefficients.AsSpan(((blockY * component.BlocksX) + blockX) * 64, 64);

                    for (var index = 0; index < 64; index++)
                    {
                        coefficients[index] *= quant[index];
                    }

                    Idct(coefficients, samples);

                    var x = blockX * 8;
                    var y = blockY * 8;

                    for (var row = 0; row < 8; row++)
                    {
                        samples.AsSpan(row * 8, 8).CopyTo(component.Plane.AsSpan(((y + row) * component.Stride) + x));
                    }
                }
            }
        }

        return true;
    }

    private bool DecodeProgressiveScan(int dataStart, out int nextOffset)
    {
        nextOffset = dataStart;
        var mcuWidth = 8 * maxH;
        var mcuHeight = 8 * maxV;
        var mcuColumns = (width + mcuWidth - 1) / mcuWidth;
        var mcuRows = (height + mcuHeight - 1) / mcuHeight;

        foreach (var component in components)
        {
            if (component.Coefficients.Length == 0)
            {
                component.BlocksX = mcuColumns * component.H;
                component.BlocksY = mcuRows * component.V;
                component.Coefficients = new int[component.BlocksX * component.BlocksY * 64];
            }
        }

        var bits = new JpegBits(data.Span, dataStart);
        eobRun = 0;
        var mcu = 0;

        if (scanComponents.Length == 1)
        {
            var component = scanComponents[0];
            var total = component.BlocksX * component.BlocksY;

            for (var blockY = 0; blockY < component.BlocksY; blockY++)
            {
                for (var blockX = 0; blockX < component.BlocksX; blockX++)
                {
                    if (!DecodeProgressiveBlock(ref bits, component, blockY, blockX))
                    {
                        return false;
                    }

                    mcu++;

                    if (restartInterval > 0 && mcu % restartInterval == 0 && mcu < total)
                    {
                        if (!bits.TryReadRestart())
                        {
                            return false;
                        }

                        component.Predictor = 0;
                        eobRun = 0;
                    }
                }
            }
        }
        else
        {
            var total = mcuColumns * mcuRows;

            for (var mcuY = 0; mcuY < mcuRows; mcuY++)
            {
                for (var mcuX = 0; mcuX < mcuColumns; mcuX++)
                {
                    foreach (var component in scanComponents)
                    {
                        for (var vertical = 0; vertical < component.V; vertical++)
                        {
                            for (var horizontal = 0; horizontal < component.H; horizontal++)
                            {
                                var blockY = (mcuY * component.V) + vertical;
                                var blockX = (mcuX * component.H) + horizontal;

                                if (!DecodeProgressiveBlock(ref bits, component, blockY, blockX))
                                {
                                    return false;
                                }
                            }
                        }
                    }

                    mcu++;

                    if (restartInterval > 0 && mcu % restartInterval == 0 && mcu < total)
                    {
                        if (!bits.TryReadRestart())
                        {
                            return false;
                        }

                        foreach (var component in scanComponents)
                        {
                            component.Predictor = 0;
                        }

                        eobRun = 0;
                    }
                }
            }
        }

        nextOffset = bits.Position;
        return true;
    }

    private bool DecodeProgressiveBlock(ref JpegBits bits, JpegComponent component, int blockY, int blockX)
    {
        var block = component.Coefficients.AsSpan(((blockY * component.BlocksX) + blockX) * 64, 64);

        if (scanStart == 0)
        {
            if (scanHigh == 0)
            {
                var dcTable = dcTables[component.DcTable];

                if (dcTable is null || !dcTable.TryRead(ref bits, out var size) || size > 11)
                {
                    return false;
                }

                var difference = 0;

                if (size > 0)
                {
                    if (!bits.ReadBits(size, out var value))
                    {
                        return false;
                    }

                    difference = Extend(value, size);
                }

                component.Predictor += difference;
                block[0] = component.Predictor * (1 << scanLow);
                return true;
            }

            // DC refinement: the bit is the next magnitude bit of the coefficient.
            if (!bits.TryReadBit(out var refinement))
            {
                return false;
            }

            if (refinement != 0)
            {
                block[0] |= 1 << scanLow;
            }

            return true;
        }

        var acTable = acTables[component.AcTable];

        if (acTable is null)
        {
            return false;
        }

        if (scanHigh == 0)
        {
            if (eobRun > 0)
            {
                eobRun--;
                return true;
            }

            var index = scanStart;

            while (index <= scanEnd)
            {
                if (!acTable.TryRead(ref bits, out var symbol))
                {
                    return false;
                }

                var run = symbol >> 4;
                var size = symbol & 15;

                if (size == 0)
                {
                    if (run == 15)
                    {
                        index += 16;
                        continue;
                    }

                    eobRun = 1 << run;

                    if (run > 0)
                    {
                        if (!bits.ReadBits(run, out var extra))
                        {
                            return false;
                        }

                        eobRun += extra;
                    }

                    // The run counts the current block; the rest are the ones that follow it.
                    eobRun--;
                    return true;
                }

                index += run;

                if (index > scanEnd || !bits.ReadBits(size, out var value))
                {
                    return false;
                }

                block[ZigZag[index]] = Extend(value, size) * (1 << scanLow);
                index++;
            }

            return true;
        }

        // AC refinement: the bits of the coefficients a previous scan stated, plus new ones at this level.
        if (eobRun > 0)
        {
            if (!RefineBand(ref bits, block, scanStart))
            {
                return false;
            }

            eobRun--;
            return true;
        }

        var position = scanStart;

        while (position <= scanEnd)
        {
            if (!acTable.TryRead(ref bits, out var symbol))
            {
                return false;
            }

            var run = symbol >> 4;
            var size = symbol & 15;

            if (size == 0)
            {
                if (run < 15)
                {
                    eobRun = 1 << run;

                    if (run > 0)
                    {
                        if (!bits.ReadBits(run, out var extra))
                        {
                            return false;
                        }

                        eobRun += extra;
                    }

                    if (!RefineBand(ref bits, block, position))
                    {
                        return false;
                    }

                    eobRun--;
                    return true;
                }

                // ZRL: sixteen zero coefficients, the nonzero ones refined on the way past.
                var remaining = 16;

                while (remaining > 0 && position <= scanEnd)
                {
                    if (block[ZigZag[position]] != 0)
                    {
                        if (!Refine(ref bits, block, position))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        remaining--;
                    }

                    position++;
                }

                continue;
            }

            if (size != 1)
            {
                return false;
            }

            // The sign bit precedes the correction bits of the coefficients the run passes over.
            if (!bits.TryReadBit(out var sign))
            {
                return false;
            }

            var newCoefficient = sign != 0 ? 1 << scanLow : -(1 << scanLow);

            // Skip the run's zero coefficients, but refine every nonzero one after them — a nonzero
            // coefficient does not consume the run, and its correction bit precedes the new coefficient.
            while (position <= scanEnd)
            {
                if (block[ZigZag[position]] != 0)
                {
                    if (!Refine(ref bits, block, position))
                    {
                        return false;
                    }
                }
                else
                {
                    if (run == 0)
                    {
                        break;
                    }

                    run--;
                }

                position++;
            }

            if (position > scanEnd)
            {
                return false;
            }

            block[ZigZag[position]] = newCoefficient;
            position++;
        }

        return true;
    }

    private bool RefineBand(ref JpegBits bits, Span<int> block, int start)
    {
        for (var index = start; index <= scanEnd; index++)
        {
            if (block[ZigZag[index]] != 0 && !Refine(ref bits, block, index))
            {
                return false;
            }
        }

        return true;
    }

    private bool Refine(ref JpegBits bits, Span<int> block, int index)
    {
        if (!bits.TryReadBit(out var bit))
        {
            return false;
        }

        if (bit != 0)
        {
            block[ZigZag[index]] += block[ZigZag[index]] > 0 ? 1 << scanLow : -(1 << scanLow);
        }

        return true;
    }

    private bool DecodeBlock(ref JpegBits bits, JpegComponent component, int[] coefficients, byte[] samples)
    {
        var dcTable = dcTables[component.DcTable];
        var acTable = acTables[component.AcTable];
        var quant = quantisation[component.Quantisation];

        if (dcTable is null || acTable is null || quant is null)
        {
            return false;
        }

        Array.Clear(coefficients);

        if (!dcTable.TryRead(ref bits, out var size) || size > 11)
        {
            return false;
        }

        var difference = 0;

        if (size > 0)
        {
            if (!bits.ReadBits(size, out var value))
            {
                return false;
            }

            difference = Extend(value, size);
        }

        component.Predictor += difference;
        coefficients[0] = component.Predictor * quant[0];

        var index = 1;

        while (index < 64)
        {
            if (!acTable.TryRead(ref bits, out var symbol))
            {
                return false;
            }

            var run = symbol >> 4;
            var coefficientWidth = symbol & 15;

            if (coefficientWidth == 0)
            {
                if (run == 15)
                {
                    index += 16;
                    continue;
                }

                break;
            }

            index += run;

            if (index >= 64 || !bits.ReadBits(coefficientWidth, out var value))
            {
                return false;
            }

            coefficients[ZigZag[index]] = Extend(value, coefficientWidth) * quant[ZigZag[index]];
            index++;
        }

        Idct(coefficients, samples);
        return true;
    }

    private static int Extend(int value, int size) =>
        value < (1 << (size - 1)) ? value - ((1 << size) - 1) : value;

    private static void Idct(ReadOnlySpan<int> coefficients, byte[] samples)
    {
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0.0;

                for (var v = 0; v < 8; v++)
                {
                    for (var u = 0; u < 8; u++)
                    {
                        sum += Cosines[(x * 8) + u] * Cosines[(y * 8) + v] * coefficients[(v * 8) + u];
                    }
                }

                samples[(y * 8) + x] = (byte)Math.Clamp((int)Math.Round((sum / 4.0) + 128.0), 0, 255);
            }
        }
    }

    private bool BuildOutput(int maxBytes, out byte[]? pixels, out string? format)
    {
        pixels = null;
        format = null;
        var count = components.Length;
        format = count switch
        {
            1 => "Gray8",
            3 => "Rgb24",
            4 => "Cmyk32",
            _ => null,
        };

        if (format is null)
        {
            return false;
        }

        var length = (long)width * height * count;

        if (length > maxBytes)
        {
            return true;
        }

        var output = new byte[length];
        var target = 0;
        var samples = new int[count];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var component = 0; component < count; component++)
                {
                    var source = components[component];
                    samples[component] = Sample(
                        source.Plane,
                        source.Stride,
                        source.Stride == 0 ? 0 : source.Plane.Length / source.Stride,
                        x,
                        y,
                        source.H,
                        source.V,
                        maxH,
                        maxV);
                }

                if (count == 1)
                {
                    output[target++] = (byte)samples[0];
                    continue;
                }

                if (count == 3)
                {
                    if (adobeTransform == 0)
                    {
                        output[target++] = (byte)samples[0];
                        output[target++] = (byte)samples[1];
                        output[target++] = (byte)samples[2];
                        continue;
                    }

                    var cb = samples[1] - 128;
                    var cr = samples[2] - 128;
                    output[target++] = Clamp(samples[0] + (1.402 * cr));
                    output[target++] = Clamp(samples[0] - (0.344136 * cb) - (0.714136 * cr));
                    output[target++] = Clamp(samples[0] + (1.772 * cb));
                    continue;
                }

                if (adobeTransform == 2)
                {
                    var cb = samples[1] - 128;
                    var cr = samples[2] - 128;
                    output[target++] = Clamp(255.0 - (samples[0] + (1.402 * cr)));
                    output[target++] = Clamp(255.0 - (samples[0] - (0.344136 * cb) - (0.714136 * cr)));
                    output[target++] = Clamp(255.0 - (samples[0] + (1.772 * cb)));
                    output[target++] = (byte)samples[3];
                    continue;
                }

                output[target++] = (byte)samples[0];
                output[target++] = (byte)samples[1];
                output[target++] = (byte)samples[2];
                output[target++] = (byte)samples[3];
            }
        }

        pixels = output;
        return true;
    }

    /// <summary>
    /// Reads one sample, upsampling a subsampled component with the 3:1 filter libjpeg calls fancy: an
    /// even output position takes three parts of its sample and one of the neighbour before it, an odd one
    /// three parts and one of the neighbour after it, rounded as libjpeg rounds.
    /// </summary>
    private static int Sample(
        byte[] plane,
        int stride,
        int rows,
        int x,
        int y,
        int horizontal,
        int vertical,
        int maxH,
        int maxV)
    {
        if (horizontal == maxH && vertical == maxV)
        {
            return plane[(y * stride) + x];
        }

        var sx = (x * horizontal) / maxH;
        var sy = (y * vertical) / maxV;
        var fancyX = horizontal * 2 == maxH;
        var fancyY = vertical * 2 == maxV;

        if (!fancyX && !fancyY)
        {
            return plane[(sy * stride) + sx];
        }

        var left = Math.Max(sx - 1, 0);
        var right = Math.Min(sx + 1, stride - 1);

        var centre = Column(sx);
        var before = Column(left);
        var after = Column(right);

        return x % 2 == 0
            ? ((3 * centre) + before + 1) >> 2
            : ((3 * centre) + after + 2) >> 2;

        int Column(int column)
        {
            if (!fancyY)
            {
                return plane[(sy * stride) + column];
            }

            return y % 2 == 0
                ? ((3 * plane[(sy * stride) + column]) + plane[(Math.Max(sy - 1, 0) * stride) + column] + 1) >> 2
                : ((3 * plane[(sy * stride) + column]) + plane[(Math.Min(sy + 1, rows - 1) * stride) + column] + 2) >> 2;
        }
    }

    private static byte Clamp(double value) =>
        (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private sealed class JpegHuffman
    {
        private readonly Dictionary<int, int> table = [];

        public static JpegHuffman Create(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols)
        {
            var huffman = new JpegHuffman();
            var code = 0;
            var index = 0;

            for (var length = 1; length <= 16; length++)
            {
                for (var count = 0; count < counts[length - 1]; count++)
                {
                    huffman.table[(length << 16) | code] = symbols[index++];
                    code++;
                }

                code <<= 1;
            }

            return huffman;
        }

        public bool TryRead(ref JpegBits bits, out int symbol)
        {
            var code = 0;

            for (var length = 1; length <= 16; length++)
            {
                if (!bits.TryReadBit(out var bit))
                {
                    symbol = 0;
                    return false;
                }

                code = (code << 1) | bit;

                if (table.TryGetValue((length << 16) | code, out symbol))
                {
                    return true;
                }
            }

            symbol = 0;
            return false;
        }
    }

    private ref struct JpegBits
    {
        private readonly ReadOnlySpan<byte> data;
        private int offset;
        private int bit;
        private int current;

        public JpegBits(ReadOnlySpan<byte> data, int offset)
        {
            this.data = data;
            this.offset = offset;
            bit = 8;
            current = 0;
        }

        public bool TryReadBit(out int value)
        {
            if (bit == 8)
            {
                if (!TryLoad())
                {
                    value = 0;
                    return false;
                }

                bit = 0;
            }

            value = (current >> (7 - bit)) & 1;
            bit++;
            return true;
        }

        public bool ReadBits(int count, out int value)
        {
            value = 0;

            for (var index = 0; index < count; index++)
            {
                if (!TryReadBit(out var next))
                {
                    return false;
                }

                value = (value << 1) | next;
            }

            return true;
        }

        public int Position => offset;

        public bool TryReadRestart()
        {
            bit = 8;

            if (offset + 1 >= data.Length || data[offset] != 0xFF)
            {
                return false;
            }

            var marker = data[offset + 1];

            if (marker is < 0xD0 or > 0xD7)
            {
                return false;
            }

            offset += 2;
            return true;
        }

        private bool TryLoad()
        {
            if (offset >= data.Length)
            {
                return false;
            }

            var value = data[offset];

            if (value == 0xFF)
            {
                if (offset + 1 >= data.Length)
                {
                    return false;
                }

                if (data[offset + 1] != 0x00)
                {
                    // A marker ends the entropy-coded data; a restart is read by TryReadRestart.
                    return false;
                }

                offset += 2;
                current = 0xFF;
                return true;
            }

            offset++;
            current = value;
            return true;
        }
    }
}
