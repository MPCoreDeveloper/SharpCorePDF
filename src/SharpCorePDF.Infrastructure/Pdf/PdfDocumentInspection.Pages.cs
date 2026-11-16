using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The page and feature half of <see cref="PdfDocumentInspection"/>.
/// </summary>
internal static partial class PdfDocumentInspection
{
    /// <summary>The largest number of outline items that are counted, as a guard against damaged trees.</summary>
    private const int MaxOutlineItems = 100_000;

    private static List<PdfPageInfo> ReadPages(PdfFileReader reader, List<PdfWarning> warnings)
    {
        var pages = new List<PdfPageInfo>(reader.Pages.Count);

        foreach (var page in reader.Pages)
        {
            var number = page.Index + 1;
            var size = TryReadPageSize(reader, page);

            if (size is null)
            {
                warnings.Add(new(
                    PdfWarningKind.PageSizeAssumed,
                    $"'{reader.SourceName}': page {number} has no readable /MediaBox; it is described as A4."));
                size = PdfPageSize.A4;
            }

            pages.Add(new PdfPageInfo(
                number,
                size.Value,
                ReadRotation(reader, page),
                page.Dictionary.Get("Contents") is not null,
                CountAnnotations(reader, page)));
        }

        return pages;
    }

    /// <summary>Reads the size a page's own or inherited <c>/MediaBox</c> states.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="page">The page.</param>
    /// <returns>The size, or <see langword="null"/> when the page has no readable media box.</returns>
    internal static PdfPageSize? TryReadPageSize(PdfFileReader reader, SourcePage page)
    {
        var box = reader.ResolveArray(page.Dictionary.Get("MediaBox"))
            ?? reader.ResolveArray(page.Inherited.Get("MediaBox"));

        if (box is null || box.Count < 4)
        {
            return null;
        }

        var lowerLeftX = ReadNumber(reader, box.Items[0]);
        var lowerLeftY = ReadNumber(reader, box.Items[1]);
        var upperRightX = ReadNumber(reader, box.Items[2]);
        var upperRightY = ReadNumber(reader, box.Items[3]);

        if (lowerLeftX is not { } x1
            || lowerLeftY is not { } y1
            || upperRightX is not { } x2
            || upperRightY is not { } y2)
        {
            return null;
        }

        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);

        return width > 0d && height > 0d ? PdfPageSize.FromPoints(width, height) : null;
    }

    /// <summary>Reads the rotation a page declares or inherits, as the nearest quarter turn.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="page">The page.</param>
    /// <returns>The rotation; <see cref="PageRotation.None"/> when none is declared.</returns>
    /// <remarks>
    /// ISO 32000-1 requires a multiple of 90; a value that is not one, or that is a real or an indirect
    /// object, is read the way other readers read it rather than refused. The inspection and the edit engine
    /// both read it here, so an edit keeps the rotation the inspection reported.
    /// </remarks>
    internal static PageRotation ReadRotation(PdfFileReader reader, SourcePage page)
    {
        var value = ReadNumber(reader, page.Dictionary.Get("Rotate"))
            ?? ReadNumber(reader, page.Inherited.Get("Rotate"));

        if (value is null)
        {
            return PageRotation.None;
        }

        // The quarter turns are counted in floating point and reduced before they become an integer, so a
        // declared value beyond the range of int is reduced rather than wrapped.
        var quarterTurns = Math.Round(value.Value / 90d) % 4d;
        var normalized = (((int)quarterTurns * 90) % 360 + 360) % 360;

        return normalized switch
        {
            90 => PageRotation.Clockwise90,
            180 => PageRotation.Clockwise180,
            270 => PageRotation.Clockwise270,
            _ => PageRotation.None,
        };
    }

    private static int CountAnnotations(PdfFileReader reader, SourcePage page) =>
        reader.ResolveArray(page.Dictionary.Get("Annots"))?.Count ?? 0;

    private static double? ReadNumber(PdfFileReader reader, PdfObject? value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        PdfReference reference => ReadNumber(reader, reader.Resolve(reference)),
        _ => null,
    };

    private static int CountOutlineItems(PdfFileReader reader)
    {
        var outline = reader.ResolveDictionary(reader.Catalog?.Get("Outlines"));

        if (outline is null)
        {
            return 0;
        }

        var count = 0;
        var queue = new Queue<PdfDictionary>();

        if (reader.ResolveDictionary(outline.Get("First")) is { } first)
        {
            queue.Enqueue(first);
        }

        while (queue.Count > 0 && count < MaxOutlineItems)
        {
            var item = queue.Dequeue();

            count++;

            if (reader.ResolveDictionary(item.Get("First")) is { } child)
            {
                queue.Enqueue(child);
            }

            if (reader.ResolveDictionary(item.Get("Next")) is { } sibling)
            {
                queue.Enqueue(sibling);
            }
        }

        return count;
    }

    private static int CountEmbeddedFiles(PdfFileReader reader)
    {
        var names = reader.ResolveDictionary(reader.Catalog?.Get("Names"));
        var embedded = reader.ResolveDictionary(names?.Get("EmbeddedFiles"));

        if (embedded is null)
        {
            return 0;
        }

        var entries = reader.ResolveArray(embedded.Get("Names"));

        if (entries is not null)
        {
            return entries.Count / 2;
        }

        // A name tree may be nested: count the leaves instead.
        var kids = reader.ResolveArray(embedded.Get("Kids"));

        return kids?.Items.Count ?? 0;
    }
}
