namespace SharpCorePDF.Infrastructure;

/// <summary>
/// Limits and switches of the PDF engine.
/// </summary>
/// <remarks>
/// The defaults favour reading documents produced by the real world over strictness: they are what a
/// caller gets when nothing is configured.
/// </remarks>
public sealed record PdfEngineOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfEngineOptions Default { get; } = new();

    /// <summary>
    /// Gets the largest source the engine reads into memory. Documents above this limit are rejected
    /// instead of exhausting the process.
    /// </summary>
    public long MaxSourceBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>
    /// Gets the largest number of bytes one filter of one stream may decode to. A stream that would decode
    /// past it is refused with a <see cref="Domain.Exceptions.PdfSourceFormatException"/> instead of
    /// exhausting the process.
    /// </summary>
    /// <remarks>
    /// <see cref="MaxSourceBytes"/> bounds what is read, not what it decodes to: Flate reaches about a
    /// thousand to one, LZW four thousand and RunLength 128, so a one-megabyte document can ask for a
    /// gigabyte. The default of 128 MiB is far above any real content stream, object stream or font program,
    /// and low enough that refusing a stream at the limit — whose buffer doubles on the way there, on the
    /// large-object heap, which the collector does not compact by default — stays reliably inside a 1 GiB
    /// process when every read pass meets the same stream; the largest value accepted is the largest array
    /// the runtime allows. An image is not refused by this limit — its samples are kept only
    /// within <see cref="MaxInlineContentBytes"/> already — but every other stream the engine has to
    /// materialise is: content, forms, object streams, cross-reference streams and font programs.
    /// </remarks>
    public long MaxDecodedStreamBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>Gets the maximum nesting depth of dictionaries and arrays.</summary>
    public int MaxNestingDepth { get; init; } = 64;

    /// <summary>Gets the maximum number of pages a single source may contribute.</summary>
    public int MaxPagesPerSource { get; init; } = 100_000;

    /// <summary>
    /// Gets a value indicating whether a damaged or missing cross-reference table is rebuilt by
    /// scanning the file for object headers.
    /// </summary>
    public bool ReconstructCrossReferenceOnFailure { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the engine re-reads its own output before returning, to prove
    /// that the merged document is well formed.
    /// </summary>
    /// <remarks>This doubles the work for a merge; it is off by default and useful in tests and diagnostics.</remarks>
    public bool VerifyOutput { get; init; }

    /// <summary>
    /// Gets a value indicating whether the binary marker comment is written behind the file header,
    /// which tells transfer tools that the file is binary.
    /// </summary>
    public bool WriteBinaryMarker { get; init; } = true;

    /// <summary>
    /// Gets the largest image, mask or embedded file payload a read model carries inline. A payload
    /// above this budget is left out of the record — <c>Data</c> is <see langword="null"/> — and the
    /// caller fetches it with the copy route instead, so enumerating a document full of large images or
    /// attachments does not grow the working set with the total size of what it found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default of 256 KiB covers the material a caller usually wants inline — icons, logos, small
    /// attachments — while a scan or a video stays out of the report. <c>0</c> means metadata only:
    /// every entry is described, nothing is carried, and the copy route is the only way to bytes.
    /// </para>
    /// <para>
    /// The budget never raises a warning when it bites, because nothing failed: <c>Data</c> being
    /// <see langword="null"/> while <c>IsBuffered</c> is <see langword="false"/> and
    /// <c>ByteLength</c> is truthful is the contract, not a defect.
    /// </para>
    /// </remarks>
    public int MaxInlineContentBytes { get; init; } = 256 * 1024;

    /// <summary>Validates these options.</summary>
    /// <returns>Every problem found, or an empty list when the options are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MaxSourceBytes < 1024)
        {
            errors.Add($"MaxSourceBytes must be at least 1024, but is {MaxSourceBytes}.");
        }

        if (MaxDecodedStreamBytes < 1024 || MaxDecodedStreamBytes > Array.MaxLength)
        {
            errors.Add($"MaxDecodedStreamBytes must be between 1024 and {Array.MaxLength}, but is {MaxDecodedStreamBytes}.");
        }

        if (MaxNestingDepth is < 4 or > 1024)
        {
            errors.Add($"MaxNestingDepth must be between 4 and 1024, but is {MaxNestingDepth}.");
        }

        if (MaxPagesPerSource < 1)
        {
            errors.Add($"MaxPagesPerSource must be at least 1, but is {MaxPagesPerSource}.");
        }

        if (MaxInlineContentBytes < 0)
        {
            errors.Add($"MaxInlineContentBytes must be at least 0, but is {MaxInlineContentBytes}.");
        }

        return errors;
    }
}
