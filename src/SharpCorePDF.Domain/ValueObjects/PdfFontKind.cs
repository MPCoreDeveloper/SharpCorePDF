namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The kind of font a document declares for one resource.</summary>
public enum PdfFontKind
{
    /// <summary>The document does not state a subtype the reader recognizes.</summary>
    Unknown = 0,

    /// <summary>A Type 1 font.</summary>
    Type1 = 1,

    /// <summary>A Multiple Master Type 1 font.</summary>
    MMType1 = 2,

    /// <summary>A Type 3 font whose glyphs are content streams.</summary>
    Type3 = 3,

    /// <summary>A TrueType font.</summary>
    TrueType = 4,

    /// <summary>A composite Type 0 font.</summary>
    Type0 = 5,

    /// <summary>A CIDFontType0 descendant.</summary>
    CIDFontType0 = 6,

    /// <summary>A CIDFontType2 descendant.</summary>
    CIDFontType2 = 7,
}
