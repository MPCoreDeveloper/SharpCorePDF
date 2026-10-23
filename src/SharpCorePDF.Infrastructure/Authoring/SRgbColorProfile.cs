using System.Buffers.Binary;
using System.Text;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Builds the sRGB colour profile written as the destination profile of a PDF/A output intent when the
/// caller does not supply one.
/// </summary>
/// <remarks>
/// <para>
/// The profile is generated rather than shipped as an asset, so nothing has to be licensed or kept in
/// step with the source, and the same bytes are written on every platform. It is a complete ICC v2
/// monitor profile: the D50 white point, the sRGB primaries, a gamma 2.2 tone curve for each channel,
/// a description and a copyright tag.
/// </para>
/// <para>
/// The tags are the ones a reader needs to interpret an RGB image: three colorants and three tone
/// curves with a single gamma each. That is what every sRGB profile states; a profile with a
/// parametric curve would describe the same colour space in more bytes.
/// </para>
/// </remarks>
internal static class SRgbColorProfile
{
    /// <summary>The description written into the profile.</summary>
    private const string Description = "sRGB IEC61966-2.1";

    /// <summary>The copyright written into the profile.</summary>
    private const string Copyright = "No copyright, use freely";

    /// <summary>The signatures of the tags, in the order their bodies are written.</summary>
    private static readonly string[] TagNames = ["desc", "cprt", "wtpt", "rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC"];

    private static readonly Lazy<byte[]> Instance = new(Build);

    /// <summary>Gets the bytes of the sRGB profile.</summary>
    public static byte[] Bytes => Instance.Value;

    /// <summary>Builds the profile.</summary>
    /// <returns>The complete ICC program.</returns>
    private static byte[] Build()
    {
        // The bodies come first: the tag table states their offsets and sizes, and the header states
        // the size of everything, so nothing can be written before the bodies are known.
        var bodies = new[]
        {
            TextDescription(Description),
            CopyrightText(Copyright),
            Xyz(0.9642, 1.0, 0.8249),
            Xyz(0.4360747, 0.2225045, 0.0139322),
            Xyz(0.3850649, 0.7168786, 0.0971045),
            Xyz(0.1430804, 0.0606169, 0.7141733),
            Curve(2.2),
            Curve(2.2),
            Curve(2.2),
        };

        var tableSize = 4 + (12 * bodies.Length);
        var offset = 128 + tableSize;
        var profile = new byte[offset + bodies.Sum(body => body.Length + Pad(body.Length))];

        WriteHeader(profile);

        WriteInt32(profile, 128, bodies.Length);

        for (var index = 0; index < bodies.Length; index++)
        {
            var entry = 132 + (index * 12);

            WriteAscii(profile, entry, TagNames[index]);
            WriteInt32(profile, entry + 4, offset);
            WriteInt32(profile, entry + 8, bodies[index].Length);

            bodies[index].CopyTo(profile, offset);
            offset += bodies[index].Length + Pad(bodies[index].Length);
        }

        return profile;
    }

    /// <summary>Writes the 128 byte header of the profile.</summary>
    /// <param name="profile">The buffer to write to.</param>
    private static void WriteHeader(byte[] profile)
    {
        WriteInt32(profile, 0, profile.Length);
        WriteAscii(profile, 4, "ADBE");
        WriteInt32(profile, 8, 0x02100000);
        WriteAscii(profile, 12, "mntr");
        WriteAscii(profile, 16, "RGB ");
        WriteAscii(profile, 20, "XYZ ");

        // The creation date is a fixed moment for the same reason the rest of the profile is: the
        // output has to be reproducible from one run to the next.
        WriteInt16(profile, 24, 2026);
        WriteInt16(profile, 26, 1);
        WriteInt16(profile, 28, 1);

        WriteAscii(profile, 36, "acsp");
        WriteAscii(profile, 80, "ADBE");

        // The D50 illuminant, as the ICC specification requires of every profile.
        WriteInt32(profile, 68, 0x0000F6D6);
        WriteInt32(profile, 72, 0x00010000);
        WriteInt32(profile, 76, 0x0000D32D);
    }

    /// <summary>Builds a <c>textDescriptionType</c> tag body.</summary>
    /// <param name="text">The description.</param>
    /// <returns>The tag body.</returns>
    private static byte[] TextDescription(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text);
        var body = new byte[12 + ascii.Length + 1];

        WriteAscii(body, 0, "desc");
        WriteInt32(body, 8, ascii.Length + 1);
        ascii.CopyTo(body, 12);

        return body;
    }

    /// <summary>Builds a <c>textType</c> tag body.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The tag body.</returns>
    private static byte[] CopyrightText(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text);
        var body = new byte[8 + ascii.Length + 1];

        WriteAscii(body, 0, "text");
        ascii.CopyTo(body, 8);

        return body;
    }

    /// <summary>Builds an <c>XYZType</c> tag body.</summary>
    /// <param name="x">The X value.</param>
    /// <param name="y">The Y value.</param>
    /// <param name="z">The Z value.</param>
    /// <returns>The tag body.</returns>
    private static byte[] Xyz(double x, double y, double z)
    {
        var body = new byte[20];

        WriteAscii(body, 0, "XYZ ");
        WriteInt32(body, 8, Fixed(x));
        WriteInt32(body, 12, Fixed(y));
        WriteInt32(body, 16, Fixed(z));

        return body;
    }

    /// <summary>Builds a <c>curveType</c> tag body that states a single gamma value.</summary>
    /// <param name="gamma">The gamma.</param>
    /// <returns>The tag body.</returns>
    private static byte[] Curve(double gamma)
    {
        var body = new byte[14];

        WriteAscii(body, 0, "curv");
        WriteInt32(body, 8, 1);
        WriteInt16(body, 12, (short)Math.Round(gamma * 256, MidpointRounding.AwayFromZero));

        return body;
    }

    /// <summary>Converts a value to the 16.16 fixed point form an <c>XYZType</c> states.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The fixed point value.</returns>
    private static int Fixed(double value) => (int)Math.Round(value * 65536, MidpointRounding.AwayFromZero);

    /// <summary>Gets the padding a tag body needs so that the next one starts on a four byte boundary.</summary>
    /// <param name="length">The length of the body.</param>
    /// <returns>The number of padding bytes.</returns>
    private static int Pad(int length) => (4 - (length % 4)) % 4;

    /// <summary>Writes a big-endian 32-bit integer.</summary>
    /// <param name="destination">The buffer to write to.</param>
    /// <param name="offset">The offset to write at.</param>
    /// <param name="value">The value.</param>
    private static void WriteInt32(byte[] destination, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset), value);

    /// <summary>Writes a big-endian 16-bit integer.</summary>
    /// <param name="destination">The buffer to write to.</param>
    /// <param name="offset">The offset to write at.</param>
    /// <param name="value">The value.</param>
    private static void WriteInt16(byte[] destination, int offset, short value) =>
        BinaryPrimitives.WriteInt16BigEndian(destination.AsSpan(offset), value);

    /// <summary>Writes ASCII text, leaving the rest of the field as zero bytes.</summary>
    /// <param name="destination">The buffer to write to.</param>
    /// <param name="offset">The offset to write at.</param>
    /// <param name="text">The text.</param>
    private static void WriteAscii(byte[] destination, int offset, string text) =>
        Encoding.ASCII.GetBytes(text).CopyTo(destination, offset);
}
