using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The content-stream half of <see cref="PdfPageContentReader"/>: the bytes of a page's painting, and
/// the transform that says where what it paints lands.
/// </summary>
public sealed partial class PdfPageContentReader
{
    /// <summary>Reads and decodes the content streams of a page, concatenated in order.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="page">The page.</param>
    /// <param name="warnings">The list that receives a remark per stream that could not be decoded.</param>
    /// <param name="pageNumber">The one-based number of the page, used in diagnostics.</param>
    /// <returns>The painting of the page, or <see langword="null"/> when it has none.</returns>
    /// <remarks>
    /// The streams of a page are one instruction sequence, so they are concatenated with a whitespace
    /// byte between them, exactly as a reader concatenates them before running them. A stream that
    /// cannot be decoded is skipped and reported: the rest of the page still paints.
    /// </remarks>
    internal static byte[]? ReadPageContent(
        PdfFileReader reader,
        SourcePage page,
        List<PdfWarning> warnings,
        int pageNumber)
    {
        var streams = new List<PdfStream>();
        var contents = page.Dictionary.Get("Contents");

        switch (contents)
        {
            case PdfStream single:
                streams.Add(single);
                break;

            case PdfArray array:
                CollectStreams(reader, array, streams);
                break;

            case PdfReference reference:
                switch (reader.Resolve(reference))
                {
                    case PdfStream stream:
                        streams.Add(stream);
                        break;

                    case PdfArray referenced:
                        CollectStreams(reader, referenced, streams);
                        break;
                }

                break;
        }

        if (streams.Count == 0)
        {
            return null;
        }

        using var buffer = new PooledByteBuffer();
        var written = 0;

        foreach (var stream in streams)
        {
            if (TryDecodeStream(stream, out var decoded))
            {
                // The separator goes between the streams, not after the last one: a single stream is its
                // own sequence, and an inline image that runs to the end of it must not gain a byte.
                if (written > 0)
                {
                    buffer.WriteByte((byte)'\n');
                }

                buffer.Write(decoded);
                written++;
                continue;
            }

            warnings.Add(new(
                PdfWarningKind.ContentStreamUnreadable,
                $"'{reader.SourceName}': a content stream of page {pageNumber} could not be decoded and was skipped."));
        }

        return buffer.Length == 0 ? null : buffer.ToExactArray();
    }

    /// <summary>Collects the streams of a <c>/Contents</c> array, following references.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="array">The array.</param>
    /// <param name="streams">The list that receives the streams.</param>
    private static void CollectStreams(PdfFileReader reader, PdfArray array, List<PdfStream> streams)
    {
        foreach (var item in array.Items)
        {
            switch (item)
            {
                case PdfStream stream:
                    streams.Add(stream);
                    break;

                case PdfReference reference when reader.Resolve(reference) is PdfStream referenced:
                    streams.Add(referenced);
                    break;

                // A damaged document may nest the array one level deeper than the specification allows.
                case PdfReference reference when reader.Resolve(reference) is PdfArray nested:
                    CollectStreams(reader, nested, streams);
                    break;
            }
        }
    }

    /// <summary>Decodes a stream whose filters are the ones a content stream may use.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="decoded">The decoded bytes when decoding succeeded.</param>
    /// <returns><see langword="true"/> when the stream could be decoded.</returns>
    private static bool TryDecodeStream(PdfStream stream, out byte[] decoded)
    {
        try
        {
            decoded = PdfFilters.Decode(stream, "content stream");
            return true;
        }
        catch (Domain.Exceptions.PdfUnsupportedFeatureException)
        {
            decoded = [];
            return false;
        }
        catch (Domain.Exceptions.PdfSourceFormatException)
        {
            decoded = [];
            return false;
        }
    }

    /// <summary>Reads a form's <c>/Matrix</c>.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value of the entry.</param>
    /// <returns>The matrix, or <see langword="null"/> when it is absent or unreadable.</returns>
    private static PdfMatrix? ReadMatrix(PdfFileReader reader, PdfObject? value)
    {
        var array = reader.ResolveArray(value);

        if (array is null || array.Count < 6)
        {
            return null;
        }

        var numbers = new double[6];

        for (var index = 0; index < numbers.Length; index++)
        {
            if (ReadNumber(reader, array.Items[index]) is not { } number)
            {
                return null;
            }

            numbers[index] = number;
        }

        return new PdfMatrix(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
    }
}

/// <summary>
/// A 2×3 affine transform, the form a PDF uses for <c>cm</c> and for a form's <c>/Matrix</c>.
/// </summary>
/// <param name="A">The x scale.</param>
/// <param name="B">The y shear.</param>
/// <param name="C">The x shear.</param>
/// <param name="D">The y scale.</param>
/// <param name="E">The x translation.</param>
/// <param name="F">The y translation.</param>
internal readonly record struct PdfMatrix(double A, double B, double C, double D, double E, double F)
{
    /// <summary>Gets the identity transform.</summary>
    public static PdfMatrix Identity => new(1d, 0d, 0d, 1d, 0d, 0d);

    /// <summary>Concatenates two transforms, as the PDF operator does.</summary>
    /// <param name="left">The transform that is applied second.</param>
    /// <param name="right">The transform that is applied first.</param>
    /// <returns>The concatenated transform.</returns>
    public static PdfMatrix operator *(PdfMatrix left, PdfMatrix right) => new(
        (left.A * right.A) + (left.B * right.C),
        (left.A * right.B) + (left.B * right.D),
        (left.C * right.A) + (left.D * right.C),
        (left.C * right.B) + (left.D * right.D),
        (left.E * right.A) + (left.F * right.C) + right.E,
        (left.E * right.B) + (left.F * right.D) + right.F);

    /// <summary>Maps a point through this transform.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The mapped point.</returns>
    public (double X, double Y) Transform(double x, double y) =>
        ((A * x) + (C * y) + E, (B * x) + (D * y) + F);

    /// <summary>Maps the unit square through this transform and returns the area it covers.</summary>
    /// <returns>The bounding rectangle of the transformed square.</returns>
    public PdfRectangle TransformUnitSquare()
    {
        var (x1, y1) = Transform(0d, 0d);
        var (x2, y2) = Transform(1d, 0d);
        var (x3, y3) = Transform(0d, 1d);
        var (x4, y4) = Transform(1d, 1d);

        return PdfRectangle.FromCorners(
            Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)),
            Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)),
            Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)),
            Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
    }
}
