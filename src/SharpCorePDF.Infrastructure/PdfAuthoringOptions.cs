using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// Controls the identity and the reproducibility of a document written by <see cref="PdfDocumentBuilder"/>.
/// </summary>
/// <remarks>
/// The defaults describe a document created now, with a document identifier derived from its content.
/// Supplying <see cref="CreationDate"/> — and, when a caller wants a particular value,
/// <see cref="DocumentId"/> — makes two builds of the same pages produce the same bytes, which is what a
/// test that compares files needs.
/// </remarks>
public sealed record PdfAuthoringOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfAuthoringOptions Default { get; } = new();

    /// <summary>
    /// Gets the moment written as the creation and modification date of the document. When
    /// <see langword="null"/> the current UTC time is used.
    /// </summary>
    public DateTimeOffset? CreationDate { get; init; }

    /// <summary>
    /// Gets the document identifier written twice in the trailer, as 32 hexadecimal digits. When
    /// <see langword="null"/> an identifier is derived from the content and <see cref="CreationDate"/>.
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>Gets the value written to the <c>/Producer</c> entry.</summary>
    public string Producer { get; init; } = MergeOutputOptions.Default.Producer;

    /// <summary>Validates these options.</summary>
    /// <returns>Every problem found, or an empty list when the options are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Producer))
        {
            errors.Add("the producer must be a non-empty string when it is set.");
        }

        if (DocumentId is { } documentId && (documentId.Length != 32 || !documentId.All(Uri.IsHexDigit)))
        {
            errors.Add($"the document identifier must be 32 hexadecimal digits, but is '{documentId}'.");
        }

        return errors;
    }
}
