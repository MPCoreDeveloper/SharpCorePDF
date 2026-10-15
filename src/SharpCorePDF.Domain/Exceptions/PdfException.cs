namespace SharpCorePDF.Domain.Exceptions;

/// <summary>
/// Base class for every error that SharpCorePDF raises.
/// </summary>
/// <remarks>
/// All public API surfaces throw either this type (or a derived type) for PDF related problems,
/// or a standard <see cref="ArgumentException"/> family member for programmer errors such as a
/// null argument.
/// </remarks>
public abstract class PdfException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    protected PdfException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    protected PdfException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
