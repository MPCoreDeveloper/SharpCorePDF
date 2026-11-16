using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// What one page may spend on the forms it paints: how many paints, how many decoded bytes of form content, and a
/// small cache so that the same form painted again is not decoded again.
/// </summary>
/// <remarks>
/// The walkers that follow a page into its forms share this rule instead of each carrying a copy, so that the
/// limit one reports is the limit the other applies. A new page starts a new budget.
/// </remarks>
internal sealed class PdfFormBudget
{
    /// <summary>The decoded form content one page may read, in bytes.</summary>
    internal const long MaxBytesPerPage = 256L * 1024 * 1024;

    /// <summary>The decoded form content kept for a page so a repeated paint does not decode it again, in bytes.</summary>
    private const long MaxCachedBytes = 16L * 1024 * 1024;

    private readonly int maxPaints;
    private readonly Dictionary<long, byte[]> decoded = [];
    private int page;
    private int paints;
    private long bytes;
    private long cachedBytes;
    private bool reported;

    /// <summary>Initializes a new instance of the <see cref="PdfFormBudget"/> class.</summary>
    /// <param name="maxPaints">The largest number of form paints per page.</param>
    public PdfFormBudget(int maxPaints) => this.maxPaints = maxPaints;

    /// <summary>Starts the budget of a page, which discards what the previous page spent and cached.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    public void StartPage(int pageNumber)
    {
        if (page == pageNumber)
        {
            return;
        }

        page = pageNumber;
        paints = 0;
        bytes = 0;
        cachedBytes = 0;
        reported = false;
        decoded.Clear();
    }

    /// <summary>Asks to paint one more form.</summary>
    /// <param name="warnings">The list that receives the remark, once per page, when the limit is reached.</param>
    /// <param name="sourceName">The name of the source, used in the remark.</param>
    /// <returns><see langword="true"/> when the paint is within the budget.</returns>
    public bool TryPaint(List<PdfWarning> warnings, string sourceName)
    {
        if (++paints <= maxPaints && bytes <= MaxBytesPerPage)
        {
            return true;
        }

        if (!reported)
        {
            reported = true;
            warnings.Add(new(
                PdfWarningKind.FormPaintLimitExceeded,
                $"'{sourceName}': page {page} paints forms more than {maxPaints} times or reads more than {MaxBytesPerPage} bytes of form content; the rest of the page was not read."));
        }

        return false;
    }

    /// <summary>Looks for the decoded content of a form painted before on this page.</summary>
    /// <param name="objectNumber">The object number of the form, or <c>0</c> for a form that is not an indirect object.</param>
    /// <param name="content">The decoded content when it was cached.</param>
    /// <returns><see langword="true"/> when the content was cached, which is also counted against the page's bytes.</returns>
    public bool TryGetCached(long objectNumber, out byte[] content)
    {
        if (objectNumber != 0 && decoded.TryGetValue(objectNumber, out content!))
        {
            bytes += content.Length;
            return true;
        }

        content = [];
        return false;
    }

    /// <summary>Counts the decoded content of a form against the page, and keeps it when there is room.</summary>
    /// <param name="objectNumber">The object number of the form, or <c>0</c> for a form that is not an indirect object.</param>
    /// <param name="content">The decoded content.</param>
    public void Spend(long objectNumber, byte[] content)
    {
        bytes += content.Length;

        if (objectNumber != 0 && cachedBytes + content.Length <= MaxCachedBytes && decoded.TryAdd(objectNumber, content))
        {
            cachedBytes += content.Length;
        }
    }
}
