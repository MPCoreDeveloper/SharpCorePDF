using System.Text;
using System.Text.RegularExpressions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;

namespace SharpCorePDF.Manual;

/// <summary>A printed document: what it says about itself, and what each page says.</summary>
/// <param name="Info">The page count, sizes and metadata a read produced.</param>
/// <param name="Pages">The normalised text of every page, indexed from zero.</param>
internal sealed record DocumentProbe(PdfDocumentInfo Info, IReadOnlyList<string> Pages);

/// <summary>
/// Reads the printed PDF back, so the contents and the outline can state measured page numbers.
/// </summary>
/// <remarks>
/// This is the C# counterpart of the text extraction the Python builder did with pypdf: the pages are
/// read with SharpCorePDF's own reader, the whitespace of each page is collapsed, and a heading is
/// located by searching forward from the previous heading's page. Searching forward is what keeps a
/// repeated section title from pulling the cursor back into an earlier chapter.
/// </remarks>
internal static class PageIndex
{
    private const string TocMarker = "cover is page 1";

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Reads a printed document's page details and page texts.</summary>
    /// <param name="source">The document to read.</param>
    /// <param name="cancellationToken">The token the read observes.</param>
    /// <returns>What the document says about itself and what each page says.</returns>
    public static async Task<DocumentProbe> ReadAsync(
        IPdfContentSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var info = await ReadInfoAsync(source, cancellationToken).ConfigureAwait(false);
        var pages = await ReadTextsAsync(source, info.PageCount, cancellationToken).ConfigureAwait(false);
        return new DocumentProbe(info, pages);
    }

    /// <summary>Reads the page count, the page sizes and the metadata of a document.</summary>
    /// <param name="source">The document to read.</param>
    /// <param name="cancellationToken">The token the read observes.</param>
    /// <returns>What the document says about itself.</returns>
    public static async Task<PdfDocumentInfo> ReadInfoAsync(
        IPdfContentSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var reader = new PdfDocumentReader();
        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return await reader
            .ReadAsync(source.DisplayName, content, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads every page as normalised text.</summary>
    /// <param name="source">The document to read.</param>
    /// <param name="pageCount">The number of pages the document has.</param>
    /// <param name="cancellationToken">The token the read observes.</param>
    /// <returns>One normalised string per page, indexed from zero.</returns>
    public static async Task<IReadOnlyList<string>> ReadTextsAsync(
        IPdfContentSource source,
        int pageCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pages = new string[Math.Max(pageCount, 0)];
        Array.Fill(pages, string.Empty);

        var reader = new PdfContentReaderService(new PdfPageContentReader());
        var result = await reader
            .ReadTextAsync(source, PageRange.All, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var group in result.Items.GroupBy(block => block.PageNumber))
        {
            if (group.Key < 1 || group.Key > pages.Length)
            {
                continue;
            }

            var text = new StringBuilder();
            foreach (var block in group)
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(block.Text);
            }

            pages[group.Key - 1] = Normalize(text.ToString());
        }

        return pages;
    }

    /// <summary>
    /// Finds the first body page: everything before it is the cover and the contents.
    /// </summary>
    /// <param name="pages">The normalised text of every page.</param>
    /// <returns>The zero-based index of the first page after the contents.</returns>
    public static int BodyStart(IReadOnlyList<string> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        for (var index = 0; index < pages.Count; index++)
        {
            if (pages[index].Contains(TocMarker, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 1;
    }

    /// <summary>Finds the page each heading starts on, by reading the produced PDF's text.</summary>
    /// <param name="pages">The normalised text of every page.</param>
    /// <param name="headings">Every heading, in document order.</param>
    /// <param name="start">The zero-based index of the first body page.</param>
    /// <returns>The measured one-based page of every anchor, and the number of pages.</returns>
    public static (Dictionary<string, int> Pages, int Total) Locate(
        IReadOnlyList<string> pages,
        IReadOnlyList<Heading> headings,
        int start)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(headings);

        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        var cursor = Math.Clamp(start, 0, pages.Count);

        foreach (var heading in headings)
        {
            var wanted = Normalize(heading.Title);
            var found = -1;

            for (var index = cursor; index < pages.Count; index++)
            {
                if (wanted.Length > 0 && pages[index].Contains(wanted, StringComparison.Ordinal))
                {
                    found = index;
                    break;
                }
            }

            if (found < 0)
            {
                // Keep the order and move on: a heading no page states is placed where it would be.
                hits[heading.Anchor] = Math.Min(cursor + 1, Math.Max(pages.Count, 1));
            }
            else
            {
                hits[heading.Anchor] = found + 1;
                cursor = found;
            }
        }

        return (hits, pages.Count);
    }

    /// <summary>Determines whether two measurements agree.</summary>
    /// <param name="left">One measurement.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when every anchor has the same page in both.</returns>
    public static bool Same(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var page) || page != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Collapses every run of whitespace to one space, as the locator compares text.</summary>
    /// <param name="text">The text to normalise.</param>
    /// <returns>The normalised text.</returns>
    public static string Normalize(string text) =>
        Whitespace.Replace(text.Normalize(NormalizationForm.FormKC), " ").Trim();
}

