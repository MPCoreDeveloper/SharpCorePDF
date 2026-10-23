using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Reading.Exporters;

/// <summary>
/// The single place that decides which of the four exporters writes a read, and the single place that
/// refuses an export before any work is done: a destination that cannot be written and a format this
/// release does not produce are both answered at the call site.
/// </summary>
internal static class LayoutExportWriter
{
    /// <summary>Checks that an export can be written at all.</summary>
    /// <param name="format">The format the caller asked for.</param>
    /// <param name="destination">The stream the document would be written to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is not a format this release writes.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> cannot be written to.</exception>
    /// <remarks>
    /// The engine calls this before it opens the document, so a caller that asks for something this release
    /// cannot produce does not pay for the read and is not left with a half-written stream.
    /// </remarks>
    public static void Validate(PdfLayoutFormat format, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "A layout export format must be one this library writes.");
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }
    }

    /// <summary>Writes the reading of a range in the format the caller selected.</summary>
    /// <param name="blocks">The blocks the read produced.</param>
    /// <param name="pages">The boxes of the pages of the range, in ascending page order.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="destination">The stream the document is written to; it is left open.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is not a format this release writes.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> cannot be written to.</exception>
    public static void Write(
        PdfReadResult<PdfTextBlock> blocks,
        IReadOnlyList<PdfPageBox> pages,
        PdfLayoutFormat format,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(pages);

        Validate(format, destination);

        var document = LayoutExportPages.Project(blocks, pages);

        switch (format)
        {
            case PdfLayoutFormat.Alto:
                AltoExporter.Write(document, destination);
                break;

            case PdfLayoutFormat.PageXml:
                PageXmlExporter.Write(document, destination);
                break;

            case PdfLayoutFormat.Hocr:
                HocrExporter.Write(document, destination);
                break;

            case PdfLayoutFormat.SvgText:
                SvgTextExporter.Write(document, destination);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "A layout export format must be one this library writes.");
        }
    }
}
