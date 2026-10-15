using System.Globalization;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the page size: the units a caller may use, the dimensions the ISO and US paper sizes have, and
/// the dimensions a dimension may not have.
/// </summary>
/// <remarks>
/// A page size is held exactly as the document stores it, so every conversion has to be reversible enough
/// for a merge to write the same <c>/MediaBox</c> back that it read.
/// </remarks>
public sealed class PdfPageSizeTests
{
    /// <summary>The three units a caller may build a size from describe the same page.</summary>
    [Fact]
    public void Creates_a_size_from_points_millimetres_and_inches()
    {
        var points = PdfPageSize.FromPoints(612d, 792d);
        var inches = PdfPageSize.FromInches(8.5d, 11d);
        var millimetres = PdfPageSize.FromMillimeters(215.9d, 279.4d);

        Assert.Equal(points, inches);
        Assert.Equal(612d, points.WidthPoints, 0.001d);
        Assert.Equal(792d, points.HeightPoints, 0.001d);
        Assert.Equal(8.5d, points.WidthInches, 0.0001d);
        Assert.Equal(11d, points.HeightInches, 0.0001d);
        Assert.Equal(215.9d, points.WidthMillimeters, 0.01d);
        Assert.Equal(279.4d, points.HeightMillimeters, 0.01d);
        Assert.Equal(points.WidthPoints, millimetres.WidthPoints, 0.001d);
        Assert.Equal(points.HeightPoints, millimetres.HeightPoints, 0.001d);
    }

    /// <summary>The paper sizes the library names are the sizes a reader expects.</summary>
    [Fact]
    public void Exposes_the_paper_sizes_it_names()
    {
        Assert.Equal(419.53d, PdfPageSize.A5.WidthPoints, 0.01d);
        Assert.Equal(595.28d, PdfPageSize.A4.WidthPoints, 0.01d);
        Assert.Equal(841.89d, PdfPageSize.A4.HeightPoints, 0.01d);
        Assert.Equal(841.89d, PdfPageSize.A3.WidthPoints, 0.01d);
        Assert.Equal(1190.55d, PdfPageSize.A3.HeightPoints, 0.01d);
        Assert.Equal(612d, PdfPageSize.Letter.WidthPoints, 0.001d);
        Assert.Equal(792d, PdfPageSize.Letter.HeightPoints, 0.001d);
        Assert.Equal(1008d, PdfPageSize.Legal.HeightPoints, 0.001d);
        Assert.Equal(72d, PdfPageSize.PointsPerInch, 0.001d);
        Assert.Equal(25.4d, PdfPageSize.MillimetersPerInch, 0.001d);
    }

    /// <summary>A dimension that is not a finite, positive number is refused.</summary>
    /// <param name="width">The width to try.</param>
    /// <param name="height">The height to try.</param>
    /// <param name="parameterName">The parameter the exception must name.</param>
    [Theory]
    [InlineData(0d, 100d, "widthPoints")]
    [InlineData(-1d, 100d, "widthPoints")]
    [InlineData(double.NaN, 100d, "widthPoints")]
    [InlineData(double.PositiveInfinity, 100d, "widthPoints")]
    [InlineData(100d, 0d, "heightPoints")]
    [InlineData(100d, -1d, "heightPoints")]
    [InlineData(100d, double.NaN, "heightPoints")]
    [InlineData(100d, double.PositiveInfinity, "heightPoints")]
    public void Rejects_a_dimension_that_is_not_positive(double width, double height, string parameterName) =>
        Assert.Equal(
            parameterName,
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPageSize(width, height)).ParamName);

    /// <summary>A square page counts as portrait, and only quarter turns transpose it.</summary>
    [Fact]
    public void Reports_orientation_and_transposition()
    {
        var portrait = PdfPageSize.FromPoints(10d, 20d);
        var square = PdfPageSize.FromPoints(10d, 10d);

        Assert.True(portrait.IsPortrait);
        Assert.False(portrait.IsLandscape);
        Assert.True(square.IsPortrait);
        Assert.False(square.IsLandscape);
        Assert.True(PdfPageSize.A4.Rotate(PageRotation.Clockwise90).IsLandscape);
        Assert.True(PdfPageSize.A4.Rotate(PageRotation.Clockwise270).IsLandscape);

        Assert.Equal(PdfPageSize.FromPoints(20d, 10d), portrait.Transpose());
        Assert.Equal(portrait, portrait.Rotate(PageRotation.None));
        Assert.Equal(portrait, portrait.Rotate(PageRotation.Clockwise180));
        Assert.Equal(portrait.Transpose(), portrait.Rotate(PageRotation.Clockwise90));
        Assert.Equal(portrait, portrait.Rotate(PageRotation.Clockwise90).Rotate(PageRotation.Clockwise90));
    }

    /// <summary>A size formats as its width and its height in points.</summary>
    /// <remarks>
    /// The culture is pinned because the text of the size is arithmetic on document units, and a document
    /// does not change because the machine it is merged on uses a comma as its decimal separator.
    /// </remarks>
    [Fact]
    public void Formats_a_size()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            Assert.Equal("595.28 x 841.89 pt", PdfPageSize.A4.ToString());
            Assert.Equal("10 x 20 pt", PdfPageSize.FromPoints(10d, 20d).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Two sizes with the same dimensions are the same size.</summary>
    [Fact]
    public void Compares_sizes_by_their_dimensions() =>
        Assert.Equal(PdfPageSize.Letter, PdfPageSize.FromPoints(612d, 792d));
}
