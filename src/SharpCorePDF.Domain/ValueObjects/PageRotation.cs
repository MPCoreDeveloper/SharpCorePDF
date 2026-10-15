namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The rotation of a page as declared by its <c>/Rotate</c> entry, expressed in the clockwise
/// direction in which a viewer must rotate the page before displaying it.
/// </summary>
public enum PageRotation
{
    /// <summary>No rotation.</summary>
    None = 0,

    /// <summary>The page is displayed rotated 90 degrees clockwise.</summary>
    Clockwise90 = 90,

    /// <summary>The page is displayed rotated 180 degrees.</summary>
    Clockwise180 = 180,

    /// <summary>The page is displayed rotated 270 degrees clockwise.</summary>
    Clockwise270 = 270,
}

/// <summary>Helpers for working with <see cref="PageRotation"/> values.</summary>
public static class PageRotationExtensions
{
    extension(PageRotation rotation)
    {
        /// <summary>Gets the rotation in degrees.</summary>
        public int Degrees => (int)rotation;

        /// <summary>Gets a value indicating whether this rotation swaps the width and height of a page when displayed.</summary>
        public bool SwapsPageDimensions =>
            rotation is PageRotation.Clockwise90 or PageRotation.Clockwise270;

        /// <summary>Gets the inverse rotation.</summary>
        public PageRotation Inverted => rotation switch
        {
            PageRotation.None => PageRotation.None,
            PageRotation.Clockwise90 => PageRotation.Clockwise270,
            PageRotation.Clockwise180 => PageRotation.Clockwise180,
            PageRotation.Clockwise270 => PageRotation.Clockwise90,
            _ => PageRotation.None,
        };
    }

    /// <summary>Converts a degree value into a rotation, normalising negative and multi-turn values.</summary>
    /// <param name="degrees">A value that must be a multiple of 90.</param>
    /// <returns>The matching rotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="degrees"/> is not a multiple of 90.</exception>
    public static PageRotation FromDegrees(int degrees) => Normalize(degrees);

    /// <summary>Converts a degree value into a rotation, normalising negative and multi-turn values.</summary>
    /// <param name="degrees">A value that must be a multiple of 90.</param>
    /// <returns>The matching rotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="degrees"/> is not a multiple of 90.</exception>
    public static PageRotation Normalize(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        return normalized switch
        {
            0 => PageRotation.None,
            90 => PageRotation.Clockwise90,
            180 => PageRotation.Clockwise180,
            270 => PageRotation.Clockwise270,
            _ => throw new ArgumentOutOfRangeException(
                nameof(degrees),
                degrees,
                "A page rotation must be a multiple of 90 degrees."),
        };
    }
}
