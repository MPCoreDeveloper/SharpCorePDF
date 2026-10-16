namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// What a reader without the owner password is allowed to do with an encrypted document.
/// </summary>
/// <remarks>
/// <para>
/// The values are the permission bits of the specification's encryption dictionary, so they can be
/// combined with <c>|</c>; the bits the specification reserves are added by the writer, not here. A
/// document that is encrypted at all always carries these bits, whether a viewer honours them or not:
/// they are a restriction on what a cooperating application offers, not a technical protection.
/// </para>
/// <para>
/// The bits that revision 2 does not define — everything from <see cref="FillFormFields"/> on — are
/// written as well; a reader that predates them ignores the entry entirely.
/// </para>
/// </remarks>
[Flags]
public enum PdfPermissions
{
    /// <summary>Allows nothing beyond viewing the pages the reader can decrypt.</summary>
    None = 0,

    /// <summary>Allows printing the document.</summary>
    Print = 1 << 2,

    /// <summary>Allows changing the content of the pages and the document as a whole.</summary>
    ModifyContents = 1 << 3,

    /// <summary>Allows copying text and graphics out of the document.</summary>
    CopyContent = 1 << 4,

    /// <summary>Allows adding and changing annotations and filling in existing form fields.</summary>
    Annotate = 1 << 5,

    /// <summary>Allows filling in form fields even when <see cref="Annotate"/> is not allowed.</summary>
    FillFormFields = 1 << 8,

    /// <summary>
    /// Allows screen readers and other tools to extract the content for accessibility, even when
    /// <see cref="CopyContent"/> is not allowed.
    /// </summary>
    ExtractForAccessibility = 1 << 9,

    /// <summary>Allows inserting, rotating and deleting pages, and building bookmarks and thumbnails.</summary>
    Assemble = 1 << 10,

    /// <summary>Allows printing at a quality that reproduces the content faithfully, not just a draft.</summary>
    PrintHighQuality = 1 << 11,

    /// <summary>The permissions a document that only needs a password to open is written with.</summary>
    All = Print
        | ModifyContents
        | CopyContent
        | Annotate
        | FillFormFields
        | ExtractForAccessibility
        | Assemble
        | PrintHighQuality,

    /// <summary>
    /// The permissions of a document that may only be read and printed: nothing can be copied out of
    /// it, taken apart or annotated, except that a screen reader may still extract its content.
    /// </summary>
    PrintOnly = Print | ExtractForAccessibility,
}
