using System.Globalization;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One node of a document's object graph as it was read: what the object is, what it holds and, for a
/// reference, what it names.
/// </summary>
/// <remarks>
/// <para>
/// Every value is set when the node is built and cannot be changed afterwards, and
/// <see cref="Children"/> is a list a caller can only read, so a graph can be held for as long as it is
/// wanted without the document — or another caller — changing under it (NFR-7.7).
/// </para>
/// <para>
/// A reference is a node of its own: <see cref="Kind"/> is <see cref="PdfObjectViewKind.Reference"/>,
/// <see cref="Reference"/> names the object and <see cref="IsResolved"/> says whether the document holds
/// it. A dangling reference is therefore visible in the graph rather than dropped from it, and a graph
/// that points in a circle is still finite.
/// </para>
/// </remarks>
public sealed record PdfObjectView
{
    /// <summary>Gets the kind of the node.</summary>
    public required PdfObjectViewKind Kind { get; init; }

    /// <summary>
    /// Gets the key the node appears under in its parent dictionary, or <see langword="null"/> for an
    /// element of an array and for the node of an indirect object.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Gets the number of the indirect object this node is, or <see langword="null"/> for a node inside
    /// another object.
    /// </summary>
    public long? ObjectNumber { get; init; }

    /// <summary>
    /// Gets the generation number of the indirect object this node is, or zero for a node inside another
    /// object.
    /// </summary>
    public int Generation { get; init; }

    /// <summary>Gets the value of a scalar node, or <see langword="null"/> when the node holds children.</summary>
    /// <remarks>
    /// A <see cref="PdfObjectViewKind.Boolean"/> node holds a <see cref="bool"/>, an
    /// <see cref="PdfObjectViewKind.Integer"/> node a <see cref="long"/>, a
    /// <see cref="PdfObjectViewKind.Real"/> node a <see cref="double"/>, and a
    /// <see cref="PdfObjectViewKind.String"/> or <see cref="PdfObjectViewKind.Name"/> node the decoded
    /// <see cref="string"/>.
    /// </remarks>
    public object? Scalar { get; init; }

    /// <summary>Gets the object a reference names, or <see langword="null"/> when the node is not a reference.</summary>
    public PdfObjectReference? Reference { get; init; }

    /// <summary>
    /// Gets a value indicating whether the node is what it claims to be: whether a reference names an
    /// object the document holds, and whether an object the table lists could be opened at all.
    /// </summary>
    public bool IsResolved { get; init; } = true;

    /// <summary>
    /// Gets the entries of a dictionary or of a stream dictionary, or the elements of an array, in
    /// document order; empty for every other kind of node.
    /// </summary>
    public IReadOnlyList<PdfObjectView> Children { get; init; } = [];

    /// <summary>
    /// Gets the length of the encoded bytes of a stream, or <see langword="null"/> for another kind of
    /// node.
    /// </summary>
    public long? StreamLength { get; init; }

    /// <summary>
    /// Gets the filter a stream declares, or the filters in the order they are applied, or
    /// <see langword="null"/> when it declares none.
    /// </summary>
    public string? Filter { get; init; }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        PdfObjectViewKind.Reference => $"{Reference} ({(IsResolved ? "resolved" : "dangling")})",
        PdfObjectViewKind.Stream => string.Create(CultureInfo.InvariantCulture, $"stream({StreamLength} bytes)"),
        PdfObjectViewKind.Array or PdfObjectViewKind.Dictionary =>
            string.Create(CultureInfo.InvariantCulture, $"{Kind}({Children.Count})"),
        PdfObjectViewKind.Null => "null",
        _ => string.Create(CultureInfo.InvariantCulture, $"{Kind} {Scalar}"),
    };
}

/// <summary>
/// A reference to an indirect object, kept as the document wrote it instead of followed.
/// </summary>
/// <param name="ObjectNumber">The object number.</param>
/// <param name="Generation">The generation number.</param>
public sealed record PdfObjectReference(long ObjectNumber, int Generation)
{
    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{ObjectNumber} {Generation} R");
}
