namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// One non-fatal remark about a document or a merge: something the reader repaired, skipped or could
/// not open, or something the merge had to change.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is the contract and <see cref="Message"/> is for humans. Two warnings are equal
/// when both are equal, which is what lets a merge fold the remarks of its sources into the result
/// without duplicating them.
/// </remarks>
public sealed record PdfWarning
{
    /// <summary>Initializes a new instance of the <see cref="PdfWarning"/> class.</summary>
    /// <param name="kind">The kind of the remark.</param>
    /// <param name="message">
    /// The human-readable text, which names the source and, where applicable, the object or page the
    /// remark is about.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="message"/> is null, empty or whitespace.</exception>
    public PdfWarning(PdfWarningKind kind, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
        Message = message;
    }

    /// <summary>Gets the kind of the remark, which is the part of a warning that is a contract.</summary>
    public PdfWarningKind Kind { get; }

    /// <summary>
    /// Gets the text of the remark. It names the source and what was done about it, and it may be
    /// reworded in any release: no behaviour and no test may depend on its exact wording.
    /// </summary>
    public string Message { get; }

    /// <summary>Formats the remark for diagnostics.</summary>
    /// <returns>The message.</returns>
    public override string ToString() => Message;
}
