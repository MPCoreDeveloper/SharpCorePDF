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

    /// <summary>
    /// Gets a value indicating whether the result is appended to the bytes of the source as a new
    /// revision, or written as a complete document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An incremental update leaves every byte of every earlier revision exactly where it was, so a
    /// signature that covers one of them still covers it in the result. The cost is that nothing the
    /// earlier revisions wrote can be removed: the file grows by the objects the edit adds, and a reader
    /// resolves the newest revision of each object through the chained cross-reference sections.
    /// </para>
    /// <para>
    /// It requires a source that can be read from its start and whose bytes are copied through
    /// unchanged, which is why the engine refuses a stream that cannot seek and a document whose
    /// cross-reference table had to be rebuilt by scanning. It cannot be combined with
    /// <see cref="Encryption"/> or <see cref="PdfAProfile"/>: an appended revision keeps the encryption
    /// and the header version of the revision it follows.
    /// </para>
    /// </remarks>
    public bool UseIncrementalUpdate { get; init; }

    /// <summary>
    /// Gets the encryption the result is written with, or <see langword="null"/> — the default — for a
    /// document that anyone can read.
    /// </summary>
    /// <remarks>
    /// An edit writes its result in the clear unless this is set, even when the source was encrypted:
    /// the result is a new document, and the password of its source is not carried into it.
    /// </remarks>
    public PdfEncryptionOptions? Encryption { get; init; }

    /// <summary>
    /// Gets the archival profile the result is written for, or <see langword="null"/> for an ordinary
    /// PDF document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The profile decides the version of the result — PDF/A-1b is a PDF 1.4 document, -2b and -3b are
    /// PDF 1.7 — and adds the XMP packet and the output intent it requires. Every font must be
    /// embedded, and features the profile forbids are removed or refused; a document that cannot be
    /// made conformant is refused rather than written with a label it does not deserve.
    /// </para>
    /// <para>
    /// A profile cannot be combined with <see cref="Encryption"/>, because ISO 19005 forbids encrypted
    /// archival documents.
    /// </para>
    /// </remarks>
    public PdfAProfile? PdfAProfile { get; init; }

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

        if (UseIncrementalUpdate && Encryption is not null)
        {
            errors.Add(
                "an incremental update cannot be combined with encryption: the appended revision is read with the encryption of the revision it follows, and re-encrypting the earlier bytes would invalidate their signatures.");
        }

        if (UseIncrementalUpdate && PdfAProfile is not null)
        {
            errors.Add(
                "an incremental update cannot be written for a PDF/A profile: the profile fixes the header version and the metadata of the whole document, which a revision appended to an earlier one cannot change.");
        }

        if (Encryption is { } encryption)
        {
            // The encryption has to be told the version, because the crypt filters of AES-128 and
            // AES-256 are newer than the formats that precede them.
            errors.AddRange(encryption.Validate(TargetVersion));
        }

        if (PdfAProfile is { } profile)
        {
            if (!Enum.IsDefined(profile))
            {
                errors.Add($"PDF/A profile '{(int)profile}' is not defined.");
            }

            if (Encryption is not null)
            {
                errors.Add(
                    $"the PDF/A profile {profile} cannot be combined with encryption: ISO 19005 requires an archival document to be readable without a password.");
            }
        }

        return errors;
    }
}
