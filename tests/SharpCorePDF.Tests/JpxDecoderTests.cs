using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the JPEG 2000 header reader against a JP2 and a raw codestream OpenJPEG wrote: the geometry,
/// the components and the transform the reader states must be the ones the codestream carries.
/// </summary>
public sealed class JpxDecoderTests
{
    private static readonly byte[] Reversible = File.ReadAllBytes(Fixture("jpx-5-3.jp2"));
    private static readonly byte[] Irreversible = File.ReadAllBytes(Fixture("jpx-9-7.jp2"));
    private static readonly byte[] Raw = File.ReadAllBytes(Fixture("jpx-5-3.j2k"));

    private static string Fixture(string name) => Path.Combine(TestAssets.Folder, "jpx", name);

    /// <summary>A JP2 built with the reversible 5/3 filter states its size, its components and its transform.</summary>
    [Fact]
    public void Reads_the_header_of_a_reversible_jp2()
    {
        Assert.True(JpxDecoder.TryReadHeader(Reversible, out var header));

        Assert.NotNull(header);
        Assert.Equal(32, header!.Width);
        Assert.Equal(32, header.Height);
        Assert.Equal(32, header.TileWidth);
        Assert.Equal(32, header.TileHeight);
        Assert.Equal(3, header.Components.Length);
        Assert.All(header.Components, component =>
        {
            Assert.Equal(8, component.BitDepth);
            Assert.False(component.Signed);
            Assert.Equal(1, component.HorizontalSubsampling);
            Assert.Equal(1, component.VerticalSubsampling);
        });
        Assert.Equal(0, header.ProgressionOrder);
        Assert.Equal(1, header.Layers);
        Assert.Equal(0, header.ColourTransform);
        Assert.Equal(5, header.DecompositionLevels);
        Assert.True(header.IsReversible);
    }

    /// <summary>The same container with the irreversible 9/7 filter states the other transform.</summary>
    [Fact]
    public void Reads_the_header_of_an_irreversible_jp2()
    {
        Assert.True(JpxDecoder.TryReadHeader(Irreversible, out var header));

        Assert.NotNull(header);
        Assert.Equal(32, header!.Width);
        Assert.Equal(3, header.Components.Length);
        Assert.False(header.IsReversible);
    }

    /// <summary>A raw J2K codestream has no boxes; its SOC marker is the start.</summary>
    [Fact]
    public void Reads_the_header_of_a_raw_codestream()
    {
        Assert.True(JpxDecoder.TryReadHeader(Raw, out var header));

        Assert.NotNull(header);
        Assert.Equal(32, header!.Width);
        Assert.Equal(32, header.Height);
        Assert.Equal(3, header.Components.Length);
        Assert.True(header.IsReversible);
    }

    /// <summary>A stream that is neither a JP2 nor a raw codestream is refused.</summary>
    [Fact]
    public void Refuses_a_stream_that_is_neither_jp2_nor_j2k() =>
        Assert.False(JpxDecoder.TryReadHeader(new byte[] { 0, 0, 0, 0 }, out _));

    /// <summary>A JP2 whose signature is right and whose codestream is missing is refused.</summary>
    [Fact]
    public void Refuses_a_jp2_without_a_codestream()
    {
        var data = new byte[20];
        data[3] = 12;
        data[4] = (byte)'j';
        data[5] = (byte)'P';
        data[6] = 0x20;
        data[7] = 0x20;
        data[8] = 0x0D;
        data[9] = 0x0A;
        data[10] = 0x87;
        data[11] = 0x0A;
        data[15] = 8;
        data[16] = (byte)'f';
        data[17] = (byte)'t';
        data[18] = (byte)'y';
        data[19] = (byte)'p';

        Assert.False(JpxDecoder.TryReadHeader(data, out _));
    }

    /// <summary>A codestream that stops before its coding style is refused rather than half read.</summary>
    [Fact]
    public void Refuses_a_codestream_without_its_coding_style()
    {
        var truncated = Raw.AsSpan(0, 40).ToArray();

        Assert.False(JpxDecoder.TryReadHeader(truncated, out _));
    }
}
