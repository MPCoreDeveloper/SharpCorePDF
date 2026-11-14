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
        PdfAProfile.PdfA2B or PdfAProfile.PdfA2U => 2,
        PdfAProfile.PdfA3B or PdfAProfile.PdfA3U => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The profile is not one the engine writes."),
    };

    /// <summary>Gets the conformance level the profile names.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The level, written as <c>pdfaid:conformance</c>: <c>B</c> or <c>U</c>.</returns>
    public static string Conformance(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B or PdfAProfile.PdfA2B or PdfAProfile.PdfA3B => "B",
        PdfAProfile.PdfA2U or PdfAProfile.PdfA3U => "U",
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
        PdfAProfile.PdfA2U => "PDF/A-2u",
        PdfAProfile.PdfA3U => "PDF/A-3u",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The profile is not one the engine writes."),
    };

    /// <summary>Gets the PDF version a document written for the profile has.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>PDF 1.4 for PDF/A-1b, PDF 1.7 for the other four.</returns>
    public static PdfVersion Version(PdfAProfile profile) => profile switch
    {
        PdfAProfile.PdfA1B => PdfVersion.V1_4,
        PdfAProfile.PdfA2B or PdfAProfile.PdfA3B or PdfAProfile.PdfA2U or PdfAProfile.PdfA3U => PdfVersion.V1_7,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "The profile is not one the engine writes."),
    };

    /// <summary>Gets a value indicating whether the profile forbids transparency.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns><see langword="true"/> for PDF/A-1b, whose base format has no transparency.</returns>
    public static bool ForbidsTransparency(PdfAProfile profile) => profile == PdfAProfile.PdfA1B;

    /// <summary>Gets a value indicating whether the profile allows embedded files.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns><see langword="true"/> for the two PDF/A-3 profiles, which allow any file as an attachment.</returns>
    public static bool AllowsEmbeddedFiles(PdfAProfile profile) =>
        profile is PdfAProfile.PdfA3B or PdfAProfile.PdfA3U;

    /// <summary>Gets the name an associated-file relationship is written as.</summary>
    /// <param name="relationship">The relationship.</param>
    /// <returns>The PDF name, for example <c>Data</c>.</returns>
    public static string RelationshipName(PdfAssociatedFileRelationship relationship) => relationship switch
    {
        PdfAssociatedFileRelationship.Data => "Data",
        PdfAssociatedFileRelationship.Source => "Source",
        PdfAssociatedFileRelationship.Alternative => "Alternative",
        PdfAssociatedFileRelationship.Supplement => "Supplement",
        PdfAssociatedFileRelationship.Unspecified => "Unspecified",
        _ => throw new ArgumentOutOfRangeException(nameof(relationship), relationship, "The relationship is not one ISO 19005-3 defines."),
    };
}
