using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.Entities;

/// <summary>
/// One participant in a merge: a document to read from, the pages to take from it, and the identity
/// that keeps it distinguishable inside a <see cref="Aggregates.MergePlan"/>.
/// </summary>
/// <remarks>
/// A source is an entity, not a value: two sources can read the identical file and still be
/// different participants of the same plan.
/// </remarks>
public sealed record PdfSource
{
    private PdfSource(
        PdfSourceId id,
        IPdfContentSource content,
        string displayName,
        PageSelection pages,
        string? password)
    {
        Id = id;
        Content = content;
        DisplayName = displayName;
        Pages = pages;
        Password = password;
    }

    /// <summary>Gets the identity of this source. Two sources in one plan never share an identity.</summary>
    public PdfSourceId Id { get; }

    /// <summary>Gets the readable content of the source document.</summary>
    public IPdfContentSource Content { get; }

    /// <summary>Gets the human readable name used in messages and in the merge result.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the pages this source contributes, in the order they must appear in the result.</summary>
    public PageSelection Pages { get; }

    /// <summary>Gets the user or owner password, when the document is encrypted.</summary>
    /// <remarks>
    /// A document that is encrypted is opened with this password before it is merged, and every string
    /// and stream of it is read in the clear. An encrypted document that is only protected against
    /// editing opens with no password at all, so this may stay <see langword="null"/> for it.
    /// </remarks>
    public string? Password { get; }

    /// <summary>Creates a source with a freshly generated identity.</summary>
    /// <param name="content">The readable content of the document.</param>
    /// <param name="displayName">The name used in messages; defaults to <see cref="IPdfContentSource.DisplayName"/>.</param>
    /// <param name="pages">The pages to contribute; defaults to every page.</param>
    /// <param name="password">The user or owner password, for an encrypted document.</param>
    /// <returns>The created source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No usable display name could be determined.</exception>
    public static PdfSource Create(
        IPdfContentSource content,
        string? displayName = null,
        PageSelection? pages = null,
        string? password = null) =>
        Create(PdfSourceId.New(), content, displayName, pages, password);

    /// <summary>Creates a source that reuses an existing identity.</summary>
    /// <param name="id">The identity to use.</param>
    /// <param name="content">The readable content of the document.</param>
    /// <param name="displayName">The name used in messages; defaults to <see cref="IPdfContentSource.DisplayName"/>.</param>
    /// <param name="pages">The pages to contribute; defaults to every page.</param>
    /// <param name="password">The user or owner password, for an encrypted document.</param>
    /// <returns>The created source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No usable display name could be determined.</exception>
    public static PdfSource Create(
        PdfSourceId id,
        IPdfContentSource content,
        string? displayName = null,
        PageSelection? pages = null,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var name = displayName;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = content.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A source needs a display name. Supply one, or use a content source that reports its own name.",
                nameof(displayName));
        }

        return new PdfSource(id, content, name, pages ?? PageSelection.All, password);
    }

    /// <summary>Returns a source that contributes different pages.</summary>
    /// <param name="pages">The pages to contribute.</param>
    /// <returns>The new source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pages"/> is <see langword="null"/>.</exception>
    public PdfSource WithPages(PageSelection pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        return new PdfSource(Id, Content, DisplayName, pages, Password);
    }

    /// <summary>Returns a source that uses a different password.</summary>
    /// <param name="password">The user password, or <see langword="null"/>.</param>
    /// <returns>The new source.</returns>
    public PdfSource WithPassword(string? password) => new(Id, Content, DisplayName, Pages, password);

    /// <summary>Returns a source with a different display name.</summary>
    /// <param name="displayName">The non-empty name to use.</param>
    /// <returns>The new source.</returns>
    /// <exception cref="ArgumentException"><paramref name="displayName"/> is null, empty or whitespace.</exception>
    public PdfSource Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new PdfSource(Id, Content, displayName, Pages, Password);
    }

    /// <summary>Formats the source as its display name followed by its page selection.</summary>
    /// <returns>The formatted source.</returns>
    public override string ToString() => $"{DisplayName} [{Pages}]";
}
