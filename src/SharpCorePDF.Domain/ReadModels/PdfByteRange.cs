using System.Globalization;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// The two byte ranges a PDF signature covers, taken from its <c>/ByteRange</c> entry.
/// </summary>
/// <remarks>
/// <para>
/// The ranges are two views of one file: the first runs from the start of the document to the opening
/// delimiter of the signature value, and the second from behind its closing delimiter to the end. The
/// digest is computed over both, in order, which is why a signature value can be written into the file
/// it signs.
/// </para>
/// <para>
/// A <c>/ByteRange</c> may hold more than two pairs; this release reads exactly two and reports anything
/// else as a malformed range rather than guessing what the extra pairs meant.
/// </para>
/// </remarks>
public readonly record struct PdfByteRange
{
    /// <summary>Initializes a new instance of the <see cref="PdfByteRange"/> struct.</summary>
    /// <param name="offset1">The offset of the first covered range.</param>
    /// <param name="length1">The length of the first covered range.</param>
    /// <param name="offset2">The offset of the second covered range.</param>
    /// <param name="length2">The length of the second covered range.</param>
    public PdfByteRange(long offset1, long length1, long offset2, long length2)
    {
        Offset1 = offset1;
        Length1 = length1;
        Offset2 = offset2;
        Length2 = length2;
    }

    /// <summary>Gets the offset of the first covered range.</summary>
    public long Offset1 { get; }

    /// <summary>Gets the length of the first covered range.</summary>
    public long Length1 { get; }

    /// <summary>Gets the offset of the second covered range.</summary>
    public long Offset2 { get; }

    /// <summary>Gets the length of the second covered range.</summary>
    public long Length2 { get; }

    /// <summary>
    /// Gets the number of bytes that are not covered: the signature value and its two delimiters, which is
    /// the gap between the two ranges.
    /// </summary>
    public long SignatureLength => Offset2 - (Offset1 + Length1);

    /// <summary>Determines whether the ranges cover every byte of a document of the given length.</summary>
    /// <param name="documentLength">The length of the document the ranges were read from.</param>
    /// <returns><see langword="true"/> when nothing beyond the signature value is left out.</returns>
    public bool Covers(long documentLength) =>
        Offset1 == 0 && Offset2 + Length2 == documentLength && Length1 >= 0 && Length2 >= 0;

    /// <summary>Determines whether the ranges are non-negative, ordered and do not overlap.</summary>
    /// <returns><see langword="true"/> when the pair describes two disjoint ranges in order.</returns>
    public bool IsWellFormed =>
        Offset1 >= 0 && Length1 >= 0 && Offset2 >= 0 && Length2 >= 0 && Offset1 + Length1 <= Offset2;

    /// <summary>Formats the ranges the way the PDF entry spells them.</summary>
    /// <returns>The four numbers, separated by spaces.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"[{Offset1} {Length1} {Offset2} {Length2}]");
}
