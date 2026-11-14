using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the page canvas that read the operations it records rather than the bytes a writer makes of
/// them: the guards a caller can reach, the geometry a drawing call records, and the state counter behind
/// <c>q</c> and <c>Q</c>.
/// </summary>
/// <remarks>
/// <c>PdfDocumentBuilderTests.Drawing</c> covers the content stream those operations become. These tests
/// pin the canvas's own contract, which is where the mutation survivors in <c>PdfPageCanvas.cs</c> were.
/// </remarks>
public sealed class PdfPageCanvasTests
{
    private static PdfTypeface Face() => PdfTypeface.Standard(StandardFont.Helvetica);

    private static PdfPaint Fill() => new(PdfColor.FromRgb(0x1F, 0x3A, 0x5F));

    private static PdfStroke Outline() => new(PdfPaint.Black, width: 1.5);

    private static PdfPath Path() =>
        new([new PdfPathSegment.MoveTo(new PdfPoint(0, 0)), new PdfPathSegment.LineTo(new PdfPoint(10, 10))]);

    private static PdfPageCanvas Canvas(Action<PdfPageCanvas>? draw = null)
    {
        PdfPageCanvas? canvas = null;
        PdfDocumentBuilder.Create().AddPage(PdfPageSize.A4, page =>
        {
            canvas = page;
            draw?.Invoke(page);
        });

        return canvas!;
    }

    /// <summary>An empty string records no text operation: there is nothing to write.</summary>
    [Fact]
    public void Records_nothing_for_empty_text()
    {
        var canvas = Canvas(page => page.DrawText(string.Empty, 10, 20, Face(), 12));

        Assert.Empty(canvas.Operations);
    }

    /// <summary>Text with no colour is black.</summary>
    [Fact]
    public void Defaults_the_text_colour_to_black()
    {
        var canvas = Canvas(page => page.DrawText("x", 10, 20, Face(), 12));

        var text = Assert.IsType<TextOperation>(Assert.Single(canvas.Operations));

        Assert.Equal(PdfColor.Black, text.Color);
    }

    /// <summary>Text that names a colour keeps it rather than falling back to black.</summary>
    [Fact]
    public void Keeps_the_text_colour_it_was_given()
    {
        var red = PdfColor.FromRgb(0xFF, 0x00, 0x00);

        var canvas = Canvas(page => page.DrawText("x", 10, 20, Face(), 12, red));

        var text = Assert.IsType<TextOperation>(Assert.Single(canvas.Operations));

        Assert.Equal(red, text.Color);
    }

    [Fact]
    public void Refuses_a_null_text() =>
        Assert.Throws<ArgumentNullException>(() => Canvas().DrawText(null!, 0, 0, Face(), 12));

    [Fact]
    public void Refuses_a_null_typeface() =>
        Assert.Throws<ArgumentNullException>(() => Canvas().DrawText("x", 0, 0, null!, 12));

    [Fact]
    public void Refuses_a_non_finite_position() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas().DrawText("x", double.NaN, 0, Face(), 12));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void Refuses_a_font_size_that_is_not_positive_and_finite(double size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas().DrawText("x", 0, 0, Face(), size));

    /// <summary>
    /// The ellipse is four cubic curves with the exact control points the bounds produce: a flipped sign or
    /// a doubled radius moves one of them.
    /// </summary>
    [Fact]
    public void Records_an_ellipse_as_four_bezier_curves_at_the_bounds_it_was_given()
    {
        PdfPageCanvas? canvas = null;
        PdfDocumentBuilder.Create().AddPage(PdfPageSize.A4, page =>
        {
            canvas = page;
            page.DrawEllipse(new PdfRectangle(100, 100, 200, 160), Fill(), stroke: null);
        });

        var path = Assert.IsType<PathOperation>(Assert.Single(canvas!.Operations)).Path;

        const double Control = 0.5522847498307936;
        var cx = 150d;
        var cy = 130d;
        var rx = 50d;
        var ry = 30d;
        var kx = rx * Control;
        var ky = ry * Control;

        PdfPathSegment[] expected =
        [
            new PdfPathSegment.MoveTo(new PdfPoint(cx + rx, cy)),
            new PdfPathSegment.CubicTo(
                new PdfPoint(cx + rx, cy + ky),
                new PdfPoint(cx + kx, cy + ry),
                new PdfPoint(cx, cy + ry)),
            new PdfPathSegment.CubicTo(
                new PdfPoint(cx - kx, cy + ry),
                new PdfPoint(cx - rx, cy + ky),
                new PdfPoint(cx - rx, cy)),
            new PdfPathSegment.CubicTo(
                new PdfPoint(cx - rx, cy - ky),
                new PdfPoint(cx - kx, cy - ry),
                new PdfPoint(cx, cy - ry)),
            new PdfPathSegment.CubicTo(
                new PdfPoint(cx + kx, cy - ry),
                new PdfPoint(cx + rx, cy - ky),
                new PdfPoint(cx + rx, cy)),
        ];

        Assert.Equal(expected, path.Segments);
        Assert.True(path.IsClosed);
    }

    [Fact]
    public void Refuses_a_line_without_a_stroke() =>
        Assert.Throws<ArgumentNullException>(() =>
            Canvas().DrawLine(new PdfPoint(0, 0), new PdfPoint(1, 1), null!));

    [Fact]
    public void Refuses_a_null_rectangle() =>
        Assert.Throws<ArgumentNullException>(() => Canvas().DrawRectangle(null!, Fill(), null));

    [Fact]
    public void Refuses_a_rectangle_without_paint() =>
        Assert.Throws<ArgumentException>(() =>
            Canvas().DrawRectangle(new PdfRectangle(0, 0, 10, 10), null, null));

    [Fact]
    public void Refuses_a_null_ellipse_bounds() =>
        Assert.Throws<ArgumentNullException>(() => Canvas().DrawEllipse(null!, Fill(), null));

    [Fact]
    public void Refuses_an_ellipse_without_paint() =>
        Assert.Throws<ArgumentException>(() =>
            Canvas().DrawEllipse(new PdfRectangle(0, 0, 10, 10), null, null));

    [Fact]
    public void Refuses_a_null_path() =>
        Assert.Throws<ArgumentNullException>(() => Canvas().DrawPath(null!, Fill(), null));

    [Fact]
    public void Refuses_a_path_without_paint() =>
        Assert.Throws<ArgumentException>(() => Canvas().DrawPath(Path(), null, null));

    [Fact]
    public void Refuses_a_null_clip_path() =>
        Assert.Throws<ArgumentNullException>(() => Canvas().SetClip(null!));

    /// <summary><c>FillPath</c> records a fill; the boolean is not a default.</summary>
    [Fact]
    public void Fill_path_records_a_fill()
    {
        var canvas = Canvas(page => page.FillPath(Path()));

        var operation = Assert.IsType<SelectedColourPathOperation>(Assert.Single(canvas.Operations));

        Assert.False(operation.Stroke);
    }

    /// <summary><c>StrokePath</c> records a stroke, the other value of the same boolean.</summary>
    [Fact]
    public void Stroke_path_records_a_stroke()
    {
        var canvas = Canvas(page => page.StrokePath(Path()));

        var operation = Assert.IsType<SelectedColourPathOperation>(Assert.Single(canvas.Operations));

        Assert.True(operation.Stroke);
    }

    [Fact]
    public void Refuses_to_restore_a_state_that_was_never_saved() =>
        Assert.Throws<InvalidOperationException>(() => Canvas().RestoreState());

    /// <summary>One save allows one restore; the counter is what refuses the second.</summary>
    [Fact]
    public void Refuses_a_second_restore_for_one_save()
    {
        var canvas = Canvas(page =>
        {
            page.SaveState();
            page.RestoreState();
        });

        Assert.Throws<InvalidOperationException>(() => canvas.RestoreState());
    }

    [Fact]
    public void Refuses_a_null_image() =>
        Assert.Throws<ArgumentNullException>(() => Canvas().DrawImage((PdfImage)null!, 0, 0, 10, 10));

    [Theory]
    [InlineData(double.NaN, 0d, 10d, 10d)]
    [InlineData(0d, double.NaN, 10d, 10d)]
    [InlineData(0d, 0d, 0d, 10d)]
    [InlineData(0d, 0d, -1d, 10d)]
    [InlineData(0d, 0d, 10d, 0d)]
    [InlineData(0d, 0d, 10d, -1d)]
    public void Refuses_an_image_placed_beyond_the_page(double x, double y, double width, double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Canvas().DrawImage(ReadOnlyMemory<byte>.Empty, x, y, width, height));

    [Fact]
    public void Refuses_a_non_finite_natural_position() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Canvas().DrawImage(ReadOnlyMemory<byte>.Empty, double.NaN, 0));

    private static PdfImage Png() =>
        PdfImage.FromPng(File.ReadAllBytes(System.IO.Path.Combine(TestAssets.Folder, "parity", "images", "png-rgb.png")));

    [Fact]
    public void Refuses_a_prepared_image_at_a_non_finite_position() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas().DrawImage(Png(), double.NaN, 0, 10, 10));

    [Fact]
    public void Refuses_a_prepared_image_at_a_size_that_is_not_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Canvas().DrawImage(Png(), 0, 0, 0, 10));
}
