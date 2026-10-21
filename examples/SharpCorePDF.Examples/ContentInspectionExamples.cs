using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Reading what a document already contains: the images a range paints, the comments and links on the pages,
/// the interactive form, the bookmarks, the attachments and the XMP packet.
/// </summary>
internal static class ContentInspectionExamples
{
    /// <summary>Reads every capability of the content reader from one sample document.</summary>
    /// <param name="context">The document to read and the ports to read it with.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    public static async Task ReadContentAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var reader = context.ContentReader;
        var source = PdfFileSource.From(context.RichContent);

        Report.Heading($"{source.DisplayName}: every image the two pages paint");

        var images = await reader.ReadImagesAsync(source, PageRange.All, cancellationToken: cancellationToken);

        foreach (var image in images.Items)
        {
            Report.Item(
                image.Id,
                $"{image.Width}x{image.Height} {image.ColorSpace}, page(s) {string.Join(", ", image.Pages)}, "
                + $"{image.ByteLength} B as {image.Filter}, inline {image.IsBuffered}");
        }

        // A record carries its bytes inline only within the budget; above it the bytes come through the copy
        // route, which writes to a stream the caller opened and leaves it open. SaveImageDataAsync is that
        // route with the file opened for you.
        var stored = context.OutputPath("rich-image.bin");
        await reader.SaveImageDataAsync(source, images.Items[0], stored, cancellationToken: cancellationToken);

        Report.Item("image written", $"{stored} ({new FileInfo(stored).Length} B)");

        Report.Heading("the text the two pages draw, in reading order");

        var text = await reader.ReadTextAsync(source, PageRange.All, cancellationToken: cancellationToken);

        // A block is a paragraph: its lines joined with a newline, its words with a single space, ordered
        // down the page and then along the line rather than in the order the stream draws them.
        // ReadingOrder is one-based and restarts on every page, which is why the page travels beside it.
        foreach (var block in text.Items)
        {
            Report.Item(
                $"page {block.PageNumber} block {block.ReadingOrder}",
                $"{block.LineCount} line(s), {block.Words.Count} word(s) in {block.BoundingBox}: "
                + $"\"{block.Text.Replace('\n', ' ')}\"");
        }

        Report.Heading("what reviewers wrote on the pages");

        var annotations = await reader.ReadAnnotationsAsync(
            source,
            PageRange.All,
            cancellationToken: cancellationToken);

        foreach (var annotation in annotations.Items)
        {
            Report.Item(
                $"page {annotation.PageNumber} {annotation.Subtype}",
                $"{annotation.Author ?? "(no author)"}: {annotation.Contents ?? "(no text)"}"
                + (annotation.Target is { } target ? $", target {target}" : string.Empty));
        }

        // The form is document-wide: a field's widgets can sit on different pages, so there is no range.
        Report.Heading("the interactive form");

        var fields = await reader.ReadFormFieldsAsync(source, cancellationToken: cancellationToken);

        foreach (var field in fields.Items)
        {
            Report.Item(field.Name, $"{field.FieldType} = \"{field.Value}\", {field.Widgets.Count} widget(s)");
        }

        Report.Heading("the bookmarks");

        var outline = await reader.ReadOutlineAsync(source, cancellationToken: cancellationToken);

        foreach (var item in outline.Items)
        {
            Report.Item(
                item.Title,
                $"{item.Destination}, open {item.IsOpen}, {item.Children.Count} child(ren)");
        }

        Report.Heading("the attachment");

        var files = await reader.ReadEmbeddedFilesAsync(source, cancellationToken: cancellationToken);

        foreach (var file in files.Items)
        {
            Report.Item(file.Id, $"{file.Name} ({file.MimeType ?? "unknown type"}), {file.ByteLength} B");

            var extracted = context.OutputPath(file.Name);
            await reader.SaveEmbeddedFileAsync(source, file, extracted, cancellationToken: cancellationToken);

            Report.Item("extracted", $"{extracted} ({new FileInfo(extracted).Length} B)");
        }

        Report.Heading("the XMP packet");

        var xmp = await reader.ReadMetadataAsync(source, cancellationToken: cancellationToken);

        // Absent and malformed are different answers: no packet is Metadata == null, a packet that is not XML
        // is a record with RawPacket intact, no properties and one XmpPacketMalformed warning.
        Report.Item("title", xmp.Metadata?.Title ?? "(no packet)");
        Report.Item("producer", xmp.Metadata?.Producer ?? "(no packet)");
        Report.Item("packet bytes", xmp.Metadata?.RawPacket.Length ?? 0);
        Report.Item("warnings", xmp.Warnings.Count);

        Report.Heading("one page at a time");

        await foreach (var page in reader.ReadPageDetailsAsync(
            source,
            PageRange.All,
            cancellationToken: cancellationToken))
        {
            Report.Item(
                $"page {page.PageNumber}",
                $"{page.DisplaySize}, {page.Images.Count} image(s), {page.Annotations.Count} annotation(s), "
                + $"{page.Links.Count} link(s), {page.FormFields.Count} field(s), {page.Warnings.Count} warning(s)");
        }
    }
}
