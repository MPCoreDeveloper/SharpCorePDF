using System.Security.Cryptography;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Turns the drawing operations of one page into a content stream.
/// </summary>
/// <remarks>
/// The stream is written as PDF operators, in page space, with the top-left origin the public canvas
/// states converted here: a text baseline at <c>y</c> from the top becomes <c>pageHeight - y</c>, and an
/// image whose top edge is at <c>y</c> becomes <c>pageHeight - y - height</c>. Text is written in the
/// font's own codes — bytes of <c>WinAnsiEncoding</c> for a standard font, two byte glyph ids for an
/// embedded one — and an embedded font's string is written in hexadecimal form, which is half the size
/// of escaping every high byte as an octal sequence.
/// </remarks>
internal static class PageContentWriter
{
    /// <summary>Writes the content stream of a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="resources">The resources the page selects, which receive every face and image it uses.</param>
    /// <param name="hash">The digest of the document, which receives the bytes.</param>
    /// <returns>The content stream.</returns>
    public static byte[] Write(PdfPageCanvas page, PageResources resources, IncrementalHash hash)
    {
        var content = new ContentStreamBuilder();

        foreach (var operation in page.Operations)
        {
            switch (operation)
            {
                case TextOperation text:
                    WriteText(content, page, text, resources);
                    break;

                case ImageOperation image:
                    WriteImage(content, page, image, resources);
                    break;

                case PathOperation path:
                    WritePath(content, page, path);
                    break;

                case SaveStateOperation:
                    content.SaveState();
                    break;

                case RestoreStateOperation:
                    content.RestoreState();
                    break;

                case ClipOperation clip:
                    content.SetClip(ToPdfPath(clip.Path, page.Height), clip.FillRule);
                    break;

                case TransformOperation transform:
                    content.Transform(ToPdfMatrix(transform.Matrix, page.Height));
                    break;
            }
        }

        resources.AddGraphicsStates(content.Resources);
        var bytes = content.Build();
        hash.AppendData(bytes);
        return bytes;
    }

    /// <summary>Writes one text operation.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="page">The page, which states the height the baseline is measured from.</param>
    /// <param name="text">The operation.</param>
    /// <param name="resources">The resources of the page.</param>
    private static void WriteText(
        ContentStreamBuilder destination,
        PdfPageCanvas page,
        TextOperation text,
        PageResources resources) =>
        TextLayoutWriter.Write(destination, page, text, resources);

    /// <summary>Writes one image operation.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="page">The page, which states the height the top edge is measured from.</param>
    /// <param name="image">The operation.</param>
    /// <param name="resources">The resources of the page.</param>
    private static void WriteImage(
        ContentStreamBuilder destination,
        PdfPageCanvas page,
        ImageOperation image,
        PageResources resources)
    {
        var resource = resources.Image(image.Image);

        destination.Write("q\n");
        destination.WriteNumber(image.Width);
        destination.Write(" 0 0 ");
        destination.WriteNumber(image.Height);
        destination.Write(" ");
        destination.WriteNumber(image.X);
        destination.Write(" ");
        destination.WriteNumber(page.Height - image.Y - image.Height);
        destination.Write(" cm\n/");
        destination.Write(resource.Name);
        destination.Write(" Do\nQ\n");
    }

    /// <summary>Writes one path operation.</summary>
    /// <param name="destination">The content stream.</param>
    /// <param name="page">The page, which states the height canvas coordinates are measured from.</param>
    /// <param name="path">The operation.</param>
    private static void WritePath(ContentStreamBuilder destination, PdfPageCanvas page, PathOperation path) =>
        destination.DrawPath(ToPdfPath(path.Path, page.Height), path.Fill, path.Stroke);

    /// <summary>Converts a path from top-left canvas coordinates to PDF user space.</summary>
    /// <param name="path">The path.</param>
    /// <param name="height">The height of the page.</param>
    /// <returns>The path in PDF user space.</returns>
    private static PdfPath ToPdfPath(PdfPath path, double height)
    {
        var segments = new PdfPathSegment[path.Segments.Count];

        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = path.Segments[index] switch
            {
                PdfPathSegment.MoveTo move => new PdfPathSegment.MoveTo(ToPdfPoint(move.Point, height)),
                PdfPathSegment.LineTo line => new PdfPathSegment.LineTo(ToPdfPoint(line.Point, height)),
                PdfPathSegment.CubicTo cubic => new PdfPathSegment.CubicTo(
                    ToPdfPoint(cubic.Control1, height),
                    ToPdfPoint(cubic.Control2, height),
                    ToPdfPoint(cubic.End, height)),
                PdfPathSegment.QuadraticTo quadratic => new PdfPathSegment.QuadraticTo(
                    ToPdfPoint(quadratic.Control, height),
                    ToPdfPoint(quadratic.End, height)),
                PdfPathSegment.Close close => close,
                var unknown => throw new ArgumentException(
                    $"The path segment {unknown.GetType().Name} is not one the engine writes.",
                    nameof(path)),
            };
        }

        return new PdfPath(segments, path.IsClosed);
    }

    /// <summary>Converts a point from top-left canvas coordinates to PDF user space.</summary>
    /// <param name="point">The point.</param>
    /// <param name="height">The height of the page.</param>
    /// <returns>The point in PDF user space.</returns>
    private static PdfPoint ToPdfPoint(PdfPoint point, double height) => new(point.X, height - point.Y);

    /// <summary>
    /// Converts a transform stated in top-left coordinates to the equivalent transform in PDF user space.
    /// </summary>
    /// <param name="matrix">The transform.</param>
    /// <param name="height">The height of the page.</param>
    /// <returns>The transform to concatenate with <c>cm</c>.</returns>
    private static SharpCorePDF.Domain.ValueObjects.PdfMatrix ToPdfMatrix(SharpCorePDF.Domain.ValueObjects.PdfMatrix matrix, double height) =>
        new(
            matrix.A,
            -matrix.B,
            -matrix.C,
            matrix.D,
            (matrix.C * height) + matrix.E,
            height - (matrix.D * height) - matrix.F);
}
