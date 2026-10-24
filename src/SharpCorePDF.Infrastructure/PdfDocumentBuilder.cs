using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// Creates a PDF document from scratch: pages of a chosen size, text set in a standard or an embedded
/// font, and PNG images, all in memory.
/// </summary>
/// <remarks>
/// <para>
/// The result is a normal PDF with a classic cross-reference table, so it can be opened by any reader,
/// inspected by <see cref="PdfDocumentReader"/> and merged in front of another document without a
/// temporary file:
/// </para>
/// <code>
/// var cover = PdfDocumentBuilder.Create()
///     .AddPage(PdfPageSize.A4, page => page
///         .DrawImage(logoPng, x: 40, y: 30, width: 152, height: 87)
///         .DrawText("Veuillez trouver ci-joint le décompte", 150, 400,
///             PdfTypeface.Standard(StandardFont.Helvetica), 12))
///     .Build();
///
/// var plan = MergePlan.Create()
///     .AddSource(PdfSource.Create(cover))
///     .AddFile(@"C:\documents\invoice.pdf");
/// </code>
/// <para>
/// The builder collects pages and writes them when <see cref="Build"/>, <see cref="ToArray"/> or
/// <see cref="SaveAsync"/> is called; adding a page afterwards does not change bytes that were already
/// returned. An instance is not thread safe, and a page belongs to the builder that created it.
/// </para>
/// </remarks>
public sealed class PdfDocumentBuilder
{
    private readonly List<IAuthoredPage> pages = [];
    private PdfMetadata metadata = PdfMetadata.Empty;
    private ReadOnlyMemory<byte>? xmp;
    private IReadOnlyList<PdfOutlineItem> bookmarks = [];

    private PdfDocumentBuilder(PdfAuthoringOptions options) => Options = options;

    /// <summary>Gets the options the document is written with.</summary>
    public PdfAuthoringOptions Options { get; }

    /// <summary>Gets the document information that will be written.</summary>
    public PdfMetadata Metadata => metadata;

    /// <summary>Gets the number of pages added so far.</summary>
    public int PageCount => pages.Count;

    /// <summary>Creates a builder.</summary>
    /// <param name="options">The options for identity and reproducibility; the defaults when omitted.</param>
    /// <returns>The builder.</returns>
    public static PdfDocumentBuilder Create(PdfAuthoringOptions? options = null) =>
        new(options ?? PdfAuthoringOptions.Default);

    /// <summary>Sets the document information of the document.</summary>
    /// <param name="metadata">The title, author and other values to write.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
    public PdfDocumentBuilder WithMetadata(PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        this.metadata = metadata;
        return this;
    }

    /// <summary>Adds a page and draws it with the supplied callback.</summary>
    /// <param name="size">The size of the page.</param>
    /// <param name="draw">The drawings of the page, or <see langword="null"/> for a blank page.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> has no positive width or height.</exception>
    public PdfDocumentBuilder AddPage(PdfPageSize size, Action<PdfPageCanvas>? draw = null)
    {
        var page = new PdfPageCanvas(size);
        draw?.Invoke(page);
        pages.Add(new DrawnPage(page));
        return this;
    }

    /// <summary>Sets the XMP packet the document carries.</summary>
    /// <param name="xmp">The packet, as the document that stated it holds it.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xmp"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The packet is empty or is not an XMP packet.</exception>
    /// <remarks>
    /// The packet is written as it is, byte for byte and uncompressed, as the catalog's
    /// <c>/Metadata</c> stream — which is how a caller carries a profile or a schema the library does
    /// not build itself. Read one from another document with the content reader, or state your own.
    /// </remarks>
    public PdfDocumentBuilder WithXmp(PdfXmpMetadata xmp)
    {
        ArgumentNullException.ThrowIfNull(xmp);
        return WithXmp(xmp.RawPacket);
    }

    /// <summary>Sets the XMP packet the document carries.</summary>
    /// <param name="packet">The bytes of the packet, UTF-8, as a producer writes them.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentException">The packet is empty or is not an XMP packet.</exception>
    public PdfDocumentBuilder WithXmp(ReadOnlyMemory<byte> packet)
    {
        if (packet.IsEmpty)
        {
            throw new ArgumentException("The XMP packet is empty.", nameof(packet));
        }

        var text = System.Text.Encoding.UTF8.GetString(packet.Span);

        if (!text.Contains("<x:xmpmeta", StringComparison.Ordinal)
            && !text.Contains("<rdf:RDF", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The bytes are not an XMP packet: they contain neither an x:xmpmeta nor an rdf:RDF element.",
                nameof(packet));
        }

        this.xmp = packet.ToArray();
        return this;
    }

    /// <summary>Sets the bookmarks of the document.</summary>
    /// <param name="outline">The top level bookmarks, in the order a reader shows them.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="outline"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A bookmark is null or has no title.</exception>
    /// <remarks>
    /// A bookmark names a page by its one-based number in this document, or a URL. The page numbers are
    /// checked when the document is written, because pages can be added after the outline is set; a
    /// bookmark that names a page the document does not have is refused there rather than dropped.
    /// </remarks>
    public PdfDocumentBuilder WithOutline(IReadOnlyList<PdfOutlineItem> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        Validate(outline, nameof(outline));

        bookmarks = [.. outline];
        return this;
    }

    /// <summary>Adds a page copied from another document.</summary>
    /// <param name="source">The document to copy the page from.</param>
    /// <param name="pageNumber">The one-based page number in <paramref name="source"/>.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageNumber"/> is not a page of the source.</exception>
    /// <remarks>
    /// The source is read when this method is called, so a document that cannot be read fails here
    /// rather than when the result is written. The page keeps its content, resources, annotations,
    /// rotation and geometry, exactly as a merged page does.
    /// </remarks>
    public PdfDocumentBuilder ImportPage(IPdfContentSource source, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        return Import(source, PageRange.Single(pageNumber));
    }

    /// <summary>Adds the pages of a range copied from another document, in source order.</summary>
    /// <param name="source">The document to copy the pages from.</param>
    /// <param name="range">The one-based range of pages to copy.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The range names pages the source does not have.</exception>
    public PdfDocumentBuilder ImportPages(IPdfContentSource source, PageRange range)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Import(source, range);
    }

    /// <summary>Writes the document and returns its bytes.</summary>
    /// <returns>The bytes of the PDF document.</returns>
    /// <exception cref="InvalidOperationException">There is no page, or the options are not usable.</exception>
    public byte[] ToArray() => PdfAuthoringWriter.Write(pages, metadata, Options, xmp, bookmarks);


    /// <summary>Writes the document to a stream.</summary>
    /// <param name="destination">A writable stream that receives the document.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes when the document has been written and flushed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="InvalidOperationException">There is no page, or the options are not usable.</exception>
    public async Task SaveAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        var bytes = ToArray();

        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the document and returns it as content a <see cref="Domain.Aggregates.MergePlan"/> can read.</summary>
    /// <param name="displayName">The name the source reports in messages and in the merge result.</param>
    /// <returns>The document as a content source.</returns>
    /// <exception cref="ArgumentException"><paramref name="displayName"/> is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">There is no page, or the options are not usable.</exception>
    public PdfByteArraySource Build(string displayName = "authored.pdf") => new(ToArray(), displayName);

    /// <summary>Checks that every bookmark of a tree can be shown.</summary>
    /// <param name="outline">The bookmarks to check, at one level of the tree.</param>
    /// <param name="name">The name of the parameter the tree came from.</param>
    /// <exception cref="ArgumentException">A bookmark is null, has no title, or states no child list.</exception>
    private static void Validate(IReadOnlyList<PdfOutlineItem> outline, string name)
    {
        for (var index = 0; index < outline.Count; index++)
        {
            var item = outline[index];

            if (item is null)
            {
                throw new ArgumentException($"The bookmark at position {index} is null.", name);
            }

            if (string.IsNullOrWhiteSpace(item.Title))
            {
                throw new ArgumentException(
                    $"The bookmark at position {index} has no title, and a bookmark without one cannot be shown.",
                    name);
            }

            if (item.Children is null)
            {
                throw new ArgumentException(
                    $"The bookmark '{item.Title}' states no child list; use an empty one when it has no children.",
                    name);
            }

            Validate(item.Children, name);
        }
    }

    /// <summary>Appends the pages of a range of another document to this one.</summary>
    /// <param name="source">The document to copy from.</param>
    /// <param name="range">The pages to copy.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range names pages the source does not have.</exception>
    private PdfDocumentBuilder Import(IPdfContentSource source, PageRange range)
    {
        var reader = new PdfFileReader(
            ReadSource(source),
            source.DisplayName,
            PdfEngineOptions.Default);

        if (reader.RequiresPassword)
        {
            throw new ArgumentException(
                $"'{source.DisplayName}' is encrypted and the password was not supplied, so its pages cannot be copied.",
                nameof(source));
        }

        var pageCount = reader.Pages.Count;

        if (pageCount == 0)
        {
            throw new ArgumentException(
                $"'{source.DisplayName}' has no page, so there is nothing to import.",
                nameof(source));
        }

        if (range.From > pageCount || (!range.IsOpenEnded && range.To > pageCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                range,
                $"The range '{range}' does not fit '{source.DisplayName}', which has {pageCount} page(s).");
        }

        foreach (var pageNumber in range.Enumerate(pageCount, source.DisplayName))
        {
            pages.Add(new ImportedPage(reader, pageNumber));
        }

        return this;
    }

    /// <summary>Reads a source into memory, because the page it contributes is copied when the document is written.</summary>
    /// <param name="source">The content source.</param>
    /// <returns>The bytes of the document.</returns>
    private static byte[] ReadSource(IPdfContentSource source)
    {
        // The port is asynchronous and the builder is not, so the source is read once, here, instead of
        // inside the write. Nothing in the engine's own sources captures a synchronisation context, so
        // waiting on the read is safe for a caller that holds one.
        using var content = source.OpenReadAsync().AsTask().GetAwaiter().GetResult();
        using var buffer = new MemoryStream();

        content.CopyTo(buffer);

        return buffer.ToArray();
    }
}
