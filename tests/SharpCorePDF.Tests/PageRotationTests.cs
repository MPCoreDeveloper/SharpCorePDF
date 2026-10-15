using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the page rotation: the quarter turns a document declares, the arithmetic that keeps them
/// meaningful in both directions, and the values that are not quarter turns at all.
/// </summary>
/// <remarks>
/// A page rotation is stored in a file as an arbitrary number, so the interesting half of this type is
/// its normalisation: several different stored values mean the same quarter turn.
/// </remarks>
public sealed class PageRotationTests
{
    /// <summary>A rotation reports itself in degrees.</summary>
    /// <param name="rotation">The rotation to read.</param>
    /// <param name="degrees">The expected number of degrees.</param>
    [Theory]
    [InlineData(PageRotation.None, 0)]
    [InlineData(PageRotation.Clockwise90, 90)]
    [InlineData(PageRotation.Clockwise180, 180)]
    [InlineData(PageRotation.Clockwise270, 270)]
    public void Reports_the_degrees_of_a_rotation(PageRotation rotation, int degrees) =>
        Assert.Equal(degrees, rotation.Degrees);

    /// <summary>Only the quarter turns transpose the page.</summary>
    /// <param name="rotation">The rotation to read.</param>
    /// <param name="expected">Whether the page is displayed transposed.</param>
    [Theory]
    [InlineData(PageRotation.None, false)]
    [InlineData(PageRotation.Clockwise90, true)]
    [InlineData(PageRotation.Clockwise180, false)]
    [InlineData(PageRotation.Clockwise270, true)]
    public void Reports_whether_a_rotation_transposes_the_page(PageRotation rotation, bool expected) =>
        Assert.Equal(expected, rotation.SwapsPageDimensions);

    /// <summary>Rotating a page back by the inverse rotation brings it home.</summary>
    /// <param name="rotation">The rotation to invert.</param>
    /// <param name="expected">The inverse rotation.</param>
    [Theory]
    [InlineData(PageRotation.None, PageRotation.None)]
    [InlineData(PageRotation.Clockwise90, PageRotation.Clockwise270)]
    [InlineData(PageRotation.Clockwise180, PageRotation.Clockwise180)]
    [InlineData(PageRotation.Clockwise270, PageRotation.Clockwise90)]
    public void Inverts_a_rotation(PageRotation rotation, PageRotation expected)
    {
        Assert.Equal(expected, rotation.Inverted);
        Assert.Equal(PageRotation.None, PageRotationExtensions.Normalize(rotation.Degrees + expected.Degrees));
    }

    /// <summary>Whole turns and negative values are normalised onto the four quarter turns.</summary>
    /// <param name="degrees">The value as a document may store it.</param>
    /// <param name="expected">The rotation it means.</param>
    [Theory]
    [InlineData(0, PageRotation.None)]
    [InlineData(90, PageRotation.Clockwise90)]
    [InlineData(180, PageRotation.Clockwise180)]
    [InlineData(270, PageRotation.Clockwise270)]
    [InlineData(360, PageRotation.None)]
    [InlineData(450, PageRotation.Clockwise90)]
    [InlineData(720, PageRotation.None)]
    [InlineData(-90, PageRotation.Clockwise270)]
    [InlineData(-450, PageRotation.Clockwise270)]
    public void Normalises_a_degree_value(int degrees, PageRotation expected)
    {
        Assert.Equal(expected, PageRotationExtensions.Normalize(degrees));
        Assert.Equal(expected, PageRotationExtensions.FromDegrees(degrees));
    }

    /// <summary>A value that is not a quarter turn is refused instead of being rounded.</summary>
    /// <param name="degrees">The value that is not a quarter turn.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(45)]
    [InlineData(-45)]
    [InlineData(180 + 45)]
    public void Refuses_a_value_that_is_not_a_quarter_turn(int degrees)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => PageRotationExtensions.Normalize(degrees));

        Assert.Equal("degrees", exception.ParamName);
        Assert.Contains("multiple of 90", exception.Message, StringComparison.Ordinal);
    }
}
