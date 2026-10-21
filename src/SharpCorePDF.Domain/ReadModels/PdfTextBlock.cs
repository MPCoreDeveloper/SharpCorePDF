using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// A block of text a page draws: one or more lines that belong together, with the words they are made
/// of and the reading order the read assigned them.
/// </summary>
/// <remarks>
/// <para>
/// A block is the unit a caller copies, translates or lays out again: a paragraph, a table cell, a
/// caption. <see cref="Text"/> is the block as plain text — its lines joined with a newline and its
/// words with a single space — which is the form a search or a translation feeds to a model, while
/// <see cref="Words"/> keeps the geometry for a caller that needs to draw something back on the page.
/// </para>
/// <para>
/// <see cref="ReadingOrder"/> is one-based and restarts on every page. Blocks are ordered the way a
/// reader takes them — top to bottom, then left to right — which is what a column-aware extractor
/// approximates and what a producer's content order often is not. The order is a reading of the page,
/// not a claim about the document's structure: nothing here knows what a heading is.
/// </para>
/// <para>
/// A block is never empty. A page whose text could not be read at all contributes no block and a
/// <see cref="ValueObjects.PdfWarningKind.TextUnreadable"/> warning to the result, so \"no text\" and
/// \"text that could not be read\" stay different answers (NFR-7.8).
/// </para>
/// </remarks>
public sealed record PdfTextBlock
{
    /// <summary>Gets the one-based number of the page the block is drawn on.</summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// Gets the text of the block: its lines joined with a newline, each line's words joined with a
    /// single space.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>Gets the area the block covers, the union of the areas of its words.</summary>
    public required PdfRectangle BoundingBox { get; init; }

    /// <summary>Gets the one-based position of the block in the reading order of its page.</summary>
    public required int ReadingOrder { get; init; }

    /// <summary>Gets the words of the block, in reading order.</summary>
    public IReadOnlyList<PdfWord> Words { get; init; } = [];

    /// <summary>Gets the number of lines the block is made of.</summary>
    public int LineCount => Text.Length == 0 ? 0 : Text.Count(character => character == '\n') + 1;

    /// <summary>Formats the block for diagnostics.</summary>
    /// <returns>The reading order, the size and the first line of the block.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"block {ReadingOrder} of page {PageNumber}: {LineCount} line(s), {Words.Count} word(s)");
}
