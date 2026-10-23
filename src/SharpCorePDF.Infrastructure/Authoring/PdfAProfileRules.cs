using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// What each archival profile states: the part of ISO 19005 it names, the conformance level, and the
/// PDF version a document written for it has.
/// </summary>
internal static class PdfAProfileRules
{
    /// <summary>Gets the number of the part of ISO 19005 the profile names.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The part number, written as <c>pdfaid:part</c>.</returns>
    public static int Part(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => 1,
        PdfAProfile.PdfA2B => 2,
        PdfAProfile.PdfA3B => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The profile is not one the engine writes."),
    };

    /// <summary>Gets the conformance level the profile names.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The level, written as <c>pdfaid:conformance</c>; always <c>B</c> in this release.</returns>
    public static string Conformance(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B or PdfAProfile.PdfA2B or PdfAProfile.PdfA3B => "B",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The profile is not one the engine writes."),
    };

    /// <summary>Gets the name of the profile, as the manual and the results spell it.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The name, for example <c>PDF/A-2b</c>.</returns>
    public static string Name(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => "PDF/A-1b",
        PdfAProfile.PdfA2B => "PDF/A-2b",
        PdfAProfile.PdfA3B => "PDF/A-3b",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The profile is not one the engine writes."),
    };

    /// <summary>Gets the PDF version a document written for the profile has.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>PDF 1.4 for PDF/A-1b, PDF 1.7 for -2b and -3b.</returns>
    public static PdfVersion Version(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => PdfVersion.V1_4,
        PdfAProfile.PdfA2B or PdfAProfile.PdfA3B => PdfVersion.V1_7,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The profile is not one the engine writes."),
    };

    /// <summary>Gets a value indicating whether the profile forbids transparency.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns><see langword="true"/> for PDF/A-1b, whose base format has no transparency.</returns>
    public static bool ForbidsTransparency(PdfAProfile profile) => profile == PdfAProfile.PdfA1B;

    /// <summary>Gets a value indicating whether the profile allows embedded files.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns><see langword="true"/> for PDF/A-3b, which allows any file as an attachment.</returns>
    public static bool AllowsEmbeddedFiles(PdfAProfile profile) => profile == PdfAProfile.PdfA3B;
}
