using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Draws the shape surface of a page: a capped and dashed rule, a path with a hole, a clipping region and a
/// group of shapes placed by one transform.
/// </summary>
/// <remarks>
/// Everything is emitted in the order the calls are made, so the file can be read as the drawing was written:
/// a clip is opened, the stripes are cut at it, the state is restored, and the last group is moved and turned
/// once instead of having every coordinate computed by hand.
/// </remarks>
internal static class DrawingExamples
{
    /// <summary>Writes one page of shapes and reads it back through the inspector.</summary>
    /// <param name="context">The ports the example needs and the directory it writes to.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task DrawShapesAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var heading = PdfTypeface.Standard(StandardFont.Helvetica);
        var navy = PdfColor.FromRgb(0x1F, 0x3A, 0x5F);
        var box = new PdfRectangle(330, 160, 535, 260);

        var document = PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with
        {
            CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DocumentId = "00112233445566778899AABBCCDDEEFF",
        })
            .AddPage(PdfPageSize.A4, page =>
            {
                page.DrawText("Drawing", 60, 80, heading, 24)

                    // Round caps hide the ends of the dashes; the phase starts the rule five points into
                    // the pattern, so the first dash is half length rather than full.
                    .DrawLine(
                        new PdfPoint(60, 120),
                        new PdfPoint(535, 120),
                        new PdfStroke(
                            PdfPaint.Black,
                            width: 3,
                            cap: PdfLineCap.Round,
                            dashArray: [10, 4],
                            dashPhase: 5))

                    // Two subpaths under the even-odd rule: the inner box is a hole in the outer one rather
                    // than a second fill painted on top of it.
                    .DrawPath(
                        new PdfPath(
                            [
                                new PdfPathSegment.MoveTo(new PdfPoint(60, 160)),
                                new PdfPathSegment.LineTo(new PdfPoint(260, 160)),
                                new PdfPathSegment.LineTo(new PdfPoint(260, 260)),
                                new PdfPathSegment.LineTo(new PdfPoint(60, 260)),
                                new PdfPathSegment.MoveTo(new PdfPoint(110, 190)),
                                new PdfPathSegment.LineTo(new PdfPoint(210, 190)),
                                new PdfPathSegment.LineTo(new PdfPoint(210, 230)),
                                new PdfPathSegment.LineTo(new PdfPoint(110, 230)),
                            ],
                            isClosed: true),
                        new PdfPaint(navy, fillRule: PdfFillRule.EvenOdd),
                        new PdfStroke(PdfPaint.Black, width: 1.5, join: PdfLineJoin.Bevel, miterLimit: 4));

                // A clip inside a saved state: every stripe is drawn across the whole column, and the ones
                // outside the box never appear. Restoring the state lifts the clip again.
                page.SaveState()
                    .SetClip(
                        new PdfPath(
                            [
                                new PdfPathSegment.MoveTo(new PdfPoint(box.Left, box.Bottom)),
                                new PdfPathSegment.LineTo(new PdfPoint(box.Right, box.Bottom)),
                                new PdfPathSegment.LineTo(new PdfPoint(box.Right, box.Top)),
                                new PdfPathSegment.LineTo(new PdfPoint(box.Left, box.Top)),
                            ],
                            isClosed: true))
                    .DrawRectangle(box, new PdfPaint(PdfColor.FromRgb(0xEC, 0xEF, 0xF4)), stroke: null);

                var stripe = new PdfStroke(new PdfPaint(navy, alpha: 0.55), width: 2, cap: PdfLineCap.Square);

                for (var offset = 0; offset < box.Height; offset += 8)
                {
                    page.DrawLine(
                        new PdfPoint(box.Left - 20, box.Bottom + offset),
                        new PdfPoint(box.Right + 20, box.Bottom + offset),
                        stripe);
                }

                page.RestoreState();

                // One transform places a whole group: the box and its label are drawn in local coordinates
                // around (0, 0), turned fifteen degrees about that origin and then moved down the page.
                // `a.Multiply(b)` applies a first, so the rotation comes before the translation.
                page.SaveState()
                    .SetTransform(PdfMatrix.Rotate(-15).Multiply(PdfMatrix.Translate(120, 470)))
                    .DrawRectangle(
                        new PdfRectangle(0, 0, 240, 70),
                        new PdfPaint(navy, alpha: 0.12),
                        new PdfStroke(PdfPaint.Black, width: 1))
                    .DrawText("Rotated by one transform", 16, 42, heading, 14)
                    .RestoreState();
            })
            .Build("shapes.pdf");

        var output = context.OutputPath("shapes.pdf");
        await File.WriteAllBytesAsync(output, document.Bytes.ToArray(), cancellationToken);

        var info = await context.Inspector.InspectAsync(document, cancellationToken: cancellationToken);

        Report.Item("wrote", output);
        Report.Item("pages", info.PageCount);
        Report.Item("size", info.Pages[0].Size);
        Report.Item("bytes", document.Length);
    }
}
