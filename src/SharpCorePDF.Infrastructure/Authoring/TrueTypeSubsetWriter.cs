using System.Buffers.Binary;
using System.Text;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Writes a TrueType subset that keeps the glyph numbering of its source and leaves out the outlines of
/// the glyphs the document never draws.
/// </summary>
/// <remarks>
/// Keeping the numbering is what makes this writer small: composite glyphs are copied byte for byte,
/// the <c>cmap</c>, <c>hmtx</c> and <c>maxp</c> tables stay valid, and a content stream can name a glyph
/// with the number the source font gave it. Every table a PDF renderer needs is recalculated: the
/// <c>glyf</c> and <c>loca</c> tables are rebuilt, the <c>head</c> table states the new offset format,
/// and the checksums of the table directory and of the whole program are recomputed.
/// </remarks>
internal static class TrueTypeSubsetWriter
{
    /// <summary>The optional tables that are copied verbatim, in the order they are looked for.</summary>
    private static readonly string[] CopiedTables = ["OS/2", "post", "name", "cvt ", "fpgm", "prep", "gasp"];

    /// <summary>Builds a subset font program.</summary>
    /// <param name="font">The source font.</param>
    /// <param name="usedGlyphs">The glyph numbers the document draws.</param>
    /// <returns>The bytes of the subset program.</returns>
    public static byte[] Build(TrueTypeFont font, IReadOnlySet<ushort> usedGlyphs)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(usedGlyphs);

        var glyphs = new HashSet<ushort> { 0 };
        var pending = new Stack<ushort>(usedGlyphs);

        while (pending.Count > 0)
        {
            var glyph = pending.Pop();

            if (glyph >= font.GlyphCount)
            {
                throw new ArgumentException(
                    $"The font references glyph {glyph}, but it defines {font.GlyphCount} glyphs.");
            }

            if (!glyphs.Add(glyph))
            {
                continue;
            }

            var components = new HashSet<ushort>();
            font.CollectComponents(glyph, components);

            foreach (var component in components)
            {
                pending.Push(component);
            }
        }

        var glyf = new MemoryStream();
        var offsets = new int[font.GlyphCount + 1];

        for (var glyph = 0; glyph < font.GlyphCount; glyph++)
        {
            offsets[glyph] = (int)glyf.Length;

            if (!glyphs.Contains((ushort)glyph))
            {
                continue;
            }

            glyf.Write(font.GetGlyph((ushort)glyph));

            while (glyf.Length % 4 != 0)
            {
                glyf.WriteByte(0);
            }
        }

        offsets[font.GlyphCount] = (int)glyf.Length;

        // A short offset is stored divided by two, so the table may hold at most 0x1FFFE bytes.
        var shortOffsets = glyf.Length <= 0x1FFFE;
        var loca = new byte[(font.GlyphCount + 1) * (shortOffsets ? 2 : 4)];

        for (var index = 0; index < offsets.Length; index++)
        {
            if (shortOffsets)
            {
                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(index * 2), (ushort)(offsets[index] / 2));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(index * 4), (uint)offsets[index]);
            }
        }

        var head = font.GetTable("head").ToArray();
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), shortOffsets ? (short)0 : (short)1);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = font.GetTable("cmap").ToArray(),
            ["glyf"] = glyf.ToArray(),
            ["head"] = head,
            ["hhea"] = font.GetTable("hhea").ToArray(),
            ["hmtx"] = font.GetTable("hmtx").ToArray(),
            ["loca"] = loca,
            ["maxp"] = font.GetTable("maxp").ToArray(),
        };

        foreach (var tag in CopiedTables.Where(font.HasTable))
        {
            tables[tag] = font.GetTable(tag).ToArray();
        }

        return Assemble(tables);
    }

    /// <summary>Assembles a table set into an sfnt font program and fixes its checksums.</summary>
    /// <param name="tables">The tables, keyed by tag.</param>
    /// <returns>The bytes of the program.</returns>
    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        var count = tables.Count;
        var entrySelector = (int)Math.Floor(Math.Log2(count));
        var searchRange = (int)Math.Pow(2, entrySelector) * 16;
        var rangeShift = (count * 16) - searchRange;
        var size = 12 + (count * 16) + tables.Values.Sum(table => Padded(table.Length));
        var font = new byte[size];

        BinaryPrimitives.WriteUInt32BigEndian(font, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(10), (ushort)rangeShift);

        var offset = 12 + (count * 16);
        var headOffset = 0;
        var index = 0;

        foreach (var table in tables)
        {
            var record = 12 + (index * 16);

            Encoding.ASCII.GetBytes(table.Key, 0, 4, font, record);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 4), Checksum(table.Value));
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(record + 12), (uint)table.Value.Length);

            table.Value.CopyTo(font, offset);

            if (table.Key == "head")
            {
                headOffset = offset;
            }

            offset += Padded(table.Value.Length);
            index++;
        }

        var adjustment = unchecked(0xB1B0AFBAu - Checksum(font));
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(headOffset + 8), adjustment);

        return font;
    }

    /// <summary>Computes the checksum of a table or of a whole program.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The sum of the bytes read as big-endian 32 bit words, padded with zeros.</returns>
    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        var sum = 0u;

        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var word = 0u;

            for (var index = 0; index < 4; index++)
            {
                word <<= 8;
                word |= offset + index < data.Length ? data[offset + index] : 0u;
            }

            sum = unchecked(sum + word);
        }

        return sum;
    }

    /// <summary>Rounds a length up to the next multiple of four.</summary>
    /// <param name="length">The length.</param>
    /// <returns>The padded length.</returns>
    private static int Padded(int length) => (length + 3) & ~3;
}
