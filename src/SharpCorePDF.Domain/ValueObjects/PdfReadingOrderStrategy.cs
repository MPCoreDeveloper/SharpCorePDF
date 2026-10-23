namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The order a layout read reports the blocks of a page in.
/// </summary>
/// <remarks>
/// <para>
/// Segmentation decides what a block is; this decides which block a reader takes first. The two are
/// separate because a page can be segmented correctly and still be listed in the wrong order — a
/// two-column page whose left column is drawn after its right one is the usual case — and because a caller
/// comparing two segmentations needs one order fixed while it varies the other.
/// </para>
/// <para>
/// The set is open. A later release may add a strategy, which is a source-compatible addition only for a
/// caller that keeps a <c>default</c> arm in its <c>switch</c>.
/// </para>
/// </remarks>
public enum PdfReadingOrderStrategy
{
    /// <summary>
    /// Report the blocks in the order the segmentation produced them, which for a raw segmentation is the
    /// order the content stream draws them. It is the order a caller wants when it compares a read against
    /// the producer.
    /// </summary>
    Raw = 0,

    /// <summary>
    /// Report the columns of a page in turn, left to right, and the blocks of a column top to bottom. The
    /// columns are the vertical gutters of the page, so a heading that spans them is read before the
    /// columns it spans. This is the default, because it is the order a reader takes a page in.
    /// </summary>
    ColumnAware = 1,
}
