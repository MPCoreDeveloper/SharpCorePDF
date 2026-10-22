using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;
using PdfMatrix = SharpCorePDF.Domain.ValueObjects.PdfMatrix;

namespace SharpCorePDF.Tests;

/// <summary>Tests of the drawing primitives a page canvas records and the writer serialises.</summary>
/// <remarks>
/// The assertions read the content stream of the authored page as Latin-1 text: the operators are ASCII,
/// and the writer leaves the stream uncompressed, so the bytes are the contract.
/// </remarks>
public sealed partial class PdfDocumentBuilderTests
{
    private static PdfPaint Fill(PdfFillRule fillRule = PdfFillRule.NonZero) =>
        new(PdfColor.FromRgb(0x1F, 0x3A, 0x5F), fillRule: fillRule);

    private static PdfStroke Outline() => new(PdfPaint.Black, width: 1.5, dashArray: [4, 2]);

    private static string Content(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>A line becomes a move, a line and a stroke, in page space.</summary>
    [Fact]
    public void Writes_a_line_as_a_stroked_subpath()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawLine(
                new PdfPoint(10, 20),
                new PdfPoint(30, 40),
                Outline()))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("10 ", content);
        Assert.Contains(" m\n", content);
        Assert.Contains(" l\n", content);
        Assert.Contains("S\n", content);
    }

    /// <summary>A rectangle is a closed subpath painted with both a fill and a stroke.</summary>
    [Fact]
    public void Writes_a_rectangle_as_a_closed_path()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawRectangle(
                new PdfRectangle(10, 20, 110, 70),
                Fill(),
                Outline()))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains(" m\n", content);
        Assert.Contains(" l\n", content);
        Assert.Contains("h\n", content);
        Assert.Contains("B\n", content);
    }

    /// <summary>An ellipse is four cubic curves, filled with the paint it was given.</summary>
    [Fact]
    public void Writes_an_ellipse_as_four_bezier_curves()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawEllipse(
                new PdfRectangle(100, 100, 200, 160),
                Fill(PdfFillRule.EvenOdd),
                stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.Equal(4, content.Split(" c\n").Length - 1);
        Assert.Contains("f*\n", content);
    }

    /// <summary>A caller's path keeps its segments, its fill rule and its stroke.</summary>
    [Fact]
    public void Writes_a_caller_path_with_its_fill_rule()
    {
        var path = new PdfPath(
            [
                new PdfPathSegment.MoveTo(new PdfPoint(10, 10)),
                new PdfPathSegment.LineTo(new PdfPoint(60, 10)),
                new PdfPathSegment.QuadraticTo(new PdfPoint(60, 40), new PdfPoint(30, 40)),
            ]);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawPath(path, Fill(PdfFillRule.EvenOdd), Outline()))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains(" m\n", content);
        Assert.Contains(" l\n", content);
        Assert.Contains(" c\n", content);
        Assert.Contains("B*\n", content);
    }

    /// <summary>A translucent fill selects an ExtGState the page's resources declare.</summary>
    [Fact]
    public void Writes_a_translucent_fill_with_an_extgstate()
    {
        var paint = new PdfPaint(PdfColor.FromRgb(0xFF, 0x00, 0x00), alpha: 0.5);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawRectangle(
                new PdfRectangle(10, 10, 60, 60),
                paint,
                stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("/GS1 gs\n", content);
        Assert.Contains("/ExtGState", content);
        Assert.Contains("/ca 0.5", content);
    }

    /// <summary>Saving and restoring the graphics state is written as the matching operators.</summary>
    [Fact]
    public void Writes_a_balanced_graphics_state()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SaveState()
                .SetTransform(PdfMatrix.Translate(10, 20))
                .DrawRectangle(new PdfRectangle(0, 0, 50, 50), Fill(), stroke: null)
                .RestoreState())
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("q\n", content);
        Assert.Contains(" cm\n", content);
        Assert.Contains("Q\n", content);
    }

    /// <summary>A clipping path is set with the fill rule and ends without painting.</summary>
    [Fact]
    public void Writes_a_clipping_path()
    {
        var clip = new PdfPath(
            [
                new PdfPathSegment.MoveTo(new PdfPoint(20, 20)),
                new PdfPathSegment.LineTo(new PdfPoint(80, 20)),
                new PdfPathSegment.LineTo(new PdfPoint(80, 80)),
            ]);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SaveState()
                .SetClip(clip, PdfFillRule.EvenOdd)
                .DrawLine(new PdfPoint(0, 0), new PdfPoint(100, 100), Outline())
                .RestoreState())
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("\nW*\n", content);
        Assert.Contains("\nn\n", content);
    }

    /// <summary>An unbalanced restore is refused where the caller makes it, and at build time.</summary>
    [Fact]
    public void Refuses_an_unbalanced_restore()
    {
        PdfPageCanvas? canvas = null;
        PdfDocumentBuilder.Create().AddPage(PdfPageSize.A4, page => canvas = page);

        Assert.Throws<InvalidOperationException>(() => canvas!.RestoreState());

        var builder = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.SaveState().DrawLine(
                new PdfPoint(0, 0),
                new PdfPoint(10, 10),
                Outline()));

        Assert.Throws<InvalidOperationException>(() => builder.ToArray());
    }

    /// <summary>Laid-out text is drawn run by run, with the style's text state, and reads back.</summary>
    [Fact]
    public async Task Writes_a_text_layout_run_by_run()
    {
        var style = new PdfTextStyle(
            Helvetica,
            12,
            PdfColor.Black,
            leading: 14,
            characterSpacing: 0.5,
            wordSpacing: 1,
            horizontalScale: 90,
            rise: 2);
        var layout = new PdfTextLayout(
            style,
            [
                new PdfTextLine("Hello world", new PdfPoint(0, 12), 60, 12),
                new PdfTextLine("second line", new PdfPoint(0, 26), 55, 26),
            ],
            new PdfRectangle(0, 0, 60, 30),
            14);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawTextLayout(layout, new PdfPoint(72, 100)))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("0.5 Tc\n", content);
        Assert.Contains("1 Tw\n", content);
        Assert.Contains("90 Tz\n", content);
        Assert.Contains("2 Ts\n", content);

        var text = PlainText(await TextAsync(bytes));
        Assert.Contains("Hello world", text);
        Assert.Contains("second line", text);
    }

    /// <summary>A null layout and a non-finite origin are refused before anything is recorded.</summary>
    [Fact]
    public void Refuses_a_layout_without_a_position()
    {
        var layout = new PdfTextLayout(
            new PdfTextStyle(Helvetica, 12),
            [new PdfTextLine("text", new PdfPoint(0, 12), 20, 12)],
            new PdfRectangle(0, 0, 20, 12),
            12);
        PdfPageCanvas? canvas = null;
        PdfDocumentBuilder.Create().AddPage(PdfPageSize.A4, page => canvas = page);

        Assert.Throws<ArgumentNullException>(() => canvas!.DrawTextLayout(null!, new PdfPoint(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => canvas!.DrawTextLayout(layout, new PdfPoint(double.NaN, 0)));
    }
}