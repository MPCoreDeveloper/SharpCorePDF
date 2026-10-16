using System.Globalization;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Reads and writes the PDF date format, <c>D:YYYYMMDDHHmmSSOHH'mm'</c>.
/// </summary>
/// <remarks>
/// Producers are notoriously sloppy about dates: the <c>D:</c> prefix, the seconds and the time zone
/// are all optional and plenty of files contain out of range values. Reading therefore clamps
/// instead of failing, while writing always produces a complete, unambiguous value.
/// </remarks>
internal static class PdfDate
{
    /// <summary>Attempts to read a PDF date.</summary>
    /// <param name="text">The stored text.</param>
    /// <param name="value">The parsed value when parsing succeeded.</param>
    /// <returns><see langword="true"/> when the text contained a usable date.</returns>
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var position = 0;

        if (span.Length > 2 && (span[0] is 'D' or 'd') && span[1] == ':')
        {
            span = span[2..];
        }

        var digits = 0;

        while (digits < 14 && position + digits < span.Length && char.IsAsciiDigit(span[position + digits]))
        {
            digits++;
        }

        if (digits == 0)
        {
            return false;
        }

        var fields = span.Slice(position, digits);
        position += digits;

        var year = ReadField(fields, 0, 4, fallback: 0);
        var month = ReadField(fields, 4, 2, fallback: 1);
        var day = ReadField(fields, 6, 2, fallback: 1);
        var hour = ReadField(fields, 8, 2, fallback: 0);
        var minute = ReadField(fields, 10, 2, fallback: 0);
        var second = ReadField(fields, 12, 2, fallback: 0);

        if (year is < 1 or > 9999)
        {
            return false;
        }

        month = Math.Clamp(month, 1, 12);
        day = Math.Clamp(day, 1, DateTime.DaysInMonth(year, month));
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        second = Math.Clamp(second, 0, 59);

        value = new DateTimeOffset(year, month, day, hour, minute, second, ReadOffset(span[position..]));
        return true;
    }

    /// <summary>
    /// Reads one fixed width field of the date, as the specification defines it: <c>YYYYMMDDHHmmSS</c>.
    /// </summary>
    /// <param name="text">The digits of the date.</param>
    /// <param name="start">The offset of the field.</param>
    /// <param name="width">The width of the field.</param>
    /// <param name="fallback">The value to use when the field was not written.</param>
    /// <returns>The value of the field.</returns>
    private static int ReadField(ReadOnlySpan<char> text, int start, int width, int fallback) =>
        start + width <= text.Length
            && int.TryParse(text.Slice(start, width), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

    /// <summary>Formats a value as a PDF date.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted date, for example <c>D:20240115093000Z</c>.</returns>
    public static string Format(DateTimeOffset value)
    {
        var text = value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        if (value.Offset == TimeSpan.Zero)
        {
            return $"D:{text}Z";
        }

        var sign = value.Offset < TimeSpan.Zero ? '-' : '+';
        return string.Create(
            CultureInfo.InvariantCulture,
            $"D:{text}{sign}{Math.Abs(value.Offset.Hours):00}'{Math.Abs(value.Offset.Minutes):00}'");
    }

    private static TimeSpan ReadOffset(ReadOnlySpan<char> rest)
    {
        if (rest.Length == 0 || rest[0] is 'Z' or 'z' or not ('+' or '-'))
        {
            return TimeSpan.Zero;
        }

        var hours = 0;
        var minutes = 0;
        var position = 1;
        var field = 0;

        while (position < rest.Length && field < 2)
        {
            if (!TryReadOffsetField(rest, ref position, out var parsed))
            {
                continue;
            }

            if (field == 0)
            {
                hours = parsed;
            }
            else
            {
                minutes = parsed;
            }

            field++;
        }

        var sign = rest[0] == '-' ? -1 : 1;
        return sign * new TimeSpan(Math.Clamp(hours, 0, 23), Math.Clamp(minutes, 0, 59), 0);
    }

    /// <summary>Reads the next run of digits of a time zone offset, skipping what is not a digit.</summary>
    /// <param name="rest">The text after the date itself.</param>
    /// <param name="position">The position in <paramref name="rest"/>, advanced past the field.</param>
    /// <param name="value">The value of the field when one was read.</param>
    /// <returns><see langword="true"/> when the run of digits parsed as a value.</returns>
    private static bool TryReadOffsetField(ReadOnlySpan<char> rest, ref int position, out int value)
    {
        value = 0;

        while (position < rest.Length && !char.IsAsciiDigit(rest[position]))
        {
            position++;
        }

        var start = position;

        while (position < rest.Length && char.IsAsciiDigit(rest[position]))
        {
            position++;
        }

        return start < position
            && int.TryParse(rest[start..position], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
