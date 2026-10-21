using System.Runtime.CompilerServices;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The page-detail half of <see cref="PdfPageContentReader"/>: one record per page, yielded as the page
/// is read.
/// </summary>
public sealed partial class PdfPageContentReader
{
    /// <inheritdoc />
    /// <remarks>
    /// Every page of the range is read on its own and yielded immediately, so the caller's memory is one
    /// page's worth rather than one report's worth. An encrypted document that could not be opened is
    /// still paged out where the page tree is readable, because the tree itself is not encrypted; each
    /// such page carries a <c>ContentStreamUnreadable</c> warning in place of its content. The
    /// collection members and inspection report the encryption warning, so they are where to learn why.
    /// </remarks>
    public async IAsyncEnumerable<PdfPageDetail> ReadPageDetailsAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var pages = SelectPages(reader, range);
        var destinations = new NamedDestinations(reader);
        var pageMap = BuildPageMap(reader);
        var formWarnings = new List<PdfWarning>();
        var formFields = ReadFormFields(reader, formWarnings);
        var first = true;

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageNumber = page.Index + 1;
            var warnings = new List<PdfWarning>();

            // Remarks about the form as a whole belong to no page in particular, so they travel with
            // the first one rather than being repeated on every page.
            if (first)
            {
                warnings.AddRange(formWarnings);
                first = false;
            }

            var walker = new ImageWalker(reader, options.MaxInlineContentBytes, warnings);
            walker.WalkPage(page);

            yield return new PdfPageDetail
            {
                PageNumber = pageNumber,
                Size = ReadPageSize(reader, page, warnings, pageNumber),
                Rotation = ReadRotation(reader, page),
                Images = walker.Build(),
                Annotations = ReadPageAnnotations(reader, page, destinations, pageMap, warnings),
                FormFields = [.. formFields.Where(field => field.Widgets.Any(widget => widget.PageNumber == pageNumber))],
                Warnings = warnings,
            };
        }
    }

    /// <summary>Reads the size a page declares, falling back to A4 with a remark.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="page">The page.</param>
    /// <param name="warnings">The list that receives a remark when the page states no usable size.</param>
    /// <param name="pageNumber">The one-based number of the page, used in diagnostics.</param>
    /// <returns>The size of the page.</returns>
    private static PdfPageSize ReadPageSize(
        PdfFileReader reader,
        SourcePage page,
        List<PdfWarning> warnings,
        int pageNumber)
    {
        var box = reader.ResolveArray(page.Dictionary.Get("MediaBox"))
            ?? reader.ResolveArray(page.Inherited.Get("MediaBox"));

        if (ReadRectangle(reader, box) is { } rectangle && rectangle.Width > 0d && rectangle.Height > 0d)
        {
            return PdfPageSize.FromPoints(rectangle.Width, rectangle.Height);
        }

        warnings.Add(new(
            PdfWarningKind.PageSizeAssumed,
            $"'{reader.SourceName}': page {pageNumber} has no readable /MediaBox; it is described as A4."));

        return PdfPageSize.A4;
    }

    /// <summary>Reads the rotation a viewer applies to a page.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="page">The page.</param>
    /// <returns>The rotation, normalised to a quarter turn.</returns>
    private static PageRotation ReadRotation(PdfFileReader reader, SourcePage page)
    {
        var value = ReadNumber(reader, page.Dictionary.Get("Rotate"))
            ?? ReadNumber(reader, page.Inherited.Get("Rotate"));

        if (value is null)
        {
            return PageRotation.None;
        }

        var normalized = (((int)Math.Round(value.Value / 90d) * 90) % 360 + 360) % 360;

        return normalized switch
        {
            90 => PageRotation.Clockwise90,
            180 => PageRotation.Clockwise180,
            270 => PageRotation.Clockwise270,
            _ => PageRotation.None,
        };
    }
}
