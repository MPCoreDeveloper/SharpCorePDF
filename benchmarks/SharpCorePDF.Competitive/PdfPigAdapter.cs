using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Writer;

namespace SharpCorePDF.Competitive;

internal sealed class PdfPigAdapter : EngineAdapter
{
    private readonly byte[] fontBytes;

    public PdfPigAdapter(byte[] fontBytes)
        : base("PdfPig", typeof(PdfDocument).Assembly) =>
        this.fontBytes = fontBytes;

    public override bool SupportsExtraction => true;
    public override bool SupportsCreation => true;
    public override bool SupportsMerge => true;
    public override bool SupportsBlocks => true;

    public override ValueTask<ExtractionObservation> ExtractAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(content);
        var lastPage = selection.PageCount is { } count
            ? Math.Min(document.NumberOfPages, selection.FirstPage + count - 1)
            : document.NumberOfPages;
        var words = new List<WordObservation>();

        for (var pageNumber = selection.FirstPage; pageNumber <= lastPage; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageNumber);
            foreach (var word in page.GetWords())
            {
                words.Add(new WordObservation(
                    pageNumber,
                    word.Text,
                    Convert(word.BoundingBox),
                    word.Letters.Select(letter => new LetterObservation(
                        pageNumber,
                        letter.Value,
                        Convert(letter.BoundingBox))).ToArray()));
            }
        }

        return ValueTask.FromResult(new ExtractionObservation(
            Math.Max(0, lastPage - selection.FirstPage + 1),
            words,
            0));
    }


    public override ValueTask<ExtractionObservation> ExtractWarmAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken) =>
        // PdfPig already opens the document once per call and walks pages from that open; the warm
        // scenario is the same shape, kept as a named row so a session-based engine can be compared.
        ExtractAsync(content, selection, cancellationToken);
    /// <inheritdoc />
    /// <remarks>
    /// NearestNeighbour is the one word rule PdfPig 0.1.16 ships as a word extractor. Its Docstrum and
    /// RecursiveXYCut types are page segmenters — they group words into blocks, not letters into words —
    /// so they cannot be a word-for-word row here and are deliberately absent rather than mislabelled.
    /// </remarks>
    public override IReadOnlyList<string> AlternateExtractionStrategies =>
        [WordStrategies.NearestNeighbour];

    /// <inheritdoc />
    /// <remarks>
    /// These are named alternates only: PdfPig's default row stays the apples-to-apples comparison,
    /// these two expose the strategies it ships so a layout-algorithm comparison is possible without
    /// changing the main table.
    /// </remarks>
    public override ValueTask<ExtractionObservation> ExtractStrategyAsync(
        string strategy,
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken)
    {
        var extractor = strategy switch
        {
            WordStrategies.NearestNeighbour => NearestNeighbourWordExtractor.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown PdfPig word strategy."),
        };

        using var document = PdfDocument.Open(content);
        var lastPage = selection.PageCount is { } count
            ? Math.Min(document.NumberOfPages, selection.FirstPage + count - 1)
            : document.NumberOfPages;
        var words = new List<WordObservation>();

        for (var pageNumber = selection.FirstPage; pageNumber <= lastPage; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageNumber);
            foreach (var word in extractor.GetWords(page.Letters))
            {
                words.Add(new WordObservation(
                    pageNumber,
                    word.Text,
                    Convert(word.BoundingBox),
                    word.Letters.Select(letter => new LetterObservation(
                        pageNumber,
                        letter.Value,
                        Convert(letter.BoundingBox))).ToArray()));
            }
        }

        return ValueTask.FromResult(new ExtractionObservation(
            Math.Max(0, lastPage - selection.FirstPage + 1),
            words,
            0));
    }

    public override ValueTask<BlockObservation> ExtractBlocksAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(content);
        var segmenter = DocstrumBoundingBoxes.Instance;
        var lastPage = selection.PageCount is { } count
            ? Math.Min(document.NumberOfPages, selection.FirstPage + count - 1)
            : document.NumberOfPages;
        var blocks = 0;
        var words = 0;

        for (var pageNumber = selection.FirstPage; pageNumber <= lastPage; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageWords = document.GetPage(pageNumber).GetWords().ToArray();

            blocks += segmenter.GetBlocks(pageWords).Count;
            words += pageWords.Length;
        }

        return ValueTask.FromResult(new BlockObservation(
            Math.Max(0, lastPage - selection.FirstPage + 1),
            blocks,
            words));
    }

    public override async ValueTask<WriteObservation> CreateAsync(
        int pages,
        int linesPerPage,
        CancellationToken cancellationToken,
        string? capturePath = null)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(fontBytes);

        for (var pageNumber = 1; pageNumber <= pages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = builder.AddPage(PageSize.A4);
            for (var line = 1; line <= linesPerPage; line++)
            {
                page.AddText(
                    SharpCorePdfAdapter.CreationText(pageNumber, line),
                    12,
                    new PdfPoint(40, 800 - ((line - 1) * 14)),
                    font);
            }
        }

        var bytes = builder.Build();
        if (capturePath is not null)
        {
            await File.WriteAllBytesAsync(capturePath, bytes, cancellationToken);
        }

        return new WriteObservation(bytes.Length, pages);
    }

    public override async ValueTask<WriteObservation> MergeAsync(
        IReadOnlyList<byte[]> sources,
        CancellationToken cancellationToken,
        string? capturePath = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = PdfMerger.Merge(sources);
        if (capturePath is not null)
        {
            await File.WriteAllBytesAsync(capturePath, bytes, cancellationToken);
        }

        return new WriteObservation(bytes.Length, null);
    }

    private static Box Convert(PdfRectangle value) => new(value.Left, value.Bottom, value.Right, value.Top);
}
