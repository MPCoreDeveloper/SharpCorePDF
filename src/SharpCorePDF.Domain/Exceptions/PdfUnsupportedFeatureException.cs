namespace SharpCorePDF.Domain.Exceptions;

/// <summary>
/// Thrown when a document uses a PDF feature that the engine deliberately does not implement,
/// such as a compression filter that is not part of the supported set for object streams and
/// cross-reference streams.
/// </summary>
public sealed class PdfUnsupportedFeatureException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="PdfUnsupportedFeatureException"/> class.</summary>
    /// <param name="sourceName">The display name of the source that uses the feature.</param>
    /// <param name="feature">The name of the unsupported feature.</param>
    /// <param name="detail">Additional, actionable detail.</param>
    public PdfUnsupportedFeatureException(string sourceName, string feature, string detail)
        : base($"'{sourceName}' uses an unsupported feature '{feature}': {detail}")
    {
        SourceName = sourceName;
        Feature = feature;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfUnsupportedFeatureException"/> class for a
    /// feature refused while a document is being authored, when there is no source file to name.
    /// </summary>
    /// <param name="feature">The name of the unsupported feature.</param>
    /// <param name="detail">Additional, actionable detail.</param>
    public PdfUnsupportedFeatureException(string feature, string detail)
        : base($"The authored document uses an unsupported feature '{feature}': {detail}")
    {
        SourceName = "the authored document";
        Feature = feature;
    }

    /// <summary>Gets the display name of the source that uses the feature.</summary>
    public string SourceName { get; }

    /// <summary>Gets the name of the unsupported feature.</summary>
    public string Feature { get; }
}
