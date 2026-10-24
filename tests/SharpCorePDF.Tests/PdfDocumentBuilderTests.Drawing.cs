using System.Globalization;
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

    /// <summary>The operands of the one line in the stream that ends with an operator.</summary>
    /// <param name="content">The content stream, read as Latin-1.</param>
    /// <param name="operatorName">The operator, without the space that precedes it.</param>
    /// <returns>The numbers, in the order the writer emitted them.</returns>
    private static double[] OperandsOf(string content, string operatorName)
    {
        var suffix = " " + operatorName;
        var line = Assert.Single(
            content.Split('\n'),
            candidate => candidate.EndsWith(suffix, StringComparison.Ordinal));

        return line[..^suffix.Length]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(operand => double.Parse(operand, CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>Applies a transform the way a viewer does: PDF order, <c>(a·x + c·y + e, b·x + d·y + f)</c>.</summary>
    /// <param name="matrix">The six operands of a <c>cm</c>, in PDF order.</param>
    /// <param name="point">The point to transform.</param>
    /// <returns>The transformed point.</returns>
    private static PdfPoint Apply(double[] matrix, PdfPoint point) =>
        new(
            (matrix[0] * point.X) + (matrix[2] * point.Y) + matrix[4],
            (matrix[1] * point.X) + (matrix[3] * point.Y) + matrix[5]);

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

    /// <summary>The cap, the join and the miter limit reach the stream as the codes PDF numbers them.</summary>
    [Fact]
    public void Writes_the_line_cap_join_and_miter_limit()
    {
        var stroke = new PdfStroke(
            PdfPaint.Black,
            width: 2.5,
            cap: PdfLineCap.Round,
            join: PdfLineJoin.Bevel,
            miterLimit: 4);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawLine(
                new PdfPoint(10, 10),
                new PdfPoint(90, 10),
                stroke))
            .ToArray();

        var content = Content(bytes);

        Assert.Equal(2.5, OperandsOf(content, "w")[0], 3);
        Assert.Equal(1, OperandsOf(content, "J")[0]);  // PdfLineCap.Round
        Assert.Equal(2, OperandsOf(content, "j")[0]);  // PdfLineJoin.Bevel
        Assert.Equal(4, OperandsOf(content, "M")[0]);
    }

    /// <summary>
    /// A dashed stroke writes its pattern and its phase before the paint that applies them; PDF takes the
    /// style at the painting operator, so it may follow the path it styles.
    /// </summary>
    [Fact]
    public void Writes_the_dash_pattern_and_its_phase()
    {
        var stroke = new PdfStroke(PdfPaint.Black, dashArray: [4, 2], dashPhase: 1);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawLine(
                new PdfPoint(10, 10),
                new PdfPoint(90, 10),
                stroke))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("[4 2] 1 d\n", content);
        Assert.True(
            content.IndexOf("[4 2] 1 d\n", StringComparison.Ordinal)
                < content.IndexOf("S\n", StringComparison.Ordinal),
            "The dash pattern must be set before the paint it applies to.");
    }

    /// <summary>A stroke with no pattern writes the empty array that tells a viewer to draw a solid line.</summary>
    [Fact]
    public void Writes_a_solid_line_as_an_empty_dash_array()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawLine(
                new PdfPoint(10, 10),
                new PdfPoint(90, 10),
                new PdfStroke(PdfPaint.Black)))
            .ToArray();

        Assert.Contains("[ ] 0 d\n", Content(bytes));
    }

    /// <summary>Fill and stroke colours are written in the device operator of the space the paint names.</summary>
    [Fact]
    public void Writes_fill_and_stroke_colours_in_their_space()
    {
        var fill = new PdfPaint(PdfColor.FromRgb(0xFF, 0x00, 0x00));
        var stroke = new PdfStroke(new PdfPaint(PdfColor.Black, colorSpace: PdfColorSpace.DeviceGray));

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawRectangle(
                new PdfRectangle(10, 10, 60, 60),
                fill,
                stroke))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("1 0 0 rg\n", content);
        Assert.Contains("0 G\n", content);
    }

    /// <summary>A paint in the CMYK space is written as four components in PDF's own offset conversion.</summary>
    [Fact]
    public void Writes_a_cmyk_paint_as_four_components()
    {
        var fill = new PdfPaint(PdfColor.FromRgb(0xFF, 0x00, 0x00), colorSpace: PdfColorSpace.DeviceCMYK);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawRectangle(
                new PdfRectangle(10, 10, 60, 60),
                fill,
                stroke: null))
            .ToArray();

        var content = Content(bytes);

        Assert.Contains("0 1 1 0 k\n", content);
        Assert.Contains("f\n", content);
    }

    /// <summary>
    /// A translation reaches the page as a <c>cm</c> that takes the page height out of the y axis, which is
    /// what makes a canvas point land where the caller said it would.
    /// </summary>
    [Fact]
    public void Writes_a_translation_as_a_page_space_transform()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetTransform(PdfMatrix.Translate(10, 20))
                .DrawLine(new PdfPoint(0, 0), new PdfPoint(10, 0), Outline()))
            .ToArray();

        var matrix = OperandsOf(Content(bytes), "cm");

        Assert.Equal(6, matrix.Length);
        Assert.Equal(1, matrix[0], 3);
        Assert.Equal(0, matrix[1], 3);
        Assert.Equal(0, matrix[2], 3);
        Assert.Equal(1, matrix[3], 3);
        Assert.Equal(10, matrix[4], 3);
        Assert.Equal(-20, matrix[5], 3);
    }

    /// <summary>A scale is anchored at the top-left corner: the origin of the canvas does not move.</summary>
    [Fact]
    public void Writes_a_scale_that_keeps_the_top_left_corner_still()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetTransform(PdfMatrix.Scale(2, 3))
                .DrawLine(new PdfPoint(0, 0), new PdfPoint(10, 10), Outline()))
            .ToArray();

        var matrix = OperandsOf(Content(bytes), "cm");
        var height = PdfPageSize.A4.HeightPoints;

        Assert.Equal(2, matrix[0], 3);
        Assert.Equal(3, matrix[3], 3);
        Assert.Equal(height, Apply(matrix, new PdfPoint(0, height)).Y, 3);
    }

    /// <summary>
    /// A rotation turns canvas coordinates, not PDF ones: the written <c>cm</c> maps the PDF image of a
    /// point to the PDF image of the point the canvas rotated.
    /// </summary>
    [Fact]
    public void Writes_a_rotation_that_turns_canvas_coordinates()
    {
        var rotation = PdfMatrix.Rotate(90);

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SetTransform(rotation)
                .DrawLine(new PdfPoint(0, 0), new PdfPoint(10, 0), Outline()))
            .ToArray();

        var matrix = OperandsOf(Content(bytes), "cm");
        var height = PdfPageSize.A4.HeightPoints;

        foreach (var point in new[] { new PdfPoint(0, 0), new PdfPoint(10, 0), new PdfPoint(0, 10) })
        {
            var expected = rotation.Transform(point);
            var actual = Apply(matrix, new PdfPoint(point.X, height - point.Y));

            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(height - expected.Y, actual.Y, 3);
        }
    }

    /// <summary>A transform is written after the save that bounds it and before the drawing it applies to.</summary>
    [Fact]
    public void Writes_the_transform_before_the_drawing_it_applies_to()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page
                .SaveState()
                .SetTransform(PdfMatrix.Translate(5, 5))
                .DrawRectangle(new PdfRectangle(0, 0, 50, 50), Fill(), stroke: null)
                .RestoreState())
            .ToArray();

        var content = Content(bytes);
        var save = content.IndexOf("q\n", StringComparison.Ordinal);
        var transform = content.IndexOf(" cm\n", StringComparison.Ordinal);
        var drawing = content.IndexOf(" m\n", StringComparison.Ordinal);
        var restore = content.IndexOf("Q\n", StringComparison.Ordinal);

        Assert.True(save >= 0 && transform >= 0 && drawing >= 0 && restore >= 0, content);
        Assert.True(save < transform, "The save must come before the transform.");
        Assert.True(transform < drawing, "The transform must come before the drawing.");
        Assert.True(drawing < restore, "The restore must come after the drawing.");
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