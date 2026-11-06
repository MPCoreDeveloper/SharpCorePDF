namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The scalar loop <see cref="SimdByteScan"/> defers to when the span is shorter than a vector, which
/// is the case for most of the stream: a content stream is read a token at a time.
/// </summary>
internal static partial class SimdByteScan
{
    private static int IndexOfScalar(ReadOnlySpan<byte> buffer, byte value)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
