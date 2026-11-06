using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Answers the two questions a content scan asks before every token — where a run of whitespace ends,
/// and where a name or a keyword ends — a vector at a time where the hardware allows it.
/// </summary>
/// <remarks>
/// <para>
/// The shape is the one this team uses elsewhere: a platform check at the entry point, one kernel per
/// instruction set, and a scalar loop for the tail the vector does not cover. AVX2 consumes 32 bytes an
/// iteration, SSE2 and AdvSimd 16, and anything shorter than a vector is answered byte at a time, which
/// is what almost every call to this class actually gets: content streams are full of short runs.
/// </para>
/// <para>
/// The two sets are the specification's own. Whitespace is <c>0x00 0x09 0x0A 0x0C 0x0D 0x20</c>; the
/// delimiters are <c>( ) &lt; &gt; [ ] { } / %</c>. Nothing here decides what a byte means to a reader —
/// only where a run of them stops — so every kernel is a membership test, not a parse.
/// </para>
/// <para>
/// A scalar answer is always available and is what the tests compare against: the vector kernels are an
/// optimisation of the same membership rule, never a second definition of it.
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
