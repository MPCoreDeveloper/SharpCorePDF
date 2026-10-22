using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the content-stream layer: the geometry values, the operators a path writes, and the
/// graphics state that a page hands to the next drawing block.
/// </summary>
/// <remarks>
/// The assertions compare the exact bytes of a stream. A content stream is the contract between the
/// authoring surface and every reader, so a number that formats differently or an operator that is
/// written in the wrong order is a defect even when a viewer happens to tolerate it.
/// </remarks>
public sealed class PdfContentStreamBuilderTests
{
    private static string Text(ContentStreamBuilder builder) => Encoding.ASCII.GetString(builder.Build());

    private static PdfPath Triangle() => new(
    [
        new PdfPathSegment.MoveTo(new PdfPoint(10, 20)),
        new PdfPathSegment.LineTo(new PdfPoint(110, 20)),
        new PdfPathSegment.LineTo(new PdfPoint(60, 90)),
    ], isClosed: true);

    /// <summary>A translation applied first and a scale applied second compose in PDF order.</summary>
    [Fact]
    public void Composes_transforms_in_pdf_order()
    {
        var matrix = PdfMatrix.Translate(10, 20).Multiply(PdfMatrix.Scale(2));

        Assert.Equal(PdfMatrix.Identity, PdfMatrix.Identity.Multiply(PdfMatrix.Identity));
        Assert.Equal(new PdfPoint(22, 42), matrix.Transform(new PdfPoint(1, 1)));
        Assert.Equal(new PdfPoint(20, 40), matrix.Transform(PdfPoint.Origin));
        Assert.Equal(0.0, PdfMatrix.Rotate(90).Transform(new PdfPoint(1, 0)).X, 12);
        Assert.Equal(1.0, PdfMatrix.Rotate(90).Transform(new PdfPoint(1, 0)).Y, 12);
    }

    /// <summary>A path writes each segment as the operator PDF defines for it.</summary>
    [Fact]
    public void Writes_path_segments_and_a_fill()
    {
        var builder = new ContentStreamBuilder();

        builder.Path()
            .MoveTo(new PdfPoint(10, 20))
            .LineTo(new PdfPoint(110, 20))
            .CubicTo(new PdfPoint(120, 20), new PdfPoint(120, 70), new PdfPoint(60, 90))
            .QuadraticTo(new PdfPoint(70, 100), new PdfPoint(80, 110))
            .Close()
            .Fill(PdfPaint.Black);

        Assert.Equal(
            "10 20 m\n110 20 l\n120 20 120 70 60 90 c\n"
            + "66.66667 96.66667 73.33333 103.33333 80 110 c\nh\n0 0 0 rg\nf\n",
            Text(builder));
    }

    /// <summary>A closed path that arrives without a close segment still writes <c>h</c>.</summary>
    [Fact]
    public void Closes_a_path_that_does_not_end_with_a_close_segment()
    {
        var builder = new ContentStreamBuilder();

        builder.Append(Triangle());

        Assert.Equal("10 20 m\n110 20 l\n60 90 l\nh\n", Text(builder));
    }

    /// <summary>A rectangle is written with <c>re</c>, and the even-odd rule selects <c>W*</c>.</summary>
    [Fact]
    public void Writes_a_rectangle_as_a_clip()
    {
        var builder = new ContentStreamBuilder();
        var path = new PdfPath([new PdfPathSegment.MoveTo(PdfPoint.Origin)], isClosed: true);

        builder.SetClip(path, PdfFillRule.EvenOdd);

        Assert.Equal("0 0 m\nh\nW*\nn\n", Text(builder));

        var rectangle = new ContentStreamBuilder();
        rectangle.Path().Rectangle(5, 10, 100, 50).EndPath();
        Assert.Equal("5 10 100 50 re\nn\n", Text(rectangle));
    }

    /// <summary>Fill and stroke write their colour, their line style and the combined <c>B</c>.</summary>
    [Fact]
    public void Paints_a_fill_and_a_stroke_together()
    {
        var builder = new ContentStreamBuilder();
        var fill = new PdfPaint(PdfColor.FromRgb(255, 0, 0));
        var stroke = new PdfStroke(
            new PdfPaint(PdfColor.FromRgb(0, 0, 255)),
            width: 2,
            cap: PdfLineCap.Round,
            join: PdfLineJoin.Bevel,
            miterLimit: 4,
            dashArray: [3, 1],
            dashPhase: 1);

        builder.DrawPath(Triangle(), fill, stroke);

        Assert.Equal(
            "10 20 m\n110 20 l\n60 90 l\nh\n"
            + "1 0 0 rg\n"
            + "2 w\n1 J\n2 j\n4 M\n[3 1] 1 d\n0 0 1 RG\nB\n",
            Text(builder));
    }

    /// <summary>An even-odd fill selects <c>f*</c>, and a stroke without a fill selects <c>S</c>.</summary>
    [Fact]
    public void Selects_the_operator_for_the_paint_combination()
    {
        var evenOdd = new ContentStreamBuilder();
        evenOdd.DrawPath(Triangle(), new PdfPaint(PdfColor.Black, fillRule: PdfFillRule.EvenOdd), null);
        Assert.EndsWith("f*\n", Text(evenOdd), StringComparison.Ordinal);

        var stroked = new ContentStreamBuilder();
        stroked.DrawPath(Triangle(), null, new PdfStroke(PdfPaint.Black));
        Assert.EndsWith("S\n", Text(stroked), StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(
            () => new ContentStreamBuilder().Path().MoveTo(PdfPoint.Origin).Paint(null, null));
    }

    /// <summary>An identical transparency state is registered once, however often it is selected.</summary>
    [Fact]
    public void Registers_an_identical_extgstate_once()
    {
        var builder = new ContentStreamBuilder();

        builder.SetAlpha(0.5, PdfBlendMode.Multiply);
        builder.SetAlpha(0.5, PdfBlendMode.Multiply);

        Assert.Single(builder.Resources.ExtGStates);
        Assert.Equal("GS1", builder.Resources.ExtGStates[0].Name);
        Assert.Equal(0.5, builder.Resources.ExtGStates[0].Dictionary.GetNumber("ca"));
        Assert.Equal(0.5, builder.Resources.ExtGStates[0].Dictionary.GetNumber("CA"));
        Assert.Equal("Multiply", builder.Resources.ExtGStates[0].Dictionary.GetName("BM"));
        Assert.Equal("/GS1 gs\n", Text(builder));

        builder.SetAlpha(0.25);
        Assert.Equal(2, builder.Resources.ExtGStates.Count);
        Assert.Equal("GS2", builder.Resources.ExtGStates[1].Name);
    }

    /// <summary>Stroke adjustment is written only when the caller requests or suppresses it.</summary>
    [Fact]
    public void Maps_stroke_adjustment_to_the_extgstate_dictionary()
    {
        var builder = new ContentStreamBuilder();
        builder.DrawPath(
            Triangle(),
            null,
            new PdfStroke(PdfPaint.Black, strokeAdjustment: PdfStrokeAdjustment.Enabled));

        var resource = Assert.Single(builder.Resources.ExtGStates);
        var adjustment = Assert.IsType<PdfBoolean>(resource.Dictionary.Get("SA"));
        Assert.True(adjustment.Value);

        var defaulted = new ContentStreamBuilder();
        defaulted.DrawPath(
            Triangle(),
            null,
            new PdfStroke(PdfPaint.Black, strokeAdjustment: PdfStrokeAdjustment.Default));

        Assert.True(defaulted.Resources.IsEmpty);
    }

    /// <summary>A default state needs no <c>/ExtGState</c>, so an opaque drawing stays small.</summary>
    [Fact]
    public void Does_not_register_transparency_for_an_opaque_state()
    {
        var builder = new ContentStreamBuilder();

        builder.SetAlpha(1.0);
        builder.DrawPath(Triangle(), PdfPaint.Black, new PdfStroke(PdfPaint.Black));

        Assert.True(builder.Resources.IsEmpty);
        Assert.DoesNotContain(" gs\n", Text(builder), StringComparison.Ordinal);
    }

    /// <summary>A save is restored with its transform, fill and clip intact.</summary>
    [Fact]
    public void Restores_the_saved_graphics_state()
    {
        var builder = new ContentStreamBuilder();
        var clip = new PdfPath([new PdfPathSegment.MoveTo(PdfPoint.Origin)], isClosed: true);

        builder.Transform(PdfMatrix.Translate(10, 20));
        builder.SaveState();
        builder.Transform(PdfMatrix.Scale(2));
        builder.SetFill(new PdfPaint(PdfColor.White));
        builder.SetClip(clip);
        builder.RestoreState();

        Assert.Equal(PdfMatrix.Translate(10, 20), builder.CurrentState.Transform);
        Assert.Equal(PdfPaint.Black, builder.CurrentState.Fill);
        Assert.Null(builder.CurrentState.ClipPath);
        Assert.Contains("q\n2 0 0 2 0 0 cm\n", Text(builder), StringComparison.Ordinal);
    }

    /// <summary>A restore without a save, and a stream that ends inside a save, are both refused.</summary>
    [Fact]
    public void Refuses_an_unbalanced_graphics_state()
    {
        Assert.Throws<InvalidOperationException>(() => new ContentStreamBuilder().RestoreState());

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        Assert.Throws<InvalidOperationException>(() => builder.Build());

        builder.RestoreState();
        Assert.Equal("q\nQ\n", Text(builder));
    }

    /// <summary>Colour spaces, device components and named separations write their own operators.</summary>
    [Fact]
    public void Writes_colour_space_and_component_operators()
    {
        var builder = new ContentStreamBuilder();

        builder.SetColorSpace(PdfColorSpace.DeviceRGB);
        builder.SetColor([1, 0.5, 0]);
        builder.SetColorSpace(PdfColorSpace.DeviceGray, fill: false);
        builder.SetColor([0.25], fill: false);
        builder.SetColor("Spot", [0, 0, 0, 1], fill: false);

        Assert.Equal(
            "/DeviceRGB cs\n1 0.5 0 sc\n/DeviceGray CS\n0.25 SC\n0 0 0 1 /Spot SCN\n",
            Text(builder));

        Assert.Throws<ArgumentException>(() => builder.SetColor([0.5, 0.5]));
        Assert.Throws<ArgumentException>(() => builder.SetColor([double.NaN, 0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.SetColorSpace((PdfColorSpace)99));
    }

    /// <summary>Values that PDF cannot represent are refused where they are supplied.</summary>
    [Fact]
    public void Refuses_values_that_pdf_cannot_represent()
    {
        var builder = new ContentStreamBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetLineWidth(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetAlpha(1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetAlpha(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetDash([1], double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => builder.SetDash([0, 0], 0));
        Assert.Throws<ArgumentException>(() => builder.SetDash([1, -1], 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfStroke(PdfPaint.Black, miterLimit: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfPoint(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PdfPaint(PdfColor.Black, alpha: 2));
    }
}
