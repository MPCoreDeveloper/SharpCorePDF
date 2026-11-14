namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The relationship a PDF/A-3 document states between itself and each embedded file.
/// </summary>
/// <remarks>
/// <para>
/// ISO 19005-3 requires every embedded file to be associated with the document and to name that
/// association. The values are the five the standard defines, and they are a statement about meaning, not
/// about bytes: an invoice that carries its XML as the machine-readable form of the same invoice states
/// <see cref="Alternative"/>, while a document that carries the data it presents states
/// <see cref="Data"/>.
/// </para>
/// <para>
/// <see cref="Unspecified"/> is the default a conversion writes, because that is the only honest value
/// when nobody told the writer what the file means. A caller who knows names it; a caller who does not
/// still gets a conformant association, and says nothing it cannot stand behind.
/// </para>
/// </remarks>
public enum PdfAssociatedFileRelationship
{
    /// <summary>The document does not state what the file is; written as <c>/Unspecified</c>.</summary>
    Unspecified = 0,

    /// <summary>The file is the data the document presents; written as <c>/Data</c>.</summary>
    Data = 1,

    /// <summary>The file is the source the document was produced from; written as <c>/Source</c>.</summary>
    Source = 2,

    /// <summary>The file is an alternative representation of the document; written as <c>/Alternative</c>.</summary>
    Alternative = 3,

    /// <summary>The file adds to, rather than reproduces, the document; written as <c>/Supplement</c>.</summary>
    Supplement = 4,
}
