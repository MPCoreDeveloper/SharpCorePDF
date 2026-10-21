using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;
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
    private readonly List<PdfPageCanvas> pages = [];
    private PdfMetadata metadata = PdfMetadata.Empty;

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
        pages.Add(page);
        return this;
    }

    /// <summary>Writes the document and returns its bytes.</summary>
    /// <returns>The bytes of the PDF document.</returns>
    /// <exception cref="InvalidOperationException">There is no page, or the options are not usable.</exception>
    public byte[] ToArray() => PdfAuthoringWriter.Write(pages, metadata, Options);

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
}
