using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// The kernel takes a pointer into the caller's buffer so a vector can be loaded without a copy. The
// analyzer's blanket advice against unsafe blocks is answered by the scalar fallback it keeps and by
// the tests that compare both paths against a scalar reference.
#pragma warning disable S6640

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The AVX2 kernel of <see cref="SimdByteScan"/>: 32 bytes compared per iteration, with the first
/// matching lane recovered from the comparison mask rather than from a branch per byte.
/// </summary>
internal static partial class SimdByteScan
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int IndexOfAvx2(ReadOnlySpan<byte> buffer, byte value)
    {
        fixed (byte* pointer = buffer)
        {
            var pattern = Vector256.Create(value);
            var index = 0;
            var vectorized = buffer.Length & ~(Avx2Width - 1);

            for (; index < vectorized; index += Avx2Width)
            {
                var data = Avx.LoadVector256(pointer + index);
                var mask = (uint)Avx2.MoveMask(Avx2.CompareEqual(data, pattern));

                if (mask != 0)
                {
                    return index + BitOperations.TrailingZeroCount(mask);
                }
            }

            for (; index < buffer.Length; index++)
            {
                if (pointer[index] == value)
                {
                    return index;
                }
            }
        }

        return -1;
    }
}
