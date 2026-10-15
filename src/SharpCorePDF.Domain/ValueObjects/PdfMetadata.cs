using System.Collections.Immutable;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The document information of a PDF document: the values carried by the document information
/// dictionary (and, where relevant, mirrored by XMP metadata) of a source file.
/// </summary>
/// <remarks>
/// Every field is optional; a PDF document is free to carry no document information at all, which is
/// what <see cref="Empty"/> represents.
/// </remarks>
public sealed record PdfMetadata
{
    /// <summary>Gets an instance with no values set.</summary>
    public static PdfMetadata Empty { get; } = new();

    /// <summary>Gets the document title.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the person or organisation that created the document.</summary>
    public string? Author { get; init; }

    /// <summary>Gets the subject of the document.</summary>
    public string? Subject { get; init; }

    /// <summary>Gets the keywords of the document, verbatim as stored in the file.</summary>
    public string? Keywords { get; init; }

    /// <summary>Gets the application that produced the content of the document.</summary>
    public string? Creator { get; init; }

    /// <summary>Gets the application that converted the document to PDF.</summary>
    public string? Producer { get; init; }

    /// <summary>Gets the date the document was created.</summary>
    public DateTimeOffset? CreationDate { get; init; }

    /// <summary>Gets the date the document was last modified.</summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>
    /// Gets additional document information entries that are not part of the standard set, for
    /// example a customer specific <c>/Company</c> key.
    /// </summary>
    public IReadOnlyDictionary<string, string> CustomProperties { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>Gets a value indicating whether no values are set at all.</summary>
    public bool IsEmpty =>
        Title is null
        && Author is null
        && Subject is null
        && Keywords is null
        && Creator is null
        && Producer is null
        && CreationDate is null
        && ModificationDate is null
        && CustomProperties.Count == 0;

    /// <summary>
    /// Combines two metadata sets: the values of <paramref name="overlay"/> win, values that
    /// <paramref name="overlay"/> leaves unset are taken from <paramref name="baseline"/>.
    /// </summary>
    /// <param name="baseline">The metadata used for fields the overlay does not set.</param>
    /// <param name="overlay">The metadata that takes precedence.</param>
    /// <returns>The combined metadata.</returns>
    public static PdfMetadata Merge(PdfMetadata baseline, PdfMetadata overlay)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(overlay);

        var custom = baseline.CustomProperties.Count == 0 && overlay.CustomProperties.Count == 0
            ? baseline.CustomProperties
            : MergeCustomProperties(baseline.CustomProperties, overlay.CustomProperties);

        return new PdfMetadata
        {
            Title = overlay.Title ?? baseline.Title,
            Author = overlay.Author ?? baseline.Author,
            Subject = overlay.Subject ?? baseline.Subject,
            Keywords = overlay.Keywords ?? baseline.Keywords,
            Creator = overlay.Creator ?? baseline.Creator,
            Producer = overlay.Producer ?? baseline.Producer,
            CreationDate = overlay.CreationDate ?? baseline.CreationDate,
            ModificationDate = overlay.ModificationDate ?? baseline.ModificationDate,
            CustomProperties = custom,
        };
    }

    /// <summary>Gets a value indicating whether the two instances carry exactly the same values.</summary>
    /// <param name="other">The metadata to compare with.</param>
    /// <returns><see langword="true"/> when all values match, including the custom properties.</returns>
    public bool Equals(PdfMetadata? other) =>
        other is not null
        && Title == other.Title
        && Author == other.Author
        && Subject == other.Subject
        && Keywords == other.Keywords
        && Creator == other.Creator
        && Producer == other.Producer
        && CreationDate == other.CreationDate
        && ModificationDate == other.ModificationDate
        && CustomProperties.Count == other.CustomProperties.Count
        && CustomProperties.All(
            entry => other.CustomProperties.TryGetValue(entry.Key, out var value) && value == entry.Value);

    /// <summary>Returns a hash code over every value.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title);
        hash.Add(Author);
        hash.Add(Subject);
        hash.Add(Keywords);
        hash.Add(Creator);
        hash.Add(Producer);
        hash.Add(CreationDate);
        hash.Add(ModificationDate);

        foreach (var entry in CustomProperties.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            hash.Add(entry.Key);
            hash.Add(entry.Value);
        }

        return hash.ToHashCode();
    }

    private static ImmutableDictionary<string, string> MergeCustomProperties(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> overlay)
    {
        var merged = baseline.ToImmutableDictionary(StringComparer.Ordinal);

        foreach (var entry in overlay)
        {
            merged = merged.SetItem(entry.Key, entry.Value);
        }

        return merged;
    }
}
