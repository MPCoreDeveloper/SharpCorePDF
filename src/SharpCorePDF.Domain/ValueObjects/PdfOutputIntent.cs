using System.Text;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The output intent an archival document states: which colour space its device colours are meant to be
/// read in, and the ICC profile that describes it.
/// </summary>
/// <remarks>
/// <para>
/// An archival conversion writes one output intent of its own — sRGB, from an ICC profile the library
/// generates — and that is what it writes when no intent is supplied. A caller that archives for print
/// supplies the profile its workflow is calibrated against instead: a CMYK profile, or an RGB one from a
/// colour management system, with the condition it names.
/// </para>
/// <para>
/// The profile is checked as an ICC profile before it is written, because a broken one produces a file that
/// looks archival and is not. An ICC profile is a self-describing binary: it declares its own length, it
/// carries the <c>acsp</c> signature at offset 36, and it states its data colour space at offset 16. Those
/// three fields are what this type reads, and a profile that contradicts itself is refused rather than
/// copied.
/// </para>
/// <para>
/// What a validator checks afterwards is a different question and stays its answer: a page that paints in
/// device colours needs an intent whose colour space matches those colours, and only the validator decides
/// whether this intent and those pages agree.
/// </para>
/// </remarks>
public sealed record PdfOutputIntent
{
    /// <summary>The offset of the <c>acsp</c> signature in an ICC profile header.</summary>
    private const int SignatureOffset = 36;

    /// <summary>The offset of the data colour space signature in an ICC profile header.</summary>
    private const int DataColourSpaceOffset = 16;

    /// <summary>The number of bytes a header and an empty tag table occupy.</summary>
    private const int MinimumLength = 132;

    /// <summary>
    /// Gets the bytes of the ICC profile, exactly as the caller supplied them.
    /// </summary>
    /// <remarks>
    /// The bytes are written verbatim as the destination profile of the intent. The library does not
    /// normalise, subset or re-encode a profile: a profile altered in those ways describes a colour space it
    /// no longer is.
    /// </remarks>
    public required ReadOnlyMemory<byte> IccProfile { get; init; }

    /// <summary>
    /// Gets the number of colour components the profile has: 1 for Gray, 3 for RGB, 4 for CMYK.
    /// </summary>
    /// <remarks>
    /// This is written as <c>/N</c> on the destination profile stream, and it has to agree with the data
    /// colour space the profile itself states.
    /// </remarks>
    public required int NumberOfComponents { get; init; }

    /// <summary>
    /// Gets the identifier of the output condition, written as <c>OutputConditionIdentifier</c>.
    /// </summary>
    /// <remarks>
    /// A registered name, such as the one a print standard publishes, or a name the caller's own workflow
    /// uses. It is the field a receipting archive reports, so it is never empty.
    /// </remarks>
    public required string OutputConditionIdentifier { get; init; }

    /// <summary>
    /// Gets a human-readable description of the output condition, written as <c>OutputCondition</c>.
    /// </summary>
    public string? OutputCondition { get; init; }

    /// <summary>Gets a human-readable description of the intent, written as <c>Info</c>.</summary>
    public string? Info { get; init; }

    /// <summary>
    /// Gets the registry the condition identifier is registered with, written as <c>RegistryName</c>.
    /// </summary>
    /// <remarks>
    /// Omitted from the dictionary when it is <see langword="null"/>: an intent whose identifier is the
    /// caller's own has no registry to name, and inventing one would state something untrue about it.
    /// </remarks>
    public string? RegistryName { get; init; }

    /// <summary>Validates this intent.</summary>
    /// <returns>Every problem found, or an empty list when the intent is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(OutputConditionIdentifier))
        {
            errors.Add(
                "the output condition identifier must be a non-empty string: it is the field an archive reports when it reads the intent.");
        }

        if (NumberOfComponents is not (1 or 3 or 4))
        {
            errors.Add(
                $"the number of colour components must be 1 (Gray), 3 (RGB) or 4 (CMYK), but is {NumberOfComponents}.");
        }

        var profile = IccProfile.Span;

        if (profile.Length < MinimumLength)
        {
            errors.Add(
                $"the ICC profile is {profile.Length} bytes, shorter than the {MinimumLength} bytes an ICC header and its tag count need.");

            return errors;
        }

        var declaredSize = ReadBigEndian(profile[..4]);

        if (declaredSize > profile.Length)
        {
            errors.Add($"the ICC profile declares {declaredSize} bytes but carries {profile.Length}.");
        }

        var signature = Encoding.ASCII.GetString(profile.Slice(SignatureOffset, 4));

        if (!string.Equals(signature, "acsp", StringComparison.Ordinal))
        {
            errors.Add(
                $"the ICC profile does not carry the 'acsp' signature at offset {SignatureOffset}, so the data is not an ICC profile.");

            return errors;
        }

        var colourSpace = Encoding.ASCII.GetString(profile.Slice(DataColourSpaceOffset, 4));
        var stated = colourSpace switch
        {
            "GRAY" => 1,
            "RGB " => 3,
            "CMYK" => 4,
            _ => 0,
        };

        // A profile whose data colour space is none of the three is not refused here: a device-link or a
        // named-colour profile is still an ICC profile, and the caller declared the count it writes.
        if (stated != 0 && stated != NumberOfComponents)
        {
            errors.Add(
                $"the ICC profile describes a {stated}-component colour space ('{colourSpace}') but the intent declares {NumberOfComponents}.");
        }

        return errors;
    }

    /// <summary>Reads a big-endian 32-bit integer, the way an ICC profile states its own length.</summary>
    /// <param name="bytes">The four bytes.</param>
    /// <returns>The value.</returns>
    private static int ReadBigEndian(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}
