using System.Globalization;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One operation of a page's content stream: the operator, the operands it was given and where it starts.
/// </summary>
/// <remarks>
/// The view is what the stream says, not what this library understood: an operator nobody implements is
/// reported with its operands exactly like one that is implemented, because an audit of a document has to
/// see the whole painting (FR-7.15). Every property is set when the operation is built.
/// </remarks>
public sealed record PdfContentOperationView
{
    /// <summary>Gets the operator, for example <c>Tj</c>, <c>cm</c> or <c>Do</c>.</summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Gets the operands of the operation in the order the stream writes them: an empty list for an
    /// operator that takes none.
    /// </summary>
    public IReadOnlyList<PdfObjectView> Operands { get; init; } = [];

    /// <summary>Gets the byte offset of the operator within the decoded content stream of the page.</summary>
    /// <remarks>
    /// The offset is stated in the bytes the read decoded, not in the bytes of the file: a compressed
    /// content stream has no file offset that means anything to a caller who wants to compare the operator
    /// with the position it was found at.
    /// </remarks>
    public required long Offset { get; init; }

    /// <summary>Gets the one-based number of the page the operation belongs to.</summary>
    public required int PageNumber { get; init; }

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"page {PageNumber} @{Offset}: {Operator} ({Operands.Count} operands)");
}
