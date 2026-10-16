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
            UndoPngRow(filterType, current, previous, bytesPerPixel, sourceName);
            current.CopyTo(previous);
        }
    }

    /// <summary>Undoes one row of PNG prediction, which is the filter type byte of that row.</summary>
    /// <param name="filterType">The filter type of the row.</param>
    /// <param name="current">The row, predicted in place.</param>
    /// <param name="previous">The row above it, already undone.</param>
    /// <param name="bytesPerPixel">The distance between two pixels of a row.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    private static void UndoPngRow(
        byte filterType,
        Span<byte> current,
        ReadOnlySpan<byte> previous,
        int bytesPerPixel,
        string sourceName)
    {
        switch (filterType)
        {
            case 0:
                return;

            case 1:
                UndoSub(current, bytesPerPixel);
                return;

            case 2:
                UndoUp(current, previous);
                return;

            case 3:
                UndoAverage(current, previous, bytesPerPixel);
                return;

            case 4:
                UndoPaeth(current, previous, bytesPerPixel);
                return;

            default:
                throw new PdfSourceFormatException(
                    sourceName,
                    $"a compressed stream uses the undefined PNG predictor filter type {filterType}.");
        }
    }

    /// <summary>Undoes filter type 1: every byte is predicted from the byte one pixel to its left.</summary>
    /// <param name="current">The row, predicted in place.</param>
    /// <param name="bytesPerPixel">The distance between two pixels of a row.</param>
    private static void UndoSub(Span<byte> current, int bytesPerPixel)
    {
        for (var index = bytesPerPixel; index < current.Length; index++)
        {
            current[index] = (byte)(current[index] + current[index - bytesPerPixel]);
        }
    }

    /// <summary>Undoes filter type 2: every byte is predicted from the byte above it.</summary>
    /// <param name="current">The row, predicted in place.</param>
    /// <param name="previous">The row above it, already undone.</param>
    private static void UndoUp(Span<byte> current, ReadOnlySpan<byte> previous)
    {
        for (var index = 0; index < current.Length; index++)
        {
            current[index] = (byte)(current[index] + previous[index]);
        }
    }

    /// <summary>Undoes filter type 3: every byte is predicted from the mean of its neighbours.</summary>
    /// <param name="current">The row, predicted in place.</param>
    /// <param name="previous">The row above it, already undone.</param>
    /// <param name="bytesPerPixel">The distance between two pixels of a row.</param>
    private static void UndoAverage(Span<byte> current, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        for (var index = 0; index < current.Length; index++)
        {
            var left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
            current[index] = (byte)(current[index] + ((left + previous[index]) / 2));
        }
    }

    /// <summary>Undoes filter type 4: every byte is predicted from its three neighbours.</summary>
    /// <param name="current">The row, predicted in place.</param>
    /// <param name="previous">The row above it, already undone.</param>
    /// <param name="bytesPerPixel">The distance between two pixels of a row.</param>
    private static void UndoPaeth(Span<byte> current, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        for (var index = 0; index < current.Length; index++)
        {
            var left = index >= bytesPerPixel ? current[index - bytesPerPixel] : (byte)0;
            var upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : (byte)0;
            current[index] = (byte)(current[index] + Paeth(left, previous[index], upperLeft));
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

        if (distanceLeft <= distanceAbove && distanceLeft <= distanceUpperLeft)
        {
            return left;
        }

        return distanceAbove <= distanceUpperLeft ? above : upperLeft;
    }
}
