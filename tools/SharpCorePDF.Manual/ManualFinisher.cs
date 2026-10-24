using System.Globalization;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Manual;

/// <summary>
/// The finishing pass: the running footer, the outline and the document metadata.
/// </summary>
/// <remarks>
/// <para>
/// Chromium lays the pages out, but it cannot say "Page 7 of 132" in a footer: a printed page has no
/// counter outside the browser's own header and footer templates, and those carry the file URL and a
/// date as well. So the number is added here, with SharpCorePDF's authoring surface for the overlay
/// and its edit engine for the composition — one one-page overlay per page, composited over it.
/// </para>
/// <para>
/// The outline points at the one-based pages of the file the overlay is applied to, which is the file
/// Chromium printed; the page numbers were measured from that same file, so no renumbering is needed.
/// </para>
/// </remarks>
internal static class ManualFinisher
{
    private const double Margin = 48.2;      // 17mm, the same inset as the stylesheet
    private const double Baseline = 30.0;    // measured up from the bottom edge
    private const double RuleOffset = 12.0;  // the rule sits this far above the baseline
    private const double FooterSize = 8.0;
    private const double RuleWidth = 0.4;

    private static readonly PdfColor RuleColor = new(217, 209, 230);
    private static readonly PdfColor TextColor = new(107, 87, 140);

    private const string Subject =
        "Merging PDFs in .NET: plans, page selections, output policies, encryption, inspection, diagnostics";

    /// <summary>Adds the footer, the outline and the metadata to a printed document.</summary>
    /// <param name="pdf">The document Chromium printed.</param>
    /// <param name="headings">Every heading, in document order.</param>
    /// <param name="pages">The measured one-based page of every anchor.</param>
    /// <param name="identity">The title the metadata states.</param>
    /// <param name="cancellationToken">The token the edit observes.</param>
    /// <returns>The finished document.</returns>
    public static async Task<byte[]> FinishAsync(
        byte[] pdf,
        IReadOnlyList<Heading> headings,
        IReadOnlyDictionary<string, int> pages,
        ManualIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(identity);

        var source = new PdfByteArraySource(pdf, "manual-printed.pdf");
        var info = await PageIndex.ReadInfoAsync(source, cancellationToken).ConfigureAwait(false);
        var typeface = PdfTypeface.Standard(StandardFont.Helvetica);

        var plan = PdfEditPlan.Create();

        // The cover carries no footer, exactly as the Python builder had it.
        for (var page = 2; page <= info.PageCount; page++)
        {
            var size = page - 1 < info.Pages.Count ? info.Pages[page - 1].Size : PdfPageSize.A4;
            var overlay = FooterOverlay(size, page, info.PageCount, identity, typeface);

            plan = plan.Overlay(
                new PdfByteArraySource(overlay, string.Create(CultureInfo.InvariantCulture, $"footer-{page}.pdf")),
                page);
        }

        plan = plan
            .SetOutline(BuildOutline(headings, pages))
            .SetMetadata(new PdfMetadata
            {
                Title = identity.Title,
                Author = "MPCoreDeveloper",
                Subject = Subject,
                Keywords = string.Create(
                    CultureInfo.InvariantCulture,
                    $"SharpCorePDF, PDF, merge, C#, .NET, manual, {identity.Version}"),
                Creator = "SharpCorePDF manual builder (markdown + Chromium + SharpCorePDF)",
                Producer = "SharpCorePDF",
            });

        var editor = new PdfDocumentEditorService(
            new PdfDocumentReader(PdfEngineOptions.Default),
            new PdfEditEngine(PdfEngineOptions.Default));

        return await editor
            .ApplyToArrayAsync(source, plan, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static byte[] FooterOverlay(
        PdfPageSize size,
        int page,
        int total,
        ManualIdentity identity,
        PdfTypeface typeface)
    {
        var builder = PdfDocumentBuilder.Create();

        builder.AddPage(size, canvas =>
        {
            var left = string.Create(
                CultureInfo.InvariantCulture,
                $"{identity.Product} {identity.Version}  |  The Manual");
            var right = string.Create(CultureInfo.InvariantCulture, $"Page {page} of {total}");

            // The authored canvas counts y down from the top-left corner, the printed page counts it up
            // from the bottom-left one, and the footer is defined against the bottom edge.
            var baseline = size.HeightPoints - Baseline;
            var ruleY = baseline - RuleOffset;

            canvas.DrawLine(
                new PdfPoint(Margin, ruleY),
                new PdfPoint(size.WidthPoints - Margin, ruleY),
                new PdfStroke(new PdfPaint(RuleColor), RuleWidth));

            canvas.DrawText(left, Margin, baseline, typeface, FooterSize, TextColor);

            var rightWidth = typeface.Measure(right, FooterSize);
            canvas.DrawText(
                right,
                size.WidthPoints - Margin - rightWidth,
                baseline,
                typeface,
                FooterSize,
                TextColor);
        });

        return builder.ToArray();
    }

    private static PdfOutline BuildOutline(
        IReadOnlyList<Heading> headings,
        IReadOnlyDictionary<string, int> pages)
    {
        var roots = new List<OutlineNode>();
        var byLevel = new Dictionary<int, OutlineNode>();

        foreach (var heading in headings)
        {
            var page = pages.TryGetValue(heading.Anchor, out var measured) ? measured : 1;
            var node = new OutlineNode(heading.Level, heading.Title, page);

            if (heading.Level > 1 && byLevel.TryGetValue(heading.Level - 1, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }

            byLevel[heading.Level] = node;
        }

        return new PdfOutline { Items = [.. roots.Select(ToOutlineItem)] };
    }

    private static PdfOutlineItem ToOutlineItem(OutlineNode node) => new()
    {
        Title = node.Title,
        Destination = new PdfDestination { PageNumber = node.Page },
        IsOpen = true,
        Children = [.. node.Children.Select(ToOutlineItem)],
    };

    private sealed class OutlineNode(int level, string title, int page)
    {
        public int Level { get; } = level;

        public string Title { get; } = title;

        public int Page { get; } = page;

        public List<OutlineNode> Children { get; } = [];
    }
}

