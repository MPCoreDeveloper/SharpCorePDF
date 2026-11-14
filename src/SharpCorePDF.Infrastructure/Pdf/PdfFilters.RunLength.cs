namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The run-length filter half of <see cref="PdfFilters"/>.
/// </summary>
/// <remarks>
/// RunLength is the oldest of the filters PDF inherits from TIFF, and the only one whose output is larger
/// than its input by design: one control byte and one data byte can stand for 128 bytes of samples. It is
/// used for bilevel images and a few scan-style payloads, and a reader that skips it hands back packed
/// runs where the document promised samples, so it is decoded rather than reported as unsupported.
/// </remarks>
internal static partial class PdfFilters
{
    /// <summary>The control byte that marks the end of the encoded data.</summary>
    private const int RunLengthEndOfData = 128;

    /// <summary>Decodes a run-length encoded stream.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <returns>The decoded bytes.</returns>
    /// <remarks>
    /// A control byte below 128 copies the next <c>control + 1</c> bytes literally; a control byte above
    /// 128 repeats the next byte <c>257 - control</c> times. A stream that ends without the end-of-data
    /// byte is incomplete rather than wrong, so the runs it did state are decoded: a literal run that
    /// reaches past the end contributes the bytes that are there, and a repeated run with no value byte
    /// contributes nothing.
    /// </remarks>
    public static byte[] DecodeRunLength(ReadOnlyMemory<byte> data)
    {
        // The output is at least as large as the input for the literal case, and can be 128 times larger
        // for the repeat case, so the buffer starts at the input length and grows only when it must.
        using var result = new PooledByteBuffer(data.Length);
        var span = data.Span;
        var index = 0;

        while (index < span.Length)
        {
            var control = span[index++];

            if (control == RunLengthEndOfData)
            {
                break;
            }

            if (control < RunLengthEndOfData)
            {
                // A literal run that reaches past the end writes the bytes that are there: the count is
                // capped at what remains, and a count of zero writes nothing and leaves the index at the
                // end, which ends the loop. The explicit guard a survivor asked about is this comment.
                var count = Math.Min(control + 1, span.Length - index);

                result.Write(span.Slice(index, count));
                index += count;
                continue;
            }

            var repeat = 257 - control;

            if (index >= span.Length)
            {
                break;
            }

            var value = span[index++];

            for (var count = 0; count < repeat; count++)
            {
                result.WriteByte(value);
            }
        }

        return result.ToExactArray();
    }
}
