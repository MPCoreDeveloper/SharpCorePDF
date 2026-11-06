using SharpCorePDF.Infrastructure.Signatures;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the DER encoder the signatures are built from: the length forms, the tag wrappers, the set
/// ordering and the integer form, which are the pieces a signature's structure is made of.
/// </summary>
/// <remarks>
/// Every expectation is written as the bytes X.690 produces, so the encoder is checked against the rule
/// rather than round-tripped through the code that wrote it. The length cases sit on the boundaries that
/// change the form — 0x7F/0x80, 0xFF/0x100, 0xFFFF/0x10000 — because those are the branches a mutation
/// can move without a signature test noticing.
/// </remarks>
public sealed class DerEncodingTests
{
    /// <summary>A length is written in the shortest form its size allows.</summary>
    /// <param name="length">The number of content bytes.</param>
    /// <param name="header">The length bytes the encoder must write.</param>
    [Theory]
    [InlineData(0x00, new byte[] { 0x00 })]
    [InlineData(0x7F, new byte[] { 0x7F })]
    [InlineData(0x80, new byte[] { 0x81, 0x80 })]
    [InlineData(0xFF, new byte[] { 0x81, 0xFF })]
    [InlineData(0x100, new byte[] { 0x82, 0x01, 0x00 })]
    [InlineData(0xFFFF, new byte[] { 0x82, 0xFF, 0xFF })]
    [InlineData(0x10000, new byte[] { 0x83, 0x01, 0x00, 0x00 })]
    public void Writes_the_length_form_the_size_needs(int length, byte[] header)
    {
        var encoded = DerEncoding.Tagged(DerEncoding.SequenceTag, new byte[length]);

        Assert.Equal(DerEncoding.SequenceTag, encoded[0]);
        Assert.Equal(header, encoded.AsSpan(1, header.Length).ToArray());
        Assert.Equal(length, encoded.Length - 1 - header.Length);
    }

    /// <summary>Values inside a tag are concatenated in the order they are given.</summary>
    [Fact]
    public void Writes_the_items_of_a_sequence_in_order()
    {
        var sequence = DerEncoding.Sequence(DerEncoding.Integer(1), DerEncoding.NullValue());

        Assert.Equal(new byte[] { 0x30, 0x05, 0x02, 0x01, 0x01, 0x05, 0x00 }, sequence);
    }

    /// <summary>An empty sequence is two bytes: the tag and a zero length.</summary>
    [Fact]
    public void Writes_an_empty_sequence() =>
        Assert.Equal(new byte[] { 0x30, 0x00 }, DerEncoding.Sequence());

    /// <summary>DER orders a set by its members' encodings, not by the order they were passed in.</summary>
    [Fact]
    public void Orders_a_set_by_its_encodings()
    {
        var two = DerEncoding.Integer(2);
        var one = DerEncoding.Integer(1);

        var set = DerEncoding.SetOf(two, one);

        Assert.Equal(new byte[] { 0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02 }, set);
    }

    /// <summary>An integer is written in two's complement, with the padding a positive value needs.</summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="expected">The encoded integer.</param>
    [Theory]
    [InlineData(0L, new byte[] { 0x02, 0x01, 0x00 })]
    [InlineData(127L, new byte[] { 0x02, 0x01, 0x7F })]
    [InlineData(128L, new byte[] { 0x02, 0x02, 0x00, 0x80 })]
    [InlineData(-1L, new byte[] { 0x02, 0x01, 0xFF })]
    [InlineData(-129L, new byte[] { 0x02, 0x02, 0xFF, 0x7F })]
    public void Writes_an_integer_in_two_s_complement(long value, byte[] expected) =>
        Assert.Equal(expected, DerEncoding.Integer(value));

    /// <summary>An object identifier is written as its first two arcs and a base-128 tail.</summary>
    [Fact]
    public void Writes_an_object_identifier()
    {
        Assert.Equal(
            new byte[] { 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x02 },
            DerEncoding.ObjectIdentifier("1.2.840.113549.1.7.2"));
    }

    /// <summary>A bit string states that its contents end on a byte boundary.</summary>
    [Fact]
    public void Writes_a_bit_string_with_no_unused_bits()
    {
        Assert.Equal(
            new byte[] { 0x03, 0x03, 0x00, 0xAB, 0xCD },
            DerEncoding.BitString(new byte[] { 0xAB, 0xCD }));
    }

    /// <summary>An algorithm identifier is a sequence of its identifier and its parameters.</summary>
    [Fact]
    public void Writes_the_three_algorithm_shapes()
    {
        // 1.2.840.113549.1.1.11 is sha256WithRSAEncryption.
        Assert.Equal(
            new byte[] { 0x30, 0x0B, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x0B },
            DerEncoding.Algorithm("1.2.840.113549.1.1.11"));

        Assert.Equal(
            new byte[] { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x0B, 0x05, 0x00 },
            DerEncoding.AlgorithmWithNullParameters("1.2.840.113549.1.1.11"));

        Assert.Equal(
            new byte[]
            {
                0x30, 0x0E, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x0B, 0x02, 0x01, 0x05,
            },
            DerEncoding.Algorithm("1.2.840.113549.1.1.11", DerEncoding.Integer(5)));
    }

    /// <summary>A time is UTC, and its form is chosen by the century DER defines.</summary>
    /// <param name="year">The year of the moment.</param>
    /// <param name="expected">The encoded time.</param>
    [Theory]
    [InlineData(2049, new byte[] { 0x17, 0x0D, 0x34, 0x39, 0x30, 0x31, 0x30, 0x31, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x5A })]
    [InlineData(2050, new byte[] { 0x18, 0x0F, 0x32, 0x30, 0x35, 0x30, 0x30, 0x31, 0x30, 0x31, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x5A })]
    public void Writes_the_time_form_the_year_needs(int year, byte[] expected) =>
        Assert.Equal(expected, DerEncoding.Time(new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)));
}
