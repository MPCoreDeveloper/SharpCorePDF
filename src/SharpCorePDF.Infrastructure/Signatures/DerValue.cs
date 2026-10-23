using System.Globalization;
using System.Numerics;
using System.Text;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>
/// One value read from a DER encoding: its tag, its contents, the bytes it came from and, when the tag is
/// constructed, its children.
/// </summary>
/// <remarks>
/// Reading is deliberately unforgiving: a value with an indefinite length, a high tag number or a length
/// that runs past the buffer is refused rather than guessed at, because a signature that cannot be read
/// exactly is a signature that cannot be checked. <see cref="Encoded"/> keeps the original bytes so that a
/// caller can verify or hash exactly what the signer wrote, without a round trip through a re-encoding
/// that could normalise a length and change the bytes.
/// </remarks>
internal sealed class DerValue
{
    private DerValue(byte tag, ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> encoded, IReadOnlyList<DerValue> children)
    {
        Tag = tag;
        Content = content;
        Encoded = encoded;
        Children = children;
    }

    /// <summary>Gets the identifier byte of the tag.</summary>
    public byte Tag { get; }

    /// <summary>Gets the contents of the value, without the tag and the length.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Gets the bytes the value was read from, tag and length included.</summary>
    public ReadOnlyMemory<byte> Encoded { get; }

    /// <summary>Gets the children of a constructed value, in the order they were written.</summary>
    public IReadOnlyList<DerValue> Children { get; }

    /// <summary>Gets a value indicating whether the tag marks a constructed value.</summary>
    public bool IsConstructed => (Tag & 0x20) != 0;

    /// <summary>Determines whether this value carries a tag.</summary>
    /// <param name="tag">The identifier byte to compare with.</param>
    /// <returns><see langword="true"/> when the tags are equal.</returns>
    public bool Is(byte tag) => Tag == tag;

    /// <summary>Gets a child by position.</summary>
    /// <param name="index">The zero-based position of the child.</param>
    /// <returns>The child, or <see langword="null"/> when the value has fewer children.</returns>
    public DerValue? Child(int index) => index >= 0 && index < Children.Count ? Children[index] : null;

    /// <summary>Reads one value that must take up the whole buffer.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The value, or <see langword="null"/> when the bytes are not exactly one value.</returns>
    public static DerValue? ReadExact(ReadOnlySpan<byte> data)
    {
        var value = Read(data, out var consumed);

        return value is not null && consumed == data.Length ? value : null;
    }

    /// <summary>Reads one value from the start of the buffer.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="consumed">The number of bytes the value took up.</param>
    /// <returns>The value, or <see langword="null"/> when the bytes do not start with a DER value.</returns>
    public static DerValue? Read(ReadOnlySpan<byte> data, out int consumed)
    {
        consumed = 0;

        if (data.Length < 2 || (data[0] & 0x1F) == 0x1F)
        {
            return null;
        }

        var tag = data[0];
        var position = 1;
        var first = data[position++];
        var length = 0;

        if (first < 0x80)
        {
            length = first;
        }
        else
        {
            var count = first & 0x7F;

            if (count is 0 or > 4 || data.Length < position + count)
            {
                return null;
            }

            for (var index = 0; index < count; index++)
            {
                length = (length << 8) | data[position++];
            }
        }

        if (length < 0 || data.Length - position < length)
        {
            return null;
        }

        var content = data.Slice(position, length).ToArray();
        var encoded = data[..(position + length)].ToArray();
        var value = new DerValue(tag, content, encoded, ReadChildren(tag, content));

        consumed = position + length;
        return value;
    }

    /// <summary>Reads the dotted identifier of an object identifier value.</summary>
    /// <returns>The identifier, or <see langword="null"/> when the value is not one.</returns>
    public string? Oid()
    {
        if (Tag != DerEncoding.ObjectIdentifierTag || Content.Length == 0)
        {
            return null;
        }

        var position = 0;
        var first = ReadBase128(Content.Span, ref position);

        if (first is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append(first.Value / 40).Append('.').Append(first.Value % 40);

        while (position < Content.Length)
        {
            var arc = ReadBase128(Content.Span, ref position);

            if (arc is null)
            {
                return null;
            }

            builder.Append('.').Append(arc.Value);
        }

        return builder.ToString();
    }

    /// <summary>Reads the contents of an integer.</summary>
    /// <returns>The number, or <see langword="null"/> when the value is not an integer.</returns>
    public BigInteger? Number() =>
        Tag == DerEncoding.IntegerTag && Content.Length > 0
            ? new BigInteger(Content.Span, isUnsigned: false, isBigEndian: true)
            : null;

    /// <summary>Reads the contents of an integer as a small number.</summary>
    /// <returns>The number, or <see langword="null"/> when the value is not an integer of that size.</returns>
    public long? Long()
    {
        var number = Number();

        return number is { } value && value >= long.MinValue && value <= long.MaxValue ? (long)value : null;
    }

    /// <summary>Reads the contents of a text value, such as a time or a directory string.</summary>
    /// <returns>The text, or <see langword="null"/> when the value is constructed.</returns>
    public string? Text()
    {
        if (IsConstructed)
        {
            // A directory string may arrive as a BER sequence of fragments; concatenating them is what
            // every X.500 reader does, and it costs nothing when there is exactly one.
            var builder = new StringBuilder();

            foreach (var child in Children)
            {
                builder.Append(child.Text());
            }

            return builder.ToString();
        }

        return Tag == DerEncoding.Utf8StringTag
            ? Encoding.UTF8.GetString(Content.Span)
            : Encoding.Latin1.GetString(Content.Span);
    }

    /// <summary>Reads the bytes of an octet string.</summary>
    /// <returns>The bytes, or <see langword="null"/> when the value is not a primitive octet string.</returns>
    public byte[]? Octets() => Tag == DerEncoding.OctetStringTag && !IsConstructed ? Content.ToArray() : null;

    /// <summary>Reads the bytes of a bit string whose unused bits are zero.</summary>
    /// <returns>The bytes, or <see langword="null"/> when the value is not a bit string of whole bytes.</returns>
    public byte[]? Bits() =>
        Tag == DerEncoding.BitStringTag && !IsConstructed && Content.Length > 0 && Content.Span[0] == 0
            ? Content[1..].ToArray()
            : null;

    /// <summary>Reads a value as a time.</summary>
    /// <returns>The moment, or <see langword="null"/> when the value is not a time this reader understands.</returns>
    public DateTimeOffset? Time()
    {
        var text = Text();

        if (text is null)
        {
            return null;
        }

        return Tag switch
        {
            DerEncoding.UtcTimeTag => ParseTime(text, "yyMMddHHmmss", twoDigitYear: true),
            DerEncoding.GeneralizedTimeTag => ParseTime(text, "yyyyMMddHHmmss", twoDigitYear: false),
            _ => null,
        };
    }

    /// <summary>Reads the children of a constructed value.</summary>
    /// <param name="tag">The identifier byte of the tag.</param>
    /// <param name="content">The contents of the value.</param>
    /// <returns>The children; empty for a primitive value.</returns>
    private static List<DerValue> ReadChildren(byte tag, ReadOnlyMemory<byte> content)
    {
        if ((tag & 0x20) == 0)
        {
            return [];
        }

        var children = new List<DerValue>();
        var remaining = content.Span;

        while (!remaining.IsEmpty)
        {
            var child = Read(remaining, out var consumed);

            if (child is null || consumed == 0)
            {
                return [];
            }

            children.Add(child);
            remaining = remaining[consumed..];
        }

        return children;
    }

    /// <summary>Reads one base-128 arc of an object identifier.</summary>
    /// <param name="content">The contents of the identifier.</param>
    /// <param name="position">The position to read from and to advance.</param>
    /// <returns>The arc, or <see langword="null"/> when the contents end mid-arc.</returns>
    private static uint? ReadBase128(ReadOnlySpan<byte> content, ref int position)
    {
        uint value = 0;

        while (position < content.Length)
        {
            var current = content[position++];
            value = (value << 7) | (uint)(current & 0x7F);

            if ((current & 0x80) == 0)
            {
                return value;
            }

            if (value > uint.MaxValue >> 7)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Parses the text of a time value.</summary>
    /// <param name="text">The text, which may carry a zone suffix.</param>
    /// <param name="format">The format of the part before the zone.</param>
    /// <param name="twoDigitYear">Whether the year has two digits.</param>
    /// <returns>The moment, or <see langword="null"/> when the text is not a time.</returns>
    /// <remarks>
    /// DER writes times in UTC with a trailing <c>Z</c>; a few signers write an offset instead, so both are
    /// accepted and the offset is honoured rather than ignored.
    /// </remarks>
    private static DateTimeOffset? ParseTime(string text, string format, bool twoDigitYear)
    {
        var value = text.Trim();
        var offset = TimeSpan.Zero;

        if (value.EndsWith('Z'))
        {
            value = value[..^1];
        }
        else if (value.Length > 5 && (value[^5] == '+' || value[^5] == '-'))
        {
            var sign = value[^5] == '-' ? -1 : 1;
            var hours = int.Parse(value.AsSpan(value.Length - 4, 2), CultureInfo.InvariantCulture);
            var minutes = int.Parse(value.AsSpan(value.Length - 2, 2), CultureInfo.InvariantCulture);

            offset = sign * new TimeSpan(hours, minutes, 0);
            value = value[..^5];
        }

        var digits = twoDigitYear ? 12 : 14;

        if (value.Length > digits && value[digits] == '.')
        {
            value = value[..digits];
        }

        if (value.Length != digits ||
            !DateTime.TryParseExact(
                value,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return new DateTimeOffset(parsed, TimeSpan.Zero).ToOffset(offset);
    }

    /// <summary>Formats the tag of this value for a diagnostic message.</summary>
    /// <returns>The tag in hexadecimal.</returns>
    public override string ToString() => Tag.ToString("X2", CultureInfo.InvariantCulture);
}
