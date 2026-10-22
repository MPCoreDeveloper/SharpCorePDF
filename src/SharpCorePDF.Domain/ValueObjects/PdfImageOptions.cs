namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Optional hints for placing an image. The image's own resolution metadata is used when
/// <see cref="Resolution"/> is null.
/// </summary>
public sealed record PdfImageOptions
{
    /// <summary>Initializes a new instance of the <see cref="PdfImageOptions"/> record.</summary>
    /// <param name="resolution">The resolution in dots per inch, or null to use the image metadata.</param>
    /// <param name="interpolate">Whether a viewer may smooth the image when it is scaled.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolution"/> is not finite or not greater than zero.</exception>
    public PdfImageOptions(double? resolution = null, bool interpolate = true)
    {
        if (resolution is { } value && (!double.IsFinite(value) || value <= 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution),
                resolution,
                "An image resolution must be finite and greater than zero.");
        }

        Resolution = resolution;
        Interpolate = interpolate;
    }

    /// <summary>Gets the default options: use the image's metadata and allow interpolation.</summary>
    public static PdfImageOptions Default { get; } = new();

    /// <summary>Gets the resolution in dots per inch, or null to use the image metadata.</summary>
    public double? Resolution { get; }

    /// <summary>Gets a value indicating whether a viewer may smooth the image when it is scaled.</summary>
    public bool Interpolate { get; }
}
