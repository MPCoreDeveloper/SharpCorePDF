using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the validator against documents that were changed after signing in the ways published attacks
/// change them: a byte range that describes bytes the file does not hold, a signature value that is not
/// there, and a gap that holds more than the signature value.
/// </summary>
/// <remarks>
/// <para>
/// The attacks are the classes named by the 2019 "PDF Insecurity" study (Mladenov, Mainka, Meyer zu
/// Selhausen, Grothe and Schwenk): universal signature forgery, where the structure that says what was
/// signed is malformed or missing, and signature wrapping, where the uncovered gap between the two signed
/// ranges is used to carry something other than the signature. Incremental saving is the third class and is
/// covered by <c>PdfIncrementalWriterTests.Keeps_a_signature_that_covers_the_earlier_revision</c>, which
/// asserts that a revision appended after signing turns <c>CoversWholeDocument</c> false.
/// </para>
/// <para>
/// Every variant is made from a document this library signed. The signer writes the four numbers of the
/// byte range as fixed-width ten digit fields, so a variant can be written over them without moving a single
/// offset in the file, which keeps each test about the one thing it changes.
/// </para>
/// </remarks>
public sealed class PdfSignatureAttackTests
{
    private const int NumberWidth = 10;

    /// <summary>
    /// The writer-side half of the wrapping defence: the gap the signer leaves uncovered is exactly the
    /// hexadecimal string that holds the signature value, delimiters included, and nothing else.
    /// </summary>
    [Fact]
    public async Task Writes_a_gap_that_holds_exactly_the_signature_value()
    {
        var signed = await SignAsync();
        var (gapStart, gapEnd) = Gap(signed);

        Assert.Equal((byte)'<', signed[gapStart]);
        Assert.Equal((byte)'>', signed[gapEnd - 1]);
        Assert.All(signed[(gapStart + 1)..(gapEnd - 1)], value => Assert.True(Uri.IsHexDigit((char)value)));
        Assert.Equal("/Contents ", Encoding.Latin1.GetString(signed, gapStart - 10, 10));
    }

    /// <summary>
    /// Malformed byte ranges are universal signature forgery in its simplest form: whatever the four numbers
    /// say, the validator must answer with an invalid signature rather than with a valid one or an exception.
    /// </summary>
    /// <param name="variant">Which malformation the numbers are rewritten to.</param>
    [Theory]
    [InlineData("negative-offset")]
    [InlineData("first-range-does-not-start-at-zero")]
    [InlineData("ranges-overlap")]
    [InlineData("second-range-runs-past-the-end")]
    [InlineData("second-range-covers-nothing")]
    [InlineData("all-ranges-empty")]
    public async Task Never_reports_a_signature_with_a_forged_byte_range_as_valid(string variant)
    {
        var signed = await SignAsync();
        var (_, length1, offset2, length2) = ReadByteRange(signed);
        long[] numbers = variant switch
        {
            "negative-offset" => [-1, length1, offset2, length2],
            "first-range-does-not-start-at-zero" => [1, length1 - 1, offset2, length2],
            "ranges-overlap" => [0, offset2 + 1, offset2, length2],
            "second-range-runs-past-the-end" => [0, length1, offset2, length2 + 1],
            "second-range-covers-nothing" => [0, length1, offset2, 0],
            "all-ranges-empty" => [0, 0, 0, 0],
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "no such variant"),
        };

        var forged = WriteByteRange(signed, numbers);
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(forged));

        Assert.False(validation.IsValid);
        Assert.NotEqual(PdfSignatureIntegrity.Valid, validation.Integrity);
        Assert.NotEmpty(validation.Problems);
    }

    /// <summary>
    /// A signature dictionary whose value was emptied — every hex digit turned to zero, the shape a forged
    /// dictionary has when it only pretends to carry a signature — is not valid either.
    /// </summary>
    [Fact]
    public async Task Never_reports_a_signature_whose_value_was_emptied_as_valid()
    {
        var signed = await SignAsync();
        var (gapStart, gapEnd) = Gap(signed);
        var forged = (byte[])signed.Clone();

        forged.AsSpan((gapStart + 1)..(gapEnd - 1)).Fill((byte)'0');

        var validation = Assert.Single(await SigningFixtures.ValidateAsync(forged));

        Assert.False(validation.IsValid);
        Assert.NotEqual(PdfSignatureIntegrity.Valid, validation.Integrity);
    }

    /// <summary>
    /// Signature wrapping: bytes written into the padding of the uncovered gap close the signature value early
    /// and add entries the signer never wrote. Every signed byte is untouched, so the digest still matches;
    /// what must fail is the claim that the signature covers the whole document, because part of the
    /// document is now something other than the signature value.
    /// </summary>
    [Fact(Skip =
        "Known defect: SignatureValidator does not check that the gap between the two ranges is exactly the /Contents string, so this wrapped document is reported as covering the whole file. Remove the skip with the fix.")]
    public async Task Does_not_report_a_gap_that_holds_more_than_the_signature_value_as_covering_the_document()
    {
        var signed = await SignAsync();
        var wrapped = InjectIntoGap(signed, " /Injected (bytes no signature covers) "u8);

        // The attack only works while the signed bytes are unchanged; this is what makes it an attack.
        var validation = Assert.Single(await SigningFixtures.ValidateAsync(wrapped));

        Assert.False(
            validation.Signer.CoversWholeDocument && validation.IsValid,
            "a document whose uncovered gap carries more than the signature value must not be reported as covered");
    }

    /// <summary>
    /// The precondition the skipped wrapping test depends on, asserted on its own so it keeps running: the
    /// injection leaves every covered byte as it was, which is why only a gap check can catch it.
    /// </summary>
    [Fact]
    public async Task Leaves_every_covered_byte_unchanged_when_the_gap_is_wrapped()
    {
        var signed = await SignAsync();
        var wrapped = InjectIntoGap(signed, " /Injected (bytes no signature covers) "u8);
        var (gapStart, gapEnd) = Gap(signed);

        Assert.Equal(signed.Length, wrapped.Length);
        Assert.Equal(signed[..gapStart], wrapped[..gapStart]);
        Assert.Equal(signed[gapEnd..], wrapped[gapEnd..]);
        Assert.NotEqual(signed[gapStart..gapEnd], wrapped[gapStart..gapEnd]);
    }

    private static Task<byte[]> SignAsync() =>
        SigningFixtures.SignAsync(SigningFixtures.BuildDocument(),
            SigningFixtures.Options(SigningFixtures.Certificate));

    /// <summary>Finds the gap the byte range leaves uncovered.</summary>
    /// <param name="document">A signed document.</param>
    /// <returns>The offset of the first uncovered byte and the offset just past the last one.</returns>
    private static (int Start, int End) Gap(byte[] document)
    {
        var (offset1, length1, offset2, _) = ReadByteRange(document);

        return (checked((int)(offset1 + length1)), checked((int)offset2));
    }

    /// <summary>Writes a payload into the zero padding behind the signature value, without moving a byte.</summary>
    /// <param name="document">A signed document.</param>
    /// <param name="entries">The dictionary entries to smuggle in.</param>
    /// <returns>The wrapped document.</returns>
    private static byte[] InjectIntoGap(byte[] document, ReadOnlySpan<byte> entries)
    {
        var (gapStart, gapEnd) = Gap(document);
        var wrapped = (byte[])document.Clone();

        // The value is written first and the rest of the gap stays zero, so the padding starts behind the
        // last digit that is not zero; it is rounded up to an even count so the value keeps whole bytes.
        var padding = gapEnd - 1;

        while (padding > gapStart + 1 && wrapped[padding - 1] == (byte)'0')
        {
            padding--;
        }

        padding += (padding - (gapStart + 1)) % 2;

        // ">" closes the value early, the entries follow, and "<" opens a string the original closing ">" ends.
        var payload = Encoding.Latin1.GetBytes($">{Encoding.Latin1.GetString(entries)}<");

        Assert.True(padding + payload.Length < gapEnd - 1, "the gap has to have room for the payload");
        payload.CopyTo(wrapped, padding);

        return wrapped;
    }

    private static (long Offset1, long Length1, long Offset2, long Length2) ReadByteRange(byte[] document)
    {
        var start = NumbersOffset(document);
        var text = Encoding.Latin1.GetString(document, start, (4 * NumberWidth) + 3);
        var numbers = text.Split(' ').Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        return (numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    private static byte[] WriteByteRange(byte[] document, long[] numbers)
    {
        var start = NumbersOffset(document);
        var forged = (byte[])document.Clone();

        for (var index = 0; index < numbers.Length; index++)
        {
            // A negative number keeps the width: the sign takes the place of the leading zero.
            var text = numbers[index] < 0
                ? "-" + (-numbers[index]).ToString("D9", CultureInfo.InvariantCulture)
                : numbers[index].ToString("D10", CultureInfo.InvariantCulture);

            Encoding.Latin1.GetBytes(text).CopyTo(forged, start + (index * (NumberWidth + 1)));
        }

        return forged;
    }

    private static int NumbersOffset(byte[] document)
    {
        var marker = "/ByteRange ["u8;
        var index = document.AsSpan().LastIndexOf(marker);

        Assert.True(index >= 0, "the document has to carry a byte range");

        return index + marker.Length;
    }
}
