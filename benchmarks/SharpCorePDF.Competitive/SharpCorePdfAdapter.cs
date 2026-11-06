using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Reading;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Competitive;

internal sealed class SharpCorePdfAdapter : EngineAdapter
{
    private readonly byte[] fontBytes;
    private readonly PdfLayoutReaderService reader = new(new PdfLayoutReader());

    public SharpCorePdfAdapter(byte[] fontBytes)
        : base("SharpCorePDF", typeof(PdfDocumentBuilder).Assembly) =>
        this.fontBytes = fontBytes;

    public override bool SupportsExtraction => true;
    public override bool SupportsCreation => true;
    public override bool SupportsMerge => true;
    public override bool SupportsBlocks => true;

    public override async ValueTask<ExtractionObservation> ExtractAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken)
    {
        var range = selection.PageCount is { } count
            ? PageRange.FromTo(selection.FirstPage, selection.FirstPage + count - 1)
            : PageRange.All;
        var source = new PdfByteArraySource(content, "competitive.pdf");
        var result = await reader.ReadWordsAsync(source, range, cancellationToken: cancellationToken);
        var words = result.Items.Select(word => new WordObservation(
            word.PageNumber,
            word.Text,
            Convert(word.BoundingBox),
            word.Letters.Select(letter => new LetterObservation(
                letter.PageNumber,
                letter.Value,
                Convert(letter.BoundingBox))).ToArray())).ToArray();

        return new ExtractionObservation(
            words.Length == 0 ? 0 : words.Max(word => word.PageNumber) - selection.FirstPage + 1,
            words,
            result.Warnings.Count);
    }


    public override async ValueTask<ExtractionObservation> ExtractWarmAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken)
    {
        var range = selection.PageCount is { } count
            ? PageRange.FromTo(selection.FirstPage, selection.FirstPage + count - 1)
            : PageRange.All;
        var source = new PdfByteArraySource(content, "competitive.pdf");
        await using var session = await reader.OpenAsync(source, cancellationToken: cancellationToken);
        var result = await session.ReadWordsAsync(range, cancellationToken: cancellationToken);
        var words = result.Items.Select(word => new WordObservation(
            word.PageNumber,
            word.Text,
            Convert(word.BoundingBox),
            word.Letters.Select(letter => new LetterObservation(
                letter.PageNumber,
                letter.Value,
                Convert(letter.BoundingBox))).ToArray())).ToArray();

        return new ExtractionObservation(
            words.Length == 0 ? 0 : words.Max(word => word.PageNumber) - selection.FirstPage + 1,
            words,
            result.Warnings.Count);
    }
    public override async ValueTask<BlockObservation> ExtractBlocksAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken)
    {
        var range = selection.PageCount is { } count
            ? PageRange.FromTo(selection.FirstPage, selection.FirstPage + count - 1)
            : PageRange.All;
        var source = new PdfByteArraySource(content, "competitive.pdf");
        var result = await reader.ReadBlocksAsync(
            source,
            range,
            new PdfLayoutOptions { Segmentation = PdfPageSegmentationStrategy.Docstrum },
            cancellationToken: cancellationToken);

        return new BlockObservation(
            result.Items.Count == 0 ? 0 : result.Items.Max(block => block.PageNumber) - selection.FirstPage + 1,
            result.Items.Count,
            result.Items.Sum(block => block.Words.Count));
    }

    public override async ValueTask<WriteObservation> CreateAsync(
        int pages,
        int linesPerPage,
        CancellationToken cancellationToken,
        string? capturePath = null)
    {
        // Parse the same supplied TrueType bytes inside every measured job. PdfPig and PDFsharp also
        // construct their font resources inside their document-creation APIs.
        var typeface = PdfTypeface.FromTrueType(fontBytes);
        var builder = PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with
        {
            CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DocumentId = "00112233445566778899AABBCCDDEEFF",
        });

        for (var pageNumber = 1; pageNumber <= pages; pageNumber++)
        {
            var capturedPage = pageNumber;
            builder.AddPage(PdfPageSize.A4, canvas =>
            {
                for (var line = 1; line <= linesPerPage; line++)
                {
                    canvas.DrawText(
                        CreationText(capturedPage, line),
                        40,
                        40 + ((line - 1) * 14),
                        typeface,
                        12);
                }
            });
        }

        using Stream destination = capturePath is null ? new CountingStream() : File.Create(capturePath);
        await builder.SaveAsync(destination, cancellationToken);
        return new WriteObservation(destination.Length, pages);
    }

    public override async ValueTask<WriteObservation> MergeAsync(
        IReadOnlyList<byte[]> sources,
        CancellationToken cancellationToken,
        string? capturePath = null)
    {
        var plan = MergePlan.Create();
        for (var index = 0; index < sources.Count; index++)
        {
            plan = plan.AddBytes(sources[index], $"source-{index + 1}.pdf");
        }

        var merger = new PdfMergerService(new PdfDocumentReader(), new PdfMergeEngine());
        using Stream destination = capturePath is null ? new CountingStream() : File.Create(capturePath);
        var result = await merger.MergeAsync(plan, destination, cancellationToken);
        return new WriteObservation(destination.Length, result.PageCount);
    }

    internal static string CreationText(int page, int line) =>
        $"Page {page} line {line}: the quick brown fox jumps over the lazy dog 0123456789.";

    private static Box Convert(PdfRectangle value) => new(value.Left, value.Bottom, value.Right, value.Top);
}
