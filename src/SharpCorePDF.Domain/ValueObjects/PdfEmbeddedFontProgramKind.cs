namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>How an embedded font program is stored, when one is present.</summary>
public enum PdfEmbeddedFontProgramKind
{
    /// <summary>No embedded program is present.</summary>
    None = 0,

    /// <summary>A Type 1 program stored as <c>/FontFile</c>.</summary>
    FontFile = 1,

    /// <summary>A TrueType program stored as <c>/FontFile2</c>.</summary>
    FontFile2 = 2,

    /// <summary>A CFF, OpenType or other program stored as <c>/FontFile3</c>.</summary>
    FontFile3 = 3,

    /// <summary>A Type 3 font whose glyphs are content streams rather than a font file.</summary>
    Type3Procedural = 4,
}
