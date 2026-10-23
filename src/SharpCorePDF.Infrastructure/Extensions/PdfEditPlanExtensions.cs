using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Extensions;

/// <summary>
/// Extensions that let an edit plan insert a page this assembly drew, so the caller does not have to
/// build a one-page document and wrap it in a source by hand:
/// </summary>
/// <remarks>
/// <code>
/// var plan = PdfEditPlan.Create()
///     .InsertPage(2, PdfPageSize.A4, page => page
///         .DrawText("Chapter 2", 72, 760, PdfTypeface.Standard(StandardFont.HelveticaBold), 18));
/// </code>
/// <para>
/// The drawing is written to memory as a one-page document when the plan is built, so the plan stays
/// a value: it can be shared, applied more than once and applied to different documents.
/// </para>
/// </remarks>
public static class PdfEditPlanExtensions
{
    extension(PdfEditPlan plan)
    {
        /// <summary>Inserts a page that a callback draws.</summary>
        /// <param name="insertAt">The one-based position the page is inserted before.</param>
        /// <param name="size">The size of the page, which the content is drawn at.</param>
        /// <param name="draw">The drawings of the page.</param>
        /// <returns>The new plan.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="draw"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> has no positive width or height.</exception>
        public PdfEditPlan InsertPage(int insertAt, PdfPageSize size, Action<PdfPageCanvas> draw)
        {
            ArgumentNullException.ThrowIfNull(draw);

            var page = PdfDocumentBuilder.Create()
                .AddPage(size, draw)
                .Build($"inserted-page-{insertAt}.pdf");

            return plan.InsertPage(insertAt, size, page);
        }
    }
}
