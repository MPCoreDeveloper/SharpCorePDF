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
/// The output intent of the result is sRGB, written from a profile the library builds itself, unless
/// <see cref="OutputIntent"/> supplies one: that intent is written as given.
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

    /// <summary>
    /// Gets the output intent the result states, or <see langword="null"/> for the sRGB profile the library
    /// writes itself.
    /// </summary>
    /// <remarks>
    /// A caller that archives for print supplies the profile its own workflow is calibrated against. The
    /// intent is written as supplied, and the pages keep the colour operators they already had, so a CMYK
    /// intent on a document that paints in device RGB is a mismatch a validator reports — not something this
    /// library converts silently.
    /// </remarks>
    public PdfOutputIntent? OutputIntent { get; init; }

    /// <summary>
    /// Gets the relationship the result states between itself and each embedded file.
    /// </summary>
    /// <remarks>
    /// Only PDF/A-3 allows embedded files, and it requires each one to be associated with the document and
    /// to name that association. The default states <c>/Unspecified</c>: a conversion that has not been told
    /// what a file means should not invent an answer. An embedded file that already names a relationship
    /// keeps it.
    /// </remarks>
    public PdfAssociatedFileRelationship AssociatedFileRelationship { get; init; }

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

        if (OutputIntent is { } intent)
        {
            errors.AddRange(intent.Validate());
        }

        if (!Enum.IsDefined(AssociatedFileRelationship))
        {
            errors.Add(
                $"the associated-file relationship '{(int)AssociatedFileRelationship}' is not defined.");
        }

        return errors;
    }
}
