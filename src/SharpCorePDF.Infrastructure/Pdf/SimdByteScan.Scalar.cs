namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The scalar loop <see cref="SimdByteScan"/> defers to for the tail a vector leaves, and for any span
/// shorter than a vector.
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
