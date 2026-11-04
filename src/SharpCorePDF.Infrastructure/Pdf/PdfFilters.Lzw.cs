using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The LZW half of <see cref="PdfFilters"/>.
/// </summary>
/// <remarks>
/// <para>
/// LZW is the compressor PDF inherits from TIFF, and the specification keeps TIFF's packing: codes are
/// nine bits wide to begin with, grow one bit at a time to twelve, and are read most significant bit
/// first out of the byte stream. A reader that skips it hands back compressed bytes where the document
/// promised samples, so an image that declares <c>LZWDecode</c> is decoded here rather than reported as
/// a filter the library does not implement.
/// </para>
/// <para>
/// The table is built while the stream is read, so a code may name an entry the decoder has not been
/// told yet: when a code equals the next free slot its sequence is the previous one followed by that
/// sequence's own first byte. That case is the one a decoder that fills the table up front gets wrong,
/// so it is stated explicitly.
/// </para>
/// </remarks>
internal static partial class PdfFilters
{
    /// <summary>The code that empties the table and returns the width to nine bits.</summary>
    private const int LzwClearTable = 256;

    /// <summary>The code that ends the stream.</summary>
    private const int LzwEndOfData = 257;

    /// <summary>The first code that stands for a sequence rather than for one byte.</summary>
    private const int LzwFirstTableCode = 258;

    /// <summary>The number of codes a table can hold, which is the largest a twelve-bit code addresses.</summary>
    private const int LzwMaxCodes = 4096;

    /// <summary>The width of the first code, in bits.</summary>
    private const int LzwInitialCodeWidth = 9;

    /// <summary>The width of the widest code, in bits.</summary>
    private const int LzwMaxCodeWidth = 12;

    /// <summary>The one-byte sequences the literal codes stand for.</summary>
    private static readonly byte[][] LzwLiterals = CreateLzwLiterals();

    /// <summary>Receives the bytes a stream produces, in order, as soon as they are known.</summary>
    /// <param name="bytes">One sequence of the table.</param>
    internal delegate void LzwByteSink(ReadOnlySpan<byte> bytes);

    /// <summary>Decodes an LZW stream, reporting every sequence the table defines.</summary>
    /// <param name="encoded">The bytes the document stores.</param>
    /// <param name="earlyChange">Whether the code width grows one code early, which is TIFF's choice
    /// and the specification's default.</param>
    /// <param name="sink">Receives the decoded sequences in order, or <see langword="null"/> when only
    /// the length is wanted.</param>
    /// <param name="length">The number of bytes the stream decodes to, whether or not they were
    /// reported.</param>
    /// <returns><see langword="true"/> when the stream decoded; <see langword="false"/> when a code
    /// named an entry the table never held, or when the stream produced nothing.</returns>
    /// <remarks>
    /// A stream that stops at a code boundary without its end-of-data code is incomplete rather than
    /// wrong, so the sequences it did define are reported: their bytes are what the document holds.
    /// </remarks>
    internal static bool TryDecodeLzw(
        ReadOnlySpan<byte> encoded,
        int earlyChange,
        LzwByteSink? sink,
        out long length)
    {
        length = 0;

        if (encoded.IsEmpty)
        {
            return false;
        }

        var table = new byte[LzwMaxCodes][];

        Array.Copy(LzwLiterals, table, LzwLiterals.Length);

        var source = new LzwCodeReader(encoded);
        var codeWidth = LzwInitialCodeWidth;
        var nextCode = LzwFirstTableCode;
        byte[]? previous = null;

        while (source.TryRead(codeWidth, out var code))
        {
            if (code == LzwClearTable)
            {
                codeWidth = LzwInitialCodeWidth;
                nextCode = LzwFirstTableCode;
                previous = null;
                continue;
            }

            if (code == LzwEndOfData)
            {
                return length > 0;
            }

            if (!TryResolve(table, nextCode, previous, code, out var sequence))
            {
                // Nothing that came before this code can be trusted to be the payload: a code the table
                // never held means the stream and the table disagree, so the length is zeroed with it.
                length = 0;
                return false;
            }

            sink?.Invoke(sequence);
            length += sequence.Length;

            if (previous is not null && nextCode < LzwMaxCodes)
            {
                var grown = new byte[previous.Length + 1];

                previous.CopyTo(grown, 0);
                grown[^1] = sequence[0];
                table[nextCode++] = grown;

                // The next code needs a wider field once the table holds everything the current width
                // can address; EarlyChange moves that moment one code earlier, which is what TIFF does.
                if (codeWidth < LzwMaxCodeWidth && nextCode + earlyChange >= 1 << codeWidth)
                {
                    codeWidth++;
                }
            }

            previous = sequence;
        }

        // The stream ended between two codes, so every code it held was decoded: what is missing is the
        // end-of-data code and possibly a partial one, neither of which carries a sample.
        return length > 0;
    }

    /// <summary>Reads the <c>EarlyChange</c> parameter, which TIFF and PDF both default to one.</summary>
    /// <param name="parameters">The decode parameters of the filter, or <see langword="null"/>.</param>
    /// <returns>Zero only when the document states it; one otherwise.</returns>
    internal static int ReadEarlyChange(PdfDictionary? parameters) =>
        parameters?.GetInteger("EarlyChange") == 0 ? 0 : 1;

    /// <summary>Decodes an LZW stream in full, for the paths that must materialise their bytes.</summary>
    /// <param name="data">The compressed bytes.</param>
    /// <param name="parameters">The decode parameters of the filter, which state <c>EarlyChange</c>.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="PdfSourceFormatException">The stream is not a valid LZW stream.</exception>
    internal static byte[] ApplyLzw(
        ReadOnlyMemory<byte> data,
        PdfDictionary? parameters,
        string sourceName)
    {
        var output = new PooledByteBuffer();

        try
        {
            if (!TryDecodeLzw(data.Span, ReadEarlyChange(parameters), output.Write, out _))
            {
                throw new PdfSourceFormatException(sourceName, "an LZW stream could not be decompressed.");
            }

            return output.ToExactArray();
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary>Resolves the sequence a code stands for.</summary>
    /// <param name="table">The codes the stream has defined so far.</param>
    /// <param name="nextCode">The first slot that is still free.</param>
    /// <param name="previous">The sequence of the code before this one, or <see langword="null"/> when
    /// this is the first code after a clear.</param>
    /// <param name="code">The code to resolve.</param>
    /// <param name="sequence">The bytes the code stands for.</param>
    /// <returns><see langword="true"/> when the code names a sequence this stream holds.</returns>
    private static bool TryResolve(
        byte[][] table,
        int nextCode,
        byte[]? previous,
        int code,
        out byte[] sequence)
    {
        if (code < nextCode && table[code] is { } stored)
        {
            sequence = stored;
            return true;
        }

        if (code == nextCode && previous is not null)
        {
            // The encoder pointed at the entry it is about to define: its sequence is the one before it,
            // followed by that sequence's own first byte.
            sequence = new byte[previous.Length + 1];
            previous.CopyTo(sequence, 0);
            sequence[^1] = previous[0];
            return true;
        }

        sequence = [];
        return false;
    }

    /// <summary>Builds the one-byte sequences the literal codes stand for.</summary>
    /// <returns>The table of literals, indexed by code.</returns>
    private static byte[][] CreateLzwLiterals()
    {
        var literals = new byte[LzwClearTable][];

        for (var value = 0; value < literals.Length; value++)
        {
            literals[value] = [(byte)value];
        }

        return literals;
    }

    /// <summary>Reads the codes of an LZW stream, most significant bit first.</summary>
    /// <remarks>
    /// Codes are packed across byte boundaries and are not aligned to them, so a reader that pulled one
    /// byte at a time would have to reassemble every code. The bits that have been consumed are masked
    /// away after each read, which keeps the accumulator below twenty bits.
    /// </remarks>
    private ref struct LzwCodeReader
    {
        private readonly ReadOnlySpan<byte> encoded;
        private int position;
        private int bits;
        private int bitCount;

        /// <summary>Initializes a new instance of the <see cref="LzwCodeReader"/> struct.</summary>
        /// <param name="encoded">The bytes to read.</param>
        public LzwCodeReader(ReadOnlySpan<byte> encoded)
        {
            this.encoded = encoded;
            position = 0;
            bits = 0;
            bitCount = 0;
        }

        /// <summary>Reads the next code.</summary>
        /// <param name="width">The number of bits the code occupies.</param>
        /// <param name="code">The code, or zero when no whole code is left.</param>
        /// <returns><see langword="true"/> when a whole code was read.</returns>
        public bool TryRead(int width, out int code)
        {
            while (bitCount < width)
            {
                if (position >= encoded.Length)
                {
                    code = 0;
                    return false;
                }

                bits = (bits << 8) | encoded[position++];
                bitCount += 8;
            }

            code = (bits >> (bitCount - width)) & ((1 << width) - 1);
            bitCount -= width;

            // Keeping only the bits that have not been consumed stops the accumulator from growing with
            // the stream: it holds at most one code plus the eight bits of the byte read last.
            bits &= (1 << bitCount) - 1;

            return true;
        }
    }
}
