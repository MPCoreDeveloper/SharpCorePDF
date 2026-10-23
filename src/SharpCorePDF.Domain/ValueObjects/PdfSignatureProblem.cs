namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// One thing that keeps a signature from being reported as valid, named by kind and described for a
/// human.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is the contract and <see cref="Message"/> is for humans: it names the field, the
/// certificate and the range the remark is about, and it may be reworded in any release.
/// </remarks>
public sealed record PdfSignatureProblem
{
    /// <summary>Initializes a new instance of the <see cref="PdfSignatureProblem"/> class.</summary>
    /// <param name="kind">The kind of the problem.</param>
    /// <param name="message">The human-readable text.</param>
    /// <exception cref="ArgumentException"><paramref name="message"/> is null, empty or whitespace.</exception>
    public PdfSignatureProblem(PdfSignatureProblemKind kind, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
        Message = message;
    }

    /// <summary>Gets the kind of the problem, which is the part that is a contract.</summary>
    public PdfSignatureProblemKind Kind { get; }

    /// <summary>Gets the text of the problem, which names what it is about.</summary>
    public string Message { get; }

    /// <summary>Formats the problem for diagnostics.</summary>
    /// <returns>The message.</returns>
    public override string ToString() => Message;
}
