using System.Text;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Decodes the bytes of a PDF name: a <c>#XX</c> escape stands for the byte its two hex digits state,
/// and every other byte stands for itself.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for the three scanners that read names — the token lexer, the painting scanner
/// and the text scanner — so the rule cannot drift between them. The common name carries no escape at
/// all, and that case is one string and nothing else, instead of a list, its growing array and a second
/// array behind it.
/// </para>
/// <para>
/// An escape that is not followed by two hex digits is the number sign itself, which is the only
/// reading that does not lose a byte the producer wrote.
/// </para>
/// </remarks>
internal static class PdfNameDecoder
{
    /// <summary>Decodes the bytes of a name.</summary>
    /// <param name="raw">The bytes between the slash and the next delimiter, without the slash.</param>
    /// <returns>The decoded name.</returns>
    public static string Decode(ReadOnlySpan<byte> raw)
    {
        if (!raw.Contains((byte)'#'))
        {
            return Encoding.Latin1.GetString(raw);
        }

        var decoded = new byte[raw.Length];
        var written = 0;
        var remaining = raw;

        while (!remaining.IsEmpty)
        {
            var escape = remaining.IndexOf((byte)'#');

            if (escape < 0)
            {
                remaining.CopyTo(decoded.AsSpan(written));
                written += remaining.Length;
                break;
            }

            remaining[..escape].CopyTo(decoded.AsSpan(written));
            written += escape;
            remaining = remaining[(escape + 1)..];

            if (remaining.Length >= 2
                && TryParseHexDigit(remaining[0], out var high)
                && TryParseHexDigit(remaining[1], out var low))
            {
                decoded[written++] = (byte)((high << 4) | low);
                remaining = remaining[2..];
                continue;
            }

            decoded[written++] = (byte)'#';
        }

        return Encoding.Latin1.GetString(decoded.AsSpan(0, written));
    }

    /// <summary>Reads one hexadecimal digit.</summary>
    /// <param name="value">The byte to read.</param>
    /// <param name="digit">The value of the digit, or zero when the byte is not one.</param>
    /// <returns><see langword="true"/> when the byte is a hexadecimal digit.</returns>
    public static bool TryParseHexDigit(byte value, out int digit)
    {
        switch (value)
        {
            case >= (byte)'0' and <= (byte)'9':
                digit = value - (byte)'0';
                return true;

            case >= (byte)'a' and <= (byte)'f':
                digit = value - (byte)'a' + 10;
                return true;

            case >= (byte)'A' and <= (byte)'F':
                digit = value - (byte)'A' + 10;
                return true;

            default:
                digit = 0;
                return false;
        }
    }
}
