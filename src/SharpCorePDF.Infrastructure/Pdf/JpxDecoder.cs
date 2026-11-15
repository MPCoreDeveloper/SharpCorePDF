namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Reads the container and the codestream header of a JPEG 2000 image: a JP2 file or a raw J2K
/// codestream.
/// </summary>
/// <remarks>
/// <para>
/// The decoder is staged, per ADR-005. This half states the geometry, the components and the transform the
/// codestream uses — the values the coefficient decoder needs — and refuses a container or a main header
/// that does not carry them. The packets, the EBCOT bit-planes and the inverse wavelet transform are the
/// next half.
/// </para>
/// </remarks>
internal static class JpxDecoder
{
    private const int SignatureLength = 12;

    /// <summary>The geometry, the components and the transform a JPEG 2000 main header states.</summary>
    internal sealed class Header
    {
        public int Width { get; init; }

        public int Height { get; init; }

        public int TileWidth { get; init; }

        public int TileHeight { get; init; }

        public Component[] Components { get; init; } = [];

        public int ProgressionOrder { get; init; }

        public int Layers { get; init; }

        public int ColourTransform { get; init; }

        public int DecompositionLevels { get; init; }

        public bool IsReversible { get; init; }
    }

    /// <summary>One component's depth, sign and subsampling, as SIZ states them.</summary>
    internal sealed class Component
    {
        public int BitDepth { get; init; }

        public bool Signed { get; init; }

        public int HorizontalSubsampling { get; init; }

        public int VerticalSubsampling { get; init; }
    }

    /// <summary>Finds the codestream in a JP2 box or a raw J2K file and reads its main header.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> data, out Header? header)
    {
        header = null;
        var offset = FindCodestream(data);

        return offset >= 0 && ParseMainHeader(data[offset..], out header);
    }

    private static int FindCodestream(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x4F)
        {
            return 0;
        }

        if (data.Length < SignatureLength
            || data[0] != 0
            || data[1] != 0
            || data[2] != 0
            || data[3] != SignatureLength
            || data[4] != (byte)'j'
            || data[5] != (byte)'P'
            || data[6] != 0x20
            || data[7] != 0x20
            || data[8] != 0x0D
            || data[9] != 0x0A
            || data[10] != 0x87
            || data[11] != 0x0A)
        {
            return -1;
        }

        var offset = SignatureLength;

        while (offset + 8 <= data.Length)
        {
            var length = ReadUInt32(data, offset);
            var type = data.Slice(offset + 4, 4);
            var payload = offset + 8;

            if (length == 1)
            {
                if (offset + 16 > data.Length)
                {
                    return -1;
                }

                var extended = ReadUInt64(data, offset + 8);

                if (extended < 16 || extended > (ulong)(data.Length - offset))
                {
                    return -1;
                }

                length = (uint)extended;
                payload = offset + 16;
            }
            else if (length == 0)
            {
                length = (uint)(data.Length - offset);
            }

            if (length < 8 || length > data.Length - offset)
            {
                return -1;
            }

            if (type[0] == (byte)'j' && type[1] == (byte)'p' && type[2] == (byte)'2' && type[3] == (byte)'c')
            {
                return payload;
            }

            offset += (int)length;
        }

        return -1;
    }

    private static bool ParseMainHeader(ReadOnlySpan<byte> data, out Header? header)
    {
        header = null;

        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0x4F)
        {
            return false;
        }

        var offset = 2;
        var size = default(Size);
        var coding = default(Coding);
        var haveSize = false;
        var haveCoding = false;

        while (offset + 1 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = data[offset + 1];
            offset += 2;

            if (marker is 0x90 or 0x93 or 0xD9)
            {
                break;
            }

            if (marker == 0x4F || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                return false;
            }

            var length = ReadUInt16(data, offset);

            if (length < 2 || offset + length > data.Length)
            {
                return false;
            }

            var segment = data.Slice(offset + 2, length - 2);

            if (marker == 0x51)
            {
                if (!ParseSize(segment, out size))
                {
                    return false;
                }

                haveSize = true;
            }
            else if (marker == 0x52)
            {
                if (!ParseCoding(segment, out coding))
                {
                    return false;
                }

                haveCoding = true;
            }

            offset += length;
        }

        if (!haveSize || !haveCoding)
        {
            return false;
        }

        header = new Header
        {
            Width = size.Width,
            Height = size.Height,
            TileWidth = size.TileWidth,
            TileHeight = size.TileHeight,
            Components = size.Components,
            ProgressionOrder = coding.ProgressionOrder,
            Layers = coding.Layers,
            ColourTransform = coding.ColourTransform,
            DecompositionLevels = coding.DecompositionLevels,
            IsReversible = coding.IsReversible,
        };

        return true;
    }

    private static bool ParseSize(ReadOnlySpan<byte> segment, out Size size)
    {
        size = default;

        if (segment.Length < 36)
        {
            return false;
        }

        var xsiz = ReadUInt32(segment, 2);
        var ysiz = ReadUInt32(segment, 6);
        var xosiz = ReadUInt32(segment, 10);
        var yosiz = ReadUInt32(segment, 14);
        var xtsiz = ReadUInt32(segment, 18);
        var ytsiz = ReadUInt32(segment, 22);
        var csiz = ReadUInt16(segment, 34);

        if (xsiz <= xosiz
            || ysiz <= yosiz
            || xtsiz == 0
            || ytsiz == 0
            || csiz is < 1 or > 16384
            || segment.Length < 36 + (csiz * 3))
        {
            return false;
        }

        var components = new Component[csiz];

        for (var index = 0; index < csiz; index++)
        {
            var depth = segment[36 + (index * 3)];
            var horizontal = segment[37 + (index * 3)];
            var vertical = segment[38 + (index * 3)];

            if ((depth & 0x7F) + 1 > 38 || horizontal == 0 || vertical == 0)
            {
                return false;
            }

            components[index] = new Component
            {
                BitDepth = (depth & 0x7F) + 1,
                Signed = (depth & 0x80) != 0,
                HorizontalSubsampling = horizontal,
                VerticalSubsampling = vertical,
            };
        }

        size = new Size
        {
            Width = (int)(xsiz - xosiz),
            Height = (int)(ysiz - yosiz),
            TileWidth = (int)xtsiz,
            TileHeight = (int)ytsiz,
            Components = components,
        };

        return true;
    }

    private static bool ParseCoding(ReadOnlySpan<byte> segment, out Coding coding)
    {
        coding = default;

        if (segment.Length < 10)
        {
            return false;
        }

        var transform = segment[9];

        coding = new Coding
        {
            ProgressionOrder = segment[1],
            Layers = ReadUInt16(segment, 2),
            ColourTransform = segment[4],
            DecompositionLevels = segment[5],
            IsReversible = transform == 1,
        };

        return coding.Layers > 0
            && coding.ProgressionOrder <= 4
            && coding.ColourTransform <= 1
            && coding.DecompositionLevels <= 32
            && segment[6] is >= 2 and <= 10
            && segment[7] is >= 2 and <= 10
            && segment[6] + segment[7] <= 12
            && transform <= 1;
    }

    private static int ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 24)
        | ((uint)data[offset + 1] << 16)
        | ((uint)data[offset + 2] << 8)
        | data[offset + 3];

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset) =>
        ((ulong)ReadUInt32(data, offset) << 32) | ReadUInt32(data, offset + 4);

    private struct Size
    {
        public int Width { get; set; }

        public int Height { get; set; }

        public int TileWidth { get; set; }

        public int TileHeight { get; set; }

        public Component[] Components { get; set; }
    }

    private struct Coding
    {
        public int ProgressionOrder { get; set; }

        public int Layers { get; set; }

        public int ColourTransform { get; set; }

        public int DecompositionLevels { get; set; }

        public bool IsReversible { get; set; }
    }
}

