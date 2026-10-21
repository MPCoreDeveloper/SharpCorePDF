namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Controls what an edit writes to the resulting document.
/// </summary>
/// <remarks>
/// The defaults are chosen so that an edit preserves as much of the source as it can: the document
/// information of the source is kept unless a value overrides it, and the modification date becomes
/// the moment the edit ran.
/// </remarks>
public sealed record PdfEditOutputOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfEditOutputOptions Default { get; } = new();

    /// <summary>Gets the PDF version written to the result. Defaults to PDF 1.7.</summary>
    public PdfVersion TargetVersion { get; init; } = PdfVersion.V1_7;

    /// <summary>
    /// Gets document information values that override the values of the source. Only the fields that
    /// are set on this instance are applied; a field left <see langword="null"/> keeps its source value.
    /// </summary>
    public PdfMetadata? Metadata { get; init; }

    /// <summary>
    /// Gets the value written as the modification date of the result. When <see langword="null"/> the
    /// current UTC time is used; supplying a value makes the output reproducible byte for byte.
    /// </summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>Gets the value written to the <c>/Producer</c> entry of the result.</summary>
    public string Producer { get; init; } = ProductInfo.DefaultProducer;

    /// <summary>
    /// Gets an optional identity for the result. When set, the same value together with the same
    /// modification date and the same source produces the same document identifier.
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>Validates these options.</summary>
    /// <returns>Every problem found, or an empty list when the options are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (TargetVersion.CompareTo(PdfVersion.V1_4) < 0)
        {
            errors.Add(
                $"target version {TargetVersion} is not supported: the engine writes cross-reference tables and requires PDF 1.4 or newer.");
        }

        if (TargetVersion.CompareTo(PdfVersion.V2_0) > 0)
        {
            errors.Add($"target version {TargetVersion} is not a known PDF version.");
        }

        if (Producer is not null && string.IsNullOrWhiteSpace(Producer))
        {
            errors.Add("the producer must be a non-empty string when it is set.");
        }

        if (DocumentId is not null && string.IsNullOrWhiteSpace(DocumentId))
        {
            errors.Add("the document identifier must be a non-empty string when it is set.");
        }

        return errors;
    }
}
