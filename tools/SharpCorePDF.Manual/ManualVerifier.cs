using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Manual;

/// <summary>
/// Checks the manual the build has just written, with the library's own reader rather than an external
/// tool.
/// </summary>
/// <remarks>
/// <para>
/// Every member here restates a promise the build makes: the cover states the edition it was built for,
/// the document information names the book, every heading sits on the page the contents states, the
/// outline has one entry per heading, and the running footer reaches the last page. A build that broke
/// one of them would otherwise be a PDF someone has to notice by hand.
/// </para>
/// <para>
/// The checks read the finished file, not the intermediate one: the footer is an edit applied after
/// Chromium printed the pages, so only the written document proves the composition kept its pages and
/// its text.
/// </para>
/// </remarks>
internal static class ManualVerifier
{
    private const string Author = "MPCoreDeveloper";

    /// <summary>Verifies a written manual.</summary>
    /// <param name="probe">The finished document, read back.</param>
    /// <param name="headings">Every heading, in document order.</param>
    /// <param name="pages">The measured one-based page of every anchor.</param>
    /// <param name="identity">The values the cover states.</param>
    /// <param name="stamped">Whether the finishing pass ran, and a footer is therefore expected.</param>
    /// <returns>The problems found, in the order they were checked; empty when the document is sound.</returns>
    public static IReadOnlyList<string> Verify(
        DocumentProbe probe,
        IReadOnlyList<Heading> headings,
        IReadOnlyDictionary<string, int> pages,
        ManualIdentity identity,
        bool stamped)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(identity);

        var problems = new List<string>();

        if (!probe.Info.IsReadable)
        {
            problems.Add("the written document does not open");
            return problems;
        }

        if (probe.Pages.Count == 0)
        {
            problems.Add("the written document has no pages");
            return problems;
        }

        if (probe.Info.PageCount != probe.Pages.Count)
        {
            problems.Add(
                $"the page tree states {probe.Info.PageCount} page(s) and {probe.Pages.Count} were read");
        }

        var cover = probe.Pages[0];
        if (!cover.Contains(identity.Version, StringComparison.Ordinal))
        {
            problems.Add($"the cover does not state version {identity.Version}");
        }

        if (!cover.Contains(identity.Product, StringComparison.Ordinal))
        {
            problems.Add($"the cover does not state the product name {identity.Product}");
        }

        VerifyMetadata(probe.Info.Metadata, identity, problems);

        if (probe.Info.OutlineItemCount != headings.Count)
        {
            problems.Add(
                $"the outline has {probe.Info.OutlineItemCount} item(s) for {headings.Count} heading(s)");
        }

        foreach (var heading in headings)
        {
            VerifyHeading(probe.Pages, pages, heading, problems);
        }

        if (stamped)
        {
            VerifyFooter(probe.Pages, problems);
        }

        return problems;
    }

    private static void VerifyMetadata(
        PdfMetadata metadata,
        ManualIdentity identity,
        List<string> problems)
    {
        if (!string.Equals(metadata.Title, identity.Title, StringComparison.Ordinal))
        {
            problems.Add($"the title reads '{metadata.Title}' rather than '{identity.Title}'");
        }

        if (!string.Equals(metadata.Subject, ManualFinisher.Subject, StringComparison.Ordinal))
        {
            problems.Add("the subject does not describe the manual");
        }

        if (!string.Equals(metadata.Author, Author, StringComparison.Ordinal))
        {
            problems.Add($"the author reads '{metadata.Author}' rather than '{Author}'");
        }

        if (metadata.Producer is null || !metadata.Producer.StartsWith("SharpCorePDF", StringComparison.Ordinal))
        {
            problems.Add($"the producer reads '{metadata.Producer}' rather than SharpCorePDF");
        }
    }

    private static void VerifyHeading(
        IReadOnlyList<string> pages,
        IReadOnlyDictionary<string, int> measured,
        Heading heading,
        List<string> problems)
    {
        if (!measured.TryGetValue(heading.Anchor, out var page))
        {
            problems.Add($"no page was measured for '{heading.Title}'");
            return;
        }

        if (page < 1 || page > pages.Count)
        {
            problems.Add($"'{heading.Title}' points at page {page} of {pages.Count}");
            return;
        }

        var wanted = PageIndex.Normalize(heading.Title);
        if (!pages[page - 1].Contains(wanted, StringComparison.Ordinal))
        {
            problems.Add($"'{heading.Title}' is not on page {page}, which the contents states");
        }
    }

    private static void VerifyFooter(IReadOnlyList<string> pages, List<string> problems)
    {
        // The cover carries no footer, so page 2 is the first page that must count from two.
        var total = pages.Count;
        if (total < 2 || !pages[1].Contains($"Page 2 of {total}", StringComparison.Ordinal))
        {
            problems.Add("page 2 carries no 'Page 2 of N' footer");
        }

        if (total > 2 && !pages[total - 1].Contains($"Page {total} of {total}", StringComparison.Ordinal))
        {
            problems.Add($"the last page carries no 'Page {total} of {total}' footer");
        }
    }
}