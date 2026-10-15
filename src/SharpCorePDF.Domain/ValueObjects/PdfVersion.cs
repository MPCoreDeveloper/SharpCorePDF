using System.Diagnostics.CodeAnalysis;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The version of the PDF specification a document conforms to, as written in the
/// <c>%PDF-1.7</c> file header.
/// </summary>
public readonly record struct PdfVersion : IComparable<PdfVersion>
{
    /// <summary>Initializes a new instance of the <see cref="PdfVersion"/> struct.</summary>
    /// <param name="major">The major version number.</param>
    /// <param name="minor">The minor version number.</param>
    /// <exception cref="ArgumentOutOfRangeException">A version component is negative.</exception>
    public PdfVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        Major = major;
        Minor = minor;
    }

    /// <summary>Gets the major version number.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version number.</summary>
    public int Minor { get; }

    /// <summary>Gets PDF 1.4, the first version with non-ASCII name escapes and object streams support rules.</summary>
    public static PdfVersion V1_4 { get; } = new(1, 4);

    /// <summary>Gets PDF 1.5, the version that introduced cross-reference streams and object streams.</summary>
    public static PdfVersion V1_5 { get; } = new(1, 5);

    /// <summary>Gets PDF 1.6.</summary>
    public static PdfVersion V1_6 { get; } = new(1, 6);

    /// <summary>Gets PDF 1.7, the default version written by the engine.</summary>
    public static PdfVersion V1_7 { get; } = new(1, 7);

    /// <summary>Gets PDF 2.0.</summary>
    public static PdfVersion V2_0 { get; } = new(2, 0);

    /// <summary>Gets the version assumed when a source has no parseable header.</summary>
    public static PdfVersion Unknown { get; } = new(1, 0);

    /// <summary>Parses a version such as <c>"1.7"</c>.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> is not a valid version.</exception>
    public static PdfVersion Parse(string? text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"'{text}' is not a valid PDF version. Expected a value such as '1.7' or '2.0'.");

    /// <summary>Attempts to parse a version such as <c>"1.7"</c>.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="version">The parsed version when parsing succeeded.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a valid version.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out PdfVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split('.');
        if (parts.Length is < 1 or > 2
            || !int.TryParse(parts[0], out var major)
            || major < 0)
        {
            return false;
        }

        if (parts.Length == 1)
        {
            version = new PdfVersion(major, 0);
            return true;
        }

        if (!int.TryParse(parts[1], out var minor) || minor < 0)
        {
            return false;
        }

        version = new PdfVersion(major, minor);
        return true;
    }

    /// <summary>Compares this version with <paramref name="other"/>.</summary>
    /// <param name="other">The version to compare against.</param>
    /// <returns>A negative value when this version is older, zero when equal, positive when newer.</returns>
    public int CompareTo(PdfVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    /// <summary>Determines whether this version is at least <paramref name="other"/>.</summary>
    /// <param name="other">The version to compare against.</param>
    /// <returns><see langword="true"/> when this version is equal to or newer than <paramref name="other"/>.</returns>
    public bool IsAtLeast(PdfVersion other) => CompareTo(other) >= 0;

    /// <summary>Determines whether this version is older than <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is older.</returns>
    public static bool operator <(PdfVersion left, PdfVersion right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether this version is older than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not newer than <paramref name="right"/>.</returns>
    public static bool operator <=(PdfVersion left, PdfVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether this version is newer than <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is newer.</returns>
    public static bool operator >(PdfVersion left, PdfVersion right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether this version is newer than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not older than <paramref name="right"/>.</returns>
    public static bool operator >=(PdfVersion left, PdfVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Formats the version exactly as it appears in a PDF header, for example <c>1.7</c>.</summary>
    /// <returns>The formatted version.</returns>
    public override string ToString() => $"{Major}.{Minor}";
}
