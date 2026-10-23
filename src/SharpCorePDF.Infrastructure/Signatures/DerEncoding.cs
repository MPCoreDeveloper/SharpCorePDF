using System.Globalization;
using System.Numerics;
using System.Text;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>
/// Writes DER encodings: the tag-length-value form a CMS signature, an X.509 certificate and an RFC 3161
/// token are all made of.
/// </summary>
/// <remarks>
/// <para>
/// A signature is built here rather than with <c>SignedCms</c> because the two things this library has to
/// control are exactly the two things that class decides for itself: the signed attributes that go into
/// the signature — CAdES needs its own — and the fact that the value must be a detached CMS whose bytes
/// the PDF byte range is computed around.
/// </para>
/// <para>
/// Every method writes definite, minimally encoded lengths, which is what DER requires and what every
/// validator expects. Values are built child-first, so a caller never has to know a length in advance.
/// The tag constants carry a <c>Tag</c> suffix so that a constant and the method that writes it can share
/// a name: <see cref="SequenceTag"/> is the identifier byte and <see cref="Sequence"/> writes a sequence.
/// </para>
/// </remarks>
internal static class DerEncoding
{
    /// <summary>The tag of an integer.</summary>
    public const byte IntegerTag = 0x02;

    /// <summary>The tag of a bit string.</summary>
    public const byte BitStringTag = 0x03;

    /// <summary>The tag of an octet string.</summary>
    public const byte OctetStringTag = 0x04;

    /// <summary>The tag of a null.</summary>
    public const byte NullTag = 0x05;

    /// <summary>The tag of an object identifier.</summary>
    public const byte ObjectIdentifierTag = 0x06;

    /// <summary>The tag of a UTF-8 string.</summary>
    public const byte Utf8StringTag = 0x0C;

    /// <summary>The tag of a UTC time.</summary>
    public const byte UtcTimeTag = 0x17;

    /// <summary>The tag of a generalized time.</summary>
    public const byte GeneralizedTimeTag = 0x18;

    /// <summary>The tag of a sequence, which is also the tag of a structure.</summary>
    public const byte SequenceTag = 0x30;

    /// <summary>The tag of a set, which is also the tag of a set of attributes.</summary>
    public const byte SetTag = 0x31;

    /// <summary>The tag of the <c>[0] IMPLICIT</c> wrapper of a CMS certificate set.</summary>
    public const byte CertificatesTag = 0xA0;

    /// <summary>The tag of the <c>[0] IMPLICIT</c> wrapper of the signed attributes of a signer.</summary>
    public const byte SignedAttributesTag = 0xA0;

    /// <summary>The tag of the <c>[1] IMPLICIT</c> wrapper of the unsigned attributes of a signer.</summary>
    public const byte UnsignedAttributesTag = 0xA1;

    /// <summary>Encodes one tag with the given contents.</summary>
    /// <param name="tag">The identifier byte of the tag.</param>
    /// <param name="contents">The contents of the value.</param>
    /// <returns>The encoded value.</returns>
    public static byte[] Tagged(byte tag, ReadOnlySpan<byte> contents)
    {
        var length = EncodeLength(contents.Length);
        var encoded = new byte[1 + length.Length + contents.Length];

        encoded[0] = tag;
        length.CopyTo(encoded, 1);
        contents.CopyTo(encoded.AsSpan(1 + length.Length));

        return encoded;
    }

    /// <summary>Encodes one tag around the concatenation of several encoded values.</summary>
    /// <param name="tag">The identifier byte of the tag.</param>
    /// <param name="items">The encoded values to place inside the tag.</param>
    /// <returns>The encoded value.</returns>
    public static byte[] Tagged(byte tag, params byte[][] items)
    {
        var contents = Concat(items);

        return Tagged(tag, contents.AsSpan());
    }

    /// <summary>Encodes a sequence around encoded values.</summary>
    /// <param name="items">The encoded values to place inside the sequence.</param>
    /// <returns>The encoded sequence.</returns>
    public static byte[] Sequence(params byte[][] items) => Tagged(SequenceTag, items);

    /// <summary>
    /// Encodes a set of values, ordered the way DER orders the members of a <c>SET OF</c>.
    /// </summary>
    /// <param name="items">The encoded values.</param>
    /// <returns>The encoded set.</returns>
    /// <remarks>
    /// DER orders the members of a set by their encodings, shortest first and byte by byte. A parser that
    /// accepts any order still reads what is written, and the canonical order is what makes two runs of
    /// the same signature byte-for-byte comparable.
    /// </remarks>
    public static byte[] SetOf(params byte[][] items)
    {
        var ordered = (byte[][])items.Clone();
        Array.Sort(ordered, Compare);

        return Tagged(SetTag, ordered);
    }

    /// <summary>Encodes a non-negative integer.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The encoded integer.</returns>
    public static byte[] Integer(long value) => Integer(new BigInteger(value));

    /// <summary>Encodes an integer.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The encoded integer, in two's complement.</returns>
    public static byte[] Integer(BigInteger value) =>
        Tagged(IntegerTag, value.ToByteArray(isUnsigned: false, isBigEndian: true));

    /// <summary>Encodes a dotted object identifier.</summary>
    /// <param name="dotted">The identifier, for example <c>1.2.840.113549.1.7.2</c>.</param>
    /// <returns>The encoded identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="dotted"/> is not a dotted identifier of at least two arcs.</exception>
    public static byte[] ObjectIdentifier(string dotted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotted);

        var parts = dotted.Split('.');

        if (parts.Length < 2)
        {
            throw new ArgumentException($"'{dotted}' is not a dotted object identifier.", nameof(dotted));
        }

        var arcs = new List<byte>();
        var first = ParseArc(parts[0], dotted);
        var second = ParseArc(parts[1], dotted);

        if (first > 2 || (first < 2 && second > 39))
        {
            throw new ArgumentException($"'{dotted}' does not start with a valid pair of arcs.", nameof(dotted));
        }

        WriteBase128(arcs, (40 * first) + second);

        for (var index = 2; index < parts.Length; index++)
        {
            WriteBase128(arcs, ParseArc(parts[index], dotted));
        }

        return Tagged(ObjectIdentifierTag, arcs.ToArray());
    }

    /// <summary>Encodes an octet string.</summary>
    /// <param name="value">The bytes to wrap.</param>
    /// <returns>The encoded octet string.</returns>
    public static byte[] OctetString(ReadOnlySpan<byte> value) => Tagged(OctetStringTag, value);

    /// <summary>Encodes a bit string whose contents are a whole number of bytes.</summary>
    /// <param name="value">The bytes of the value.</param>
    /// <returns>The encoded bit string.</returns>
    public static byte[] BitString(ReadOnlySpan<byte> value)
    {
        var contents = new byte[value.Length + 1];
        value.CopyTo(contents.AsSpan(1));

        return Tagged(BitStringTag, contents);
    }

    /// <summary>Encodes a UTF-8 string.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The encoded string.</returns>
    public static byte[] Utf8String(string value) => Tagged(Utf8StringTag, Encoding.UTF8.GetBytes(value));

    /// <summary>Encodes the value null.</summary>
    /// <returns>The encoded value.</returns>
    public static byte[] NullValue() => [NullTag, 0x00];

    /// <summary>Encodes an algorithm identifier with absent parameters.</summary>
    /// <param name="identifier">The dotted identifier of the algorithm.</param>
    /// <returns>The encoded algorithm identifier.</returns>
    public static byte[] Algorithm(string identifier) => Sequence(ObjectIdentifier(identifier));

    /// <summary>Encodes an algorithm identifier with explicit parameters.</summary>
    /// <param name="identifier">The dotted identifier of the algorithm.</param>
    /// <param name="parameters">The encoded parameters.</param>
    /// <returns>The encoded algorithm identifier.</returns>
    public static byte[] Algorithm(string identifier, byte[] parameters) =>
        Sequence(ObjectIdentifier(identifier), parameters);

    /// <summary>Encodes an algorithm identifier with the null parameters older identifiers require.</summary>
    /// <param name="identifier">The dotted identifier of the algorithm.</param>
    /// <returns>The encoded algorithm identifier.</returns>
    public static byte[] AlgorithmWithNullParameters(string identifier) =>
        Sequence(ObjectIdentifier(identifier), NullValue());

    /// <summary>Encodes a moment the way DER writes a time, choosing the form by century.</summary>
    /// <param name="value">The moment.</param>
    /// <returns>The encoded time, always in UTC.</returns>
    public static byte[] Time(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();

        return utc.Year is >= 1950 and <= 2049
            ? Tagged(UtcTimeTag, Encoding.ASCII.GetBytes(utc.ToString("yyMMddHHmmss'Z'", CultureInfo.InvariantCulture)))
            : Tagged(
                GeneralizedTimeTag,
                Encoding.ASCII.GetBytes(utc.ToString("yyyyMMddHHmmss'Z'", CultureInfo.InvariantCulture)));
    }

    /// <summary>Reads one arc of a dotted identifier.</summary>
    /// <param name="text">The text of the arc.</param>
    /// <param name="dotted">The whole identifier, for the message of an error.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not an unsigned number.</exception>
    private static uint ParseArc(string text, string dotted) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var arc)
            ? arc
            : throw new ArgumentException($"'{dotted}' is not a dotted object identifier.", nameof(dotted));

    /// <summary>Writes the base-128 form an object identifier arc uses.</summary>
    /// <param name="destination">The list to append to.</param>
    /// <param name="value">The arc.</param>
    private static void WriteBase128(List<byte> destination, uint value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var position = buffer.Length;

        buffer[--position] = (byte)(value & 0x7F);

        while ((value >>= 7) != 0)
        {
            buffer[--position] = (byte)((value & 0x7F) | 0x80);
        }

        destination.AddRange(buffer[position..].ToArray());
    }

    /// <summary>Encodes a DER length.</summary>
    /// <param name="length">The length of the contents.</param>
    /// <returns>The encoded length.</returns>
    private static byte[] EncodeLength(int length) => length switch
    {
        < 0x80 => [(byte)length],
        < 0x100 => [0x81, (byte)length],
        < 0x10000 => [0x82, (byte)(length >> 8), (byte)length],
        _ => [0x83, (byte)(length >> 16), (byte)(length >> 8), (byte)length],
    };

    /// <summary>Concatenates encoded values into one buffer.</summary>
    /// <param name="items">The encoded values.</param>
    /// <returns>The bytes of all of them, in order.</returns>
    private static byte[] Concat(byte[][] items)
    {
        var total = 0;

        foreach (var item in items)
        {
            total += item.Length;
        }

        var bytes = new byte[total];
        var offset = 0;

        foreach (var item in items)
        {
            item.CopyTo(bytes, offset);
            offset += item.Length;
        }

        return bytes;
    }

    /// <summary>Orders two encoded values the way DER orders the members of a set.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>A negative number, zero or a positive number.</returns>
    private static int Compare(byte[] left, byte[] right)
    {
        var shared = Math.Min(left.Length, right.Length);

        for (var index = 0; index < shared; index++)
        {
            if (left[index] != right[index])
            {
                return left[index] < right[index] ? -1 : 1;
            }
        }

        return left.Length.CompareTo(right.Length);
    }
}
