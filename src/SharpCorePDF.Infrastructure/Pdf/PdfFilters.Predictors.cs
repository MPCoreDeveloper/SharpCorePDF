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

        ValidatePredictor(predictor, bitsPerComponent, sourceName);

        if (predictor == 2)
        {
            return ApplyTiffPredictor(data, colors, columns);
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

    private static byte[] ApplyTiffPredictor(byte[] data, int colors, int columns)
    {
        var rowLength = columns * colors;

        for (var row = 0; row + rowLength <= data.Length; row += rowLength)
        {
            UndoTiffRow(data.AsSpan(row, rowLength), colors);
        }

        return data;
    }

    /// <summary>
    /// Undoes TIFF prediction in one row, where every sample is predicted from the sample of the same
    /// component in the previous pixel.
    /// </summary>
    /// <param name="row">The row, predicted in place.</param>
    /// <param name="colors">The number of components of a pixel.</param>
    private static void UndoTiffRow(Span<byte> row, int colors)
    {
        for (var index = colors; index < row.Length; index++)
        {
            row[index] = (byte)(row[index] + row[index - colors]);
        }
    }

    /// <summary>Reports a predictor value, or a bit depth, that prediction cannot be undone for.</summary>
    /// <param name="predictor">The predictor the filter declares.</param>
    /// <param name="bitsPerComponent">The bits of each sample.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <exception cref="PdfUnsupportedFeatureException">
    /// The predictor is not defined by the specification, or TIFF prediction is used with a bit depth
    /// other than eight.
    /// </exception>
    private static void ValidatePredictor(int predictor, int bitsPerComponent, string sourceName)
    {
        if (predictor == 2)
        {
            if (bitsPerComponent != 8)
            {
                throw new PdfUnsupportedFeatureException(
                    sourceName,
                    "Predictor",
                    $"TIFF prediction with {bitsPerComponent} bits per component is not implemented; PNG prediction is.");
            }

            return;
        }

        if (predictor is < 10 or > 15)
        {
            throw new PdfUnsupportedFeatureException(
                sourceName,
                "Predictor",
                $"the predictor value {predictor} is not defined by the PDF specification.");
        }
    }

    /// <summary>Creates the row-at-a-time decoder for the predictor a filter declares.</summary>
    /// <param name="parameters">The decode parameters of the filter, or <see langword="null"/>.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>
    /// The decoder, or <see langword="null"/> when the filter declares no predictor to undo. The caller
    /// owns the decoder and disposes it.
    /// </returns>
    /// <exception cref="PdfUnsupportedFeatureException">
    /// The predictor is not defined by the specification, TIFF prediction is used with a bit depth other
    /// than eight, or a predicted row is larger than the engine decodes.
    /// </exception>
    public static PredictorRowDecoder? CreatePredictorDecoder(
        PdfDictionary? parameters,
        string sourceName)
    {
        var predictor = (int)(parameters?.GetInteger("Predictor") ?? 1);

        if (predictor <= 1)
        {
            return null;
        }

        var bitsPerComponent = (int)(parameters?.GetInteger("BitsPerComponent") ?? 8);
        var colors = (int)(parameters?.GetInteger("Colors") ?? parameters?.GetInteger("ColorSpace") ?? 1);
        var columns = (int)(parameters?.GetInteger("Columns") ?? 1);

        ValidatePredictor(predictor, bitsPerComponent, sourceName);

        return new PredictorRowDecoder(
            predictor,
            Math.Max(1, colors),
            bitsPerComponent,
            Math.Max(1, columns),
            sourceName);
    }

    /// <summary>
    /// The largest row of a predicted payload the engine decodes, as a guard against a header that
    /// claims a gigabyte and turns a read into an allocation.
    /// </summary>
    private const int MaximumPredictedRowBytes = 8 * 1024 * 1024;

    /// <summary>Receives one undone row of a predicted payload.</summary>
    /// <param name="row">The row, which is pooled and valid only for the duration of the call.</param>
    internal delegate void RowHandler(ReadOnlySpan<byte> row);

    /// <summary>
    /// Undoes PNG or TIFF prediction one row at a time, so a payload is decoded while it is inflated
    /// instead of being held twice: once compressed and once whole.
    /// </summary>
    /// <remarks>
    /// The decoder keeps the three rows it needs — the encoded row, the row being undone and the row
    /// above it — and hands each finished row to its caller, which is what lets an image larger than
    /// the inline budget be counted rather than buffered.
    /// </remarks>
    internal sealed class PredictorRowDecoder : IDisposable
    {
        private readonly bool tiff;
        private readonly int rowLength;
        private readonly int stride;
        private readonly int colors;
        private readonly int bytesPerPixel;
        private readonly string sourceName;
        private readonly byte[] encoded;
        private readonly byte[] current;
        private readonly byte[] previous;
        private int fill;
        private bool returned;

        /// <summary>Initializes a new instance of the <see cref="PredictorRowDecoder"/> class.</summary>
        /// <param name="predictor">The predictor the filter declares.</param>
        /// <param name="colors">The number of components of a pixel, at least one.</param>
        /// <param name="bitsPerComponent">The bits of each sample.</param>
        /// <param name="columns">The number of samples in a row, at least one.</param>
        /// <param name="sourceName">The name of the source, used in diagnostics.</param>
        /// <exception cref="PdfUnsupportedFeatureException">The row is larger than the engine decodes.</exception>
        public PredictorRowDecoder(
            int predictor,
            int colors,
            int bitsPerComponent,
            int columns,
            string sourceName)
        {
            tiff = predictor == 2;
            this.colors = colors;
            this.sourceName = sourceName;

            var bits = (long)columns * colors * bitsPerComponent;

            if (bits <= 0 || bits > MaximumPredictedRowBytes * 8L)
            {
                throw new PdfUnsupportedFeatureException(
                    sourceName,
                    "Predictor",
                    "the predicted row is larger than the engine decodes.");
            }

            rowLength = (int)((bits + 7) / 8);
            bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
            stride = tiff ? rowLength : rowLength + 1;

            encoded = ArrayPool<byte>.Shared.Rent(stride);
            current = ArrayPool<byte>.Shared.Rent(rowLength);
            previous = ArrayPool<byte>.Shared.Rent(rowLength);

            // A rented array still holds whatever the pool last used it for, and the first row is
            // predicted against a row of zeroes rather than against that.
            previous.AsSpan(0, rowLength).Clear();
        }

        /// <summary>Gets the number of bytes in an undone row.</summary>
        public int RowLength => rowLength;

        /// <summary>Gets the number of whole rows undone so far.</summary>
        public long RowsDecoded { get; private set; }

        /// <summary>Gets the bytes of a row that is not complete yet, which prediction cannot undo.</summary>
        public ReadOnlySpan<byte> PendingRow => encoded.AsSpan(0, fill);

        /// <summary>Undoes every row that the given bytes complete, handing each to a caller.</summary>
        /// <param name="data">The inflated bytes, in the order the stream produced them.</param>
        /// <param name="onRow">Receives each row as soon as it is undone.</param>
        public void Push(ReadOnlySpan<byte> data, RowHandler onRow)
        {
            ArgumentNullException.ThrowIfNull(onRow);

            var offset = 0;

            while (offset < data.Length)
            {
                var take = Math.Min(stride - fill, data.Length - offset);

                data.Slice(offset, take).CopyTo(encoded.AsSpan(fill));

                offset += take;
                fill += take;

                if (fill < stride)
                {
                    continue;
                }

                fill = 0;

                var row = current.AsSpan(0, rowLength);

                if (tiff)
                {
                    encoded.AsSpan(0, rowLength).CopyTo(row);
                    UndoTiffRow(row, colors);
                }
                else
                {
                    var filterType = encoded[0];

                    encoded.AsSpan(1, rowLength).CopyTo(row);
                    UndoPngRow(filterType, row, previous.AsSpan(0, rowLength), bytesPerPixel, sourceName);
                }

                onRow(row);
                row.CopyTo(previous);
                RowsDecoded++;
            }
        }

        /// <summary>Returns the rented rows to the pool.</summary>
        public void Dispose()
        {
            if (returned)
            {
                return;
            }

            returned = true;

            ArrayPool<byte>.Shared.Return(encoded);
            ArrayPool<byte>.Shared.Return(current);
            ArrayPool<byte>.Shared.Return(previous);
        }
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
