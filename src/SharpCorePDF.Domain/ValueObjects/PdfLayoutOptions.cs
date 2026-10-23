namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The rules a layout read applies: how the words of a page become blocks, which block is read first and
/// whether the parts of a page that carry no prose are reported with them.
/// </summary>
/// <remarks>
/// <para>
/// The options are immutable and validate themselves when they are created, so a strategy this release does
/// not implement is refused at the call site and a read never pays for a document it cannot segment.
/// </para>
/// <para>
/// <see cref="IncludeImages"/> and <see cref="IncludeDecorations"/> describe the parts of a page that draw
/// ink without drawing prose. A block is text, so neither can add a word to one: the first is reserved for
/// the image regions an export of a later release writes beside the text — this release's exports describe
/// text only, so the flag changes nothing today — and the second decides whether a block that carries no
/// letter or digit — a rule drawn as a run of hyphens, a lone bullet, a separator — is kept. A caller that
/// indexes prose sets both to <see langword="false"/> and gets only what a reader reads.
/// </para>
/// </remarks>
public sealed record PdfLayoutOptions
{
    private PdfPageSegmentationStrategy segmentation = PdfPageSegmentationStrategy.RecursiveXYCut;
    private PdfReadingOrderStrategy readingOrder = PdfReadingOrderStrategy.ColumnAware;

    /// <summary>Initializes a new instance of the <see cref="PdfLayoutOptions"/> record.</summary>
    public PdfLayoutOptions()
    {
    }

    /// <summary>
    /// Gets how the words of a page become blocks: the recursive XY cut by default, or the raw order of the
    /// content, the nearest-neighbour paragraphs or the Docstrum-style line clustering.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a strategy this release implements.</exception>
    public PdfPageSegmentationStrategy Segmentation
    {
        get => segmentation;
        init => segmentation = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A page segmentation strategy must be one this library implements.");
    }

    /// <summary>
    /// Gets the order the blocks are reported in: the columns of the page left to right by default, or the
    /// order the segmentation produced them in.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a strategy this release implements.</exception>
    public PdfReadingOrderStrategy ReadingOrder
    {
        get => readingOrder;
        init => readingOrder = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A reading order strategy must be one this library implements.");
    }

    /// <summary>
    /// Gets a value indicating whether the images a page paints are reported beside its text. A block never
    /// carries an image, and this release's exports describe text only: the flag is reserved for the image
    /// regions a later release places beside the text, and it adds nothing today.
    /// </summary>
    public bool IncludeImages { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a block that carries no letter and no digit is kept. Such a block is
    /// a rule, a bullet or a separator: it is ink the page draws, but it is not prose.
    /// </summary>
    public bool IncludeDecorations { get; init; } = true;
}
