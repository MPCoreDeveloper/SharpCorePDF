using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Infrastructure.Extensions;

/// <summary>
/// Extensions that build a merge plan from the content sources this assembly provides, so the common
/// shape of a merge stays a one-liner:
/// </summary>
/// <remarks>
/// <code>
/// var plan = MergePlan.Create()
///     .AddFile(@"C:\reports\summary.pdf")
///     .AddFile(@"C:\reports\appendix.pdf", pages: "1-3");
/// </code>
/// </remarks>
public static class MergePlanExtensions
{
    extension(MergePlan plan)
    {
        /// <summary>Appends a document stored in a file.</summary>
        /// <param name="path">The path of the file.</param>
        /// <param name="pages">The pages to contribute, in the <c>1,3-5,8-</c> notation; every page when omitted.</param>
        /// <param name="displayName">The name used in messages; defaults to the file name.</param>
        /// <returns>The new plan.</returns>
        /// <exception cref="FormatException"><paramref name="pages"/> is not a valid page selection.</exception>
        public MergePlan AddFile(string path, string? pages = null, string? displayName = null) =>
            plan.AddContent(new PdfFileSource(path, displayName), pages);

        /// <summary>Appends a document that is already in memory.</summary>
        /// <param name="content">The bytes of the document.</param>
        /// <param name="displayName">The name used in messages.</param>
        /// <param name="pages">The pages to contribute; every page when omitted.</param>
        /// <returns>The new plan.</returns>
        /// <exception cref="FormatException"><paramref name="pages"/> is not a valid page selection.</exception>
        public MergePlan AddBytes(byte[] content, string displayName, string? pages = null) =>
            plan.AddContent(new PdfByteArraySource(content, displayName), pages);

        /// <summary>Appends a document that is read from a stream.</summary>
        /// <param name="content">The stream that holds the document; it is read once, at merge time.</param>
        /// <param name="displayName">The name used in messages.</param>
        /// <param name="pages">The pages to contribute; every page when omitted.</param>
        /// <returns>The new plan.</returns>
        /// <exception cref="FormatException"><paramref name="pages"/> is not a valid page selection.</exception>
        public MergePlan AddStream(Stream content, string displayName, string? pages = null) =>
            plan.AddContent(new PdfStreamSource(content, displayName), pages);

        /// <summary>Appends any content source.</summary>
        /// <param name="content">The content of the document.</param>
        /// <param name="pages">The pages to contribute; every page when omitted.</param>
        /// <returns>The new plan.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
        /// <exception cref="FormatException"><paramref name="pages"/> is not a valid page selection.</exception>
        public MergePlan AddContent(IPdfContentSource content, string? pages = null)
        {
            ArgumentNullException.ThrowIfNull(content);

            return plan.AddSource(PdfSource.Create(content, pages: SelectPages(pages)));
        }
    }

    /// <summary>Reads a page selection, treating an empty text as "every page".</summary>
    /// <param name="pages">The text to read.</param>
    /// <returns>The selection, or <see langword="null"/> when every page is meant.</returns>
    private static PageSelection? SelectPages(string? pages) =>
        string.IsNullOrWhiteSpace(pages) ? null : PageSelection.Parse(pages);
}
