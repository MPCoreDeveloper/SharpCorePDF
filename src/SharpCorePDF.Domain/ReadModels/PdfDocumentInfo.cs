using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// A read-only description of a PDF document, produced by
/// <see cref="Ports.IPdfInspector.InspectAsync"/>.
/// </summary>
public sealed record PdfDocumentInfo
{
    /// <summary>Gets the name of the inspected source.</summary>
    public required string SourceName { get; init; }

    /// <summary>Gets the version from the document header.</summary>
    public required PdfVersion Version { get; init; }

    /// <summary>
    /// Gets the number of pages. When <see cref="IsEncrypted"/> is <see langword="true"/> the page
    /// tree cannot be read without the password and this is zero.
    /// </summary>
    public required int PageCount { get; init; }

    /// <summary>Gets a value indicating whether the document is encrypted.</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// Gets a value indicating whether a user password is required to open the document. Only
    /// meaningful when <see cref="IsEncrypted"/> is <see langword="true"/>.
    /// </summary>
    public bool RequiresPassword { get; init; }

    /// <summary>Gets the document information of the source.</summary>
    public PdfMetadata Metadata { get; init; } = PdfMetadata.Empty;

    /// <summary>Gets the description of every page, in document order.</summary>
    public IReadOnlyList<PdfPageInfo> Pages { get; init; } = [];

    /// <summary>Gets a value indicating whether the document carries bookmarks.</summary>
    public bool HasOutlines { get; init; }

    /// <summary>Gets the number of bookmarks, counting nested items.</summary>
    public int OutlineItemCount { get; init; }

    /// <summary>Gets the number of embedded files.</summary>
    public int EmbeddedFileCount { get; init; }

    /// <summary>Gets a value indicating whether the document carries an interactive form.</summary>
    public bool HasAcroForm { get; init; }

    /// <summary>Gets the size of the source in bytes when it is known.</summary>
    public long? SourceLengthBytes { get; init; }

    /// <summary>
    /// Gets problems that did not stop the document from being read, for example a damaged
    /// cross-reference table that had to be reconstructed by scanning the file.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Gets the first page size, or <see langword="null"/> for a document without pages.</summary>
    public PdfPageSize? FirstPageSize => Pages.Count > 0 ? Pages[0].Size : null;

    /// <summary>Gets a value indicating whether all pages share one size.</summary>
    public bool HasUniformPageSize =>
        Pages.Count == 0 || Pages.All(page => page.Size == Pages[0].Size);

    /// <summary>Formats the document for diagnostics.</summary>
    /// <returns>The formatted document.</returns>
    public override string ToString() => IsEncrypted
        ? $"{SourceName}: encrypted PDF {Version}"
        : $"{SourceName}: PDF {Version}, {PageCount} page(s)";
}
