using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

// The kernel takes a pointer into the caller's buffer so a vector can be loaded without a copy. The
// analyzer's blanket advice against unsafe blocks is answered by the scalar fallback it keeps and by
// the tests that compare both paths against a scalar reference.
#pragma warning disable S6640

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The AdvSimd (ARM NEON) kernel of <see cref="SimdByteScan"/>. ARM has no portable vector-to-mask
/// instruction in the base instruction set, so the comparison result is read lane by lane — still one
/// comparison per 16 bytes rather than one per byte.
/// </summary>
internal static partial class SimdByteScan
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int IndexOfAdvSimd(ReadOnlySpan<byte> buffer, byte value)
    {
        fixed (byte* pointer = buffer)
        {
            var pattern = Vector128.Create(value);
            var index = 0;
            var vectorized = buffer.Length & ~(Vector128Width - 1);

            for (; index < vectorized; index += Vector128Width)
            {
                var data = AdvSimd.LoadVector128(pointer + index);
                var match = AdvSimd.CompareEqual(data, pattern);

                for (var lane = 0; lane < Vector128Width; lane++)
                {
                    if (match.GetElement(lane) != 0)
                    {
                        return index + lane;
                    }
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
