namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The kind of a node in the read-only object graph: the object types of the PDF specification plus a
/// reference, which is reported rather than followed.
/// </summary>
public enum PdfObjectViewKind
{
    /// <summary>
    /// The null object. A node that the cross-reference table lists but that the reader could not open is
    /// also a null node, with <c>PdfObjectView.IsResolved</c> cleared so that the two are told apart.
    /// </summary>
    Null,

    /// <summary>A boolean, <c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>An integer number.</summary>
    Integer,

    /// <summary>A real number.</summary>
    Real,

    /// <summary>A string, written in literal or hexadecimal form.</summary>
    String,

    /// <summary>A name, without its leading slash.</summary>
    Name,

    /// <summary>An array.</summary>
    Array,

    /// <summary>A dictionary.</summary>
    Dictionary,

    /// <summary>A stream: its dictionary, the length of its encoded bytes and the filters it declares.</summary>
    Stream,

    /// <summary>
    /// A reference to another object. The graph states the number and the generation it names and whether
    /// the document contains them, and never walks into the object, so a document whose objects point at
    /// each other still produces a finite graph.
    /// </summary>
    Reference,
}
