using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Editing;

/// <summary>
/// Appends a revision of an edited document to the bytes of the source instead of rewriting them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in an incremental update changes an earlier revision: the source bytes are copied into the
/// result exactly as they were read, the new objects follow them, and the cross-reference section of the
/// revision points back at the previous one through <c>/Prev</c>. That is what keeps a signature over an
/// earlier revision valid in the edited document — the bytes it covers are still the bytes it covered.
/// </para>
/// <para>
/// New objects are numbered above every object the source used, because reusing a number would make the
/// earlier revision ambiguous to a reader that resolves an object through the newest section first.
/// Offsets are absolute in the output, so the writer is told where the revision starts.
/// </para>
/// </remarks>
internal sealed class IncrementalWriter
{
    private IncrementalWriter(PdfWriter writer, long priorLength, long previousXref)
    {
        Writer = writer;
        PriorLength = priorLength;
        PreviousXref = previousXref;
    }

    /// <summary>Gets the writer of the appended revision.</summary>
    public PdfWriter Writer { get; }

    /// <summary>Gets the number of bytes of the revisions this one follows, as they were copied through.</summary>
    public long PriorLength { get; }

    /// <summary>Gets the offset of the cross-reference section this revision chains to.</summary>
    public long PreviousXref { get; }

    /// <summary>Copies the earlier revisions into the destination and opens a writer after them.</summary>
    /// <param name="destination">The stream the result is written to.</param>
    /// <param name="priorBytes">The bytes of the source, exactly as they were read.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="previousXref">The offset the source's own <c>startxref</c> holds.</param>
    /// <param name="highestObjectNumber">The highest object number the source used.</param>
    /// <returns>The writer of the new revision.</returns>
    /// <remarks>
    /// A destination that cannot seek is assumed to have been empty when the edit started, because the
    /// offsets of the revision have to count every byte that precedes it and no other measurement is
    /// available then. A destination used for an incremental update is therefore normally a file or a
    /// memory stream.
    /// </remarks>
    public static IncrementalWriter Begin(
        Stream destination,
        byte[] priorBytes,
        PdfEngineOptions options,
        long previousXref,
        int highestObjectNumber)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(priorBytes);
        ArgumentNullException.ThrowIfNull(options);

        destination.Write(priorBytes, 0, priorBytes.Length);

        // A revision begins on a line of its own. A producer normally ends a file with %%EOF and a
        // newline, but nothing promises it, and an object header that does not start a line is not an
        // object header to a reader.
        var separator = priorBytes.Length > 0 && priorBytes[^1] is not ((byte)'\n' or (byte)'\r');

        if (separator)
        {
            destination.WriteByte((byte)'\n');
        }

        var appended = priorBytes.Length + (separator ? 1L : 0L);
        var origin = destination.CanSeek ? destination.Position : appended;
        var writer = PdfWriter.Append(destination, options, highestObjectNumber, origin);

        return new IncrementalWriter(writer, priorBytes.Length, previousXref);
    }
}
