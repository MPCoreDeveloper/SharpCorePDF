using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Everything a single merge needs while it runs: the writer, the request, the sources with their
/// open readers and copiers, and the remarks that end up in the result.
/// </summary>
internal sealed class PdfMergeContext
{
    /// <summary>Initializes a new instance of the <see cref="PdfMergeContext"/> class.</summary>
    /// <param name="request">The request being executed.</param>
    /// <param name="writer">The writer of the merged document.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="modificationDate">The moment written as the modification date of the result, which the identifier of the document is derived from as well.</param>
    public PdfMergeContext(
        MergeEngineRequest request,
        PdfWriter writer,
        PdfEngineOptions options,
        DateTimeOffset? modificationDate = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        ModificationDate = modificationDate ?? request.Output.ModificationDate ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the request being executed.</summary>
    public MergeEngineRequest Request { get; }

    /// <summary>Gets the writer of the merged document.</summary>
    public PdfWriter Writer { get; }

    /// <summary>Gets the engine options.</summary>
    public PdfEngineOptions Options { get; }

    /// <summary>Gets the output options of the merge.</summary>
    public MergeOutputOptions Output => Request.Output;

    /// <summary>Gets the moment written as the modification date of the result.</summary>
    public DateTimeOffset ModificationDate { get; }

    /// <summary>Gets the capture stream that records the output, when the caller asked for verification.</summary>
    public PdfCaptureStream? Capture { get; init; }

    /// <summary>Gets the bytes of the merged document, when the caller asked for verification.</summary>
    public byte[]? WrittenBytes => Capture?.CapturedBytes;

    /// <summary>Gets the object number of the catalog of the result.</summary>
    public required int CatalogNumber { get; init; }

    /// <summary>Gets the object number of the single page tree node of the result.</summary>
    public required int PagesNodeNumber { get; init; }

    /// <summary>Gets the remarks collected while merging.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Gets the sources in merge order.</summary>
    public List<MergeSource> Sources { get; } = [];

    /// <summary>Finds the source that leads the merge.</summary>
    /// <returns>The primary source.</returns>
    /// <exception cref="InvalidOperationException">The primary source is not part of the merge.</exception>
    public MergeSource PrimarySource =>
        Sources.FirstOrDefault(source => source.Resolved.Source.Id == Request.PrimarySourceId)
        ?? throw new InvalidOperationException(
            "The primary source of the merge is not among the sources that were read.");
}

/// <summary>One source document of a merge while it is being written.</summary>
internal sealed class MergeSource
{
    /// <summary>Initializes a new instance of the <see cref="MergeSource"/> class.</summary>
    /// <param name="resolved">The resolved source.</param>
    /// <param name="reader">The reader of the source document.</param>
    /// <param name="copier">The copier that writes objects of this source into the result.</param>
    public MergeSource(ResolvedPdfSource resolved, PdfFileReader reader, PdfObjectCopier copier)
    {
        Resolved = resolved;
        Reader = reader;
        Copier = copier;
    }

    /// <summary>Gets the resolved source.</summary>
    public ResolvedPdfSource Resolved { get; }

    /// <summary>Gets the reader of the source document.</summary>
    public PdfFileReader Reader { get; }

    /// <summary>Gets the copier that writes the objects of this source into the result.</summary>
    public PdfObjectCopier Copier { get; }

    /// <summary>Gets the display name of the source.</summary>
    public string DisplayName => Resolved.Source.DisplayName;

    /// <summary>
    /// Gets the object number in the result for every selected page, keyed by the one-based page
    /// number of the source.
    /// </summary>
    public Dictionary<int, int> PageTargets { get; } = [];

    /// <summary>Gets the object numbers of the contributed pages, in merge order.</summary>
    public List<int> PagesInOrder { get; } = [];

    /// <summary>Gets the target object number of the first page this source contributes.</summary>
    public int FirstPageTarget => PagesInOrder.Count > 0 ? PagesInOrder[0] : 0;

    /// <summary>Builds the summary of what this source contributed.</summary>
    /// <returns>The summary.</returns>
    public MergedSourceSummary ToSummary() => new(
        Resolved.Source.DisplayName,
        Resolved.Pages.Count,
        Resolved.Info.PageCount,
        Resolved.Info.Version);
}
