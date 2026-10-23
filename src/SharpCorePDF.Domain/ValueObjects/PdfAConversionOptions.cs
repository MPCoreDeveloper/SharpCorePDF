namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Controls what a PDF/A conversion writes besides the profile itself: the document information and
/// the identity of the result.
/// </summary>
/// <remarks>
/// <para>
/// The defaults describe an ordinary document converted now. Supplying <see cref="ModificationDate"/>
/// and <see cref="DocumentId"/> makes two conversions of the same source produce the same bytes, which
/// is what a test that compares files needs.
/// </para>
/// <para>
/// The document information of the source is kept unless <see cref="Metadata"/> overrides it, field by
/// field. The profile then requires that information and the XMP packet to agree, which is what the
/// writer guarantees for the fields it writes: title, author, subject, keywords, creator, producer and
/// the two dates. Custom information entries have no home in the packet without an extension schema,
/// so the writer drops them and reports that it did.
/// </para>
/// <para>
/// The output intent of the result is sRGB, written from a profile the library builds itself. A
/// caller-supplied ICC profile is not part of this release.
/// </para>
/// </remarks>
public sealed record PdfAConversionOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfAConversionOptions Default { get; } = new();

    /// <summary>
    /// Gets document information values that override the values of the source. Only the fields that
    /// are set on this instance are applied; a field left <see langword="null"/> keeps its source value.
    /// </summary>
    public PdfMetadata? Metadata { get; init; }

    /// <summary>
    /// Gets the value written as the modification date of the result and mirrored into its XMP packet.
    /// When <see langword="null"/> the current UTC time is used.
    /// </summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>Gets the value written to the <c>/Producer</c> entry of the result.</summary>
    public string Producer { get; init; } = ProductInfo.DefaultProducer;

    /// <summary>
    /// Gets an optional identity for the result, as 32 hexadecimal digits. When set, the same value
    /// together with the same modification date and the same source produces the same document
    /// identifier.
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>Validates these options.</summary>
    /// <returns>Every problem found, or an empty list when the options are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Producer))
        {
            errors.Add("the producer must be a non-empty string.");
        }

        if (DocumentId is { } documentId && (documentId.Length != 32 || !documentId.All(Uri.IsHexDigit)))
        {
            errors.Add($"the document identifier must be 32 hexadecimal digits, but is '{documentId}'.");
        }

        return errors;
    }
}

