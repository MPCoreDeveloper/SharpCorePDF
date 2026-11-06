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
/// The SSE2 kernel of <see cref="SimdByteScan"/>: 16 bytes compared per iteration, which is the width
/// every x64 processor has, including the ones an AVX2 build may still run on.
/// </summary>
internal static partial class SimdByteScan
{
    /// <summary>The mask a 16-byte comparison produces: one bit per byte.</summary>
    private const uint Vector128Mask = 0xFFFFu;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int IndexOfSse2(ReadOnlySpan<byte> buffer, byte value)
    {
        fixed (byte* pointer = buffer)
        {
            var pattern = Vector128.Create(value);
            var index = 0;
            var vectorized = buffer.Length & ~(Vector128Width - 1);

            for (; index < vectorized; index += Vector128Width)
            {
                var data = Sse2.LoadVector128(pointer + index);
                var mask = (uint)Sse2.MoveMask(Sse2.CompareEqual(data, pattern)) & Vector128Mask;

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
