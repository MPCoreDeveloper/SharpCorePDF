namespace SharpCorePDF.Domain.Exceptions;

/// <summary>
/// Thrown when a source is not a readable PDF document, or when its internal structure cannot be
/// trusted enough to be merged (for example a truncated file or a damaged cross-reference table
/// that could not be reconstructed).
/// </summary>
public sealed class PdfSourceFormatException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="PdfSourceFormatException"/> class.</summary>
    /// <param name="sourceName">The display name of the offending source.</param>
    /// <param name="detail">What exactly is wrong with the source.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, if any.</param>
    public PdfSourceFormatException(string sourceName, string detail, Exception? innerException = null)
        : base($"'{sourceName}' is not a readable PDF document: {detail}", innerException)
    {
        SourceName = sourceName;
        Detail = detail;
    }

    /// <summary>Gets the display name of the source that failed to load.</summary>
    public string SourceName { get; }

    /// <summary>Gets the specific reason the source was rejected.</summary>
    public string Detail { get; }
}
