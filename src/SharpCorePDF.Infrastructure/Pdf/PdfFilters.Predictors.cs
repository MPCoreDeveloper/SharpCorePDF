using System.Buffers;
using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The predictor half of <see cref="PdfFilters"/>.
/// </summary>
/// <remarks>
/// Cross-reference streams almost always use PNG prediction to keep tables of offsets compact, so a
/// reader that ignores predictors reads nonsense.
/// </remarks>
internal static partial class PdfFilters
{
    private static byte[] ApplyPredictor(
        byte[] data,
        int predictor,
        int colors,
        int bitsPerComponent,
        int columns,
        string sourceName)
    {
        if (colors < 1)
        {
            colors = 1;
        }

        if (columns < 1)
        {
            columns = 1;
        }

        if (predictor == 2)
        {
            return ApplyTiffPredictor(data, colors, bitsPerComponent, columns, sourceName);
        }

        if (predictor is < 10 or > 15)
        {
            throw new PdfUnsupportedFeatureException(
                sourceName,
                "Predictor",
                $"the predictor value {predictor} is not defined by the PDF specification.");
        }

        var bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
        var rowLength = (columns * colors * bitsPerComponent + 7) / 8;

        if (rowLength <= 0 || data.Length <= rowLength)
        {
            return data;
        }

        // Every row is preceded by a filter type byte, so the stride is one larger than the row.
        var rows = data.Length / (rowLength + 1);
        var result = new byte[rows * rowLength];
        var previous = ArrayPool<byte>.Shared.Rent(rowLength);

        try
        {
            // A rented array still holds whatever the pool last used it for, and the first row is
            // predicted against a row of zeroes rather than against that.
            previous.AsSpan(0, rowLength).Clear();

            ApplyPngRows(data, result, previous.AsSpan(0, rowLength), rows, rowLength, bytesPerPixel, sourceName);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(previous);
        }

        return result;
    }

    /// <summary>Undoes PNG prediction one row at a time.</summary>
    private static void ApplyPngRows(
        byte[] data,
        byte[] result,
        Span<byte> previous,
        int rows,
        int rowLength,
        int bytesPerPixel,
        string sourceName)
    {
        for (var row = 0; row < rows; row++)
        {
            var source = row * (rowLength + 1);
            var filterType = data[source];
            var current = result.AsSpan(row * rowLength, rowLength);

            data.AsSpan(source + 1, rowLength).CopyTo(current);

            switch (filterType)
            {
                case 0:
                    break;

                case 1:
                    for (var index = bytesPerPixel; index < rowLength; index++)
                    {
                        current[index] = (byte)(current[index] + current[index - bytesPerPixel]);
                    }

                    break;

                case 2:
                    for (var index = 0; index < rowLength; index++)
                    {
                        current[index] = (byte)(current[index] + previous[index]);
                    }

                    break;

                case 3:
                    for (var index = 0; index < rowLength; index++)
                    {
                        var left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
                        current[index] = (byte)(current[index] + ((left + previous[index]) / 2));
                    }

                    break;

                case 4:
                    for (var index = 0; index < rowLength; index++)
                    {
                        var left = index >= bytesPerPixel ? current[index - bytesPerPixel] : (byte)0;
                        var upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : (byte)0;
                        current[index] = (byte)(current[index] + Paeth(left, previous[index], upperLeft));
                    }

                    break;

                default:
                    throw new PdfSourceFormatException(
                        sourceName,
                        $"a compressed stream uses the undefined PNG predictor filter type {filterType}.");
            }

            current.CopyTo(previous);
        }
    }

    private static byte[] ApplyTiffPredictor(
        byte[] data,
        int colors,
        int bitsPerComponent,
        int columns,
        string sourceName)
    {
        if (bitsPerComponent != 8)
        {
            throw new PdfUnsupportedFeatureException(
                sourceName,
                "Predictor",
                $"TIFF prediction with {bitsPerComponent} bits per component is not implemented; PNG prediction is.");
        }

        var rowLength = columns * colors;

        for (var row = 0; row + rowLength <= data.Length; row += rowLength)
        {
            for (var index = colors; index < rowLength; index++)
            {
                data[row + index] = (byte)(data[row + index] + data[row + index - colors]);
            }
        }

        return data;
    }

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        var estimate = left + above - upperLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceAbove = Math.Abs(estimate - above);
        var distanceUpperLeft = Math.Abs(estimate - upperLeft);

        return distanceLeft <= distanceAbove && distanceLeft <= distanceUpperLeft
            ? left
            : distanceAbove <= distanceUpperLeft ? above : upperLeft;
    }
}
