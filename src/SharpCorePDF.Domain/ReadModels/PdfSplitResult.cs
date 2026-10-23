using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>One document produced by a split.</summary>
public sealed record PdfSplitPart
{
    /// <summary>Gets the one-based number of this part.</summary>
    public required int PartNumber { get; init; }

    /// <summary>Gets the pages of the edited document this part holds, in their edited order.</summary>
    public required PageRange Pages { get; init; }

    /// <summary>Gets the bytes of this part; they are a complete PDF document.</summary>
    public required byte[] Content { get; init; }

    /// <summary>Gets what was written for this part, including its warnings.</summary>
    public required PdfEditResult Result { get; init; }

    /// <summary>Gets the number of pages in this part.</summary>
    public int PageCount => Result.PageCount;

    /// <summary>Formats the part for diagnostics.</summary>
    /// <returns>The formatted part.</returns>
    public override string ToString() =>
        $"part {PartNumber}: pages '{Pages}', {Content.Length} bytes";
}

/// <summary>
/// The outcome of a split: one document per run of pages, the remarks that apply to all of them, and
/// how long the whole operation took.
/// </summary>
public sealed record PdfSplitResult
{
    /// <summary>Gets the parts, in page order, numbered from one.</summary>
    public required IReadOnlyList<PdfSplitPart> Parts { get; init; }

    /// <summary>Gets how long the split took, including every part that was written.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Gets the non-fatal remarks about the split, folded together from the inspection of the source
    /// and from every part without duplicates.
    /// </summary>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];

    /// <summary>Gets the number of pages across all parts.</summary>
    public int PageCount => Parts.Sum(part => part.PageCount);

    /// <summary>Formats the result for diagnostics.</summary>
    /// <returns>The formatted result.</returns>
    public override string ToString() =>
        $"split into {Parts.Count} part(s) covering {PageCount} page(s) in {Elapsed.TotalMilliseconds:0} ms";
}
