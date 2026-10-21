using System.Buffers.Binary;
using System.Text;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// A TrueType font program as far as embedding it needs: its glyph map, its metrics and its glyph
/// outlines, together with the writer that leaves the outlines of unused glyphs out.
/// </summary>
/// <remarks>
/// <para>
/// Glyph numbers are preserved. A subset that renumbers its glyphs has to rewrite every composite glyph
/// that refers to one, and every code in every content stream. Keeping the original numbers lets a
/// content stream carry the glyph id directly and lets a composite glyph be copied byte for byte. The
/// unused outlines — usually the overwhelming majority of the program — are still left out.
/// </para>
/// <para>
/// Only the tables a PDF renderer needs are written: <c>head</c>, <c>hhea</c>, <c>maxp</c>,
/// <c>hmtx</c>, <c>cmap</c>, <c>glyf</c> and <c>loca</c>, plus the optional hinting, metrics and naming
/// tables when the source states them. Layout tables belong to a text shaper, not to a PDF page.
/// </para>
/// </remarks>
internal sealed class TrueTypeFont
{
    private const uint TrueTypeVersion = 0x00010000;
    private const uint AppleTrueTypeVersion = 0x74727565;
    private const uint OpenTypeCffVersion = 0x4F54544F;

    /// <summary>The tables a PDF renderer needs from a TrueType program.</summary>
    private static readonly string[] RequiredTables = ["head", "hhea", "maxp", "hmtx", "cmap", "loca", "glyf"];

    private readonly Dictionary<string, TableRecord> tables;
    private readonly int headOffset;
    private readonly int hheaOffset;
    private readonly int hmtxOffset;
    private readonly int locaOffset;
    private readonly int glyfOffset;
    private readonly bool longLocaOffsets;
    private readonly int cmapOffset;

    private TrueTypeFont(
        byte[] data,
        Dictionary<string, TableRecord> directory,
        int unitsPerEm,
        int glyphCount,
        int numberOfHMetrics)
    {
        Data = data;
        tables = directory;
        UnitsPerEm = unitsPerEm;
        GlyphCount = glyphCount;
        NumberOfHMetrics = numberOfHMetrics;
        Name = ReadName(data, directory);

        headOffset = directory["head"].Offset;
        hheaOffset = directory["hhea"].Offset;
        hmtxOffset = directory["hmtx"].Offset;
        locaOffset = directory["loca"].Offset;
        glyfOffset = directory["glyf"].Offset;
        longLocaOffsets = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(headOffset + 50)) != 0;
        cmapOffset = SelectCmap(data, directory["cmap"]);
    }

    /// <summary>Gets the bytes of the whole font program.</summary>
    public byte[] Data { get; }

    /// <summary>Gets the PostScript name the font states, or a fallback.</summary>
    public string Name { get; }

    /// <summary>Gets the number of font units in one em.</summary>
    public int UnitsPerEm { get; }

    /// <summary>Gets the number of glyphs the font defines.</summary>
    public int GlyphCount { get; }

    /// <summary>Gets the number of long horizontal metrics the font states.</summary>
    public int NumberOfHMetrics { get; }

    /// <summary>Gets the ascender in font units.</summary>
    public int Ascender => BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(hheaOffset + 4));

    /// <summary>Gets the descender in font units, which is negative.</summary>
    public int Descender => BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(hheaOffset + 6));

    /// <summary>Gets the italic angle of the face in degrees.</summary>
    public double ItalicAngle =>
        tables.TryGetValue("post", out var post) && post.Length >= 8
            ? BinaryPrimitives.ReadInt32BigEndian(Data.AsSpan(post.Offset + 4)) / 65536d
            : 0d;

    /// <summary>Gets the cap height in font units, falling back on the ascender.</summary>
    public int CapHeight
    {
        get
        {
            if (!tables.TryGetValue("OS/2", out var os2) || os2.Length < 90)
            {
                return (int)(Ascender * 0.7d);
            }

            var version = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(os2.Offset));

            return version >= 2
                ? BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(os2.Offset + 88))
                : (int)(Ascender * 0.7d);
        }
    }

    /// <summary>Gets the bounding box of the face in font units.</summary>
    public (int Left, int Bottom, int Right, int Top) BoundingBox =>
        (
            BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(headOffset + 36)),
            BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(headOffset + 38)),
            BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(headOffset + 40)),
            BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(headOffset + 42)));

    /// <summary>Parses a TrueType font program.</summary>
    /// <param name="data">The bytes of the program.</param>
    /// <returns>The parsed font.</returns>
    /// <exception cref="ArgumentException">The bytes are not a TrueType font, or a required table is missing.</exception>
    /// <exception cref="NotSupportedException">The program is an OpenType font with PostScript outlines.</exception>
    public static TrueTypeFont Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 12)
        {
            throw new ArgumentException("The font program is too short to hold a table directory.", nameof(data));
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(data);

        if (version == OpenTypeCffVersion)
        {
            throw new NotSupportedException(
                "The font is an OpenType font with PostScript (CFF) outlines; SharpCorePDF embeds TrueType (glyf) outlines only.");
        }

        if (version != TrueTypeVersion && version != AppleTrueTypeVersion)
        {
            throw new ArgumentException("The font program is not a TrueType font: the sfnt version is not 1.0.", nameof(data));
        }

        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        var directory = new Dictionary<string, TableRecord>(tableCount, StringComparer.Ordinal);

        for (var index = 0; index < tableCount; index++)
        {
            var record = 12 + (index * 16);

            if (record + 16 > data.Length)
            {
                throw new ArgumentException("The font program has a truncated table directory.", nameof(data));
            }

            var tag = Encoding.ASCII.GetString(data, record, 4);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));

            if (offset < 0 || length < 0 || (long)offset + length > data.Length)
            {
                throw new ArgumentException($"The font program states a table '{tag}' outside the file.", nameof(data));
            }

            directory[tag] = new TableRecord(offset, length);
        }

        var missing = RequiredTables.FirstOrDefault(tag => !directory.ContainsKey(tag));

        if (missing is not null)
        {
            throw new NotSupportedException(
                $"The font program has no '{missing}' table; SharpCorePDF embeds TrueType fonts with glyf outlines only.");
        }

        if (directory["head"].Length < 54
            || directory["hhea"].Length < 36
            || directory["maxp"].Length < 6)
        {
            throw new ArgumentException("The font program states a table shorter than its own header requires.", nameof(data));
        }

        var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(directory["head"].Offset + 18));
        var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(directory["maxp"].Offset + 4));
        var numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(directory["hhea"].Offset + 34));

        if (unitsPerEm == 0 || glyphCount == 0 || numberOfHMetrics == 0 || numberOfHMetrics > glyphCount)
        {
            throw new ArgumentException("The font program states no em size, glyphs or horizontal metrics.", nameof(data));
        }

        var longLoca = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(directory["head"].Offset + 50)) != 0;
        var minimumLoca = (glyphCount + 1) * (longLoca ? 4 : 2);
        var minimumHmtx = (numberOfHMetrics * 4) + ((glyphCount - numberOfHMetrics) * 2);

        if (directory["cmap"].Length < 4
            || directory["loca"].Length < minimumLoca
            || directory["hmtx"].Length < minimumHmtx)
        {
            throw new ArgumentException("The font program states a table shorter than its own header requires.", nameof(data));
        }

        return new TrueTypeFont(data, directory, unitsPerEm, glyphCount, numberOfHMetrics);
    }

    /// <summary>Gets the bytes of one table, or an empty span when the font states none.</summary>
    /// <param name="tag">The four character table tag.</param>
    /// <returns>The bytes of the table.</returns>
    internal ReadOnlySpan<byte> GetTable(string tag) =>
        tables.TryGetValue(tag, out var table) ? Data.AsSpan(table.Offset, table.Length) : default;

    /// <summary>Determines whether the font states a table.</summary>
    /// <param name="tag">The four character table tag.</param>
    /// <returns><see langword="true"/> when the table exists.</returns>
    internal bool HasTable(string tag) => tables.ContainsKey(tag);

    /// <summary>Builds a font program that contains the outlines of the used glyphs only.</summary>
    /// <param name="usedGlyphs">The glyph numbers the document draws.</param>
    /// <returns>The bytes of the subset program.</returns>
    internal byte[] BuildSubset(IReadOnlySet<ushort> usedGlyphs) => TrueTypeSubsetWriter.Build(this, usedGlyphs);

    /// <summary>Finds the glyph a code point is mapped to.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <param name="glyph">The glyph number when the font maps the code point.</param>
    /// <returns><see langword="true"/> when the font maps the code point to a real glyph.</returns>
    public bool TryGetGlyph(int codePoint, out ushort glyph) => TryGetCmapGlyph(codePoint, out glyph) && glyph != 0;

    /// <summary>Gets the advance width of a glyph in font units.</summary>
    /// <param name="glyph">The glyph number.</param>
    /// <returns>The advance width.</returns>
    public int GetAdvanceWidth(ushort glyph)
    {
        var metric = Math.Min(glyph, NumberOfHMetrics - 1);

        return BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(hmtxOffset + (metric * 4)));
    }

    /// <summary>Gets the outline bytes of one glyph.</summary>
    /// <param name="glyph">The glyph number.</param>
    /// <returns>The bytes of the glyph, empty when the glyph has no outline.</returns>
    public ReadOnlySpan<byte> GetGlyph(ushort glyph)
    {
        var start = LocaOffset(glyph);
        var end = LocaOffset((ushort)(glyph + 1));

        return end > start ? Data.AsSpan(glyfOffset + start, end - start) : [];
    }

    /// <summary>Enumerates the glyphs a composite glyph is built from.</summary>
    /// <param name="glyph">The glyph number.</param>
    /// <param name="components">The receiver of the component glyph numbers.</param>
    public void CollectComponents(ushort glyph, ISet<ushort> components)
    {
        var outline = GetGlyph(glyph);

        if (outline.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(outline) >= 0)
        {
            return;
        }

        var offset = 10;

        while (offset + 4 <= outline.Length)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(outline[offset..]);
            var component = BinaryPrimitives.ReadUInt16BigEndian(outline[(offset + 2)..]);

            components.Add(component);
            offset += 4;
            offset += (flags & 0x0001) != 0 ? 4 : 2;

            if ((flags & 0x0008) != 0)
            {
                offset += 2;
            }
            else if ((flags & 0x0040) != 0)
            {
                offset += 4;
            }
            else if ((flags & 0x0080) != 0)
            {
                offset += 8;
            }

            if ((flags & 0x0020) == 0)
            {
                break;
            }
        }
    }

    /// <summary>Gets the offset of a glyph in the <c>glyf</c> table.</summary>
    /// <param name="glyph">The glyph number.</param>
    /// <returns>The offset from the start of the table.</returns>
    private int LocaOffset(ushort glyph) =>
        longLocaOffsets
            ? (int)BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(locaOffset + (glyph * 4)))
            : BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(locaOffset + (glyph * 2))) * 2;

    /// <summary>Chooses the character map subtable the font is read through.</summary>
    /// <param name="data">The bytes of the program.</param>
    /// <param name="cmap">The <c>cmap</c> table.</param>
    /// <returns>The offset of the chosen subtable.</returns>
    /// <exception cref="NotSupportedException">The font states no character map this library reads.</exception>
    private static int SelectCmap(byte[] data, TableRecord cmap)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cmap.Offset + 2));
        var bestScore = -1;
        var bestOffset = -1;

        for (var index = 0; index < count; index++)
        {
            var record = cmap.Offset + 4 + (index * 8);

            if (record + 8 > data.Length)
            {
                break;
            }

            var platform = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record));
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 2));
            var offset = cmap.Offset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 4));

            if (offset + 4 > data.Length)
            {
                continue;
            }

            var format = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
            var score = format switch
            {
                12 => 30,
                4 => 20,
                6 => 10,
                0 => 0,
                _ => -1,
            };

            if (score < 0)
            {
                continue;
            }

            score += platform switch
            {
                3 when encoding == 10 => 6,
                3 when encoding == 1 => 5,
                0 => 4,
                3 => 3,
                1 => 2,
                _ => 0,
            };

            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        return bestOffset >= 0
            ? bestOffset
            : throw new NotSupportedException(
                "The font states no Unicode character map (cmap format 4 or 12), so its text cannot be mapped to glyphs.");
    }

    /// <summary>Reads the glyph a code point maps to, through the chosen character map.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <param name="glyph">The glyph number.</param>
    /// <returns><see langword="true"/> when the map states a glyph for the code point.</returns>
    private bool TryGetCmapGlyph(int codePoint, out ushort glyph)
    {
        glyph = 0;

        return BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(cmapOffset)) switch
        {
            0 => TryGetFormat0Glyph(codePoint, out glyph),
            4 => TryGetFormat4Glyph(codePoint, out glyph),
            6 => TryGetFormat6Glyph(codePoint, out glyph),
            12 => TryGetFormat12Glyph(codePoint, out glyph),
            _ => false,
        };
    }

    /// <summary>Reads a glyph from a byte encoding table.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <param name="glyph">The glyph number.</param>
    /// <returns><see langword="true"/> when the table maps the code point.</returns>
    private bool TryGetFormat0Glyph(int codePoint, out ushort glyph)
    {
        glyph = 0;

        if (codePoint is < 0 or > 255 || cmapOffset + 6 + codePoint >= Data.Length)
        {
            return false;
        }

        glyph = Data[cmapOffset + 6 + codePoint];
        return glyph != 0;
    }

    /// <summary>Reads a glyph from a segmented mapping table, the one Windows fonts almost always state.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <param name="glyph">The glyph number.</param>
    /// <returns><see langword="true"/> when the table maps the code point.</returns>
    private bool TryGetFormat4Glyph(int codePoint, out ushort glyph)
    {
        glyph = 0;

        if (codePoint is < 0 or > 0xFFFF)
        {
            return false;
        }

        var segmentCount = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(cmapOffset + 6)) / 2;
        var endCodes = cmapOffset + 14;
        var startCodes = endCodes + (segmentCount * 2) + 2;
        var deltas = startCodes + (segmentCount * 2);
        var rangeOffsets = deltas + (segmentCount * 2);

        for (var segment = 0; segment < segmentCount; segment++)
        {
            var end = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(endCodes + (segment * 2)));

            if (codePoint > end)
            {
                continue;
            }

            var start = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(startCodes + (segment * 2)));

            if (codePoint < start)
            {
                return false;
            }

            var delta = BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(deltas + (segment * 2)));
            var rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(rangeOffsets + (segment * 2)));

            if (rangeOffset == 0)
            {
                glyph = (ushort)((codePoint + delta) & 0xFFFF);
                return glyph != 0;
            }

            var address = rangeOffsets + (segment * 2) + rangeOffset + ((codePoint - start) * 2);

            if (address + 2 > Data.Length)
            {
                return false;
            }

            glyph = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(address));

            if (glyph != 0)
            {
                glyph = (ushort)((glyph + delta) & 0xFFFF);
            }

            return glyph != 0;
        }

        return false;
    }

    /// <summary>Reads a glyph from a trimmed mapping table.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <param name="glyph">The glyph number.</param>
    /// <returns><see langword="true"/> when the table maps the code point.</returns>
    private bool TryGetFormat6Glyph(int codePoint, out ushort glyph)
    {
        glyph = 0;

        var first = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(cmapOffset + 6));
        var count = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(cmapOffset + 8));

        if (codePoint < first || codePoint >= first + count)
        {
            return false;
        }

        glyph = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(cmapOffset + 10 + ((codePoint - first) * 2)));
        return glyph != 0;
    }

    /// <summary>Reads a glyph from a segmented coverage table, which covers every Unicode plane.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <param name="glyph">The glyph number.</param>
    /// <returns><see langword="true"/> when the table maps the code point.</returns>
    private bool TryGetFormat12Glyph(int codePoint, out ushort glyph)
    {
        glyph = 0;

        var groupCount = (int)BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(cmapOffset + 12));
        var groups = cmapOffset + 16;

        for (var group = 0; group < groupCount; group++)
        {
            var record = groups + (group * 12);

            if (record + 12 > Data.Length)
            {
                return false;
            }

            var start = (int)BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(record));
            var end = (int)BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(record + 4));

            if (codePoint < start)
            {
                return false;
            }

            if (codePoint > end)
            {
                continue;
            }

            var firstGlyph = (int)BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(record + 8));
            var value = firstGlyph + (codePoint - start);

            glyph = value is >= 0 and <= 0xFFFF ? (ushort)value : (ushort)0;
            return glyph != 0;
        }

        return false;
    }

    /// <summary>Reads the PostScript name of the font from its <c>name</c> table.</summary>
    /// <param name="data">The bytes of the program.</param>
    /// <param name="tables">The table directory.</param>
    /// <returns>The name, or a fallback when the table states none.</returns>
    private static string ReadName(byte[] data, Dictionary<string, TableRecord> tables)
    {
        if (!tables.TryGetValue("name", out var name) || name.Length < 6)
        {
            return "EmbeddedFont";
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(name.Offset + 2));
        var strings = name.Offset + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(name.Offset + 4));

        for (var index = 0; index < count; index++)
        {
            var record = name.Offset + 6 + (index * 12);

            if (record + 12 > data.Length)
            {
                break;
            }

            var platform = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record));
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 2));
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 6));
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 8));
            var offset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 10));

            if (nameId != 6 || strings + offset + length > data.Length)
            {
                continue;
            }

            var text = platform == 3 && encoding is 1 or 10
                ? Encoding.BigEndianUnicode.GetString(data, strings + offset, length)
                : Encoding.Latin1.GetString(data, strings + offset, length);

            text = SanitizeName(text);

            if (text.Length > 0)
            {
                return text;
            }
        }

        return "EmbeddedFont";
    }

    /// <summary>Removes the characters a PDF name cannot carry from a font name.</summary>
    /// <param name="name">The font name.</param>
    /// <returns>The sanitized name.</returns>
    private static string SanitizeName(string name) =>
        new([.. name.Where(character =>
            character is > ' ' and <= '~'
            && character is not ('(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%' or '#'))]);

    /// <summary>The position and length of one table in the font program.</summary>
    /// <param name="Offset">The offset from the start of the program.</param>
    /// <param name="Length">The length in bytes.</param>
    private readonly record struct TableRecord(int Offset, int Length);
}
