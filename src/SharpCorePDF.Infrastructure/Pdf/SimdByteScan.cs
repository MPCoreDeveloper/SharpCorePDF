using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Finds one byte a vector at a time where the hardware allows it, for the one scan whose run is bulk:
/// the terminator of an inline image.
/// </summary>
/// <remarks>
/// <para>
/// The shape is the one this team uses elsewhere: a platform check at the entry point, one kernel per
/// instruction set, and a scalar loop for the tail the vector does not cover. AVX2 consumes 32 bytes an
/// iteration, SSE2 and AdvSimd 16, and anything shorter than a vector is answered byte at a time.
/// </para>
/// <para>
/// The use is deliberately narrow. A content stream is parsed a token at a time, and a vector applied to
/// a one-to-three-byte run costs more than the branches it replaces: that use measured 97.6 ms against
/// 75.2 ms and was reverted, which <c>docs/mutation-testing.md</c> records. An inline image puts hundreds
/// of kilobytes between <c>ID</c> and <c>EI</c>, and that is the run this class exists for.
/// </para>
/// <para>
/// Every kernel answers the same question as the scalar loop and returns the same index; the tests
/// compare the two at every length a vector boundary can disturb.
/// </para>
/// </remarks>
internal static partial class SimdByteScan
{
    /// <summary>The number of bytes an AVX2 kernel consumes per iteration.</summary>
    private const int Avx2Width = 32;

    /// <summary>The number of bytes an SSE2 or AdvSimd kernel consumes per iteration.</summary>
    private const int Vector128Width = 16;

    /// <summary>Gets a value indicating whether AVX2 is available.</summary>
    public static bool IsAvx2Supported => Avx2.IsSupported;

    /// <summary>Gets a value indicating whether SSE2 is available.</summary>
    public static bool IsSse2Supported => Sse2.IsSupported;

    /// <summary>Gets a value indicating whether AdvSimd (ARM NEON) is available.</summary>
    public static bool IsAdvSimdSupported => AdvSimd.IsSupported;

    /// <summary>Finds the first occurrence of one byte.</summary>
    /// <param name="buffer">The bytes to search.</param>
    /// <param name="value">The byte to find.</param>
    /// <returns>The zero-based index of the first occurrence, or -1 when the byte is absent.</returns>
    /// <remarks>
    /// The kernel is used where a run is bulk — the search for the <c>E</c> of <c>EI</c> through inline
    /// image data — and not for the per-token scans of a content stream. That use was measured and
    /// rejected: those runs are one to three bytes, and the vector costs more than the branches it
    /// replaces. `docs/mutation-testing.md` records the measurement.
    /// </remarks>
    public static int IndexOf(ReadOnlySpan<byte> buffer, byte value)
    {
        if (buffer.IsEmpty)
        {
            return -1;
        }

        if (Avx2.IsSupported && buffer.Length >= Avx2Width)
        {
            return IndexOfAvx2(buffer, value);
        }

        if (Sse2.IsSupported && buffer.Length >= Vector128Width)
        {
            return IndexOfSse2(buffer, value);
        }

        if (AdvSimd.IsSupported && buffer.Length >= Vector128Width)
        {
            return IndexOfAdvSimd(buffer, value);
        }

        return IndexOfScalar(buffer, value);
    }
}
