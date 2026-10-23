using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading.LayoutAnalysis;

/// <summary>
/// One line of a page: the words that share a baseline and sit near enough to belong together, with the
/// area they cover.
/// </summary>
/// <remarks>
/// A line is the intermediate level every block strategy needs: the raw segmentation cuts it by the order
/// of the content, the nearest-neighbour and Docstrum strategies join consecutive ones into paragraphs, and
/// the recursive XY cut splits a region along the whitespace between them. It is a reading of the page rather
/// than a claim the document makes, so a producer that draws one line as two runs far apart yields two
/// lines here.
/// </remarks>
internal sealed record TextLine
{
    /// <summary>Gets the words of the line, in the order the grouping placed them.</summary>
    public required IReadOnlyList<PdfWord> Words { get; init; }

    /// <summary>Gets the area the line covers, the union of the areas of its words.</summary>
    public required PdfRectangle Bounds { get; init; }

    /// <summary>Gets the baseline the line sits on.</summary>
    public double Baseline => Words.Count > 0 ? Words[0].BaselineY : Bounds.Bottom;

    /// <summary>Gets the size of the em box of the line, the largest of its words.</summary>
    public double Size => Words.Count > 0 ? Words.Max(word => word.FontSize) : 0d;
}
