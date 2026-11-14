namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Stable identity of one PDF/A requirement the writer or the preflight analysis reports.
/// </summary>
/// <remarks>
/// Match on this kind rather than on the human-readable message. The set is open, so a
/// <c>switch</c> must keep a <c>default</c> arm.
/// </remarks>
public enum PdfARequirementKind
{
    /// <summary>A condition that is not one of the named kinds below.</summary>
    Other = 0,

    /// <summary>A font used by the document does not carry its program.</summary>
    FontNotEmbedded = 1,

    /// <summary>A CID font subset is missing the <c>/CIDSet</c> stream PDF/A-1b requires.</summary>
    CidSetMissing = 2,

    /// <summary>The document uses transparency that the profile forbids.</summary>
    TransparencyForbidden = 3,

    /// <summary>The document carries optional content the profile forbids.</summary>
    OptionalContentForbidden = 4,

    /// <summary>The document carries an attachment the profile forbids.</summary>
    AttachmentForbidden = 5,

    /// <summary>A stream uses a filter the profile forbids.</summary>
    FilterForbidden = 6,

    /// <summary>An action the profile forbids is present and cannot be removed safely.</summary>
    ActionForbidden = 7,

    /// <summary>An annotation is missing the appearance stream the profile requires.</summary>
    AnnotationAppearanceMissing = 8,

    /// <summary>A stream takes its bytes from an external file.</summary>
    ExternalFileStream = 9,

    /// <summary>A catalog feature will be removed rather than refusing conversion.</summary>
    RemovableCatalogFeature = 10,

    /// <summary>A form flag will be cleared rather than refusing conversion.</summary>
    RemovableFormFeature = 11,
}
