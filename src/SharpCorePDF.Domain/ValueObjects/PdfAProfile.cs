namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The archival profile a document is written for.
/// </summary>
/// <remarks>
/// <para>
/// The number of each member is the part of ISO 19005 it names, which is also the value written as
/// <c>pdfaid:part</c> in the XMP packet of the result. Every profile here is level B: the document
/// keeps its visual appearance, and accessibility or Unicode mapping are not claimed.
/// </para>
/// <para>
/// A profile is a set of rules the writer enforces, not a promise of conformance. Conformance is
/// established by an independent validator; the library refuses to label a document it knows is not
/// conformant, and never claims more than the structures it wrote.
/// </para>
/// </remarks>
public enum PdfAProfile
{
    /// <summary>
    /// PDF/A-1b, ISO 19005-1: a PDF 1.4 document without transparency, JPEG 2000, JavaScript,
    /// optional content or embedded files.
    /// </summary>
    PdfA1B = 1,

    /// <summary>
    /// PDF/A-2b, ISO 19005-2: a PDF 1.7 document that keeps transparency and JPEG 2000 but still
    /// forbids JavaScript and embedded files.
    /// </summary>
    PdfA2B = 2,

    /// <summary>
    /// PDF/A-3b, ISO 19005-3: PDF/A-2b with any file allowed as an embedded attachment.
    /// </summary>
    PdfA3B = 3,
}
