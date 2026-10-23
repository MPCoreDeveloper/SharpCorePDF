using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// The outcome of a PDF/A conversion: whether a document was written, and what the writer could not
/// satisfy when it was not.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsWritten"/> is the honest answer to "may I ship this file as an archival master". It is
/// <see langword="false"/> when the writer knows the profile is not satisfied — an unembedded font, a
/// transparency the profile forbids, an attachment — and in that case the caller's destination was
/// left untouched and <see cref="UnsatisfiedRequirements"/> names every reason.
/// </para>
/// <para>
/// <see cref="IsWritten"/> being <see langword="true"/> means the structures the profile requires were
/// written, not that a validator has accepted the file. veraPDF remains the external authority, and
/// the library makes no conformance claim of its own.
/// </para>
/// </remarks>
public sealed record PdfAConversionResult
{
    /// <summary>Gets the profile the conversion targeted.</summary>
    public required PdfAProfile Profile { get; init; }

    /// <summary>Gets a value indicating whether a document was written to the destination.</summary>
    public required bool IsWritten { get; init; }

    /// <summary>
    /// Gets the remarks of the conversion: what was read, what was removed, and that the written
    /// document was read back.
    /// </summary>
    public IReadOnlyList<PdfWarning> Warnings { get; init; } = [];

    /// <summary>
    /// Gets the requirements the writer could not satisfy, in the order they were found. Empty when a
    /// document was written.
    /// </summary>
    public IReadOnlyList<string> UnsatisfiedRequirements { get; init; } = [];

    /// <summary>Gets the number of pages of the written document, or the number of pages that would have been written.</summary>
    public int PageCount { get; init; }

    /// <summary>Gets the size in bytes of the written document, or <c>0</c> when nothing was written.</summary>
    public long BytesWritten { get; init; }

    /// <summary>Formats the result for diagnostics.</summary>
    /// <returns>The formatted result.</returns>
    public override string ToString() => IsWritten
        ? $"{Profile}: {PageCount} page(s), {BytesWritten} bytes written"
        : $"{Profile}: nothing written, {UnsatisfiedRequirements.Count} unsatisfied requirement(s)";
}
