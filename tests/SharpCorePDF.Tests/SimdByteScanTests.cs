using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the vectorised byte search: the same question the inline-image scan asks, answered against
/// a scalar reference at every length the kernels can meet.
/// </summary>
/// <remarks>
/// The kernels exist to be faster, not to be different: every assertion here compares the vector answer
/// with a loop written in the test, so a lane that is read one byte off, or a tail that is forgotten,
/// fails here rather than on a real document. The lengths either side of a vector boundary — 15/16/17
/// and 31/32/33 — are the ones a tail bug hides in.
/// </remarks>
public sealed class SimdByteScanTests
{
    /// <summary>A byte is found wherever it is written, including the last lane of a vector.</summary>
    /// <param name="value">The byte to search for.</param>
    [Theory]
    [InlineData((byte)'E')]
    [InlineData((byte)'I')]
    [InlineData((byte)0x00)]
    [InlineData((byte)0xFF)]
    public void Finds_the_byte_wherever_it_is_written(byte value)
    {
        for (var position = 0; position < 80; position++)
        {
            var data = Enumerable.Repeat((byte)'A', 80).ToArray();
            data[position] = value;

            Assert.Equal(position, SimdByteScan.IndexOf(data, value));
        }
    }

    /// <summary>A byte that is absent is reported as absent, however long the span.</summary>
    [Fact]
    public void Reports_minus_one_when_the_byte_is_absent()
    {
        foreach (var length in new[] { 0, 1, 15, 16, 31, 32, 33, 63, 64, 65, 200 })
        {
            var data = Enumerable.Repeat((byte)'A', length).ToArray();
            var first = length == 0 ? -1 : 0;

            Assert.Equal(-1, SimdByteScan.IndexOf(data, (byte)'Z'));
            Assert.Equal(first, SimdByteScan.IndexOf(data, (byte)'A'));
        }
    }

    /// <summary>The kernel agrees with a scalar loop at every length from zero to three hundred.</summary>
    [Fact]
    public void Agrees_with_a_scalar_reference_at_every_length()
    {
        var random = new Random(20261106);

        for (var length = 0; length <= 300; length++)
        {
            foreach (var data in Cases(length, random))
            {
                Assert.Equal(ReferenceIndexOf(data, (byte)'Z'), SimdByteScan.IndexOf(data, (byte)'Z'));
                Assert.Equal(ReferenceIndexOf(data, (byte)'A'), SimdByteScan.IndexOf(data, (byte)'A'));
                Assert.Equal(ReferenceIndexOf(data, (byte)'E'), SimdByteScan.IndexOf(data, (byte)'E'));
            }
        }
    }

    private static IEnumerable<byte[]> Cases(int length, Random random)
    {
        yield return new byte[length];

        if (length == 0)
        {
            yield break;
        }

        yield return Enumerable.Repeat((byte)'A', length).ToArray();

        var randomBytes = new byte[length];
        random.NextBytes(randomBytes);
        yield return randomBytes;

        var first = Enumerable.Repeat((byte)'A', length).ToArray();
        first[0] = (byte)'E';
        yield return first;

        var last = Enumerable.Repeat((byte)'A', length).ToArray();
        last[^1] = (byte)'E';
        yield return last;

        var middle = Enumerable.Repeat((byte)'A', length).ToArray();
        middle[length / 2] = (byte)'E';
        yield return middle;
    }

    private static int ReferenceIndexOf(ReadOnlySpan<byte> data, byte value)
    {
        for (var index = 0; index < data.Length; index++)
        {
            if (data[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
