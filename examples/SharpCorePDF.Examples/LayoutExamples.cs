using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Reading how a page is laid out — the letters, the words they make, the blocks those words make and the
/// order the page is read in — and reading a document as the object graph it is.
/// </summary>
internal static class LayoutExamples
{
    /// <summary>Reads the layout of a document and writes its reading in the four export formats.</summary>
    /// <param name="context">The document to read and the ports to read it with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task ExtractLayoutAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var reader = context.LayoutReader;
        var document = context.Summary;
        var name = Path.GetFileName(document);

        Report.Heading($"{name}: the block of every paragraph, in reading order");

        var blocks = await reader.ReadBlocksAsync(
            PdfFileSource.From(document),
            PageRange.All,
            cancellationToken: cancellationToken);

        foreach (var block in blocks.Items)
        {
            Report.Item(
                $"page {block.PageNumber} block {block.ReadingOrder}",
                $"{block.LineCount} line(s), {block.Words.Count} word(s) in {block.BoundingBox}: "
                + $"\"{block.Text.Replace('\n', ' ')}\"");
        }

        // The result names the strategy that produced the segmentation and the order, because a caller that
        // compares two runs needs to know which one it asked for. Nothing is chosen for it here: the defaults
        // are the ones the contract documents.
        Report.Item("segmentation", blocks.Segmentation?.ToString() ?? "(not reported)");
        Report.Item("reading order", blocks.ReadingOrderStrategy?.ToString() ?? "(not reported)");

        Report.Heading("the letters of page 1, with the box and baseline of each");

        // A letter is the unit a caller measures with: its own box, the baseline it sits on and the font it
        // came from. Whitespace glyphs are reported too, because the gaps between words are what a caller's
        // own segmentation measures.
        var letters = await reader.ReadLettersAsync(
            PdfFileSource.From(document),
            PageRange.Single(1),
            cancellationToken: cancellationToken);

        foreach (var letter in letters.Items.Take(8))
        {
            Report.Item(
                $"letter {letter.PageNumber}",
                $"{(letter.IsWhitespace ? "(space)" : $"\"{letter.Value}\"")} in {letter.BoundingBox}, "
                + $"baseline {letter.StartBaseLine}, {letter.FontSize:F1} pt {letter.FontName ?? "(no name)"}");
        }

        Report.Item("letters", letters.Count);

        Report.Heading("the same reading, written for another tool");

        // An export is the read written down, once per format. A source is the position it was read to, so
        // each export opens its own: reusing one would export an empty document.
        foreach (var (format, extension) in Exports)
        {
            var path = context.OutputPath($"{Path.GetFileNameWithoutExtension(document)}.{extension}");

            await using (var destination = File.Create(path))
            {
                var written = await reader.ExportAsync(
                    PdfFileSource.From(document),
                    PageRange.All,
                    format,
                    destination,
                    cancellationToken: cancellationToken);

                Report.Item(
                    format.ToString(),
                    $"{written.Count} block(s), {written.Warnings.Count} warning(s), "
                    + $"{new FileInfo(path).Length} B in {Path.GetFileName(path)}");
            }
        }
    }

    /// <summary>Reads the object graph of a document and the operators of one page's content stream.</summary>
    /// <param name="context">The document to read and the ports to read it with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task InspectObjectsAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var inspector = context.ObjectInspector;

        Report.Heading($"{Path.GetFileName(context.Summary)}: every object the file lists");

        var objects = await inspector.ReadObjectsAsync(
            PdfFileSource.From(context.Summary),
            cancellationToken: cancellationToken);

        foreach (var node in objects.Items.Take(10))
        {
            Report.Item($"object {node.ObjectNumber}", node.ToString());
        }

        Report.Item("objects", objects.Count);

        // A reference is a node of its own and is not followed: an object that is missing from the file is
        // reported where it was referenced, so the page that pointed at it stays visible in the graph.
        var dangling = objects.Items.SelectMany(Flatten).Count(node => !node.IsResolved);

        Report.Item("dangling references", dangling);
        Report.Item("warnings", objects.Warnings.Count);

        Report.Heading("the operators page 1 paints, in the order it paints them");

        var operations = await inspector.ReadOperationsAsync(
            PdfFileSource.From(context.Summary),
            pageNumber: 1,
            cancellationToken: cancellationToken);

        foreach (var operation in operations.Items.Take(12))
        {
            Report.Item(
                $"offset {operation.Offset}",
                $"{operation.Operator} "
                + $"{string.Join(' ', operation.Operands.Select(operand => operand.ToString()))}");
        }

        Report.Item("operations", operations.Count);
    }

    /// <summary>Gets the formats an export writes, with the file extension of each.</summary>
    private static IReadOnlyList<(PdfLayoutFormat Format, string Extension)> Exports { get; } =
    [
        (PdfLayoutFormat.Alto, "alto.xml"),
        (PdfLayoutFormat.PageXml, "page.xml"),
        (PdfLayoutFormat.Hocr, "hocr.html"),
        (PdfLayoutFormat.SvgText, "svg"),
    ];

    /// <summary>Walks a node and every node below it.</summary>
    /// <param name="node">The node to walk.</param>
    /// <returns>The node and its descendants, depth first.</returns>
    private static IEnumerable<PdfObjectView> Flatten(PdfObjectView node) =>
        node.Children.SelectMany(Flatten).Prepend(node);
}
