namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Controls what a merge writes to the resulting document.
/// </summary>
/// <remarks>
/// The defaults are chosen so that the common case — "glue these documents together and lose as
/// little as possible" — needs no configuration at all.
/// </remarks>
public sealed record MergeOutputOptions
{
    /// <summary>Gets the default options.</summary>
    public static MergeOutputOptions Default { get; } = new();

    /// <summary>
    /// Gets the source whose document level features (metadata, viewer preferences, form fields,
    /// named destinations, page labels) lead the result.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> the first source in the plan is the primary source. Page content,
    /// annotations and bookmarks of <em>every</em> source are always merged; only features that
    /// cannot sensibly exist twice in one document are taken from the primary source alone.
    /// </remarks>
    public PdfSourceId? PrimarySourceId { get; init; }

    /// <summary>Gets the PDF version written to the result. Defaults to PDF 1.7.</summary>
    public PdfVersion TargetVersion { get; init; } = PdfVersion.V1_7;

    /// <summary>Gets the policy for the document information of the result.</summary>
    public MetadataPolicy MetadataPolicy { get; init; } = MetadataPolicy.FromPrimarySource;

    /// <summary>
    /// Gets document information values that override whatever the selected policy produced. Only the
    /// fields that are set on this instance are applied.
    /// </summary>
    public PdfMetadata? MetadataOverride { get; init; }

    /// <summary>
    /// Gets the value written as the modification date of the result. When <see langword="null"/> the
    /// current UTC time is used; supplying a value makes the output reproducible byte for byte.
    /// </summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>Gets the policy for the bookmarks of the result.</summary>
    public OutlinePolicy OutlinePolicy { get; init; } = OutlinePolicy.Preserve;

    /// <summary>Gets the policy for the page labels of the result.</summary>
    public PageLabelPolicy PageLabelPolicy { get; init; } = PageLabelPolicy.Drop;

    /// <summary>Gets a value indicating whether annotations of the merged pages are carried over.</summary>
    public bool PreserveAnnotations { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the interactive form of the primary source is carried over.
    /// The form fields of the other sources remain visible as page annotations.
    /// </summary>
    public bool PreserveAcroForm { get; init; } = true;

    /// <summary>Gets a value indicating whether embedded files of the primary source are carried over.</summary>
    public bool PreserveEmbeddedFiles { get; init; } = true;

    /// <summary>
    /// Gets the encryption the result is written with, or <see langword="null"/> — the default — for a
    /// document that anyone can read.
    /// </summary>
    /// <remarks>
    /// A merged document is written in the clear unless this is set, even when every source it was
    /// made from was encrypted: a merged document is a new document, and the passwords of its sources
    /// are not carried into it.
    /// </remarks>
    public PdfEncryptionOptions? Encryption { get; init; }

    /// <summary>Gets the value written to the <c>/Producer</c> entry of the result.</summary>
    public string Producer { get; init; } = ProductInfo.DefaultProducer;

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

        if (!Enum.IsDefined(MetadataPolicy))
        {
            errors.Add($"metadata policy '{(int)MetadataPolicy}' is not defined.");
        }

        if (!Enum.IsDefined(OutlinePolicy))
        {
            errors.Add($"outline policy '{(int)OutlinePolicy}' is not defined.");
        }

        if (!Enum.IsDefined(PageLabelPolicy))
        {
            errors.Add($"page label policy '{(int)PageLabelPolicy}' is not defined.");
        }

        if (Producer is not null && string.IsNullOrWhiteSpace(Producer))
        {
            errors.Add("the producer must be a non-empty string when it is set.");
        }

        if (Encryption is { } encryption)
        {
            // The encryption has to be told the version, because the crypt filters of AES-128 and
            // AES-256 are newer than the formats that precede them.
            errors.AddRange(encryption.Validate(TargetVersion));
        }

        return errors;
    }
}
